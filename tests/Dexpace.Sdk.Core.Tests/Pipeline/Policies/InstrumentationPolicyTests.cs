// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Diagnostics;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pipeline;
using Dexpace.Sdk.Core.Pipeline.Policies;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pipeline.Policies;

public sealed class InstrumentationPolicyTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public InstrumentationPolicyTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Dexpace.Sdk",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => _activities.Add(a),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static Request MakeRequest(Uri url) =>
        new Request(Method.Get, url);

    private static DexpaceClientOptions DefaultOptions() => new();

    // Runs the policy with a scripted transport.
    private static async Task<Response> RunAsync(
        HttpPipelinePolicy policy,
        Request request,
        IAsyncHttpClient transport)
    {
        var pipeline = new PipelineBuilder().Add(policy).Build(transport);
        return await pipeline.SendAsync(request, DefaultOptions());
    }

    // ─── Stage ───────────────────────────────────────────────────────────────

    [Fact]
    public void Stage_IsDiagnostics()
    {
        Assert.Equal(PipelineStage.Diagnostics, new InstrumentationPolicy().Stage);
    }

    // ─── Activity tracing ────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_StartsActivity_WithClientKind()
    {
        var transport = new StaticTransport(new Response(Status.Ok));
        var policy = new InstrumentationPolicy();
        var url = new Uri("https://api.example.com/v1/items");

        await RunAsync(policy, MakeRequest(url), transport);

        var activity = Assert.Single(_activities);
        Assert.Equal(ActivityKind.Client, activity.Kind);
    }

    [Fact]
    public async Task ProcessAsync_ActivityName_IsHttpMethod()
    {
        var transport = new StaticTransport(new Response(Status.Ok));
        var policy = new InstrumentationPolicy();
        var url = new Uri("https://api.example.com/v1/items");

        await RunAsync(policy, MakeRequest(url), transport);

        var activity = Assert.Single(_activities);
        Assert.Equal("GET", activity.DisplayName);
    }

    [Fact]
    public async Task ProcessAsync_Activity_HasExpectedOtelTags()
    {
        var transport = new StaticTransport(new Response(Status.Ok));
        var policy = new InstrumentationPolicy();
        var url = new Uri("https://api.example.com:8443/v1/items");

        await RunAsync(policy, MakeRequest(url), transport);

        var activity = Assert.Single(_activities);
        Assert.Equal("GET", activity.GetTagItem("http.request.method"));
        Assert.NotNull(activity.GetTagItem("url.full"));
        Assert.Equal("api.example.com", activity.GetTagItem("server.address"));
        Assert.Equal(8443, activity.GetTagItem("server.port"));
        Assert.Equal(200, activity.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public async Task ProcessAsync_UrlFull_IsSensitiveParamRedacted()
    {
        var transport = new StaticTransport(new Response(Status.Ok));
        var policy = new InstrumentationPolicy();
        // "api_key" is in UrlRedactor.DefaultSensitiveParams
        var url = new Uri("https://api.example.com/v1/items?api_key=SECRET123&page=2");

        await RunAsync(policy, MakeRequest(url), transport);

        var activity = Assert.Single(_activities);
        var urlFull = activity.GetTagItem("url.full") as string;
        Assert.NotNull(urlFull);
        Assert.DoesNotContain("SECRET123", urlFull);
        Assert.Contains("REDACTED", urlFull);
        // Non-sensitive param should be preserved
        Assert.Contains("page=2", urlFull);
    }

    [Fact]
    public async Task ProcessAsync_AttemptNumber_SetOnResendCountTag()
    {
        // Use RetryPolicy + InstrumentationPolicy so AttemptNumber increments
        var transport = new ScriptedTransport([
            new Response(Status.ServiceUnavailable),
            new Response(Status.Ok),
        ]);
        var pipeline = new PipelineBuilder()
            .Add(new RetryPolicy(new InstantTimeProvider()))
            .Add(new InstrumentationPolicy())
            .Build(transport);

        await pipeline.SendAsync(MakeRequest(new Uri("https://api.example.com/")), DefaultOptions());

        // Two activities should have been started: attempt 0 and attempt 1
        Assert.Equal(2, _activities.Count);
        Assert.Equal(0, _activities[0].GetTagItem("http.request.resend_count"));
        Assert.Equal(1, _activities[1].GetTagItem("http.request.resend_count"));
    }

    [Fact]
    public async Task ProcessAsync_ActivitySetOnContext_DuringContinuation()
    {
        // CapturingPolicy must run AFTER InstrumentationPolicy sets context.Activity.
        // InstrumentationPolicy is at Diagnostics=600; we use stage 650 so it sorts after.
        Activity? capturedActivity = null;
        var capturingPolicy = new CapturingPolicy(ctx => capturedActivity = ctx.Activity, stage: (PipelineStage)650);

        var transport = new StaticTransport(new Response(Status.Ok));
        var pipeline = new PipelineBuilder()
            .Add(new InstrumentationPolicy())
            .Add(capturingPolicy)
            .Build(transport);

        await pipeline.SendAsync(MakeRequest(new Uri("https://api.example.com/")), DefaultOptions());

        Assert.NotNull(capturedActivity);
    }

    [Fact]
    public async Task ProcessAsync_Exception_SetsErrorTypeTag_AndActivityStatusError()
    {
        var ex = new InvalidOperationException("boom");
        var transport = new ThrowingTransport(ex);
        var policy = new InstrumentationPolicy();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(policy, MakeRequest(new Uri("https://api.example.com/")), transport));

        Assert.Same(ex, thrown);

        var activity = Assert.Single(_activities);
        var errorType = activity.GetTagItem("error.type") as string;
        Assert.NotNull(errorType);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task ProcessAsync_NoListener_DoesNotThrow()
    {
        // Dispose the listener — now no listener is active, StartActivity returns null.
        _listener.Dispose();

        var transport = new StaticTransport(new Response(Status.Ok));
        var policy = new InstrumentationPolicy();

        // Must not throw even when Activity is null
        var result = await RunAsync(policy, MakeRequest(new Uri("https://api.example.com/")), transport);
        Assert.Equal(Status.Ok, result.Status);
    }

    // ─── Metrics ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_RecordsDurationHistogram()
    {
        double? recordedDuration = null;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Dexpace.Sdk" && instrument.Name == "http.client.request.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((_, measurement, _, _) =>
        {
            recordedDuration = measurement;
        });
        meterListener.Start();

        var transport = new StaticTransport(new Response(Status.Ok));
        var policy = new InstrumentationPolicy();
        await RunAsync(policy, MakeRequest(new Uri("https://api.example.com/")), transport);

        meterListener.RecordObservableInstruments();
        Assert.NotNull(recordedDuration);
        Assert.True(recordedDuration >= 0, "Duration must be non-negative");
    }

    [Fact]
    public async Task ProcessAsync_ActiveRequestsCounter_IncrementsThenDecrements()
    {
        long maxObserved = 0;
        long lastObserved = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Dexpace.Sdk" && instrument.Name == "http.client.active_requests")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            lastObserved += measurement;
            if (lastObserved > maxObserved)
            {
                maxObserved = lastObserved;
            }
        });
        meterListener.Start();

        var transport = new StaticTransport(new Response(Status.Ok));
        var policy = new InstrumentationPolicy();
        await RunAsync(policy, MakeRequest(new Uri("https://api.example.com/")), transport);

        meterListener.RecordObservableInstruments();
        // After completion the counter should be back to 0 (net effect)
        Assert.Equal(0, lastObserved);
        // And at some point during the call it was positive
        Assert.True(maxObserved > 0, "Active requests should have been incremented");
    }

    // ─── Logging ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_LogsStructuredEvent_WithRedactedUrl()
    {
        var logger = new RecordingLogger();
        var transport = new StaticTransport(new Response(Status.Ok));
        var policy = new InstrumentationPolicy(logger);
        var url = new Uri("https://api.example.com/v1/items?api_key=SECRET&x=1");

        await RunAsync(policy, MakeRequest(url), transport);

        Assert.NotEmpty(logger.Entries);
        // Verify that no log entry contains the secret
        foreach (var (_, message) in logger.Entries)
        {
            Assert.DoesNotContain("SECRET", message);
        }
        // Verify the redacted marker is present in at least one entry
        Assert.Contains(logger.Entries, e => e.Message.Contains("REDACTED"));
    }

    [Fact]
    public async Task ProcessAsync_NullLogger_DoesNotThrow()
    {
        // Passing null logger should fall back to NullLogger.Instance
        var transport = new StaticTransport(new Response(Status.Ok));
        var policy = new InstrumentationPolicy(null);
        var result = await RunAsync(policy, MakeRequest(new Uri("https://api.example.com/")), transport);
        Assert.Equal(Status.Ok, result.Status);
    }

    // ─── Nested helpers ──────────────────────────────────────────────────────

    private sealed class StaticTransport(Response response) : IAsyncHttpClient
    {
        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default) =>
            Task.FromResult(response);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingTransport(Exception ex) : IAsyncHttpClient
    {
        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default) =>
            Task.FromException<Response>(ex);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ScriptedTransport : IAsyncHttpClient
    {
        private readonly List<object> _script;
        private int _index;

        public ScriptedTransport(IEnumerable<object> script) => _script = [.. script];

        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default)
        {
            var entry = _script[_index++];
            return entry switch
            {
                Response r => Task.FromResult(r),
                Exception ex => Task.FromException<Response>(ex),
                _ => throw new InvalidOperationException($"Unknown script entry: {entry.GetType()}"),
            };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CapturingPolicy(Action<PipelineContext> capture, PipelineStage stage = PipelineStage.PerAttempt) : HttpPipelinePolicy
    {
        public override PipelineStage Stage => stage;

        public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
        {
            capture(context);
            await continuation.RunAsync(context).ConfigureAwait(false);
        }
    }

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

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
