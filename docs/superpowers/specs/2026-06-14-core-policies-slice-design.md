# Core policies — slice design

- **Date:** 2026-06-14
- **Status:** Approved; ready for implementation planning.
- **Part of:** [.NET SDK Platform Architecture & Build Plan](2026-06-14-dotnet-sdk-platform-design.md) — slice 5.

## 1. Purpose & scope

The concrete policies that run on the spine, the default pipeline assembly, and the error-response
contract.

**In scope:** Operation, Redirect, Retry, Idempotency, SetDate, ClientIdentity, and Instrumentation
policies; `Response.EnsureSuccessAsync`; a default-pipeline factory; the finalized stage ordering.

**Out of scope:** auth credential/challenge policies (slice 6).

## 2. Error-response contract

- `HttpPipeline.SendAsync` **returns** the `Response` for any status — success or error.
- `Response.EnsureSuccessAsync(maxErrorBytes = 1 MiB, ct)` throws `HttpResponseException` when the
  status is not success, **buffering the bounded error body into the exception** so
  `GetErrorAsync<T>` (serde slice) can read it. The cap guards against oversized error pages.
- This keeps the toolkit unopinionated; a future higher-level/generated client owns any
  throw-by-default behavior.

## 3. Finalized stage ordering (refines slice 4's working default)

```
Operation   = 100  (pillar)      once per call
Redirect    = 200  (pillar)
PerCall     = 250  (non-pillar)  once, above Retry — Idempotency, ClientIdentity
Retry       = 300  (pillar)
PerAttempt  = 400  (non-pillar)  per attempt, inside Retry — SetDate
Auth        = 500  (pillar)      inside Retry → refreshed token used on retry
Diagnostics = 600  (pillar)      inside Retry → per-attempt span/metrics/logs
-> transport terminal
```

Rationale: anything that must be **stable across attempts** (the idempotency key) sits *above* Retry;
anything that must be **fresh per attempt** (the `Date` header, auth token, per-attempt span) sits
*below* it. Redirect wraps Retry, so each hop runs a full retry loop.

## 4. Policies

- **`OperationPolicy`** (Operation) — opens the once-per-call operation `Activity`; applies
  `OverallTimeout` via a linked `CancellationTokenSource`.
- **`RedirectPolicy`** (Redirect) — follows 3xx up to `MaxRedirects`. 307/308 preserve method + body;
  301/302/303 become GET and drop the body. Strips `Authorization`/`Cookie` on cross-origin hops;
  rejects HTTPS→HTTP unless `AllowHttpsToHttpDowngrade`. Emits the redirect counter.
- **`RetryPolicy`** (Retry) — retries on 408/429/500/502/503/504 and on `ServiceRequestException` /
  `ServiceResponseException` for idempotent methods. Exponential backoff with **full jitter**, capped
  at `MaxDelay`; honors `Retry-After` (delta-seconds and HTTP-date) when `HonorRetryAfter`. Non-
  idempotent methods retry only when the body is replayable and `RetryNonIdempotentWhenReplayable` is
  set. Closes the retryable response before sleeping; emits the retry counter.
- **`IdempotencyPolicy`** (PerCall) — sets `Idempotency-Key` for configured methods (default POST),
  generating a GUID v4 once and **stashing it in the context property bag** so retries and redirect
  hops reuse the same key.
- **`SetDatePolicy`** (PerAttempt) — stamps a fresh RFC 1123 `Date` header each attempt.
- **`ClientIdentityPolicy`** (PerCall) — stamps `User-Agent` from `DexpaceClientOptions.UserAgent`.
- **`InstrumentationPolicy`** (Diagnostics) — per attempt: starts the client-kind `Activity` with
  OTel tags, injects `traceparent`/`tracestate` from `Activity.Current`, records the duration
  histogram and active-requests counter, and writes redacted structured `ILogger` events. Consolidates
  the siblings' separate logging + tracing into one policy (they share timing + redaction).

## 5. Default pipeline assembly

```csharp
public static class DexpacePipeline
{
    public static HttpPipeline CreateDefault(
        IAsyncHttpClient transport,
        DexpaceClientOptions options,
        HttpPipelinePolicy? authPolicy = null,
        ILogger? logger = null);
}
```

Assembles Operation → Redirect → Idempotency → ClientIdentity → Retry → SetDate → (auth, if supplied)
→ Instrumentation → transport. Auth is injected by the auth slice or the DI layer; everything else is
on by default and tunable through `options`.

## 6. Project & repo changes

- `Core`: add `Pipeline/Policies/` (`OperationPolicy`, `RedirectPolicy`, `RetryPolicy`,
  `IdempotencyPolicy`, `SetDatePolicy`, `ClientIdentityPolicy`, `InstrumentationPolicy`),
  `Pipeline/DexpacePipeline.cs`, and `Response.EnsureSuccessAsync`.
- Adds the `PerCall` stage to `PipelineStage`.
- No new dependencies.

## 7. Open items (resolve during planning)

- Jitter formula (full vs decorrelated) and whether the retry predicate/backoff are caller-overridable
  via `RetryOptions` hooks in this slice or a follow-up.
- Whether `IdempotencyPolicy` also covers PATCH by default.
- Buffering cap default (`1 MiB`) for `EnsureSuccessAsync`.
- Splitting `InstrumentationPolicy` into separate logging/tracing policies if users want to replace
  one without the other (leaning: one policy, with toggles).
