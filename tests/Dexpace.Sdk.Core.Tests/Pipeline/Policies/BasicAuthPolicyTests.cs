// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Text;
using Dexpace.Sdk.Core.Auth;
using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pipeline;
using Dexpace.Sdk.Core.Pipeline.Policies;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pipeline.Policies;

public sealed class BasicAuthPolicyTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Request MakeRequest(string url = "https://api.example.com/v1/items")
        => Request.Get(url);

    private static DexpaceClientOptions MakeOptions() => new();

    private static string Base64(string user, string pass)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));

    /// <summary>
    /// Captures the last request received and returns a canned 200 OK.
    /// </summary>
    private sealed class CapturingTransport : IAsyncHttpClient
    {
        public Request? LastRequest { get; private set; }

        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new Response(Status.Ok));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Stage
    // -------------------------------------------------------------------------

    [Fact]
    public void Stage_IsAuth()
    {
        var policy = new BasicAuthPolicy(new BasicCredential("user", "pass"));
        Assert.Equal(PipelineStage.Auth, policy.Stage);
    }

    // -------------------------------------------------------------------------
    // Header stamping
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_StampsBasicAuthorizationHeader()
    {
        var credential = new BasicCredential("alice", "s3cr3t");
        var transport = new CapturingTransport();
        var pipeline = new PipelineBuilder()
            .Add(new BasicAuthPolicy(credential))
            .Build(transport);

        await pipeline.SendAsync(MakeRequest(), MakeOptions());

        var value = transport.LastRequest!.Headers.Get("Authorization");
        Assert.Equal($"Basic {Base64("alice", "s3cr3t")}", value);
    }

    [Fact]
    public async Task ProcessAsync_EmptyPassword_StampsCorrectly()
    {
        var credential = new BasicCredential("user", string.Empty);
        var transport = new CapturingTransport();
        var pipeline = new PipelineBuilder()
            .Add(new BasicAuthPolicy(credential))
            .Build(transport);

        await pipeline.SendAsync(MakeRequest(), MakeOptions());

        var value = transport.LastRequest!.Headers.Get("Authorization");
        Assert.Equal($"Basic {Base64("user", "")}", value);
    }

    [Fact]
    public async Task ProcessAsync_ReplacesExistingAuthorizationHeader()
    {
        var credential = new BasicCredential("bob", "hunter2");
        var request = MakeRequest() with
        {
            Headers = Headers.Empty.Set("Authorization", "Bearer old-token")
        };

        var transport = new CapturingTransport();
        var pipeline = new PipelineBuilder()
            .Add(new BasicAuthPolicy(credential))
            .Build(transport);

        await pipeline.SendAsync(request, MakeOptions());

        var values = transport.LastRequest!.Headers.GetAll("Authorization");
        Assert.Single(values);
        Assert.StartsWith("Basic ", values[0], StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Cross-origin withholding
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_CrossOriginRequest_WithholdsCredential()
    {
        var credential = new BasicCredential("user", "pass");
        var options = MakeOptions();

        var originalRequest = MakeRequest("https://api.example.com/v1/resource");
        var context = new PipelineContext(originalRequest, options);

        var recordingTransport = new CapturingTransport();
        var recordingRunner = new PipelineRunner([], 0, recordingTransport);
        var policy = new BasicAuthPolicy(credential);

        // First run: records origin and stamps header.
        await policy.ProcessAsync(context, recordingRunner);
        Assert.NotNull(context.Request.Headers.Get("Authorization"));

        // Simulate cross-origin redirect.
        context.Request = MakeRequest("https://other-service.example.org/callback") with
        {
            Headers = Headers.Empty
        };

        var foreignTransport = new CapturingTransport();
        var foreignRunner = new PipelineRunner([], 0, foreignTransport);

        // Second run: different origin → credential must be withheld.
        await policy.ProcessAsync(context, foreignRunner);
        Assert.Null(context.Request.Headers.Get("Authorization"));
    }

    [Fact]
    public async Task ProcessAsync_SameOriginRerun_StampsCredentialAgain()
    {
        var credential = new BasicCredential("user", "pass");
        var options = MakeOptions();
        var request = MakeRequest("https://api.example.com/v1/resource");
        var context = new PipelineContext(request, options);
        var transport = new CapturingTransport();
        var runner = new PipelineRunner([], 0, transport);
        var policy = new BasicAuthPolicy(credential);

        await policy.ProcessAsync(context, runner);
        Assert.NotNull(context.Request.Headers.Get("Authorization"));

        // Reset the header.
        context.Request = context.Request with { Headers = Headers.Empty };

        // Same origin: must stamp again.
        await policy.ProcessAsync(context, runner);
        Assert.NotNull(context.Request.Headers.Get("Authorization"));
        Assert.StartsWith("Basic ", context.Request.Headers.Get("Authorization"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_CrossOriginRequest_StripsStaleAuthorizationHeader()
    {
        // The request carries a stale Authorization header from the original hop.
        // After the policy runs on a cross-origin request, that header must be absent —
        // the foreign origin must never see it, even without RedirectPolicy in the pipeline.
        var credential = new BasicCredential("user", "pass");
        var options = MakeOptions();

        var originalRequest = MakeRequest("https://api.example.com/v1/resource");
        var context = new PipelineContext(originalRequest, options);

        var recordingTransport = new CapturingTransport();
        var recordingRunner = new PipelineRunner([], 0, recordingTransport);
        var policy = new BasicAuthPolicy(credential);

        // First run: records origin and stamps header.
        await policy.ProcessAsync(context, recordingRunner);
        Assert.NotNull(context.Request.Headers.Get("Authorization"));

        // Simulate a cross-origin redirect with the stale Authorization header still in place.
        context.Request = MakeRequest("https://other-service.example.org/callback") with
        {
            Headers = Headers.Empty.Set("Authorization", $"Basic {Base64("user", "pass")}")
        };

        var foreignTransport = new CapturingTransport();
        var foreignRunner = new PipelineRunner([], 0, foreignTransport);

        // Second run: different origin → stale Authorization header must be stripped.
        await policy.ProcessAsync(context, foreignRunner);

        Assert.Null(context.Request.Headers.Get("Authorization"));
    }
}
