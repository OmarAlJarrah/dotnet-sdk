// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

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

public sealed class BearerTokenAuthPolicyTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly DateTimeOffset FarFuture =
        new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Request MakeRequest(string url = "https://api.example.com/v1/items")
        => Request.Get(url);

    private static DexpaceClientOptions MakeOptions() => new();

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

    /// <summary>
    /// A <see cref="TokenCredential"/> that returns a canned token and tracks how many times
    /// <see cref="GetTokenAsync"/> was called.
    /// </summary>
    private sealed class FakeTokenCredential(string token) : TokenCredential
    {
        private int _callCount;

        /// <summary>Number of times <see cref="GetTokenAsync"/> was called.</summary>
        public int CallCount => _callCount;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext context,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            return new ValueTask<AccessToken>(new AccessToken(token, FarFuture));
        }
    }

    // -------------------------------------------------------------------------
    // Stage
    // -------------------------------------------------------------------------

    [Fact]
    public void Stage_IsAuth()
    {
        var policy = new BearerTokenAuthPolicy(
            new FakeTokenCredential("tok"),
            "https://api.example.com/.default");

        Assert.Equal(PipelineStage.Auth, policy.Stage);
    }

    // -------------------------------------------------------------------------
    // Bearer token stamping
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_StampsBearerTokenInAuthorizationHeader()
    {
        var credential = new FakeTokenCredential("abc123");
        var transport = new CapturingTransport();
        var pipeline = new PipelineBuilder()
            .Add(new BearerTokenAuthPolicy(credential, "scope1", "scope2"))
            .Build(transport);

        await pipeline.SendAsync(MakeRequest(), MakeOptions());

        var value = transport.LastRequest!.Headers.Get("Authorization");
        Assert.Equal("Bearer abc123", value);
    }

    [Fact]
    public async Task ProcessAsync_ReplacesExistingAuthorizationHeader()
    {
        var credential = new FakeTokenCredential("fresh-token");
        var request = MakeRequest() with
        {
            Headers = Headers.Empty.Set("Authorization", "Bearer old-token")
        };

        var transport = new CapturingTransport();
        var pipeline = new PipelineBuilder()
            .Add(new BearerTokenAuthPolicy(credential))
            .Build(transport);

        await pipeline.SendAsync(request, MakeOptions());

        var values = transport.LastRequest!.Headers.GetAll("Authorization");
        Assert.Single(values);
        Assert.Equal("Bearer fresh-token", values[0]);
    }

    // -------------------------------------------------------------------------
    // Cache reuse — credential called only once across two requests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_CacheReused_CredentialCalledOnceAcrossTwoRequests()
    {
        // Two pipeline sends share the same BearerTokenAuthPolicy instance, so they share
        // the same AccessTokenCache. The token expires far in the future, so the second
        // send must reuse the cached token without calling the credential again.
        var credential = new FakeTokenCredential("shared-token");
        var transport = new CapturingTransport();
        var policy = new BearerTokenAuthPolicy(credential, "read", "write");
        var pipeline = new PipelineBuilder()
            .Add(policy)
            .Build(transport);

        await pipeline.SendAsync(MakeRequest(), MakeOptions());
        var firstValue = transport.LastRequest!.Headers.Get("Authorization");

        await pipeline.SendAsync(MakeRequest(), MakeOptions());
        var secondValue = transport.LastRequest!.Headers.Get("Authorization");

        Assert.Equal("Bearer shared-token", firstValue);
        Assert.Equal("Bearer shared-token", secondValue);
        Assert.Equal(1, credential.CallCount);
    }

    // -------------------------------------------------------------------------
    // Scopes are forwarded to the credential
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ForwardsConfiguredScopesToCredential()
    {
        string[]? capturedScopes = null;
        var credential = new CapturingScopesCredential(
            "scope-token",
            scopes => capturedScopes = scopes);

        var transport = new CapturingTransport();
        var pipeline = new PipelineBuilder()
            .Add(new BearerTokenAuthPolicy(credential, "openid", "profile"))
            .Build(transport);

        await pipeline.SendAsync(MakeRequest(), MakeOptions());

        Assert.NotNull(capturedScopes);
        Assert.Equal(["openid", "profile"], capturedScopes!);
    }

    // -------------------------------------------------------------------------
    // Cross-origin withholding
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_CrossOriginRequest_WithholdsCredential()
    {
        var credential = new FakeTokenCredential("secret-bearer");
        var options = MakeOptions();

        var originalRequest = MakeRequest("https://api.example.com/v1/resource");
        var context = new PipelineContext(originalRequest, options);

        var recordingTransport = new CapturingTransport();
        var recordingRunner = new PipelineRunner([], 0, recordingTransport);
        var policy = new BearerTokenAuthPolicy(credential, "scope");

        // First run: record origin and stamp.
        await policy.ProcessAsync(context, recordingRunner);
        Assert.Equal("Bearer secret-bearer", context.Request.Headers.Get("Authorization"));

        // Simulate cross-origin redirect.
        context.Request = MakeRequest("https://other-service.example.org/callback") with
        {
            Headers = Headers.Empty
        };

        var foreignTransport = new CapturingTransport();
        var foreignRunner = new PipelineRunner([], 0, foreignTransport);

        // Second run on same context: different origin → credential must be withheld.
        await policy.ProcessAsync(context, foreignRunner);
        Assert.Null(context.Request.Headers.Get("Authorization"));
    }

    [Fact]
    public async Task ProcessAsync_SameOriginRerun_StampsBearerAgain()
    {
        var credential = new FakeTokenCredential("retry-bearer");
        var options = MakeOptions();
        var request = MakeRequest("https://api.example.com/v1/resource");
        var context = new PipelineContext(request, options);
        var transport = new CapturingTransport();
        var runner = new PipelineRunner([], 0, transport);
        var policy = new BearerTokenAuthPolicy(credential, "scope");

        await policy.ProcessAsync(context, runner);
        Assert.Equal("Bearer retry-bearer", context.Request.Headers.Get("Authorization"));

        context.Request = context.Request with { Headers = Headers.Empty };

        // Same origin retry: must stamp again.
        await policy.ProcessAsync(context, runner);
        Assert.Equal("Bearer retry-bearer", context.Request.Headers.Get("Authorization"));
    }

    [Fact]
    public async Task ProcessAsync_CrossOriginRequest_StripsStaleAuthorizationHeader()
    {
        // The request carries a stale Authorization header from the original hop.
        // After the policy runs on a cross-origin request, that header must be absent —
        // the foreign origin must never see it, even without RedirectPolicy in the pipeline.
        var credential = new FakeTokenCredential("secret-bearer");
        var options = MakeOptions();

        var originalRequest = MakeRequest("https://api.example.com/v1/resource");
        var context = new PipelineContext(originalRequest, options);

        var recordingTransport = new CapturingTransport();
        var recordingRunner = new PipelineRunner([], 0, recordingTransport);
        var policy = new BearerTokenAuthPolicy(credential, "scope");

        // First run: records origin, stamps header (calls credential once).
        await policy.ProcessAsync(context, recordingRunner);
        Assert.Equal("Bearer secret-bearer", context.Request.Headers.Get("Authorization"));
        var callCountAfterFirstRun = credential.CallCount;

        // Simulate a cross-origin redirect with the stale Authorization header still in place.
        context.Request = MakeRequest("https://other-service.example.org/callback") with
        {
            Headers = Headers.Empty.Set("Authorization", "Bearer secret-bearer")
        };

        var foreignTransport = new CapturingTransport();
        var foreignRunner = new PipelineRunner([], 0, foreignTransport);

        // Second run: different origin → stale Authorization header must be stripped.
        // Crucially, the token cache / credential must NOT be called again.
        await policy.ProcessAsync(context, foreignRunner);

        Assert.Null(context.Request.Headers.Get("Authorization"));
        Assert.Equal(callCountAfterFirstRun, credential.CallCount);
    }

    // -------------------------------------------------------------------------
    // Helper: captures scopes passed to GetTokenAsync
    // -------------------------------------------------------------------------

    private sealed class CapturingScopesCredential(
        string token,
        Action<string[]> onGetToken) : TokenCredential
    {
        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext context,
            CancellationToken ct = default)
        {
            onGetToken([.. context.Scopes]);
            return new ValueTask<AccessToken>(new AccessToken(token, FarFuture));
        }
    }
}
