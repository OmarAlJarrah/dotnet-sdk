# Pagination — slice design

- **Date:** 2026-06-14
- **Status:** Approved; ready for implementation planning.
- **Part of:** [.NET SDK Platform Architecture & Build Plan](2026-06-14-dotnet-sdk-platform-design.md) — slice 7.

## 1. Purpose & scope

Item- and page-level async iteration over paginated operations.

**In scope:** `AsyncPageable<T>`, `Page<T>`, a `Pageable` factory driven by typed selectors, built-in
cursor / page-number / link-header helpers, and a `maxPages` safety cap.

**Out of scope:** streaming-array deserialization of a single huge response (a later serde add-on).

## 2. Decisions

- **Surface mirrors Azure.Core:** `AsyncPageable<T> : IAsyncEnumerable<T>` with an `AsPages()` view —
  the idiom .NET consumers already know.
- **Typed selectors, not JSON-path strings.** The caller supplies a strongly-typed page-envelope
  model plus delegates to select items and derive the next request — fully AOT / source-gen friendly.
- **Each page request runs through the full `HttpPipeline`** with a fresh `PipelineContext`, so retry,
  auth, and telemetry apply per page.
- **`maxPages` cap** guards against runaway iteration.

## 3. Surface (sketch)

```csharp
namespace Dexpace.Sdk.Core.Pagination;

public abstract class AsyncPageable<T> : IAsyncEnumerable<T>
{
    public abstract IAsyncEnumerable<Page<T>> AsPages(int? pageSizeHint = null);
    public abstract IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default);
}

public sealed class Page<T>
{
    public IReadOnlyList<T> Values { get; }
    public string? ContinuationToken { get; }
    public Status Status { get; }
    public Headers Headers { get; }
}

public static class Pageable
{
    public static AsyncPageable<T> Create<TPage, T>(
        HttpPipeline pipeline,
        Request first,
        ISerde serde,
        DexpaceClientOptions options,
        Func<TPage, IReadOnlyList<T>> selectItems,
        Func<TPage, Response, Request, Request?> nextRequest,    // null ends iteration
        int? maxPages = null);
}
```

## 4. Built-in strategies

Thin helpers that produce the `nextRequest` delegate:

- **Cursor** — read a cursor field from `TPage`, set it as a query parameter on the next request.
- **Page number** — increment a page query parameter until a page returns fewer than the page size.
- **Link header** — parse `Link: <url>; rel="next"` from the `Response` headers (RFC 8288).

`nextRequest` receives both the deserialized `TPage` and the raw `Response`, so body-driven cursors
and header-driven links are both expressible.

## 5. Response lifecycle

The pager deserializes each page through the serde, captures `Status`/`Headers`/values/continuation
into `Page<T>`, then disposes the response. Item enumeration lazily fetches the next page only when
the consumer advances past the current page's items.

## 6. Project & repo changes

- `Core`: add `Pagination/` (`AsyncPageable<T>`, `Page<T>`, `Pageable`, the strategy helpers).
- No new dependencies.

## 7. Open items (resolve during planning)

- Whether `Page<T>` should optionally expose the raw (already-consumed) `Response` for advanced cases.
- Per-page vs shared `DexpaceClientOptions`/cancellation semantics.
- Page-size-hint plumbing into the first request.
