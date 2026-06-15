// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Pipeline;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pipeline;

public class PipelineContextTests
{
    private static Request MakeRequest() =>
        Request.Get("https://api.example.com/v1/resource");

    [Fact]
    public void Constructor_StoresRequest()
    {
        var request = MakeRequest();
        var options = new DexpaceClientOptions();
        var ctx = new PipelineContext(request, options);

        Assert.Same(request, ctx.Request);
    }

    [Fact]
    public void Constructor_StoresOptions()
    {
        var options = new DexpaceClientOptions { UserAgent = "test-agent/1.0" };
        var ctx = new PipelineContext(MakeRequest(), options);

        Assert.Same(options, ctx.Options);
    }

    [Fact]
    public void Constructor_StoresCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var ctx = new PipelineContext(MakeRequest(), new DexpaceClientOptions(), cts.Token);

        Assert.Equal(cts.Token, ctx.CancellationToken);
    }

    [Fact]
    public void Constructor_DefaultCancellationToken_IsDefault()
    {
        var ctx = new PipelineContext(MakeRequest(), new DexpaceClientOptions());

        Assert.Equal(CancellationToken.None, ctx.CancellationToken);
    }

    [Fact]
    public void AttemptNumber_DefaultsToZero()
    {
        var ctx = new PipelineContext(MakeRequest(), new DexpaceClientOptions());

        Assert.Equal(0, ctx.AttemptNumber);
    }

    [Fact]
    public void Response_DefaultsToNull()
    {
        var ctx = new PipelineContext(MakeRequest(), new DexpaceClientOptions());

        Assert.Null(ctx.Response);
    }

    [Fact]
    public void Activity_DefaultsToNull()
    {
        var ctx = new PipelineContext(MakeRequest(), new DexpaceClientOptions());

        Assert.Null(ctx.Activity);
    }

    [Fact]
    public void PropertyBag_RoundTrips_TypedValue()
    {
        var ctx = new PipelineContext(MakeRequest(), new DexpaceClientOptions());
        ctx.SetProperty("idempotency-key", "idem-abc123");
        var retrieved = ctx.GetProperty<string>("idempotency-key");

        Assert.Equal("idem-abc123", retrieved);
    }

    [Fact]
    public void PropertyBag_MissingKey_ReturnsDefault()
    {
        var ctx = new PipelineContext(MakeRequest(), new DexpaceClientOptions());
        var result = ctx.GetProperty<int>("nonexistent");

        Assert.Equal(0, result);
    }

    [Fact]
    public void PropertyBag_MissingKeyForReferenceType_ReturnsNull()
    {
        var ctx = new PipelineContext(MakeRequest(), new DexpaceClientOptions());
        var result = ctx.GetProperty<string>("nonexistent");

        Assert.Null(result);
    }
}
