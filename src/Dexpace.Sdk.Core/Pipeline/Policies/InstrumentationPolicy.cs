// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Dexpace.Sdk.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dexpace.Sdk.Core.Pipeline.Policies;

/// <summary>
/// A per-attempt diagnostics policy that records distributed tracing spans, metrics, and
/// structured log events for each HTTP request attempt.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tracing.</b> A client-kind <see cref="Activity"/> is started from
/// <see cref="DexpaceDiagnostics.ActivitySource"/> for each attempt. The activity name is the
/// HTTP method (low cardinality). OTel HTTP semantic-convention tags are attached:
/// <c>http.request.method</c>, <c>url.full</c> (redacted), <c>server.address</c>,
/// <c>server.port</c>, <c>http.response.status_code</c>, and <c>http.request.resend_count</c>.
/// On exception, <c>error.type</c> is set and the activity status is
/// <see cref="ActivityStatusCode.Error"/>. When no listener is registered,
/// <c>ActivitySource.StartActivity</c> returns <see langword="null"/> and the hot path
/// allocates nothing for tracing.
/// </para>
/// <para>
/// <b>Metrics.</b> Two instruments are recorded per attempt:
/// <list type="bullet">
///   <item>
///     <c>http.client.request.duration</c> — <see cref="Histogram{T}"/> in seconds.
///   </item>
///   <item>
///     <c>http.client.active_requests</c> — <see cref="UpDownCounter{T}"/> incremented before
///     the send and decremented after (in a <c>finally</c> block).
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Logging.</b> Structured <see cref="ILogger"/> events are emitted at
/// <see cref="LogLevel.Debug"/> with the redacted URL. Secrets are never logged.
/// </para>
/// </remarks>
public sealed partial class InstrumentationPolicy : HttpPipelinePolicy
{
    // Instruments are created once from the shared Meter.
    private static readonly Histogram<double> s_requestDuration =
        DexpaceDiagnostics.Meter.CreateHistogram<double>(
            "http.client.request.duration",
            unit: "s",
            description: "Duration of HTTP client requests.");

    private static readonly UpDownCounter<long> s_activeRequests =
        DexpaceDiagnostics.Meter.CreateUpDownCounter<long>(
            "http.client.active_requests",
            unit: "{request}",
            description: "Number of HTTP requests currently in flight.");

    private static readonly UrlRedactor s_redactor = new();

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new <see cref="InstrumentationPolicy"/>.
    /// </summary>
    /// <param name="logger">
    /// The logger to write request/response events to. Defaults to
    /// <see cref="NullLogger.Instance"/> when <see langword="null"/>.
    /// </param>
    public InstrumentationPolicy(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public override PipelineStage Stage => PipelineStage.Diagnostics;

    /// <inheritdoc/>
    public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        var method = request.Method.Name;
        var redactedUrl = s_redactor.Redact(request.Url);

        // Start a client-kind Activity only when there are listeners; null if none.
        using var activity = DexpaceDiagnostics.ActivitySource.StartActivity(
            method,
            ActivityKind.Client);

        if (activity is not null)
        {
            activity.SetTag("http.request.method", method);
            activity.SetTag("url.full", redactedUrl);
            activity.SetTag("server.address", request.Url.Host);
            activity.SetTag("server.port", request.Url.IsDefaultPort ? -1 : request.Url.Port);
            activity.SetTag("http.request.resend_count", context.AttemptNumber);
            context.Activity = activity;
        }

        LogSendingRequest(_logger, method, redactedUrl);

        var sw = Stopwatch.StartNew();
        s_activeRequests.Add(1);

        try
        {
            await continuation.RunAsync(context).ConfigureAwait(false);

            var statusCode = context.Response?.Status.Code;
            if (activity is not null && statusCode.HasValue)
            {
                activity.SetTag("http.response.status_code", statusCode.Value);
            }

            LogReceivedResponse(_logger, method, statusCode, redactedUrl);
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.SetTag("error.type", ex.GetType().FullName);
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            }

            LogRequestFailed(_logger, ex, method, redactedUrl, ex.GetType().Name);

            throw;
        }
        finally
        {
            s_activeRequests.Add(-1);
            s_requestDuration.Record(sw.Elapsed.TotalSeconds);
            // Null out context.Activity so downstream observers don't access a disposed span.
            context.Activity = null;
        }
    }

    // ─── Source-generated zero-alloc logger messages ──────────────────────────

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Sending {Method} request to {Url}")]
    private static partial void LogSendingRequest(ILogger logger, string method, string url);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Received {Method} response {StatusCode} from {Url}")]
    private static partial void LogReceivedResponse(ILogger logger, string method, int? statusCode, string url);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Request {Method} to {Url} failed with {ErrorType}")]
    private static partial void LogRequestFailed(ILogger logger, Exception ex, string method, string url, string errorType);
}
