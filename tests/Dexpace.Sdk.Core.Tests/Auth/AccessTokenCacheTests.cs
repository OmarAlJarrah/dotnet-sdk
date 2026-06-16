// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Auth;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Auth;

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/// <summary>
/// A fake TokenCredential that counts calls and returns configurable tokens.
/// Thread-safe via Interlocked.
/// </summary>
file sealed class FakeTokenCredential : TokenCredential
{
    private int _callCount;
    private Func<TokenRequestContext, AccessToken> _factory;

    public int CallCount => _callCount;

    public FakeTokenCredential(Func<TokenRequestContext, AccessToken> factory)
        => _factory = factory;

    /// <summary>Replace the factory (used to inject failures between calls).</summary>
    public void SetFactory(Func<TokenRequestContext, AccessToken> factory) => _factory = factory;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        return ValueTask.FromResult(_factory(context));
    }
}

/// <summary>
/// A fake TokenCredential that throws on demand.
/// </summary>
file sealed class ThrowingTokenCredential : TokenCredential
{
    private readonly Exception _ex;

    public ThrowingTokenCredential(Exception ex) => _ex = ex;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct = default)
        => throw _ex;
}

/// <summary>
/// A controllable TimeProvider for deterministic time tests.
/// </summary>
file sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset initial) => _now = initial;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    public void SetUtcNow(DateTimeOffset value) => _now = value;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class AccessTokenCacheTests
{
    private static TokenRequestContext Ctx(string scope = "scope") =>
        new TokenRequestContext([scope]);

    private static DateTimeOffset Now() => DateTimeOffset.UtcNow;

    // -----------------------------------------------------------------------
    // 1. Repeated GetAsync within validity calls the credential ONCE
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_WithinValidity_ReturnsTokenWithoutRefetch()
    {
        var time = new ManualTimeProvider(Now());
        var expected = new AccessToken("tok", time.GetUtcNow().AddHours(1));
        var cred = new FakeTokenCredential(_ => expected);
        var cache = new AccessTokenCache(cred, time);
        var ctx = Ctx();

        var t1 = await cache.GetAsync(ctx);
        var t2 = await cache.GetAsync(ctx);
        var t3 = await cache.GetAsync(ctx);

        Assert.Equal(expected.Token, t1.Token);
        Assert.Equal(expected.Token, t2.Token);
        Assert.Equal(expected.Token, t3.Token);
        Assert.Equal(1, cred.CallCount);
    }

    // -----------------------------------------------------------------------
    // 2. Advancing past RefreshOn triggers exactly one refresh
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_PastRefreshOn_RefreshesOnce()
    {
        var start = Now();
        var time = new ManualTimeProvider(start);

        // First token: expires in 1h, refresh hint at 50m
        var firstToken = new AccessToken("first", start.AddHours(1), start.AddMinutes(50));
        var secondToken = new AccessToken("second", start.AddHours(2));
        var callCount = 0;
        var cred = new FakeTokenCredential(_ =>
        {
            callCount++;
            return callCount == 1 ? firstToken : secondToken;
        });

        var cache = new AccessTokenCache(cred, time);
        var ctx = Ctx();

        // Call within validity window — gets first token
        var t1 = await cache.GetAsync(ctx);
        Assert.Equal("first", t1.Token);
        Assert.Equal(1, callCount);

        // Advance past RefreshOn but still before ExpiresOn
        time.Advance(TimeSpan.FromMinutes(51));

        var t2 = await cache.GetAsync(ctx);
        Assert.Equal("second", t2.Token);
        Assert.Equal(2, callCount);

        // Further calls still use refreshed token — no additional fetches
        var t3 = await cache.GetAsync(ctx);
        Assert.Equal("second", t3.Token);
        Assert.Equal(2, callCount);
    }

    // -----------------------------------------------------------------------
    // 3. Advancing past ExpiresOn triggers a refresh
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_PastExpiresOn_RefreshesToken()
    {
        var start = Now();
        var time = new ManualTimeProvider(start);

        var firstToken = new AccessToken("expired", start.AddMinutes(10));
        var secondToken = new AccessToken("fresh", start.AddHours(2));
        var callCount = 0;
        var cred = new FakeTokenCredential(_ =>
        {
            callCount++;
            return callCount == 1 ? firstToken : secondToken;
        });

        var cache = new AccessTokenCache(cred, time);
        var ctx = Ctx();

        var t1 = await cache.GetAsync(ctx);
        Assert.Equal("expired", t1.Token);

        // Jump past ExpiresOn
        time.Advance(TimeSpan.FromMinutes(11));

        var t2 = await cache.GetAsync(ctx);
        Assert.Equal("fresh", t2.Token);
        Assert.Equal(2, callCount);
    }

    // -----------------------------------------------------------------------
    // 4. Concurrent GetAsync calls result in a single credential call (single-flight)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_Concurrent_SingleFlightRefresh()
    {
        var time = new ManualTimeProvider(Now());

        // Use a TaskCompletionSource to make the credential artificially slow so
        // all concurrent callers arrive before any returns.
        var gate = new TaskCompletionSource<AccessToken>(TaskCreationOptions.RunContinuationsAsynchronously);

        var slowCred = new SlowTokenCredential(gate);
        var cache = new AccessTokenCache(slowCred, time);
        var ctx = Ctx();

        // Start 50 concurrent requests before any token is available
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => cache.GetAsync(ctx).AsTask())
            .ToArray();

        // Let the credential complete
        var token = new AccessToken("concurrent_tok", time.GetUtcNow().AddHours(1));
        gate.SetResult(token);

        var results = await Task.WhenAll(tasks);

        // All callers get the same token
        Assert.All(results, r => Assert.Equal("concurrent_tok", r.Token));
        // Credential was called exactly once
        Assert.Equal(1, slowCred.CallCount);
    }

    // -----------------------------------------------------------------------
    // 5. Refresh that throws while valid returns cached token
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_RefreshThrows_WhileStillValid_ReturnsCachedToken()
    {
        var start = Now();
        var time = new ManualTimeProvider(start);

        // Token: expires in 1h, refresh hint at 50m
        var validToken = new AccessToken("valid", start.AddHours(1), start.AddMinutes(50));
        var callCount = 0;
        var cred = new FakeTokenCredential(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return validToken;
            }

            throw new InvalidOperationException("Credential failure");
        });

        var cache = new AccessTokenCache(cred, time);
        var ctx = Ctx();

        // Populate cache
        var t1 = await cache.GetAsync(ctx);
        Assert.Equal("valid", t1.Token);

        // Advance past RefreshOn — triggers a refresh attempt
        time.Advance(TimeSpan.FromMinutes(51));

        // Despite the throw, should return the still-valid cached token
        var t2 = await cache.GetAsync(ctx);
        Assert.Equal("valid", t2.Token);

        // Credential was called for the failed refresh
        Assert.Equal(2, callCount);
    }

    // -----------------------------------------------------------------------
    // 6. Refresh that throws with no valid token propagates
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_RefreshThrows_WithNoValidToken_Propagates()
    {
        var time = new ManualTimeProvider(Now());
        var ex = new InvalidOperationException("no token");
        var cred = new ThrowingTokenCredential(ex);
        var cache = new AccessTokenCache(cred, time);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetAsync(Ctx()).AsTask());

        Assert.Same(ex, thrown);
    }

    // -----------------------------------------------------------------------
    // 7. Different contexts are cached independently
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_DifferentContexts_CachedIndependently()
    {
        var time = new ManualTimeProvider(Now());
        var callCount = 0;
        var cred = new FakeTokenCredential(ctx =>
        {
            callCount++;
            return new AccessToken($"tok-{ctx.Scopes[0]}", time.GetUtcNow().AddHours(1));
        });

        var cache = new AccessTokenCache(cred, time);

        var t1 = await cache.GetAsync(Ctx("scope1"));
        var t2 = await cache.GetAsync(Ctx("scope2"));
        var t3 = await cache.GetAsync(Ctx("scope1")); // should hit cache

        Assert.Equal("tok-scope1", t1.Token);
        Assert.Equal("tok-scope2", t2.Token);
        Assert.Equal("tok-scope1", t3.Token);
        // scope1 fetched once, scope2 fetched once = 2 calls
        Assert.Equal(2, callCount);
    }
}

// Helper that cannot easily be expressed as a lambda:

file sealed class SlowTokenCredential : TokenCredential
{
    private readonly TaskCompletionSource<AccessToken> _gate;
    private int _callCount;

    public int CallCount => _callCount;

    public SlowTokenCredential(TaskCompletionSource<AccessToken> gate)
        => _gate = gate;

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        return await _gate.Task.ConfigureAwait(false);
    }
}
