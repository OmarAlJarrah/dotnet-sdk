// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Errors;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pipeline;
using Dexpace.Sdk.Core.Pipeline.Policies;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pipeline.Policies;

public sealed class RetryPolicyTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Request MakeGetRequest() => Request.Get("https://api.example.com/v1/items");

    private static Request MakePostRequest(bool replayable = false)
    {
        var body = replayable
            ? RequestBody.FromBytes(ReadOnlyMemory<byte>.Empty)
            : RequestBody.FromStream(new MemoryStream([1, 2, 3]));
        return Request.Post("https://api.example.com/v1/items", body);
    }

    private static DexpaceClientOptions MakeOptions(
        int maxRetryAttempts = 3,
        bool honorRetryAfter = true,
        bool retryNonIdempotentWhenReplayable = false)
    {
        return new DexpaceClientOptions
        {
            Retry = new RetryOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(10),
                HonorRetryAfter = honorRetryAfter,
                RetryNonIdempotentWhenReplayable = retryNonIdempotentWhenReplayable,
            }
        };
    }

    // -------------------------------------------------------------------------
    // Stage
    // -------------------------------------------------------------------------

    [Fact]
    public void Stage_IsRetry()
    {
        Assert.Equal(PipelineStage.Retry, new RetryPolicy().Stage);
    }

    // -------------------------------------------------------------------------
    // Successful responses (no retry needed)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SuccessOnFirstAttempt_ReturnsResponse_NoRetry()
    {
        var response200 = new Response(Status.Ok);
        var transport = new ScriptedTransport(new object[] { response200 });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions());

        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(1, transport.CallCount);
    }

    // -------------------------------------------------------------------------
    // Retryable status codes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_503ThenSuccess_RetriesAndReturnsSuccess()
    {
        var response503 = new Response(Status.ServiceUnavailable);
        var response200 = new Response(Status.Ok);
        var transport = new ScriptedTransport(new object[] { response503, response200 });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 3));

        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_Repeated503_ReturnsLastResponseAfterMaxAttempts()
    {
        // MaxRetryAttempts = 3 → 1 initial + 3 retries = 4 total calls.
        var responses = Enumerable
            .Repeat<object>(new Response(Status.ServiceUnavailable), 4)
            .ToArray();
        var transport = new ScriptedTransport(responses);
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 3));

        Assert.Equal(Status.ServiceUnavailable, result.Status);
        Assert.Equal(4, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_500_IsRetried()
    {
        var transport = new ScriptedTransport(
            new object[] { new Response(Status.InternalServerError), new Response(Status.Ok) });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 1));

        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_429_IsRetried()
    {
        var transport = new ScriptedTransport(
            new object[] { new Response(Status.TooManyRequests), new Response(Status.Ok) });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 1));

        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(2, transport.CallCount);
    }

    // -------------------------------------------------------------------------
    // Non-retryable status codes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_400_ReturnsImmediately_NoRetry()
    {
        var response400 = new Response(Status.BadRequest);
        var transport = new ScriptedTransport(new object[] { response400 });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions());

        Assert.Equal(Status.BadRequest, result.Status);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_404_ReturnsImmediately_NoRetry()
    {
        var transport = new ScriptedTransport(new object[] { new Response(Status.NotFound) });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions());

        Assert.Equal(Status.NotFound, result.Status);
        Assert.Equal(1, transport.CallCount);
    }

    // -------------------------------------------------------------------------
    // Non-idempotent methods
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_Post_NonReplayableBody_503_NotRetried()
    {
        // POST with a stream body (not replayable) — must not retry.
        var response503 = new Response(Status.ServiceUnavailable);
        var transport = new ScriptedTransport(new object[] { response503 });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(
            MakePostRequest(replayable: false),
            MakeOptions(retryNonIdempotentWhenReplayable: false));

        Assert.Equal(Status.ServiceUnavailable, result.Status);
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_Post_ReplayableBody_RetryNonIdempotentEnabled_503_IsRetried()
    {
        var transport = new ScriptedTransport(
            new object[] { new Response(Status.ServiceUnavailable), new Response(Status.Ok) });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(
            MakePostRequest(replayable: true),
            MakeOptions(maxRetryAttempts: 1, retryNonIdempotentWhenReplayable: true));

        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_Post_ReplayableBody_RetryNonIdempotentDisabled_503_NotRetried()
    {
        var transport = new ScriptedTransport(new object[] { new Response(Status.ServiceUnavailable) });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(
            MakePostRequest(replayable: true),
            MakeOptions(retryNonIdempotentWhenReplayable: false));

        Assert.Equal(Status.ServiceUnavailable, result.Status);
        Assert.Equal(1, transport.CallCount);
    }

    // -------------------------------------------------------------------------
    // Retryable exceptions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ServiceRequestException_OnGet_IsRetried()
    {
        var ex = new ServiceRequestException("DNS failure");
        var transport = new ScriptedTransport(
            new object[] { ex, new Response(Status.Ok) });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 1));

        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_ServiceRequestException_ExhaustsRetries_Rethrows()
    {
        var ex = new ServiceRequestException("persistent failure");
        // 1 initial + 3 retries = 4 total.
        var script = Enumerable.Repeat<object>(ex, 4).ToArray();
        var transport = new ScriptedTransport(script);
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var thrown = await Assert.ThrowsAsync<ServiceRequestException>(
            () => pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 3)).AsTask());

        Assert.Same(ex, thrown);
        Assert.Equal(4, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_ServiceResponseException_IsRetried()
    {
        var ex = new ServiceResponseException("connection dropped");
        var transport = new ScriptedTransport(
            new object[] { ex, new Response(Status.Ok) });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 1));

        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_ServiceRequestException_Post_NonReplayable_NotRetried()
    {
        var ex = new ServiceRequestException("DNS failure");
        var transport = new ScriptedTransport(new object[] { ex });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        await Assert.ThrowsAsync<ServiceRequestException>(
            () => pipeline.SendAsync(
                MakePostRequest(replayable: false),
                MakeOptions(retryNonIdempotentWhenReplayable: false)).AsTask());

        Assert.Equal(1, transport.CallCount);
    }

    // -------------------------------------------------------------------------
    // Cancellation propagates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_Cancellation_Propagates_NotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Transport throws OCE directly — must not be retried.
        var transport = new ScriptedTransport(
            new object[] { new OperationCanceledException(cts.Token) });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.SendAsync(MakeGetRequest(), MakeOptions(), cts.Token).AsTask());

        Assert.Equal(1, transport.CallCount);
    }

    // -------------------------------------------------------------------------
    // Retry-After header
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_RetryAfterDeltaSeconds_IsHonored()
    {
        // Retry-After: 1 — parsed and honored; we just verify the retry happens.
        var headers = new Headers.Builder().Set("Retry-After", "1").Build();
        var response503 = new Response(Status.ServiceUnavailable, headers);
        var response200 = new Response(Status.Ok);
        var transport = new ScriptedTransport(new object[] { response503, response200 });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 1));

        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_RetryAfterHttpDate_IsHonored()
    {
        var fixedNow = new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.Zero);
        var future = fixedNow.AddSeconds(5).ToString("r");

        var headers = new Headers.Builder().Set("Retry-After", future).Build();
        var response503 = new Response(Status.ServiceUnavailable, headers);
        var response200 = new Response(Status.Ok);
        var transport = new ScriptedTransport(new object[] { response503, response200 });

        // Fake time pinned to fixedNow so the delta is parsed correctly.
        var pipeline = new PipelineBuilder()
            .Add(new RetryPolicy(new FixedTimeProvider(fixedNow)))
            .Build(transport);

        var result = await pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 1));

        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_RetryAfterIgnored_WhenHonorRetryAfterFalse()
    {
        var headers = new Headers.Builder().Set("Retry-After", "60").Build();
        var response503 = new Response(Status.ServiceUnavailable, headers);
        var response200 = new Response(Status.Ok);
        var transport = new ScriptedTransport(new object[] { response503, response200 });
        var pipeline = new PipelineBuilder().Add(new RetryPolicy(new InstantTimeProvider())).Build(transport);

        var opts = MakeOptions(maxRetryAttempts: 1, honorRetryAfter: false);
        var result = await pipeline.SendAsync(MakeGetRequest(), opts);

        // Still retries (with jitter), just doesn't wait 60s.
        Assert.Equal(Status.Ok, result.Status);
        Assert.Equal(2, transport.CallCount);
    }

    // -------------------------------------------------------------------------
    // AttemptNumber tracking
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_AttemptNumber_IsZeroOnFirstAttempt()
    {
        var recordedAttempts = new List<int>();
        var capturePolicy = new CapturingAttemptPolicy(recordedAttempts);
        var transport = new ScriptedTransport(new object[] { new Response(Status.Ok) });

        var pipeline = new PipelineBuilder()
            .Add(new RetryPolicy(new InstantTimeProvider()))
            .Add(capturePolicy)
            .Build(transport);

        await pipeline.SendAsync(MakeGetRequest(), MakeOptions());

        Assert.Single(recordedAttempts);
        Assert.Equal(0, recordedAttempts[0]);
    }

    [Fact]
    public async Task ProcessAsync_AttemptNumber_IncrementsOnRetry()
    {
        var recordedAttempts = new List<int>();
        var capturePolicy = new CapturingAttemptPolicy(recordedAttempts);

        var transport = new ScriptedTransport(new object[]
        {
            new Response(Status.ServiceUnavailable),
            new Response(Status.ServiceUnavailable),
            new Response(Status.Ok),
        });

        var pipeline = new PipelineBuilder()
            .Add(new RetryPolicy(new InstantTimeProvider()))
            .Add(capturePolicy)
            .Build(transport);

        await pipeline.SendAsync(MakeGetRequest(), MakeOptions(maxRetryAttempts: 3));

        Assert.Equal([0, 1, 2], recordedAttempts);
    }

    // -------------------------------------------------------------------------
    // Nested helpers
    // -------------------------------------------------------------------------

    private sealed class CapturingAttemptPolicy(List<int> log) : HttpPipelinePolicy
    {
        public override PipelineStage Stage => PipelineStage.PerAttempt;

        public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
        {
            log.Add(context.AttemptNumber);
            await continuation.RunAsync(context).ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Delay computation — backoff cap and no-overflow
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_OverflowGuard_AllRecordedDelaysAreNonNegativeAndBoundedByMaxDelay()
    {
        // Choose BaseDelay and MaxRetryAttempts such that the OLD formula
        //   BaseDelay.Ticks * (1L << shift)
        // would overflow long and wrap negative on at least one attempt.
        //
        // TimeSpan.FromDays(700).Ticks ≈ 6.05e17.  At shift = 4 (attempt 4):
        //   6.05e17 << 4 = ~9.7e18, which is > long.MaxValue (9.22e18) → wraps negative.
        // With MaxRetryAttempts = 5 we get shifts 0..4, covering the overflow at shift 4.
        //
        // The overflow-safe implementation clamps to MaxDelay instead of overflowing,
        // so every recorded delay must be non-negative and <= MaxDelay.
        // Against the old code, at least one attempt would compute a negative cap,
        // causing the jitter draw to be zero and skipping Task.Delay entirely — which
        // would mean a delay of TimeSpan.Zero being recorded by RecordingTimeProvider
        // after a shift that should have been clamped to MaxDelay.  The assertion that
        // ALL recorded delays are > TimeSpan.Zero would catch that (jitter from
        // cap = 0 → delay = 0 → not recorded, but the assertion on Count would fail
        // because a skipped delay leaves fewer entries).  We therefore assert both:
        //   (a) the count equals MaxRetryAttempts, and
        //   (b) every entry is in [0, MaxDelay].
        var baseDelay = TimeSpan.FromDays(700);   // ~6e17 ticks; shift≥4 overflows old code
        var maxDelay = TimeSpan.FromSeconds(30);  // small cap — meaningful upper bound

        // 1 initial + 5 retries = 6 transport calls.
        var responses = Enumerable
            .Repeat<object>(new Response(Status.ServiceUnavailable), 5)
            .Append(new Response(Status.Ok))
            .ToArray();
        var recording = new RecordingTimeProvider();

        var options = new DexpaceClientOptions
        {
            Retry = new RetryOptions
            {
                MaxRetryAttempts = 5,
                BaseDelay = baseDelay,
                MaxDelay = maxDelay,
                HonorRetryAfter = false,
            }
        };

        var pipeline = new PipelineBuilder().Add(new RetryPolicy(recording)).Build(transport: new ScriptedTransport(responses));
        await pipeline.SendAsync(MakeGetRequest(), options);

        // Five retries → five delays recorded (one per retry, not per attempt).
        Assert.Equal(5, recording.RequestedDelays.Count);
        Assert.All(recording.RequestedDelays, pair =>
        {
            Assert.True(pair.Item2 >= TimeSpan.Zero,
                $"Delay at timer call {pair.Item1} was negative: {pair.Item2}");
            Assert.True(pair.Item2 <= maxDelay,
                $"Delay at timer call {pair.Item1} exceeded MaxDelay ({maxDelay}): {pair.Item2}");
        });
    }

    [Fact]
    public async Task ProcessAsync_DelayBound_LaterAttemptsArePinnedToMaxDelay()
    {
        // Choose BaseDelay == MaxDelay so that even attempt 0 (shift = 0) computes
        //   cap = min(BaseDelay * 1, MaxDelay) = MaxDelay.
        // This means the jitter-cap is MaxDelay for EVERY attempt, and the assertion
        //   delay <= MaxDelay
        // is meaningful — it is governed by the cap logic, not by BaseDelay being small.
        // Against a naive implementation that forgets to apply the cap, some draws
        // (from a cap larger than MaxDelay) could land above MaxDelay.
        var maxDelay = TimeSpan.FromSeconds(30);
        var baseDelay = maxDelay; // BaseDelay == MaxDelay → cap == MaxDelay for all attempts

        // 1 initial + 3 retries = 4 transport calls.
        var responses = Enumerable
            .Repeat<object>(new Response(Status.ServiceUnavailable), 3)
            .Append(new Response(Status.Ok))
            .ToArray();
        var recording = new RecordingTimeProvider();

        var options = new DexpaceClientOptions
        {
            Retry = new RetryOptions
            {
                MaxRetryAttempts = 3,
                BaseDelay = baseDelay,
                MaxDelay = maxDelay,
                HonorRetryAfter = false,
            }
        };

        var pipeline = new PipelineBuilder().Add(new RetryPolicy(recording)).Build(transport: new ScriptedTransport(responses));
        await pipeline.SendAsync(MakeGetRequest(), options);

        // Three retries → three delays recorded.
        Assert.Equal(3, recording.RequestedDelays.Count);
        Assert.All(recording.RequestedDelays, pair =>
        {
            Assert.True(pair.Item2 >= TimeSpan.Zero,
                $"Delay at timer call {pair.Item1} was negative: {pair.Item2}");
            Assert.True(pair.Item2 <= maxDelay,
                $"Delay at timer call {pair.Item1} exceeded MaxDelay ({maxDelay}): {pair.Item2}");
        });
    }

    /// <summary>
    /// A TimeProvider whose Task.Delay fires after 1 ms so tests don't block on real delays.
    /// </summary>
    private sealed class InstantTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.Zero);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) =>
            base.CreateTimer(callback, state, TimeSpan.FromMilliseconds(1), period);
    }

    /// <summary>
    /// A TimeProvider pinned to a fixed instant (useful for Retry-After HTTP-date tests).
    /// Task.Delay also fires after 1 ms.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) =>
            base.CreateTimer(callback, state, TimeSpan.FromMilliseconds(1), period);
    }

    /// <summary>
    /// A TimeProvider that records every (attemptNumber, dueTime) pair requested via
    /// <see cref="CreateTimer"/> so tests can assert on actual delays passed by the retry policy.
    /// </summary>
    private sealed class RecordingTimeProvider : TimeProvider
    {
        private readonly List<(int Attempt, TimeSpan Delay)> _delays = [];
        private int _timerCallCount;

        public List<(int Attempt, TimeSpan Delay)> RequestedDelays => _delays;

        public override DateTimeOffset GetUtcNow() =>
            new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.Zero);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var attempt = Interlocked.Increment(ref _timerCallCount) - 1;
            _delays.Add((attempt, dueTime));
            // Fire immediately so the test doesn't block.
            return base.CreateTimer(callback, state, TimeSpan.FromMilliseconds(1), period);
        }
    }

    /// <summary>
    /// A transport whose responses (or exceptions) are scripted per call index.
    /// Entries may be <see cref="Response"/> instances, <see cref="Exception"/> instances,
    /// or <c>Func&lt;Response&gt;</c> factories.
    /// </summary>
    private sealed class ScriptedTransport : IAsyncHttpClient
    {
        private readonly List<object> _script;
        private int _callCount;

        public ScriptedTransport(IEnumerable<object> script)
        {
            _script = script.ToList();
        }

        public int CallCount => _callCount;

        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _callCount) - 1;
            if (index >= _script.Count)
            {
                throw new InvalidOperationException($"Script ran out of entries at call {index + 1}.");
            }

            var entry = _script[index];
            return entry switch
            {
                Response r => Task.FromResult(r),
                Exception ex => Task.FromException<Response>(ex),
                Func<Response> f => Task.FromResult(f()),
                _ => throw new InvalidOperationException($"Unknown script entry type: {entry.GetType()}"),
            };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
