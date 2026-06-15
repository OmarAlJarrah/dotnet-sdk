// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Text;
using Dexpace.Sdk.Core.Errors;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Serialization;
using Xunit;

namespace Dexpace.Sdk.Serialization.SystemTextJson.Tests;

public sealed class BodyConvenienceTests
{
    private static SystemTextJsonSerde Serde() => new(TestJsonContext.Default);

    [Fact]
    public async Task FromValue_produces_replayable_json_body()
    {
        var body = RequestBody.FromValue(new Widget("gear", 9), Serde());

        Assert.True(body.IsReplayable);
        Assert.Equal(CommonMediaTypes.ApplicationJsonUtf8, body.ContentType);

        using var first = new MemoryStream();
        await body.WriteToAsync(first);
        using var second = new MemoryStream();
        await body.WriteToAsync(second);
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public async Task ReadValueAsync_deserializes_the_body()
    {
        var json = Encoding.UTF8.GetBytes("""{"Name":"bolt","Size":3}""");
        var body = ResponseBody.FromBytes(json, CommonMediaTypes.ApplicationJson);

        var widget = await body.ReadValueAsync<Widget>(Serde());

        Assert.Equal(new Widget("bolt", 3), widget);
    }

    [Fact]
    public async Task ReadValueAsync_is_single_use()
    {
        var json = Encoding.UTF8.GetBytes("""{"Name":"bolt","Size":3}""");
        var body = ResponseBody.FromBytes(json, CommonMediaTypes.ApplicationJson);

        await body.ReadValueAsync<Widget>(Serde());
        await Assert.ThrowsAsync<StreamConsumedException>(
            async () => await body.ReadValueAsync<Widget>(Serde()));
    }

    [Fact]
    public async Task GetErrorAsync_deserializes_the_buffered_error_body()
    {
        var json = Encoding.UTF8.GetBytes("""{"Code":"rate_limited","Message":"slow down"}""");
        var response = new Response(Status.TooManyRequests, Headers.Empty,
            ResponseBody.FromBytes(json, CommonMediaTypes.ApplicationJson));
        var ex = new HttpResponseException(response);

        var error = await ex.GetErrorAsync<ApiError>(Serde());

        Assert.Equal(new ApiError("rate_limited", "slow down"), error);
    }

    [Fact]
    public async Task GetErrorAsync_throws_when_body_already_consumed()
    {
        var json = Encoding.UTF8.GetBytes("""{"Code":"x","Message":"y"}""");
        var response = new Response(Status.BadRequest, Headers.Empty,
            ResponseBody.FromBytes(json, CommonMediaTypes.ApplicationJson));
        var ex = new HttpResponseException(response);

        await ex.GetErrorAsync<ApiError>(Serde());
        await Assert.ThrowsAsync<ResponseNotReadException>(
            async () => await ex.GetErrorAsync<ApiError>(Serde()));
    }
}
