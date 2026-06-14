# Options & configuration — slice design

- **Date:** 2026-06-14
- **Status:** Approved; ready for implementation planning.
- **Part of:** [.NET SDK Platform Architecture & Build Plan](2026-06-14-dotnet-sdk-platform-design.md) — slice 2.

## 1. Purpose & scope

The configuration objects that feed pipeline and policy defaults, exposed the .NET-native way.

**In scope:** option POCOs in `Core`; their defaults; the DI-side binding and validation glue.

**Out of scope:** a bespoke env/override reader (the siblings hand-rolled one — `IConfiguration`
covers it); proxy configuration (`HttpClient` honors `HTTP(S)_PROXY`/`NO_PROXY` natively); behavioral
predicates and hooks (retryable-status callbacks, etc.), which belong to the policies slice.

## 2. Decisions

- **Plain POCOs in `Core`, grouped per policy.** A root `DexpaceClientOptions` aggregates
  cross-cutting settings and the per-policy option objects. Every option type is constructable with
  `new` and carries sensible defaults — the pipeline is fully usable without a container.
- **Policies take the POCOs directly, not `IOptions<T>`.** This keeps policy construction trivial in
  the DI-less path and keeps `Core` decoupled from the Options package.
- **The `Microsoft.Extensions.Options` machinery lives only in the DI integration package** —
  `IOptions<T>`, `IConfiguration` binding, and `IValidateOptions<T>`. This narrows `Core`'s added
  dependencies to `Microsoft.Extensions.Logging.Abstractions` + `System.Diagnostics.DiagnosticSource`
  (a correction to platform-spec D2, already applied).
- **Naming aligns with the .NET resilience ecosystem.** Retry uses `MaxRetryAttempts` (number of
  retries *after* the first send), matching Polly v8 / `Microsoft.Extensions.Http.Resilience`.

## 3. Option types (sketch)

```csharp
namespace Dexpace.Sdk.Core.Configuration;

public sealed class DexpaceClientOptions
{
    public Uri? BaseAddress { get; set; }
    public string UserAgent { get; set; } = DefaultUserAgent;     // "dexpace-dotnet/<version>"
    public TimeSpan? OverallTimeout { get; set; }                 // whole operation (redirect + retry)
    public TimeSpan? AttemptTimeout { get; set; }                 // per send attempt
    public RetryOptions Retry { get; set; } = new();
    public RedirectOptions Redirect { get; set; } = new();
}

public sealed class RetryOptions
{
    public int MaxRetryAttempts { get; set; } = 3;                // retries after the first send
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    public bool HonorRetryAfter { get; set; } = true;
    public bool RetryNonIdempotentWhenReplayable { get; set; } = false;
}

public sealed class RedirectOptions
{
    public int MaxRedirects { get; set; } = 20;
    public bool AllowHttpsToHttpDowngrade { get; set; } = false;
    public bool StripSensitiveHeadersOnCrossOrigin { get; set; } = true;
}
```

Defaults rationale: retry/backoff numbers match common SDK behavior; max-redirects 20 mirrors
browser/`HttpClient` norms; downgrade-to-HTTP and cross-origin header leakage are off by default for
safety.

## 4. DI-side binding & validation (delivered with the DI slice)

```csharp
// programmatic
services.AddDexpaceClient(o =>
{
    o.BaseAddress = new Uri("https://api.example.com");
    o.Retry.MaxRetryAttempts = 5;
});

// or bound from configuration (env vars + appsettings + Key Vault, all via IConfiguration)
services.AddDexpaceClient().BindConfiguration("Dexpace");
```

- An `IValidateOptions<DexpaceClientOptions>` enforces ranges (attempts ≥ 0, delays > 0,
  `MaxDelay ≥ BaseDelay`, redirects ≥ 0) and runs with `ValidateOnStart`, so misconfiguration fails
  fast at host startup rather than mid-request.
- Configuration keys follow the section shape `Dexpace:Retry:MaxRetryAttempts`, etc.

## 5. Manual (DI-less) usage

```csharp
var options = new DexpaceClientOptions { Retry = { MaxRetryAttempts = 5 } };
// passed straight to the pipeline builder / individual policies — no container required.
```

## 6. Project & repo changes

- `Core`: add `Configuration/DexpaceClientOptions.cs`, `RetryOptions.cs`, `RedirectOptions.cs` —
  plain POCOs, no new dependency.
- `Directory.Packages.props`: `Microsoft.Extensions.Options` (and the configuration/binder packages)
  referenced only by the DI integration project.
- Platform spec D2 + package graph updated to move Options out of `Core` (done).

## 7. Open items (resolve during planning or in dependent slices)

- Final default values for timeouts (per-attempt vs overall) once the retry/redirect policies are
  designed (slice 5).
- Whether logging/tracing verbosity toggles live on `DexpaceClientOptions` or on their own
  policy-scoped options (leaning toward policy-scoped, defined in the instrumentation/policies
  slices).
