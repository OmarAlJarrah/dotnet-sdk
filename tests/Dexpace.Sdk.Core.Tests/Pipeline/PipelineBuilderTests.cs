// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pipeline;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pipeline;

public class PipelineBuilderTests
{
    // ---------------------------------------------------------------------------
    // Concrete test policy stubs
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Pass-through stub for cardinality / type-not-found tests.
    /// </summary>
    private abstract class StubPolicy(PipelineStage stage) : HttpPipelinePolicy
    {
        public override PipelineStage Stage => stage;

        public override ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation) =>
            continuation.RunAsync(context);
    }

    private sealed class OperationStub() : StubPolicy(PipelineStage.Operation);
    private sealed class RedirectStub() : StubPolicy(PipelineStage.Redirect);
    private sealed class RetryStub() : StubPolicy(PipelineStage.Retry);
    private sealed class AuthStub() : StubPolicy(PipelineStage.Auth);
    private sealed class DiagnosticsStub() : StubPolicy(PipelineStage.Diagnostics);
    private sealed class PerCallStubA() : StubPolicy(PipelineStage.PerCall);
    private sealed class PerAttemptStub() : StubPolicy(PipelineStage.PerAttempt);

    /// <summary>
    /// Recording stub: appends <c>"name:in"</c> before and <c>"name:out"</c> after the continuation.
    /// Used by execution-log tests to verify actual invocation order.
    /// </summary>
    private sealed class RecordingPolicy(string name, PipelineStage stage, List<string> log)
        : HttpPipelinePolicy
    {
        public override PipelineStage Stage => stage;

        public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
        {
            log.Add($"{name}:in");
            await continuation.RunAsync(context).ConfigureAwait(false);
            log.Add($"{name}:out");
        }
    }

    /// <summary>Two distinct PerCall recording types for InsertAfter/InsertBefore/Replace/Remove tests.</summary>
    private sealed class RecordingPerCallA(List<string> log) : HttpPipelinePolicy
    {
        public override PipelineStage Stage => PipelineStage.PerCall;

        public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
        {
            log.Add("A:in");
            await continuation.RunAsync(context).ConfigureAwait(false);
            log.Add("A:out");
        }
    }

    private sealed class RecordingPerCallA2(List<string> log) : HttpPipelinePolicy
    {
        public override PipelineStage Stage => PipelineStage.PerCall;

        public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
        {
            log.Add("A2:in");
            await continuation.RunAsync(context).ConfigureAwait(false);
            log.Add("A2:out");
        }
    }

    private sealed class RecordingPerCallB(List<string> log) : HttpPipelinePolicy
    {
        public override PipelineStage Stage => PipelineStage.PerCall;

        public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
        {
            log.Add("B:in");
            await continuation.RunAsync(context).ConfigureAwait(false);
            log.Add("B:out");
        }
    }

    // ---------------------------------------------------------------------------
    // Fakes / helpers
    // ---------------------------------------------------------------------------

    private sealed class FakeTransport : IAsyncHttpClient
    {
        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Response(Status.Ok));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static FakeTransport MakeTransport() => new();

    private static DexpaceClientOptions MakeOptions() => new();

    private static Request MakeRequest() => Request.Get("https://api.example.com/v1/resource");

    // ---------------------------------------------------------------------------
    // Tests: Stage sort
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Policies added in reverse stage order must execute in ascending stage order
    /// (Operation=100, Redirect=200, Diagnostics=600).
    /// </summary>
    [Fact]
    public async Task Add_StageSortedExecution_PoliciesRunInStageOrder()
    {
        var log = new List<string>();

        // Added in reverse stage order: Diagnostics, Redirect, Operation
        var pipeline = new PipelineBuilder()
            .Add(new RecordingPolicy("diag", PipelineStage.Diagnostics, log))
            .Add(new RecordingPolicy("redirect", PipelineStage.Redirect, log))
            .Add(new RecordingPolicy("op", PipelineStage.Operation, log))
            .Build(MakeTransport());

        await pipeline.SendAsync(MakeRequest(), MakeOptions());

        // Build stable-sorts by stage (ascending), so execution order is:
        // op (100) → redirect (200) → diag (600), then unwind.
        Assert.Equal(
            ["op:in", "redirect:in", "diag:in", "diag:out", "redirect:out", "op:out"],
            log);
    }

    /// <summary>
    /// Multiple policies in the same non-pillar stage must not throw at Build time.
    /// </summary>
    [Fact]
    public void Add_MultiplePoliciesInSameNonPillarStage_DoesNotThrow()
    {
        var pipeline = new PipelineBuilder()
            .Add(new PerCallStubA())
            .Add(new PerCallStubA())
            .Build(MakeTransport());

        Assert.NotNull(pipeline);
    }

    /// <summary>
    /// Two policies in a pillar stage must cause Build to throw with the stage name in the message.
    /// </summary>
    [Fact]
    public void Build_TwoPoliciesInPillarStage_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PipelineBuilder()
                .Add(new RetryStub())
                .Add(new RetryStub())
                .Build(MakeTransport()));

        Assert.Contains("Retry", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------
    // Tests: InsertAfter within a stage
    // ---------------------------------------------------------------------------

    /// <summary>
    /// InsertAfter&lt;A&gt;(B) where A and B share the same stage (PerCall) must produce
    /// execution order [A, B] — Build preserves within-stage list order after the sort.
    /// </summary>
    [Fact]
    public async Task InsertAfter_SameStage_InsertsAfterAnchor()
    {
        var log = new List<string>();

        var pipeline = new PipelineBuilder()
            .Add(new RecordingPerCallA(log))
            .InsertAfter<RecordingPerCallA>(new RecordingPerCallB(log)) // list: [A, B]
            .Build(MakeTransport());

        await pipeline.SendAsync(MakeRequest(), MakeOptions());

        Assert.Equal(["A:in", "B:in", "B:out", "A:out"], log);
    }

    /// <summary>
    /// InsertAfter when the anchor type is absent must throw with the type name in the message.
    /// </summary>
    [Fact]
    public void InsertAfter_TypeNotPresent_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PipelineBuilder()
                .InsertAfter<RetryStub>(new PerCallStubA()));

        Assert.Contains("RetryStub", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------
    // Tests: InsertBefore within a stage
    // ---------------------------------------------------------------------------

    /// <summary>
    /// InsertBefore&lt;A&gt;(B) where A and B share the same stage (PerCall) must produce
    /// execution order [B, A] — Build preserves within-stage list order after the sort.
    /// </summary>
    [Fact]
    public async Task InsertBefore_SameStage_InsertsBeforeAnchor()
    {
        var log = new List<string>();

        var pipeline = new PipelineBuilder()
            .Add(new RecordingPerCallA(log))
            .InsertBefore<RecordingPerCallA>(new RecordingPerCallB(log)) // list: [B, A]
            .Build(MakeTransport());

        await pipeline.SendAsync(MakeRequest(), MakeOptions());

        Assert.Equal(["B:in", "A:in", "A:out", "B:out"], log);
    }

    /// <summary>
    /// InsertBefore when the anchor type is absent must throw with the type name in the message.
    /// </summary>
    [Fact]
    public void InsertBefore_TypeNotPresent_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PipelineBuilder()
                .InsertBefore<RetryStub>(new PerCallStubA()));

        Assert.Contains("RetryStub", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------
    // Tests: Replace
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Replace&lt;A&gt;(A2) where A2 is a distinct PerCall type must put A2 in the execution
    /// log and exclude A.
    /// </summary>
    [Fact]
    public async Task Replace_SubstitutesPolicy_LogContainsReplacementNotOriginal()
    {
        var log = new List<string>();

        var pipeline = new PipelineBuilder()
            .Add(new RecordingPerCallA(log))
            .Replace<RecordingPerCallA>(new RecordingPerCallA2(log)) // A swapped for A2
            .Build(MakeTransport());

        await pipeline.SendAsync(MakeRequest(), MakeOptions());

        Assert.Contains("A2:in", log);
        Assert.DoesNotContain("A:in", log);
    }

    /// <summary>
    /// Replace when the target type is absent must throw with the type name in the message.
    /// </summary>
    [Fact]
    public void Replace_TypeNotPresent_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PipelineBuilder()
                .Replace<RetryStub>(new RetryStub()));

        Assert.Contains("RetryStub", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------
    // Tests: Remove
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Remove&lt;A&gt; with both A and B present must keep B in the execution log and drop A.
    /// </summary>
    [Fact]
    public async Task Remove_RemovesMatchingType_OtherPolicyStillRuns()
    {
        var log = new List<string>();

        var pipeline = new PipelineBuilder()
            .Add(new RecordingPerCallA(log))
            .Add(new RecordingPerCallB(log))
            .Remove<RecordingPerCallA>()
            .Build(MakeTransport());

        await pipeline.SendAsync(MakeRequest(), MakeOptions());

        Assert.Contains("B:in", log);
        Assert.DoesNotContain("A:in", log);
    }

    // ---------------------------------------------------------------------------
    // Tests: Edge cases
    // ---------------------------------------------------------------------------

    /// <summary>
    /// An empty builder must produce a valid pipeline that the transport can service.
    /// </summary>
    [Fact]
    public async Task Build_EmptyPipeline_TransportResponds()
    {
        var pipeline = new PipelineBuilder().Build(MakeTransport());
        var response = await pipeline.SendAsync(MakeRequest(), MakeOptions());
        Assert.NotNull(response);
    }
}
