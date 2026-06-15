// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pipeline;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pipeline;

public class HttpPipelineTests
{
    private static Request MakeRequest() =>
        Request.Get("https://api.example.com/v1/resource");

    private static DexpaceClientOptions MakeOptions() => new();

    private sealed class CannedTransport(Response canned) : IAsyncHttpClient
    {
        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default) =>
            Task.FromResult(canned);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task SendAsync_ReturnsTransportResponse()
    {
        var expected = new Response(Status.Ok);
        var pipeline = new PipelineBuilder().Build(new CannedTransport(expected));

        var actual = await pipeline.SendAsync(MakeRequest(), MakeOptions());

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Send_ReturnsTransportResponse()
    {
        var expected = new Response(Status.Ok);
        var pipeline = new PipelineBuilder().Build(new CannedTransport(expected));

        var actual = pipeline.Send(MakeRequest(), MakeOptions());

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task SendAsync_WithPolicies_PoliciesInvokedAndResponseReturned()
    {
        var log = new List<string>();
        var expected = new Response(Status.Ok);

        var pipeline = new PipelineBuilder()
            .Add(new LoggingPolicy("a", PipelineStage.Operation, log))
            .Add(new LoggingPolicy("b", PipelineStage.PerAttempt, log))
            .Build(new CannedTransport(expected));

        var actual = await pipeline.SendAsync(MakeRequest(), MakeOptions());

        Assert.Same(expected, actual);
        Assert.Equal(["a:in", "b:in", "b:out", "a:out"], log);
    }

    private sealed class LoggingPolicy(string name, PipelineStage stage, List<string> log)
        : HttpPipelinePolicy
    {
        public override PipelineStage Stage => stage;

        public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
        {
            log.Add($"{name}:in");
            await continuation.RunAsync(context);
            log.Add($"{name}:out");
        }
    }
}
