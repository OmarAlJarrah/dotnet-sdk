// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Diagnostics;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Diagnostics;

public class UrlRedactorTests
{
    // Use the default-set instance for most tests.
    private static readonly UrlRedactor DefaultRedactor = new();

    [Fact]
    public void Redact_UserInfo_IsStripped()
    {
        var uri = new Uri("https://user:secret@api.example.com/path");
        var result = DefaultRedactor.Redact(uri);

        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("secret", result);
        Assert.Contains("api.example.com", result);
    }

    [Fact]
    public void Redact_SensitiveQueryParam_ValueIsReplaced()
    {
        var uri = new Uri("https://api.example.com/v1/items?access_token=super-secret&page=2");
        var result = DefaultRedactor.Redact(uri);

        Assert.Contains("access_token=REDACTED", result);
        Assert.Contains("page=2", result);
        Assert.DoesNotContain("super-secret", result);
    }

    [Fact]
    public void Redact_NonSensitiveQueryParam_IsPreserved()
    {
        var uri = new Uri("https://api.example.com/search?q=hello&lang=en");
        var result = DefaultRedactor.Redact(uri);

        Assert.Contains("q=hello", result);
        Assert.Contains("lang=en", result);
    }

    [Fact]
    public void Redact_SensitiveParamCheck_IsCaseInsensitive()
    {
        var uri = new Uri("https://api.example.com/v1?API_KEY=abc123");
        var result = DefaultRedactor.Redact(uri);

        Assert.Contains("API_KEY=REDACTED", result);
        Assert.DoesNotContain("abc123", result);
    }

    [Fact]
    public void Redact_NoQueryString_ReturnsSafeUrl()
    {
        var uri = new Uri("https://api.example.com/v1/resource");
        var result = DefaultRedactor.Redact(uri);

        Assert.Equal("https://api.example.com/v1/resource", result);
    }

    [Fact]
    public void Redact_CustomSensitiveParams_AreRedacted()
    {
        var redactor = new UrlRedactor(["x-custom-secret"]);
        var uri = new Uri("https://api.example.com/?x-custom-secret=mysecret&other=value");
        var result = redactor.Redact(uri);

        Assert.Contains("x-custom-secret=REDACTED", result);
        Assert.Contains("other=value", result);
    }

    // ── Edge-case tests added by code-review hardening ────────────────────────

    [Fact]
    public void Redact_RelativeUri_SensitiveQueryParamIsRedacted_NoException()
    {
        // A relative URI carrying a sensitive query param must not throw and must not leak.
        var uri = new Uri("/v1/items?token=super-secret&page=2", UriKind.Relative);
        var result = DefaultRedactor.Redact(uri);

        Assert.Contains("token=REDACTED", result);
        Assert.DoesNotContain("super-secret", result);
        Assert.Contains("page=2", result);
        Assert.Contains("/v1/items", result);
    }

    [Fact]
    public void Redact_Fragment_IsDropped()
    {
        // Fragments should be stripped from absolute URIs.
        var uri = new Uri("https://api.example.com/path?q=hello#section");
        var result = DefaultRedactor.Redact(uri);

        Assert.DoesNotContain("#section", result);
        Assert.Contains("q=hello", result);
    }

    [Fact]
    public void Redact_RelativeUri_Fragment_IsDropped()
    {
        // Fragments should be stripped from relative URIs too.
        var uri = new Uri("/path?q=hello#section", UriKind.Relative);
        var result = DefaultRedactor.Redact(uri);

        Assert.DoesNotContain("#section", result);
        Assert.Contains("q=hello", result);
    }

    [Fact]
    public void Redact_ValuelessParam_DoesNotCrash_AndSensitiveParamIsStillRedacted()
    {
        // "?flag" has no '=' so it is skipped (no value to leak); token must still be redacted.
        var uri = new Uri("https://api.example.com/v1?flag&token=x");
        var result = DefaultRedactor.Redact(uri);

        // The bare flag is dropped (no value — safe to omit).
        Assert.DoesNotContain("flag=", result);
        // The sensitive token value must not appear.
        Assert.Contains("token=REDACTED", result);
        Assert.DoesNotContain("=x", result);
    }

    [Fact]
    public void Redact_RepeatedSensitiveParam_BothValuesAreRedacted()
    {
        // Both occurrences of a repeated sensitive param must be redacted.
        var uri = new Uri("https://api.example.com/v1?token=A&token=B");
        var result = DefaultRedactor.Redact(uri);

        Assert.DoesNotContain("=A", result);
        Assert.DoesNotContain("=B", result);
        // Both occurrences of the key should appear, both redacted.
        Assert.Equal(2, result.Split("token=REDACTED").Length - 1);
    }

    [Fact]
    public void Redact_PercentEncodedSensitiveValue_IsRedacted()
    {
        // A percent-encoded sensitive value must still be caught and redacted.
        var uri = new Uri("https://api.example.com/v1?token=my%2Fsecret%3Dvalue");
        var result = DefaultRedactor.Redact(uri);

        Assert.Contains("token=REDACTED", result);
        Assert.DoesNotContain("secret", result);
    }

    [Fact]
    public void Redact_PathSegmentThatLooksSecret_IsPreservedVerbatim()
    {
        // Path components are NOT inspected — secrets in the path are the caller's responsibility.
        // This test documents the boundary: path is preserved, no redaction is applied there.
        var uri = new Uri("https://api.example.com/token/super-secret-value?page=1");
        var result = DefaultRedactor.Redact(uri);

        Assert.Contains("/token/super-secret-value", result);
        Assert.Contains("page=1", result);
    }
}
