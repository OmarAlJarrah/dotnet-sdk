// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pipeline;
using Dexpace.Sdk.Core.Pipeline.Policies;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pipeline.Policies;

public sealed class OperationPolicyTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Request MakeRequest() => Request.Get("https://api.example.com/v1/items");

    private static DexpaceClientOptions OptionsWithTimeout(TimeSpan timeout) =>
        new() { OverallTimeout = timeout };

    private static DexpaceClientOptions OptionsNoTimeout() => new() { OverallTimeout = null };

    // A transport that delays indefinitely until its token is cancelled.
    private sealed class HangingTransport : IAsyncHttpClient
    {
        public async Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new Response(Status.Ok);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // A transport that completes immediately with 200 OK.
    private sealed class InstantTransport : IAsyncHttpClient
    {
        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Response(Status.Ok));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Stage
    // -------------------------------------------------------------------------

    [Fact]
    public void Stage_IsOperation()
    {
        var policy = new OperationPolicy();
        Assert.Equal(PipelineStage.Operation, policy.Stage);
    }

    // -------------------------------------------------------------------------
    // Timeout behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_WithShortTimeout_ThrowsWhenTransportHangs()
    {
        var pipeline = new PipelineBuilder()
            .Add(new OperationPolicy())
            .Build(new HangingTransport());

        var options = OptionsWithTimeout(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.SendAsync(MakeRequest(), options).AsTask());
    }

    [Fact]
    public async Task ProcessAsync_WithNoTimeout_CompletesNormally()
    {
        var pipeline = new PipelineBuilder()
            .Add(new OperationPolicy())
            .Build(new InstantTransport());

        var response = await pipeline.SendAsync(MakeRequest(), OptionsNoTimeout());

        Assert.Equal(Status.Ok, response.Status);
    }

    [Fact]
    public async Task ProcessAsync_WithZeroTimeout_CompletesNormally()
    {
        // A zero TimeSpan is non-positive — treated as "no timeout".
        var pipeline = new PipelineBuilder()
            .Add(new OperationPolicy())
            .Build(new InstantTransport());

        var options = OptionsWithTimeout(TimeSpan.Zero);
        var response = await pipeline.SendAsync(MakeRequest(), options);

        Assert.Equal(Status.Ok, response.Status);
    }

    [Fact]
    public async Task ProcessAsync_WithNegativeTimeout_CompletesNormally()
    {
        // A negative TimeSpan is non-positive — treated as "no timeout".
        var pipeline = new PipelineBuilder()
            .Add(new OperationPolicy())
            .Build(new InstantTransport());

        var options = OptionsWithTimeout(TimeSpan.FromSeconds(-1));
        var response = await pipeline.SendAsync(MakeRequest(), options);

        Assert.Equal(Status.Ok, response.Status);
    }

    [Fact]
    public async Task ProcessAsync_CallerCancellation_Propagates_EvenWithTimeout()
    {
        // Caller cancels before the pipeline finishes — OperationCanceledException propagates.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var pipeline = new PipelineBuilder()
            .Add(new OperationPolicy())
            .Build(new HangingTransport());

        var options = OptionsWithTimeout(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.SendAsync(MakeRequest(), options, cts.Token).AsTask());
    }
}
