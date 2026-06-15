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
}
