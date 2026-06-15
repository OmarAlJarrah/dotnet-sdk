# Server-Sent Events (SSE) — slice design

- **Date:** 2026-06-14
- **Status:** Approved; ready for implementation planning.
- **Part of:** [.NET SDK Platform Architecture & Build Plan](2026-06-14-dotnet-sdk-platform-design.md) — slice 8.

## 1. Purpose & scope

Read `text/event-stream` responses as a typed async stream, with optional reconnection.

**In scope:** a WHATWG-compliant `IAsyncEnumerable<ServerSentEvent>` parser over a response stream,
and a reconnecting client (Last-Event-ID resume, server `retry:` backoff).

**Out of scope:** an SSE *server*; transforming events into typed domain models (caller's job, via the
serde).

## 2. Decisions

- **Parser yields `IAsyncEnumerable<ServerSentEvent>`** over a `Stream` obtained from
  `ResponseBody.OpenReadAsync` — single-use streaming, no buffering of the whole body.
- **Ship the reconnecting client too** — resumes with `Last-Event-ID`, honors the server's `retry:`
  hint (capped), and bounds reconnect attempts.
- **`TimeProvider` for backoff timing** (BCL, testable) — no bespoke clock.

## 3. Surface (sketch)

```csharp
namespace Dexpace.Sdk.Core.ServerSentEvents;

public sealed record ServerSentEvent(string? Id, string EventType, string Data, TimeSpan? Retry);

public static class ServerSentEventReader
{
    public static IAsyncEnumerable<ServerSentEvent> ReadAsync(Stream stream, CancellationToken ct = default);
}

public sealed class ServerSentEventStream                  // reconnecting
{
    public ServerSentEventStream(
        HttpPipeline pipeline, Request request, DexpaceClientOptions options,
        SseReconnectOptions? reconnect = null, TimeProvider? timeProvider = null);

    public IAsyncEnumerable<ServerSentEvent> ReadAsync(CancellationToken ct = default);
}
```

## 4. Parsing rules (WHATWG)

- Line terminators LF / CR / CRLF; UTF-8 with U+FFFD replacement; leading BOM stripped once.
- `data:` fields accumulate, joined by `\n`; a blank line dispatches the event.
- `event:` sets the type (default `"message"`); `id:` is sticky for the connection; `retry:` (ms)
  updates the reconnection delay; lines beginning `:` are comments.
- A bounded line buffer guards against unterminated input.

## 5. Reconnection

On stream end or transport error, the reconnecting client waits the current `retry` delay (server hint
or configured default, capped), re-issues the request with `Last-Event-ID`, and resumes — up to
`SseReconnectOptions.MaxAttempts`. Cancellation stops promptly.

## 6. Project & repo changes

- `Core`: add `ServerSentEvents/` (`ServerSentEvent`, `ServerSentEventReader`, `ServerSentEventStream`,
  `SseReconnectOptions`). `CommonMediaTypes.TextEventStream` already exists.
- No new dependencies.

## 7. Open items (resolve during planning)

- Default `retry` value and cap when the server sends none.
- Whether to surface raw `:` comments to the consumer or drop them (leaning drop, with an opt-in).
- Backpressure expectations under slow consumers (documented; `IAsyncEnumerable` naturally pulls).
