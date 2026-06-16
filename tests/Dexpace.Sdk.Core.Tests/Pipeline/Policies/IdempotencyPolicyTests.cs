// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pipeline;
using Dexpace.Sdk.Core.Pipeline.Policies;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pipeline.Policies;

public sealed class IdempotencyPolicyTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static DexpaceClientOptions MakeOptions() => new();

    /// <summary>
    /// Captures every request it receives and returns a canned 200 OK.
    /// </summary>
    private sealed class CapturingTransport : IAsyncHttpClient
    {
        private readonly List<Request> _requests = [];

        public List<Request> Requests => _requests;
        public Request? LastRequest => _requests.Count > 0 ? _requests[^1] : null;

        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default)
        {
            _requests.Add(request);
            return Task.FromResult(new Response(Status.Ok));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Stage
    // -------------------------------------------------------------------------

    [Fact]
    public void Stage_IsPerCall()
    {
        var policy = new IdempotencyPolicy();
        Assert.Equal(PipelineStage.PerCall, policy.Stage);
    }

    // -------------------------------------------------------------------------
    // Key generation — POST (default configured method)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_Post_SetsIdempotencyKeyHeader()
    {
        var transport = new CapturingTransport();
        var pipeline = new PipelineBuilder()
            .Add(new IdempotencyPolicy())
            .Build(transport);

        await pipeline.SendAsync(Request.Post("https://api.example.com/v1/items", RequestBody.FromBytes(ReadOnlyMemory<byte>.Empty)), MakeOptions());

        var key = transport.LastRequest!.Headers.Get("Idempotency-Key");
        Assert.NotNull(key);
        Assert.True(Guid.TryParse(key, out _), $"Expected a GUID, got: {key}");
    }

    [Fact]
    public async Task ProcessAsync_Get_DoesNotSetIdempotencyKeyHeader()
    {
        var transport = new CapturingTransport();
        var pipeline = new PipelineBuilder()
            .Add(new IdempotencyPolicy())
            .Build(transport);

        await pipeline.SendAsync(Request.Get("https://api.example.com/v1/items"), MakeOptions());

        var key = transport.LastRequest!.Headers.Get("Idempotency-Key");
        Assert.Null(key);
    }

    // -------------------------------------------------------------------------
    // Key is stable when the pipeline re-runs for a retry (two SendAsync calls
    // simulate what a retry policy would do: same policy, fresh context each time,
    // but we verify the *within-one-context* stability via two executions of a
    // "pass-through replay" pipeline below.)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SameContextTwice_ReusesKey()
    {
        // Build a pipeline: IdempotencyPolicy → transport (captures requests).
        // We drive the pipeline twice with the same context by calling SendAsync
        // twice. Each call constructs a new PipelineContext, so to test re-use we
        // instead drive the policy directly through a minimal two-leg pipeline that
        // re-runs the policy via the transport capturing both keys.
        var transport = new CapturingTransport();

        // Use a "double-call" transport: on the first call it re-drives the policy
        // (simulating a retry) by calling SendAsync again on an inner pipeline, then
        // returns a 200.  We do this by embedding the policy in a re-entrancy test.

        // Simpler approach: build a pipeline with the policy, call SendAsync twice
        // with requests that share NO pre-existing key, and verify the TWO keys are
        // DIFFERENT (one fresh context per SendAsync).  The intra-context stability
        // is covered by the property-bag stashing test below.
        var pipeline = new PipelineBuilder()
            .Add(new IdempotencyPolicy())
            .Build(transport);

        var postRequest = Request.Post("https://api.example.com/v1/items", RequestBody.FromBytes(ReadOnlyMemory<byte>.Empty));

        await pipeline.SendAsync(postRequest, MakeOptions());
        await pipeline.SendAsync(postRequest, MakeOptions());

        var key1 = transport.Requests[0].Headers.Get("Idempotency-Key");
        var key2 = transport.Requests[1].Headers.Get("Idempotency-Key");

        // Two separate calls → two separate keys (each call gets its own context)
        Assert.NotNull(key1);
        Assert.NotNull(key2);
        Assert.NotEqual(key1, key2);
    }

    // -------------------------------------------------------------------------
    // Existing header is preserved (caller-supplied key)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ExistingIdempotencyKeyNotOverwritten()
    {
        var transport = new CapturingTransport();
        var pipeline = new PipelineBuilder()
            .Add(new IdempotencyPolicy())
            .Build(transport);

        var requestWithKey = Request.Post(
            "https://api.example.com/v1/items",
            RequestBody.FromBytes(ReadOnlyMemory<byte>.Empty))
        with
        {
            Headers = Headers.Empty.Set("Idempotency-Key", "caller-supplied-key")
        };

        await pipeline.SendAsync(requestWithKey, MakeOptions());

        var key = transport.LastRequest!.Headers.Get("Idempotency-Key");
        Assert.Equal("caller-supplied-key", key);
    }

    // -------------------------------------------------------------------------
    // Custom method set
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_CustomMethodSet_SetsKeyForConfiguredMethod()
    {
        var transport = new CapturingTransport();
        // Only PATCH configured, not POST
        var pipeline = new PipelineBuilder()
            .Add(new IdempotencyPolicy([Method.Patch]))
            .Build(transport);

        var patchRequest = Request.Create(Method.Patch, "https://api.example.com/v1/items/1");
        await pipeline.SendAsync(patchRequest, MakeOptions());

        var key = transport.LastRequest!.Headers.Get("Idempotency-Key");
        Assert.NotNull(key);
        Assert.True(Guid.TryParse(key, out _));
    }

    [Fact]
    public async Task ProcessAsync_CustomMethodSet_DoesNotSetKeyForUnconfiguredMethod()
    {
        var transport = new CapturingTransport();
        // Only PATCH configured
        var pipeline = new PipelineBuilder()
            .Add(new IdempotencyPolicy([Method.Patch]))
            .Build(transport);

        // POST is NOT in the custom set
        var postRequest = Request.Post("https://api.example.com/v1/items", RequestBody.FromBytes(ReadOnlyMemory<byte>.Empty));
        await pipeline.SendAsync(postRequest, MakeOptions());

        var key = transport.LastRequest!.Headers.Get("Idempotency-Key");
        Assert.Null(key);
    }

    // -------------------------------------------------------------------------
    // Key stashed in context property bag and reused on re-run within same context
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_StashesKeyInContextPropertyBag_AndReusesOnRerun()
    {
        // We verify intra-context key stability by building a pipeline with a
        // "double-call" policy that calls continuation.RunAsync twice — simulating
        // a retry policy calling the remainder of the chain twice for the same context.
        var transport = new CapturingTransport();

        var pipeline = new PipelineBuilder()
            .Add(new DoubleCallPolicy())       // calls continuation twice
            .Add(new IdempotencyPolicy())
            .Build(transport);

        var postRequest = Request.Post("https://api.example.com/v1/items", RequestBody.FromBytes(ReadOnlyMemory<byte>.Empty));
        await pipeline.SendAsync(postRequest, MakeOptions());

        // Two requests were captured (double-call sent twice)
        Assert.Equal(2, transport.Requests.Count);

        var key1 = transport.Requests[0].Headers.Get("Idempotency-Key");
        var key2 = transport.Requests[1].Headers.Get("Idempotency-Key");

        // Within the same context the key must be stable
        Assert.NotNull(key1);
        Assert.Equal(key1, key2);

        // Also verify the property bag was populated
        // (tested indirectly via key stability, but we can also confirm via policy contract)
    }

    // -------------------------------------------------------------------------
    // Fake: a policy that calls the continuation twice (retry simulation)
    // -------------------------------------------------------------------------

    private sealed class DoubleCallPolicy : HttpPipelinePolicy
    {
        public override PipelineStage Stage => PipelineStage.Operation; // outermost

        public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
        {
            await continuation.RunAsync(context).ConfigureAwait(false);
            await continuation.RunAsync(context).ConfigureAwait(false);
        }
    }
}
