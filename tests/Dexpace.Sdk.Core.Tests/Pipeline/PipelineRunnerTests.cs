// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pipeline;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pipeline;

public class PipelineRunnerTests
{
    private static Request MakeRequest() =>
        Request.Get("https://api.example.com/v1/resource");

    private static PipelineContext MakeContext() =>
        new(MakeRequest(), new DexpaceClientOptions());

    // ---------------------------------------------------------------------------
    // Test fakes
    // ---------------------------------------------------------------------------

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

    private sealed class FakeTransport : IAsyncHttpClient
    {
        private readonly Response _canned;

        public FakeTransport(Response? canned = null) =>
            _canned = canned ?? new Response(Status.Ok);

        public int InvocationCount { get; private set; }

        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(_canned);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutionOrder_PoliciesRunInStageOrderInAndReversedOut_TransportInvokedOnce()
    {
        var log = new List<string>();
        var transport = new FakeTransport();

        // a = Operation (100), b = PerAttempt (400) — stage ordering: a before b
        var policies = new HttpPipelinePolicy[]
        {
            new RecordingPolicy("a", PipelineStage.Operation, log),
            new RecordingPolicy("b", PipelineStage.PerAttempt, log),
        };

        var runner = new PipelineRunner(policies, 0, transport);
        var context = MakeContext();
        await runner.RunAsync(context);

        Assert.Equal(["a:in", "b:in", "b:out", "a:out"], log);
        Assert.Equal(1, transport.InvocationCount);
    }

    [Fact]
    public async Task Reentrancy_PolicyCallingNextTwice_TransportInvokedTwice()
    {
        var transport = new FakeTransport();
        var doubleCallPolicy = new DoubleDipPolicy();
        var policies = new HttpPipelinePolicy[] { doubleCallPolicy };

        var runner = new PipelineRunner(policies, 0, transport);
        var context = MakeContext();
        await runner.RunAsync(context);

        Assert.Equal(2, transport.InvocationCount);
    }

    private sealed class DoubleDipPolicy : HttpPipelinePolicy
    {
        public override PipelineStage Stage => PipelineStage.PerAttempt;

        public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
        {
            await continuation.RunAsync(context).ConfigureAwait(false);
            await continuation.RunAsync(context).ConfigureAwait(false);
        }
    }
}
