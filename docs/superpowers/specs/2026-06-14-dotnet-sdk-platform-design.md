# Dexpace .NET SDK — Platform Architecture & Build Plan

- **Date:** 2026-06-14
- **Status:** Platform shape approved. Each subsystem slice gets its own design spec → plan → build.

## 1. Purpose & guiding principle

The .NET SDK is the C#/.NET member of the dexpace SDK family, alongside the Java and Python
ports. Like them it is an **HTTP-client toolkit, not an HTTP client**: it provides abstractions,
immutable models, a transport SPI, and a composable request/response pipeline. Consuming libraries
plug in a concrete transport.

**Guiding principle: the .NET platform leads the design.** The Java and Python SDKs are a
*capability checklist and a map of the architectural seams* — they tell us which subsystems exist
and how they relate — but the public surface and mechanics are designed from scratch in idiomatic
C#. Where a sibling invented an abstraction to fill a gap in its own ecosystem (a bespoke logger,
tracer, meter, or configuration type), the .NET standard equivalent is used instead. We do not port
for the sake of symmetry.

## 2. Current state

The foundation (architecture layers 1–5) is complete and shipping:

- HTTP value models: `Method`, `Protocol`, `MediaType`, `Headers`, `HttpHeaderName`, `Status`.
- `Request` / `RequestBody` and `Response` / `ResponseBody`, with replayable vs. single-use body
  semantics and deterministic disposal.
- Transport SPI: `IHttpClient` / `IAsyncHttpClient` with sync↔async bridges.
- A 12-type `SdkException` hierarchy (already anticipating the upper stack:
  `PipelineAbortedException`, `SerializationException`, `DeserializationException`).
- `Dexpace.Sdk.Http.SystemNet`, the reference transport over `System.Net.Http.HttpClient`.

Everything above the transport seam — pipeline, policies, context, auth, SSE, pagination, webhooks,
serde, instrumentation, configuration, and DI integration — is to be built.

## 3. Cross-cutting decisions

- **D1 — Native-first.** Optimize for the idioms, semantics, and mechanics of the .NET platform.
  The siblings inform *what* to build, not *how*.
- **D2 — `Core` embraces the standard abstraction packages.** `Core` may depend on
  `Microsoft.Extensions.Logging.Abstractions` and `System.Diagnostics.DiagnosticSource` — the
  near-universal, Microsoft-owned packages that *are* how a .NET library plugs into the ecosystem.
  `Core` still ships no transport, no concrete serializer, and no heavy runtime dependencies. The
  bespoke `ClientLogger` / `InstrumentationContext` / `Tracer` / `Meter` / `Configuration` types from
  the siblings are **not ported** — `Core` uses `ILogger` and `Activity`/`ActivitySource`/`Meter`
  directly. Configuration is plain option POCOs in `Core`; the `Microsoft.Extensions.Options`
  machinery (`IOptions<T>`, binding, validation) lives in the DI integration package, not `Core`
  (see the Options slice).
- **D3 — Transport-agnostic pipeline with native interop.** `IAsyncHttpClient` remains the bottom
  seam. The pipeline is a thin, native, transport-agnostic chain above it and owns *SDK-domain*
  concerns: auth, idempotency keys, typed-error- and `Retry-After`-aware retry, redirect semantics,
  and SDK spans/metrics. *Connection-level* concerns (proxy, mTLS, HTTP/2-3, corporate telemetry
  handlers) live in the transport's underlying `HttpClient`, ideally created via `IHttpClientFactory`,
  so an enterprise's existing `DelegatingHandler` / Polly chain composes **underneath** the SDK.
- **D4 — Modern multi-target, AOT-safe.** Libraries target `net8.0;net10.0`, fully trim-safe and
  NativeAOT-compatible. This makes `System.Text.Json` source generators mandatory and forbids runtime
  reflection on hot paths.

### Native defaults (apply throughout)

- Async-first: `Task` / `ValueTask`, `CancellationToken` threaded everywhere. Sync remains a thin
  `IHttpClient` bridge — kept, not dropped, for sync-only call sites.
- `IAsyncEnumerable<T>` is the surface for streaming (pagination and SSE).
- `System.Text.Json` with source-generated `JsonSerializerContext` — no reflection on hot paths.
- First-class DI (`AddDexpaceClient(...)`), the Options pattern for configuration, and
  `IHttpClientFactory` interop for the transport.
- Observability is `Activity` / `ActivitySource` / `Meter` / `ILogger` straight through, following
  OpenTelemetry HTTP semantic conventions — no bespoke telemetry types.
- Strong-named and signed assemblies, for enterprises with assembly-load policies.

## 4. Package topology

One cohesive `Core` toolkit plus separated implementations, integrations, and transports:

```
Microsoft.Extensions.Logging.Abstractions ┐ abstractions only, no heavy deps
System.Diagnostics.DiagnosticSource       ┘ (Options machinery lives in the DI package)
                  ▲
        ┌─────────┴──────────┐
        │  Dexpace.Sdk.Core  │  net8.0; net10.0 — the whole toolkit:
        │                    │  models · bodies · IAsyncHttpClient SPI · errors ·
        │                    │  serde ABSTRACTION · pipeline + policies · context ·
        │                    │  instrumentation · auth · SSE · pagination · webhooks
        └─────────┬──────────┘
       ▲          ▲           ▲
┌──────┴───┐ ┌────┴────────┐ ┌┴──────────────────────────────┐
│ Http.    │ │ Serializa-  │ │ Extensions.DependencyInjection │
│ SystemNet│ │ tion.System │ │  AddDexpaceClient(...) wires   │
│          │ │ TextJson    │ │  Options · ILogger ·           │
│          │ │             │ │  IHttpClientFactory · pipeline │
│          │ │             │ │  · transport · serde           │
└──────────┘ └─────────────┘ └────────────────────────────────┘
```

**Why this granularity:** it mirrors how the siblings ship (their `core` already holds
pipeline/auth/SSE/pagination together) and keeps versioning simple. Alternatives considered and set
aside: *minimal* (fold DI into `Core`, rejected because it forces a container on toolkit-only
consumers) and *granular* (a NuGet per subsystem — `Auth`, `Sse`, `Webhooks`, …), which buys
independent release cadence at the cost of coordinating ~8 packages that share dependencies and are
co-designed. The granular split is the one knob worth revisiting if independent versioning becomes a
requirement.

**Naming scheme:** `Dexpace.Sdk.<Area>.<Impl>` (e.g., `Dexpace.Sdk.Http.SystemNet`,
`Dexpace.Sdk.Serialization.SystemTextJson`, `Dexpace.Sdk.Extensions.DependencyInjection`).

## 5. Build order

Each slice is a self-contained design spec → implementation plan → build cycle.

| #  | Slice                          | Depends on | Scope summary |
|----|--------------------------------|------------|---------------|
| 1  | Serde + System.Text.Json       | —          | Serde seam in `Core`; source-generated STJ impl; typed error bodies; optional/PATCH field handling |
| 2  | Options & configuration        | —          | `DexpaceClientOptions`, validation, `IConfiguration` binding; supplies pipeline defaults |
| 3  | Instrumentation + context chain| —          | OTel-semantic `ActivitySource`/`Meter` naming; dispatch→request→exchange context propagation |
| 4  | Pipeline spine                 | 3          | Stage model + middleware-style `PipelineDelegate`; builder with insert/replace/remove |
| 5  | Core policies                  | 2, 3, 4    | redirect · semantic retry · idempotency · set-date · client-identity · logging · tracing |
| 6  | Auth                           | 4, 5       | key/basic/bearer/named credentials · `TokenCredential` refresh + cache · RFC 7235 challenges |
| 7  | Pagination                     | 1, 4       | `IAsyncEnumerable<T>`; cursor / page-number / link-header strategies |
| 8  | SSE                            | 4          | `IAsyncEnumerable<ServerSentEvent>` + reconnecting connection — *parallelizable* |
| 9  | Webhooks                       | 1          | Standard Webhooks HMAC-SHA256 verify / unwrap (BCL crypto) — *parallelizable* |
| 10 | DI / hosting integration       | most       | `Dexpace.Sdk.Extensions.DependencyInjection`; ties Options, ILogger, IHttpClientFactory, pipeline, transport, serde together |

## 6. Non-goals (v1)

- Not a code-generated service client; this is the generic toolkit only.
- Not a reimplementation of connection-level resilience that `Microsoft.Extensions.Http.Resilience`
  (Polly v8) already provides — circuit breaking, hedging, and concurrency limiting compose in the
  transport's handler chain, not in the SDK pipeline.
- No .NET Framework / `netstandard2.0` support.
- No bespoke logging, tracing, metrics, or configuration abstractions.

## 7. Open questions, deferred to the relevant slice spec

- **Serde:** depth of the `ISerde` seam vs. leaning on `System.Text.Json` directly; representation
  of optional/absent fields for PATCH (a dedicated `Optional<T>` vs. STJ-native patterns).
- **Context (slice 3):** ambient `AsyncLocal` context vs. an explicit context value threaded through
  the pipeline delegate.
- **Pipeline (slice 4):** exact `PipelineDelegate` signature and per-call state model.
- **Errors:** whether to add a generic typed-error exception (`HttpResponseException<TError>`) or a
  deserialize-on-demand accessor on the existing `HttpResponseException`.

## 8. Revisions to existing repo conventions (tracked, applied per slice)

- Revise the CLAUDE.md "`Core` is BCL-only" rule to "no transport, no concrete/heavy deps; the three
  standard abstraction packages are permitted" (per D2).
- `Directory.Build.props`: multi-target `net8.0;net10.0`; enable `IsTrimmable`, `IsAotCompatible`,
  and the trim/AOT analyzers; add strong-naming.
- `Directory.Packages.props`: add the abstraction package versions referenced by `Core`.
