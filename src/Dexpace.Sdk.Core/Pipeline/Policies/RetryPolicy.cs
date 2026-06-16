// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Errors;
using Dexpace.Sdk.Core.Http.Common;

namespace Dexpace.Sdk.Core.Pipeline.Policies;

/// <summary>
/// A retry pipeline policy that retries failed requests with exponential back-off and
/// full jitter, optionally honoring <c>Retry-After</c> response headers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retryable statuses:</b> 408, 429, 500, 502, 503, 504. Any other status (including 4xx)
/// is returned immediately.
/// </para>
/// <para>
/// <b>Retryable exceptions:</b> <see cref="ServiceRequestException"/> (request never sent) and
/// <see cref="ServiceResponseException"/> (sent but response unreadable). All other exceptions,
/// including <see cref="OperationCanceledException"/>, propagate unchanged.
/// </para>
/// <para>
/// <b>Non-idempotent requests</b> are retried only when the request body is replayable
/// (or absent) AND <see cref="RetryOptions.RetryNonIdempotentWhenReplayable"/> is
/// <see langword="true"/>.
/// </para>
/// <para>
/// <b>Delay:</b> when <c>Retry-After</c> is present and
/// <see cref="RetryOptions.HonorRetryAfter"/> is <see langword="true"/>, the parsed value
/// is used; otherwise the delay is drawn from a uniform random distribution over
/// <c>[0, min(BaseDelay × 2^attempt, MaxDelay)]</c> (full jitter). The
/// <see cref="TimeProvider"/> passed to the constructor drives both the current-time lookup
/// (for HTTP-date parsing) and the <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>
/// overload so tests can control delays without real sleeps.
/// </para>
/// <para>
/// <b>Response disposal:</b> when a retryable response is going to be retried, the response
/// is disposed before sleeping to release the connection promptly.
/// </para>
/// </remarks>
public sealed class RetryPolicy : HttpPipelinePolicy
{
    private static readonly HashSet<int> s_retryableStatusCodes = [408, 429, 500, 502, 503, 504];

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new <see cref="RetryPolicy"/>.
    /// </summary>
    /// <param name="timeProvider">
    /// The time source used to obtain the current UTC instant (for <c>Retry-After</c> HTTP-date
    /// parsing) and to drive <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>.
    /// Defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.
    /// </param>
    public RetryPolicy(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public override PipelineStage Stage => PipelineStage.Retry;

    /// <inheritdoc/>
    public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options.Retry;
        var attempt = 0;

        while (true)
        {
            context.AttemptNumber = attempt;

            Exception? caughtException = null;

            try
            {
                await continuation.RunAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRetryableException(ex))
            {
                caughtException = ex;
            }

            var request = context.Request;
            var canRetryRequest = CanRetryRequest(request, options);

            if (caughtException is not null)
            {
                // Exception path: re-throw if exhausted or not retryable.
                if (attempt >= options.MaxRetryAttempts || !canRetryRequest)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(caughtException);
                }

                await SleepAsync(null, attempt, options, context.CancellationToken)
                    .ConfigureAwait(false);
                attempt++;
                continue;
            }

            // Success or non-retryable response path.
            var response = context.Response;

            if (response is null
                || attempt >= options.MaxRetryAttempts
                || !canRetryRequest
                || !IsRetryableStatus(response.Status.Code))
            {
                // Leave context.Response as-is and return.
                return;
            }

            // Parse Retry-After before disposing the response.
            TimeSpan? retryAfterDelay = null;
            if (options.HonorRetryAfter)
            {
                var retryAfterHeader = response.Headers.Get(HttpHeaderName.WellKnown.RetryAfter.Original);
                retryAfterDelay = ParseRetryAfter(retryAfterHeader);
            }

            // Dispose the retryable response before sleeping to release the connection.
            await response.DisposeAsync().ConfigureAwait(false);
            context.Response = null;

            await SleepAsync(retryAfterDelay, attempt, options, context.CancellationToken)
                .ConfigureAwait(false);
            attempt++;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="ex"/> is a retryable transport
    /// exception. <see cref="OperationCanceledException"/> is explicitly excluded so
    /// cancellation always propagates.
    /// </summary>
    private static bool IsRetryableException(Exception ex) =>
        ex is ServiceRequestException or ServiceResponseException
        && ex is not OperationCanceledException;

    private static bool IsRetryableStatus(int code) =>
        s_retryableStatusCodes.Contains(code);

    private static bool CanRetryRequest(
        Http.Request.Request request,
        RetryOptions options)
    {
        var bodyReplayable = request.Body is null || request.Body.IsReplayable;
        return bodyReplayable
            && (request.Method.IsIdempotent || options.RetryNonIdempotentWhenReplayable);
    }

    /// <summary>
    /// Parses a <c>Retry-After</c> header value.
    /// Returns the delay as a <see cref="TimeSpan"/>, or <see langword="null"/> when the
    /// value cannot be interpreted.
    /// </summary>
    /// <remarks>
    /// Accepts two forms per RFC 7231 §7.1.3:
    /// <list type="bullet">
    ///   <item>An integer representing a delta-seconds value.</item>
    ///   <item>An HTTP-date whose distance from the current instant is the delay (floored at zero).</item>
    /// </list>
    /// </remarks>
    private TimeSpan? ParseRetryAfter(string? headerValue)
    {
        if (string.IsNullOrEmpty(headerValue))
        {
            return null;
        }

        // Delta-seconds form.
        if (int.TryParse(headerValue, System.Globalization.NumberStyles.None, null, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        // HTTP-date form (RFC 1123 / "r" format).
        if (DateTimeOffset.TryParseExact(
                headerValue,
                "r",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var httpDate))
        {
            var delta = httpDate - _timeProvider.GetUtcNow();
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return null;
    }

    /// <summary>
    /// Sleeps for the appropriate back-off delay, using <paramref name="explicitDelay"/> when
    /// supplied (from <c>Retry-After</c>) or full-jitter exponential back-off otherwise.
    /// </summary>
    private async Task SleepAsync(
        TimeSpan? explicitDelay,
        int attempt,
        RetryOptions options,
        CancellationToken cancellationToken)
    {
        TimeSpan delay;

        if (explicitDelay.HasValue)
        {
            delay = explicitDelay.Value;
        }
        else
        {
            // Full jitter: uniform in [0, min(BaseDelay * 2^attempt, MaxDelay)].
            // Guard the shift: cap at 30 to avoid overflow (2^30 ≈ 1e9 ms >> any MaxDelay).
            var shift = Math.Min(attempt, 30);
            var cap = TimeSpan.FromTicks(
                Math.Min(
                    (long)(options.BaseDelay.Ticks * (1L << shift)),
                    options.MaxDelay.Ticks));
            delay = TimeSpan.FromTicks((long)(cap.Ticks * Random.Shared.NextDouble()));
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
