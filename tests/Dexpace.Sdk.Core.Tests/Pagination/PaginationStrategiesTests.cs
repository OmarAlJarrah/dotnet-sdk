// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Buffers;
using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pagination;
using Dexpace.Sdk.Core.Pipeline;
using Dexpace.Sdk.Core.Serialization;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Pagination;

/// <summary>
/// Unit tests for <see cref="PaginationStrategies"/>.
/// </summary>
public class PaginationStrategiesTests
{
    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    private static DexpaceClientOptions DefaultOptions => new();

    // Simple page envelope.
    private sealed record TestPage(IReadOnlyList<int> Items, string? Cursor, bool HasMore);

    private static Request BaseRequest(string url) => Request.Get(url);

    // A no-op response used when the response is not examined by the strategy under test.
    private static Response EmptyResponse => new(Status.Ok);

    // A response that carries a Link header.
    private static Response ResponseWithLink(string linkHeaderValue) =>
        new(Status.Ok, Headers.Empty.With("Link", linkHeaderValue));

    // ── ScriptedTransport + ScriptedSerde (mirrors PageableTests helpers) ──────────────────────

    private sealed class ScriptedTransport(params Response[] responses) : IAsyncHttpClient
    {
        private int _index;

        public int CallCount => _index;

        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default)
        {
            if (_index >= responses.Length)
            {
                throw new InvalidOperationException(
                    $"ScriptedTransport exhausted: call #{_index + 1} received.");
            }

            return Task.FromResult(responses[_index++]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ScriptedSerde<TScripted>(params TScripted[] pages) : ISerde
    {
        private int _index;

        public MediaType DefaultMediaType => MediaType.Of("application", "json");

        public ValueTask SerializeAsync<TVal>(Stream destination, TVal value, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public async ValueTask<TVal?> DeserializeAsync<TVal>(Stream source, CancellationToken ct = default)
        {
            await source.CopyToAsync(Stream.Null, ct).ConfigureAwait(false);

            if (typeof(TVal) != typeof(TScripted))
            {
                throw new InvalidOperationException(
                    $"ScriptedSerde<{typeof(TScripted).Name}> asked for {typeof(TVal).Name}.");
            }

            if (_index >= pages.Length)
            {
                throw new InvalidOperationException("ScriptedSerde exhausted.");
            }

            return (TVal)(object)pages[_index++]!;
        }

        public void Serialize<TVal>(IBufferWriter<byte> destination, TVal value) { }

        public TVal? Deserialize<TVal>(ReadOnlySpan<byte> utf8) => default;
    }

    // Builds a pipeline from a scripted transport.
    private static (HttpPipeline, ScriptedTransport) MakePipeline(params Response[] responses)
    {
        var transport = new ScriptedTransport(responses);
        var pipeline = new PipelineBuilder().Build(transport);
        return (pipeline, transport);
    }

    // ── Cursor ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cursor_WithNonNullCursor_SetsQueryParameter()
    {
        var strategy = PaginationStrategies.Cursor<TestPage>(p => p.Cursor, "cursor");
        var current = BaseRequest("https://api.example.com/items");
        var page = new TestPage([], "abc123", HasMore: true);

        var next = strategy(page, EmptyResponse, current);

        Assert.NotNull(next);
        Assert.Equal("abc123", GetQueryParam(next.Url, "cursor"));
    }

    [Fact]
    public void Cursor_WithNullCursor_ReturnsNull()
    {
        var strategy = PaginationStrategies.Cursor<TestPage>(p => p.Cursor, "cursor");
        var current = BaseRequest("https://api.example.com/items");
        var page = new TestPage([], null, HasMore: false);

        var next = strategy(page, EmptyResponse, current);

        Assert.Null(next);
    }

    [Fact]
    public void Cursor_WithEmptyStringCursor_ReturnsNull()
    {
        var strategy = PaginationStrategies.Cursor<TestPage>(p => string.Empty, "cursor");
        var current = BaseRequest("https://api.example.com/items");
        var page = new TestPage([], string.Empty, HasMore: false);

        var next = strategy(page, EmptyResponse, current);

        Assert.Null(next);
    }

    [Fact]
    public void Cursor_PreservesMethodAndHeaders()
    {
        var strategy = PaginationStrategies.Cursor<TestPage>(p => p.Cursor, "after");
        var current = BaseRequest("https://api.example.com/items")
            .WithHeader("Authorization", "Bearer tok");
        var page = new TestPage([], "tok_next", HasMore: true);

        var next = strategy(page, EmptyResponse, current);

        Assert.NotNull(next);
        Assert.Equal(current.Method, next.Method);
        Assert.Equal("Bearer tok", next.Headers.Get("Authorization"));
    }

    [Fact]
    public void Cursor_ReplacesExistingCursorOnSubsequentPages()
    {
        var strategy = PaginationStrategies.Cursor<TestPage>(p => p.Cursor, "cursor");
        var current = BaseRequest("https://api.example.com/items?cursor=old");
        var page = new TestPage([], "new_cursor", HasMore: true);

        var next = strategy(page, EmptyResponse, current);

        Assert.NotNull(next);
        Assert.Equal("new_cursor", GetQueryParam(next.Url, "cursor"));
        // Old value must not appear.
        Assert.DoesNotContain("old", next.Url.Query);
    }

    // ── PageNumber ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PageNumber_WhenHasMore_IncrementsPageParameter()
    {
        var strategy = PaginationStrategies.PageNumber<TestPage>("page", p => p.HasMore);
        var current = BaseRequest("https://api.example.com/items?page=2");
        var page = new TestPage([], null, HasMore: true);

        var next = strategy(page, EmptyResponse, current);

        Assert.NotNull(next);
        Assert.Equal("3", GetQueryParam(next.Url, "page"));
    }

    [Fact]
    public void PageNumber_WhenPageParameterAbsent_DefaultsToOneAndIncrements()
    {
        var strategy = PaginationStrategies.PageNumber<TestPage>("page", p => p.HasMore);
        var current = BaseRequest("https://api.example.com/items");
        var page = new TestPage([], null, HasMore: true);

        var next = strategy(page, EmptyResponse, current);

        Assert.NotNull(next);
        Assert.Equal("2", GetQueryParam(next.Url, "page"));
    }

    [Fact]
    public void PageNumber_WhenNoMore_ReturnsNull()
    {
        var strategy = PaginationStrategies.PageNumber<TestPage>("page", p => p.HasMore);
        var current = BaseRequest("https://api.example.com/items?page=3");
        var page = new TestPage([], null, HasMore: false);

        var next = strategy(page, EmptyResponse, current);

        Assert.Null(next);
    }

    // ── LinkHeader ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LinkHeader_WithNextRel_ReturnsAbsoluteUrl()
    {
        var strategy = PaginationStrategies.LinkHeader<TestPage>("next");
        var current = BaseRequest("https://api.example.com/items");
        var response = ResponseWithLink("<https://api.example.com/items?page=2>; rel=\"next\"");

        var next = strategy(default!, response, current);

        Assert.NotNull(next);
        Assert.Equal(new Uri("https://api.example.com/items?page=2"), next.Url);
    }

    [Fact]
    public void LinkHeader_WithNoMatchingRel_ReturnsNull()
    {
        var strategy = PaginationStrategies.LinkHeader<TestPage>("next");
        var current = BaseRequest("https://api.example.com/items");
        var response = ResponseWithLink("<https://api.example.com/items?page=1>; rel=\"prev\"");

        var next = strategy(default!, response, current);

        Assert.Null(next);
    }

    [Fact]
    public void LinkHeader_WithRelativeUrl_ResolvesAgainstCurrentRequest()
    {
        var strategy = PaginationStrategies.LinkHeader<TestPage>("next");
        var current = BaseRequest("https://api.example.com/items");
        // Relative URL — should be resolved against current request URL.
        var response = ResponseWithLink("</items?page=2>; rel=\"next\"");

        var next = strategy(default!, response, current);

        Assert.NotNull(next);
        Assert.Equal(new Uri("https://api.example.com/items?page=2"), next.Url);
    }

    [Fact]
    public void LinkHeader_WithMissingLinkHeader_ReturnsNull()
    {
        var strategy = PaginationStrategies.LinkHeader<TestPage>("next");
        var current = BaseRequest("https://api.example.com/items");

        var next = strategy(default!, EmptyResponse, current);

        Assert.Null(next);
    }

    [Fact]
    public void LinkHeader_WithMultipleRelEntries_FindsNext()
    {
        var strategy = PaginationStrategies.LinkHeader<TestPage>("next");
        var current = BaseRequest("https://api.example.com/items");
        var response = ResponseWithLink(
            "<https://api.example.com/items?page=1>; rel=\"prev\", <https://api.example.com/items?page=3>; rel=\"next\"");

        var next = strategy(default!, response, current);

        Assert.NotNull(next);
        Assert.Equal(new Uri("https://api.example.com/items?page=3"), next.Url);
    }

    [Fact]
    public void LinkHeader_WithMalformedEntry_ReturnsNull()
    {
        var strategy = PaginationStrategies.LinkHeader<TestPage>("next");
        var current = BaseRequest("https://api.example.com/items");
        // No angle brackets — malformed.
        var response = ResponseWithLink("not-a-valid-link-header");

        var next = strategy(default!, response, current);

        Assert.Null(next);
    }

    [Fact]
    public void LinkHeader_DefaultRelIsNext()
    {
        // No rel= parameter — use default.
        var strategy = PaginationStrategies.LinkHeader<TestPage>();
        var current = BaseRequest("https://api.example.com/items");
        var response = ResponseWithLink("<https://api.example.com/items?page=2>; rel=\"next\"");

        var next = strategy(default!, response, current);

        Assert.NotNull(next);
    }

    // ── End-to-end: Pageable.Create + a strategy ──────────────────────────────────────────────

    [Fact]
    public async Task EndToEnd_CursorStrategy_TwoPagesViaFakePipeline()
    {
        var page1 = new TestPage([1, 2], Cursor: "cur1", HasMore: true);
        var page2 = new TestPage([3, 4], Cursor: null, HasMore: false);

        var serde = new ScriptedSerde<TestPage>(page1, page2);
        var (pipeline, transport) = MakePipeline(
            new Response(Status.Ok),
            new Response(Status.Ok));

        var strategy = PaginationStrategies.Cursor<TestPage>(p => p.Cursor, "cursor");

        var pageable = Pageable.Create<TestPage, int>(
            pipeline,
            Request.Get("https://api.example.com/items"),
            serde,
            DefaultOptions,
            p => p.Items,
            strategy);

        var items = new List<int>();
        await foreach (var item in pageable)
        {
            items.Add(item);
        }

        Assert.Equal([1, 2, 3, 4], items);
        Assert.Equal(2, transport.CallCount);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the raw (decoded) value of <paramref name="key"/> from <paramref name="uri"/>'s
    /// query string, or <see langword="null"/> if absent.
    /// </summary>
    private static string? GetQueryParam(Uri uri, string key)
    {
        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrEmpty(query)) { return null; }

        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            var pairKey = eq < 0 ? pair : pair[..eq];
            if (string.Equals(Uri.UnescapeDataString(pairKey), key, StringComparison.OrdinalIgnoreCase))
            {
                return eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        return null;
    }
}
