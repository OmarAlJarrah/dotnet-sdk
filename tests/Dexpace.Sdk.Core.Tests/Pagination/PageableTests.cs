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
/// Integration tests for <see cref="Pageable.Create{TPage,T}"/> /
/// <see cref="AsyncPageable{T}"/> / <see cref="Page{T}"/>.
/// </summary>
public class PageableTests
{
    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    private static DexpaceClientOptions DefaultOptions => new();

    // A simple page envelope for tests.
    private sealed record TestPage(IReadOnlyList<int> Items, bool HasNext);

    // Builds a pipeline + scripted transport from a sequence of pre-canned responses.
    private static (HttpPipeline, ScriptedTransport) MakePipeline(params Response[] responses)
    {
        var transport = new ScriptedTransport(responses);
        var pipeline = new PipelineBuilder().Build(transport);
        return (pipeline, transport);
    }

    // nextRequest: advance to the next URL when HasNext is true.
    private static Request? NextRequest(TestPage page, Response _, Request current) =>
        page.HasNext ? current with { Url = new Uri(current.Url + "/next") } : null;

    // Convenience: create a pageable over TestPage with int items.
    private static AsyncPageable<int> MakePageable(
        HttpPipeline pipeline,
        ISerde serde,
        int? maxPages = null) =>
        Pageable.Create<TestPage, int>(
            pipeline,
            Request.Get("https://api.example.com/items"),
            serde,
            DefaultOptions,
            p => p.Items,
            NextRequest,
            maxPages);

    // ── scripted transport ─────────────────────────────────────────────────────────────────────

    private sealed class ScriptedTransport(params Response[] responses) : IAsyncHttpClient
    {
        private int _index;

        public int CallCount => _index;

        public Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken = default)
        {
            if (_index >= responses.Length)
            {
                throw new InvalidOperationException(
                    $"ScriptedTransport exhausted: {responses.Length} response(s) scripted, call #{_index + 1} received.");
            }

            return Task.FromResult(responses[_index++]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // ── scripted serde ─────────────────────────────────────────────────────────────────────────

    // A fake ISerde that ignores the stream and returns scripted page objects.
    private sealed class ScriptedSerde<TScripted>(params TScripted[] pages) : ISerde
    {
        private int _index;

        public MediaType DefaultMediaType => MediaType.Of("application", "json");

        public ValueTask SerializeAsync<TVal>(Stream destination, TVal value, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public async ValueTask<TVal?> DeserializeAsync<TVal>(Stream source, CancellationToken ct = default)
        {
            // Consume the stream to avoid leak warnings.
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

    // ── tracking response body (asserts disposal) ─────────────────────────────────────────────

    private sealed class TrackingBody : ResponseBody
    {
        private int _consumed;

        public bool Disposed { get; private set; }

        public override MediaType? ContentType => null;

        public override Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            if (System.Threading.Interlocked.Exchange(ref _consumed, 1) != 0)
            {
                throw new Errors.StreamConsumedException("Already consumed.");
            }

            return Task.FromResult<Stream>(new MemoryStream(Array.Empty<byte>(), writable: false));
        }

        public override void Dispose()
        {
            Disposed = true;
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }
    }

    // ── item flattening ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsyncEnumerator_FlattensItemsAcrossPages()
    {
        var page1 = new TestPage([1, 2], HasNext: true);
        var page2 = new TestPage([3, 4], HasNext: false);

        var serde = new ScriptedSerde<TestPage>(page1, page2);
        var (pipeline, _) = MakePipeline(
            new Response(Status.Ok),
            new Response(Status.Ok));

        var pageable = MakePageable(pipeline, serde);

        var items = new List<int>();
        await foreach (var item in pageable)
        {
            items.Add(item);
        }

        Assert.Equal([1, 2, 3, 4], items);
    }

    // ── AsPages ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AsPages_YieldsCorrectNumberOfPages()
    {
        var page1 = new TestPage([10, 20], HasNext: true);
        var page2 = new TestPage([30], HasNext: false);

        var serde = new ScriptedSerde<TestPage>(page1, page2);
        var (pipeline, transport) = MakePipeline(
            new Response(Status.Ok),
            new Response(Status.Ok));

        var pageable = MakePageable(pipeline, serde);

        var count = 0;
        await foreach (var _ in pageable.AsPages())
        {
            count++;
        }

        Assert.Equal(2, count);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task AsPages_PageValuesMatchSelectItems()
    {
        var page1 = new TestPage([7, 8, 9], HasNext: false);

        var serde = new ScriptedSerde<TestPage>(page1);
        var (pipeline, _) = MakePipeline(new Response(Status.Ok));

        var pageable = MakePageable(pipeline, serde);

        var pages = new List<Page<int>>();
        await foreach (var page in pageable.AsPages())
        {
            pages.Add(page);
        }

        Assert.Single(pages);
        Assert.Equal([7, 8, 9], pages[0].Values);
    }

    [Fact]
    public async Task AsPages_ExposesStatusAndHeaders()
    {
        var responseHeaders = Headers.Empty.With("X-Page", "42");
        var page1 = new TestPage([1], HasNext: false);

        var serde = new ScriptedSerde<TestPage>(page1);
        var (pipeline, _) = MakePipeline(new Response(Status.Ok, responseHeaders));

        var pageable = MakePageable(pipeline, serde);

        var pages = new List<Page<int>>();
        await foreach (var page in pageable.AsPages())
        {
            pages.Add(page);
        }

        Assert.Single(pages);
        Assert.Equal(Status.Ok, pages[0].Status);
        Assert.Equal(
            responseHeaders.GetAll("X-Page"),
            pages[0].Headers.GetAll("X-Page"));
    }

    // ── laziness ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Laziness_AfterFirstPageConsumed_TransportCalledOnce()
    {
        var page1 = new TestPage([1, 2], HasNext: true);
        var page2 = new TestPage([3, 4], HasNext: false);

        var serde = new ScriptedSerde<TestPage>(page1, page2);
        var (pipeline, transport) = MakePipeline(
            new Response(Status.Ok),
            new Response(Status.Ok));

        var pageable = MakePageable(pipeline, serde);

        // Consume only the first page via AsPages enumerator.
        await using var enumerator = pageable.AsPages().GetAsyncEnumerator();
        var moved = await enumerator.MoveNextAsync();

        Assert.True(moved);
        // Only one HTTP call should have been made — the second page must not be pre-fetched.
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task Laziness_AdvancingToSecondPage_TriggersSecondSend()
    {
        var page1 = new TestPage([1], HasNext: true);
        var page2 = new TestPage([2], HasNext: false);

        var serde = new ScriptedSerde<TestPage>(page1, page2);
        var (pipeline, transport) = MakePipeline(
            new Response(Status.Ok),
            new Response(Status.Ok));

        var pageable = MakePageable(pipeline, serde);

        await using var enumerator = pageable.AsPages().GetAsyncEnumerator();

        await enumerator.MoveNextAsync(); // fetches page 1 → 1 call
        Assert.Equal(1, transport.CallCount);

        await enumerator.MoveNextAsync(); // fetches page 2 → 2 calls
        Assert.Equal(2, transport.CallCount);
    }

    // ── maxPages ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MaxPages_CapsNumberOfPagesFetched()
    {
        // Script 5 pages; cap at 2.
        var scriptedPages = Enumerable.Range(0, 5)
            .Select(i => new TestPage([i], HasNext: true))
            .ToArray();

        var serde = new ScriptedSerde<TestPage>(scriptedPages);
        var responses = Enumerable.Range(0, 5).Select(_ => new Response(Status.Ok)).ToArray();
        var (pipeline, transport) = MakePipeline(responses);

        var pageable = MakePageable(pipeline, serde, maxPages: 2);

        var collected = new List<int>();
        await foreach (var item in pageable)
        {
            collected.Add(item);
        }

        Assert.Equal(2, transport.CallCount);
        Assert.Equal([0, 1], collected);
    }

    [Fact]
    public async Task MaxPages_WhenNull_IteratesAllPages()
    {
        var page1 = new TestPage([1], HasNext: true);
        var page2 = new TestPage([2], HasNext: true);
        var page3 = new TestPage([3], HasNext: false);

        var serde = new ScriptedSerde<TestPage>(page1, page2, page3);
        var (pipeline, transport) = MakePipeline(
            new Response(Status.Ok),
            new Response(Status.Ok),
            new Response(Status.Ok));

        var pageable = MakePageable(pipeline, serde, maxPages: null);

        var items = new List<int>();
        await foreach (var item in pageable)
        {
            items.Add(item);
        }

        Assert.Equal(3, transport.CallCount);
        Assert.Equal([1, 2, 3], items);
    }

    // ── nextRequest returning null ends iteration ──────────────────────────────────────────────

    [Fact]
    public async Task NextRequestReturningNull_EndsIteration()
    {
        var page1 = new TestPage([1, 2], HasNext: false); // HasNext=false → nextRequest returns null

        var serde = new ScriptedSerde<TestPage>(page1);
        var (pipeline, transport) = MakePipeline(new Response(Status.Ok));

        var pageable = MakePageable(pipeline, serde);

        var items = new List<int>();
        await foreach (var item in pageable)
        {
            items.Add(item);
        }

        Assert.Equal(1, transport.CallCount);
        Assert.Equal([1, 2], items);
    }

    // ── response disposal ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EachResponse_IsDisposedAfterPageYield()
    {
        var body1 = new TrackingBody();
        var body2 = new TrackingBody();

        var page1 = new TestPage([1], HasNext: true);
        var page2 = new TestPage([2], HasNext: false);

        var serde = new ScriptedSerde<TestPage>(page1, page2);
        var (pipeline, _) = MakePipeline(
            new Response(Status.Ok, body: body1),
            new Response(Status.Ok, body: body2));

        var pageable = MakePageable(pipeline, serde);

        await foreach (var _ in pageable) { }

        Assert.True(body1.Disposed, "First response body should be disposed.");
        Assert.True(body2.Disposed, "Second response body should be disposed.");
    }

    // ── Page<T> constructor ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Page_Constructor_SetsProperties()
    {
        var values = new List<int> { 1, 2, 3 };
        var status = Status.Ok;
        var headers = Headers.Empty.With("X-Foo", "bar");

        var page = new Page<int>(values, status, headers);

        Assert.Same(values, page.Values);
        Assert.Equal(status, page.Status);
        Assert.Same(headers, page.Headers);
    }

    [Fact]
    public void Page_Constructor_NullValues_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Page<int>(null!, Status.Ok, Headers.Empty));
    }

    [Fact]
    public void Page_Constructor_NullHeaders_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Page<int>(Array.Empty<int>(), Status.Ok, null!));
    }

    // ── Pageable.Create argument guards ───────────────────────────────────────────────────────

    [Fact]
    public void Create_NullPipeline_Throws()
    {
        var serde = new ScriptedSerde<TestPage>();
        Assert.Throws<ArgumentNullException>(() =>
            Pageable.Create<TestPage, int>(
                null!,
                Request.Get("https://x.com"),
                serde,
                DefaultOptions,
                p => p.Items,
                NextRequest));
    }

    [Fact]
    public void Create_NullRequest_Throws()
    {
        var (pipeline, _) = MakePipeline();
        var serde = new ScriptedSerde<TestPage>();
        Assert.Throws<ArgumentNullException>(() =>
            Pageable.Create<TestPage, int>(
                pipeline,
                null!,
                serde,
                DefaultOptions,
                p => p.Items,
                NextRequest));
    }

    [Fact]
    public void Create_NullSerde_Throws()
    {
        var (pipeline, _) = MakePipeline();
        Assert.Throws<ArgumentNullException>(() =>
            Pageable.Create<TestPage, int>(
                pipeline,
                Request.Get("https://x.com"),
                null!,
                DefaultOptions,
                p => p.Items,
                NextRequest));
    }

    [Fact]
    public void Create_NullOptions_Throws()
    {
        var (pipeline, _) = MakePipeline();
        var serde = new ScriptedSerde<TestPage>();
        Assert.Throws<ArgumentNullException>(() =>
            Pageable.Create<TestPage, int>(
                pipeline,
                Request.Get("https://x.com"),
                serde,
                null!,
                p => p.Items,
                NextRequest));
    }

    [Fact]
    public void Create_NullSelectItems_Throws()
    {
        var (pipeline, _) = MakePipeline();
        var serde = new ScriptedSerde<TestPage>();
        Assert.Throws<ArgumentNullException>(() =>
            Pageable.Create<TestPage, int>(
                pipeline,
                Request.Get("https://x.com"),
                serde,
                DefaultOptions,
                null!,
                NextRequest));
    }

    [Fact]
    public void Create_NullNextRequest_Throws()
    {
        var (pipeline, _) = MakePipeline();
        var serde = new ScriptedSerde<TestPage>();
        Assert.Throws<ArgumentNullException>(() =>
            Pageable.Create<TestPage, int>(
                pipeline,
                Request.Get("https://x.com"),
                serde,
                DefaultOptions,
                p => p.Items,
                null!));
    }

    // ── disposal: early break ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EarlyBreak_FirstBodyDisposed_AndTransportCalledOnce()
    {
        var body1 = new TrackingBody();
        var page1 = new TestPage([1, 2], HasNext: true);
        var page2 = new TestPage([3, 4], HasNext: false);

        var serde = new ScriptedSerde<TestPage>(page1, page2);
        var (pipeline, transport) = MakePipeline(
            new Response(Status.Ok, body: body1),
            new Response(Status.Ok));

        var pageable = MakePageable(pipeline, serde);

        // Consume only the first item then break — second page must never be fetched.
        await foreach (var _ in pageable)
        {
            break;
        }

        Assert.True(body1.Disposed, "First response body should be disposed after early break.");
        Assert.Equal(1, transport.CallCount);
    }

    // ── disposal: exception path ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExceptionFromSerde_BodyStillDisposed()
    {
        var body = new TrackingBody();

        // Serde throws on the first call.
        var throwingSerde = new ThrowingOnFirstCallSerde();
        var (pipeline, _) = MakePipeline(new Response(Status.Ok, body: body));

        var pageable = Pageable.Create<TestPage, int>(
            pipeline,
            Request.Get("https://api.example.com/items"),
            throwingSerde,
            DefaultOptions,
            p => p.Items,
            NextRequest);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in pageable) { }
        });

        Assert.True(body.Disposed, "Response body must be disposed even when the serde throws.");
    }

    // ── re-enumeration ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReEnumeration_EachEnumerationRestartsFromFirst()
    {
        // Two passes, each should see the same items and drive the transport independently.
        var p1 = new TestPage([10, 20], HasNext: false);
        var p2 = new TestPage([10, 20], HasNext: false); // scripted twice

        var serde = new ScriptedSerde<TestPage>(p1, p2);
        var (pipeline, transport) = MakePipeline(
            new Response(Status.Ok),
            new Response(Status.Ok));

        var pageable = MakePageable(pipeline, serde);

        var first = new List<int>();
        await foreach (var item in pageable) { first.Add(item); }

        var second = new List<int>();
        await foreach (var item in pageable) { second.Add(item); }

        Assert.Equal([10, 20], first);
        Assert.Equal([10, 20], second);
        // Each enumeration should have caused exactly one HTTP call (2 total).
        Assert.Equal(2, transport.CallCount);
    }

    // ── cancellation ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_AlreadyCancelled_Items_ThrowsBeforeTransport()
    {
        var (pipeline, transport) = MakePipeline(new Response(Status.Ok));
        var serde = new ScriptedSerde<TestPage>(new TestPage([1], HasNext: false));
        var pageable = MakePageable(pipeline, serde);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in pageable.WithCancellation(cts.Token)) { }
        });

        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task Cancellation_AlreadyCancelled_Pages_ThrowsBeforeTransport()
    {
        var (pipeline, transport) = MakePipeline(new Response(Status.Ok));
        var serde = new ScriptedSerde<TestPage>(new TestPage([1], HasNext: false));
        var pageable = MakePageable(pipeline, serde);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in pageable.AsPages().WithCancellation(cts.Token)) { }
        });

        Assert.Equal(0, transport.CallCount);
    }

    // ── helpers for the new tests ─────────────────────────────────────────────────────────────

    // ISerde that always throws InvalidOperationException from DeserializeAsync.
    private sealed class ThrowingOnFirstCallSerde : ISerde
    {
        public MediaType DefaultMediaType => MediaType.Of("application", "json");

        public ValueTask SerializeAsync<TVal>(Stream destination, TVal value, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public async ValueTask<TVal?> DeserializeAsync<TVal>(Stream source, CancellationToken ct = default)
        {
            // Drain the body before throwing so disposal tracking stays clean.
            await source.CopyToAsync(Stream.Null, ct).ConfigureAwait(false);
            throw new InvalidOperationException("Serde failure injected by test.");
        }

        public void Serialize<TVal>(System.Buffers.IBufferWriter<byte> destination, TVal value) { }

        public TVal? Deserialize<TVal>(ReadOnlySpan<byte> utf8) => default;
    }
}
