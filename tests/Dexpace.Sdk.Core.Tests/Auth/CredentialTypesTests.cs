// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Auth;
using Dexpace.Sdk.Core.Http.Common;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Auth;

public class AccessTokenTests
{
    [Fact]
    public void Ctor_SetsAllProperties()
    {
        var token = "tok_abc";
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        var refresh = DateTimeOffset.UtcNow.AddMinutes(50);

        var at = new AccessToken(token, expires, refresh);

        Assert.Equal(token, at.Token);
        Assert.Equal(expires, at.ExpiresOn);
        Assert.Equal(refresh, at.RefreshOn);
    }

    [Fact]
    public void Ctor_NullableRefreshOn_IsNull_WhenOmitted()
    {
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        var at = new AccessToken("tok", expires);

        Assert.Null(at.RefreshOn);
    }

    [Fact]
    public void Ctor_ThrowsOnNullToken()
    {
        Assert.Throws<ArgumentNullException>(() => new AccessToken(null!, DateTimeOffset.UtcNow));
    }
}

public class TokenRequestContextTests
{
    [Fact]
    public void Ctor_SetsScopes()
    {
        IReadOnlyList<string> scopes = ["https://example.com/.default"];
        var ctx = new TokenRequestContext(scopes);

        Assert.Same(scopes, ctx.Scopes);
        Assert.Null(ctx.Claims);
    }

    [Fact]
    public void Ctor_SetsScopesAndClaims()
    {
        IReadOnlyList<string> scopes = ["scope1", "scope2"];
        var ctx = new TokenRequestContext(scopes, "some-claims");

        Assert.Equal(scopes, ctx.Scopes);
        Assert.Equal("some-claims", ctx.Claims);
    }

    [Fact]
    public void CacheKey_IsSameForSameInputs()
    {
        IReadOnlyList<string> scopes = ["a", "b"];
        var ctx1 = new TokenRequestContext(scopes, "claims");
        var ctx2 = new TokenRequestContext(scopes, "claims");

        Assert.Equal(ctx1.CacheKey, ctx2.CacheKey);
    }

    [Fact]
    public void CacheKey_DiffersForDifferentScopes()
    {
        var ctx1 = new TokenRequestContext(["scopeA"]);
        var ctx2 = new TokenRequestContext(["scopeB"]);

        Assert.NotEqual(ctx1.CacheKey, ctx2.CacheKey);
    }

    [Fact]
    public void CacheKey_DiffersWhenClaimsChange()
    {
        IReadOnlyList<string> scopes = ["s"];
        var ctx1 = new TokenRequestContext(scopes);
        var ctx2 = new TokenRequestContext(scopes, "extra");

        Assert.NotEqual(ctx1.CacheKey, ctx2.CacheKey);
    }

    [Fact]
    public void CacheKey_StableForSameScopesInSameOrder()
    {
        var ctx1 = new TokenRequestContext(["x", "y"]);
        var ctx2 = new TokenRequestContext(["x", "y"]);

        Assert.Equal(ctx1.CacheKey, ctx2.CacheKey);
    }

    [Fact]
    public void Ctor_ThrowsOnNullScopes()
    {
        Assert.Throws<ArgumentNullException>(() => new TokenRequestContext(null!));
    }
}

public class TokenCredentialTests
{
    private sealed class ConstantTokenCredential : TokenCredential
    {
        private readonly AccessToken _token;

        public ConstantTokenCredential(AccessToken token) => _token = token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct = default)
            => ValueTask.FromResult(_token);
    }

    [Fact]
    public async Task GetTokenAsync_ReturnsExpectedToken()
    {
        var expected = new AccessToken("async_tok", DateTimeOffset.UtcNow.AddHours(1));
        var cred = new ConstantTokenCredential(expected);
        var ctx = new TokenRequestContext(["scope"]);

        var actual = await cred.GetTokenAsync(ctx);

        Assert.Equal(expected.Token, actual.Token);
    }

    [Fact]
    public void GetToken_SyncBridge_ReturnsSameAsAsync()
    {
        var expected = new AccessToken("sync_tok", DateTimeOffset.UtcNow.AddHours(1));
        var cred = new ConstantTokenCredential(expected);
        var ctx = new TokenRequestContext(["scope"]);

        var actual = cred.GetToken(ctx);

        Assert.Equal(expected.Token, actual.Token);
    }
}

public class ApiKeyCredentialTests
{
    [Fact]
    public void Ctor_DefaultsToAuthorizationHeader_AndNoScheme()
    {
        var cred = new ApiKeyCredential("my-key");

        Assert.Equal("my-key", cred.Key);
        Assert.Equal(HttpHeaderName.WellKnown.Authorization, cred.HeaderName);
        Assert.Null(cred.Scheme);
    }

    [Fact]
    public void Ctor_CustomHeader_AndScheme()
    {
        var header = HttpHeaderName.Of("X-Api-Key");
        var cred = new ApiKeyCredential("k", header, "Bearer");

        Assert.Equal("k", cred.Key);
        Assert.Equal(header, cred.HeaderName);
        Assert.Equal("Bearer", cred.Scheme);
    }

    [Fact]
    public void Ctor_ThrowsOnNullKey()
    {
        Assert.Throws<ArgumentNullException>(() => new ApiKeyCredential(null!));
    }

    [Fact]
    public void Ctor_ThrowsOnEmptyKey()
    {
        Assert.Throws<ArgumentException>(() => new ApiKeyCredential(string.Empty));
    }
}

public class BasicCredentialTests
{
    [Fact]
    public void Ctor_SetsUsernameAndPassword()
    {
        var cred = new BasicCredential("user", "pass");

        Assert.Equal("user", cred.Username);
        Assert.Equal("pass", cred.Password);
    }

    [Fact]
    public void Ctor_ThrowsOnNullUsername()
    {
        Assert.Throws<ArgumentNullException>(() => new BasicCredential(null!, "p"));
    }

    [Fact]
    public void Ctor_ThrowsOnNullPassword()
    {
        Assert.Throws<ArgumentNullException>(() => new BasicCredential("u", null!));
    }

    [Fact]
    public void ToBase64_ProducesCorrectEncoding()
    {
        var cred = new BasicCredential("user", "pass");
        var expected = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:pass"));

        Assert.Equal(expected, cred.ToBase64());
    }

    [Fact]
    public void ToBase64_HandlesSpecialChars()
    {
        var cred = new BasicCredential("user@example.com", "p@ss:word");
        var expected = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user@example.com:p@ss:word"));

        Assert.Equal(expected, cred.ToBase64());
    }
}
