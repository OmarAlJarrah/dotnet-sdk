# Pipeline Prerequisites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add four small building blocks (`DexpaceClientOptions`, `DexpaceDiagnostics`, `UrlRedactor`, `PipelineContext`) to `Dexpace.Sdk.Core` so the pipeline and policy slices have their configuration/observability/context foundation.

**Architecture:** Each new type lives in its own file under a new namespace folder (`Configuration/`, `Diagnostics/`, `Pipeline/`). All types are plain BCL — no new runtime packages needed for options or context; `System.Diagnostics.DiagnosticSource` (ActivitySource + Meter) ships in-box on net8/net10 BCL, so no NuGet package is required either. Four independent tasks, one commit each, driven by TDD.

**Tech Stack:** C# `latest`, net8.0 / net10.0 multi-target, xUnit, `System.Diagnostics.ActivitySource`, `System.Diagnostics.Metrics.Meter`.

---

## Environment notes (critical)

- Build: `dotnet build /Users/omar/dexpace/dotnet-sdk -c Release` — 0 warnings required (TreatWarningsAsErrors; CS1591 doc gate; EnforceCodeStyleInBuild).
- Test: `dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj`  
  **Never** `dotnet test` bare — net8 runtime absent on this machine.
- Every new `.cs` file must start with the two-line MIT header:
  ```csharp
  // Copyright (c) 2026 dexpace and Omar Aljarrah.
  // Licensed under the MIT License. See LICENSE in the repository root for details.
  ```
- Every `public` member needs a `///` XML doc comment.
- File-scoped namespaces (`namespace Foo;`), not block-scoped.
- Central Package Management: version goes in `Directory.Packages.props`; `PackageReference` carries no `Version`.

---

## File map

| New file | Responsibility |
|---|---|
| `src/Dexpace.Sdk.Core/Configuration/DexpaceClientOptions.cs` | Root options POCO + `RetryOptions` + `RedirectOptions` |
| `tests/Dexpace.Sdk.Core.Tests/Configuration/DexpaceClientOptionsTests.cs` | Task A tests |
| `src/Dexpace.Sdk.Core/Diagnostics/DexpaceDiagnostics.cs` | Static `ActivitySource` + `Meter` |
| `tests/Dexpace.Sdk.Core.Tests/Diagnostics/DexpaceDiagnosticsTests.cs` | Task B tests |
| `src/Dexpace.Sdk.Core/Diagnostics/UrlRedactor.cs` | Userinfo + sensitive-query-param redaction |
| `tests/Dexpace.Sdk.Core.Tests/Diagnostics/UrlRedactorTests.cs` | Task C tests |
| `src/Dexpace.Sdk.Core/Pipeline/PipelineContext.cs` | Per-call mutable pipeline state |
| `tests/Dexpace.Sdk.Core.Tests/Pipeline/PipelineContextTests.cs` | Task D tests |

---

## Task A: DexpaceClientOptions POCOs

**Files:**
- Create: `src/Dexpace.Sdk.Core/Configuration/DexpaceClientOptions.cs`
- Create: `tests/Dexpace.Sdk.Core.Tests/Configuration/DexpaceClientOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Dexpace.Sdk.Core.Tests/Configuration/DexpaceClientOptionsTests.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Configuration;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Configuration;

public class DexpaceClientOptionsTests
{
    [Fact]
    public void DexpaceClientOptions_Defaults_AreCorrect()
    {
        var opts = new DexpaceClientOptions();

        Assert.Null(opts.BaseAddress);
        Assert.NotNull(opts.UserAgent);
        Assert.StartsWith("dexpace-dotnet/", opts.UserAgent);
        Assert.Null(opts.OverallTimeout);
        Assert.Null(opts.AttemptTimeout);
        Assert.NotNull(opts.Retry);
        Assert.NotNull(opts.Redirect);
    }

    [Fact]
    public void RetryOptions_Defaults_AreCorrect()
    {
        var retry = new RetryOptions();

        Assert.Equal(3, retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(200), retry.BaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), retry.MaxDelay);
        Assert.True(retry.HonorRetryAfter);
        Assert.False(retry.RetryNonIdempotentWhenReplayable);
    }

    [Fact]
    public void RedirectOptions_Defaults_AreCorrect()
    {
        var redirect = new RedirectOptions();

        Assert.Equal(20, redirect.MaxRedirects);
        Assert.False(redirect.AllowHttpsToHttpDowngrade);
        Assert.True(redirect.StripSensitiveHeadersOnCrossOrigin);
    }

    [Fact]
    public void DexpaceClientOptions_RetryAndRedirect_AreNonNullOnFreshInstance()
    {
        var opts = new DexpaceClientOptions();

        // Property bag objects must be initialized — not null — so callers can do
        // opts.Retry.MaxRetryAttempts = 5 without a null ref.
        Assert.NotNull(opts.Retry);
        Assert.NotNull(opts.Redirect);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj
```

Expected: build error — `Dexpace.Sdk.Core.Configuration` namespace not found.

- [ ] **Step 3: Create the implementation**

Create `src/Dexpace.Sdk.Core/Configuration/DexpaceClientOptions.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Reflection;

namespace Dexpace.Sdk.Core.Configuration;

/// <summary>
/// Top-level configuration options for the Dexpace SDK client.
/// </summary>
/// <remarks>
/// All properties carry sensible defaults; the client is fully usable with <c>new DexpaceClientOptions()</c>.
/// Per-policy sub-options are exposed as nested objects (<see cref="Retry"/>, <see cref="Redirect"/>).
/// </remarks>
public sealed class DexpaceClientOptions
{
    private static readonly string s_defaultUserAgent = BuildDefaultUserAgent();

    /// <summary>
    /// The base address prepended to relative request URLs, or <see langword="null"/> when
    /// requests always use absolute URLs.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// The <c>User-Agent</c> header value sent with every request.
    /// Defaults to <c>dexpace-dotnet/&lt;assembly-version&gt;</c>.
    /// </summary>
    public string UserAgent { get; set; } = s_defaultUserAgent;

    /// <summary>
    /// The wall-clock deadline for an entire operation (all redirect hops and retry attempts
    /// combined), or <see langword="null"/> for no overall deadline.
    /// </summary>
    public TimeSpan? OverallTimeout { get; set; }

    /// <summary>
    /// The deadline for a single send attempt, or <see langword="null"/> for no per-attempt deadline.
    /// </summary>
    public TimeSpan? AttemptTimeout { get; set; }

    /// <summary>
    /// Retry-policy options. Defaults to <see cref="RetryOptions"/> with its built-in defaults.
    /// </summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Redirect-policy options. Defaults to <see cref="RedirectOptions"/> with its built-in defaults.
    /// </summary>
    public RedirectOptions Redirect { get; set; } = new();

    private static string BuildDefaultUserAgent()
    {
        var version = typeof(DexpaceClientOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(DexpaceClientOptions).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // Strip git commit hash suffix (e.g. "0.0.1-alpha.1+abc123" → "0.0.1-alpha.1").
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            version = version[..plusIndex];
        }

        return $"dexpace-dotnet/{version}";
    }
}

/// <summary>
/// Options for the retry policy.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>
    /// The number of retry attempts after the initial send. Defaults to <c>3</c>.
    /// Matches the Polly v8 / <c>Microsoft.Extensions.Http.Resilience</c> naming convention.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// The base delay for exponential back-off. Defaults to <c>200 ms</c>.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The maximum back-off delay cap. Defaults to <c>30 s</c>.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <see langword="true"/>, the retry policy respects a <c>Retry-After</c> response
    /// header. Defaults to <see langword="true"/>.
    /// </summary>
    public bool HonorRetryAfter { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, the retry policy may retry non-idempotent methods (e.g.
    /// <c>POST</c>) if the request body is replayable. Defaults to <see langword="false"/>.
    /// </summary>
    public bool RetryNonIdempotentWhenReplayable { get; set; }
}

/// <summary>
/// Options for the redirect-following policy.
/// </summary>
public sealed class RedirectOptions
{
    /// <summary>
    /// The maximum number of redirect hops to follow. Defaults to <c>20</c>,
    /// matching browser and <c>HttpClient</c> norms.
    /// </summary>
    public int MaxRedirects { get; set; } = 20;

    /// <summary>
    /// When <see langword="true"/>, the policy follows <c>https → http</c> downgrade redirects.
    /// Defaults to <see langword="false"/> for security.
    /// </summary>
    public bool AllowHttpsToHttpDowngrade { get; set; }

    /// <summary>
    /// When <see langword="true"/>, sensitive headers (e.g. <c>Authorization</c>) are stripped
    /// when the redirect crosses an origin boundary. Defaults to <see langword="true"/>.
    /// </summary>
    public bool StripSensitiveHeadersOnCrossOrigin { get; set; } = true;
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj
```

Expected: all prior tests still pass, plus 4 new ones — total 34 passed, 0 failed.

- [ ] **Step 5: Verify full build is clean (0 warnings)**

```bash
dotnet build /Users/omar/dexpace/dotnet-sdk -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git -C /Users/omar/dexpace/dotnet-sdk add \
    src/Dexpace.Sdk.Core/Configuration/DexpaceClientOptions.cs \
    tests/Dexpace.Sdk.Core.Tests/Configuration/DexpaceClientOptionsTests.cs
git -C /Users/omar/dexpace/dotnet-sdk commit -m "feat: add DexpaceClientOptions and per-policy option types"
```

---

## Task B: Diagnostics ActivitySource and Meter

**Files:**
- Create: `src/Dexpace.Sdk.Core/Diagnostics/DexpaceDiagnostics.cs`
- Create: `tests/Dexpace.Sdk.Core.Tests/Diagnostics/DexpaceDiagnosticsTests.cs`

**Note on package:** `ActivitySource` lives in `System.Diagnostics` and `Meter` in `System.Diagnostics.Metrics`; both ship in the BCL on net8+ via `System.Diagnostics.DiagnosticSource`. Try building first — if the types are missing, add `System.Diagnostics.DiagnosticSource` to `Directory.Packages.props` and a `PackageReference` (no `Version`) in the Core `.csproj`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Dexpace.Sdk.Core.Tests/Diagnostics/DexpaceDiagnosticsTests.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Diagnostics;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Diagnostics;

public class DexpaceDiagnosticsTests
{
    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        Assert.Equal("Dexpace.Sdk", DexpaceDiagnostics.ActivitySource.Name);
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        Assert.Equal("Dexpace.Sdk", DexpaceDiagnostics.Meter.Name);
    }

    [Fact]
    public void ActivitySource_VersionIsNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(DexpaceDiagnostics.ActivitySource.Version));
    }

    [Fact]
    public void Meter_VersionIsNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(DexpaceDiagnostics.Meter.Version));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj
```

Expected: build error — `DexpaceDiagnostics` not found.

- [ ] **Step 3: Create the implementation**

Create `src/Dexpace.Sdk.Core/Diagnostics/DexpaceDiagnostics.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Dexpace.Sdk.Core.Diagnostics;

/// <summary>
/// Central home for the SDK's OpenTelemetry instrumentation objects.
/// </summary>
/// <remarks>
/// Consumers wire up collection by subscribing to <see cref="ActivitySource"/> (tracing) and
/// <see cref="Meter"/> (metrics) via their chosen OTel SDK — no SDK-internal configuration needed.
/// When no listener is active, <see cref="ActivitySource.StartActivity"/> returns
/// <see langword="null"/> and the hot path allocates nothing for tracing.
/// </remarks>
public static class DexpaceDiagnostics
{
    private static readonly string s_version = BuildVersion();

    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> used by the SDK for distributed tracing.
    /// Name: <c>"Dexpace.Sdk"</c>.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("Dexpace.Sdk", s_version);

    /// <summary>
    /// The <see cref="System.Diagnostics.Metrics.Meter"/> used by the SDK for metrics.
    /// Name: <c>"Dexpace.Sdk"</c>.
    /// </summary>
    public static readonly Meter Meter = new("Dexpace.Sdk", s_version);

    private static string BuildVersion()
    {
        var version = typeof(DexpaceDiagnostics).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(DexpaceDiagnostics).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // Strip git commit hash suffix (e.g. "0.0.1-alpha.1+abc123" → "0.0.1-alpha.1").
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }
}
```

- [ ] **Step 4: Attempt build — check if BCL types are available**

```bash
dotnet build /Users/omar/dexpace/dotnet-sdk -c Release
```

If build succeeds with 0 warnings, proceed to Step 5.

If build fails with `CS0234`/`CS0246` errors about `ActivitySource` or `Meter` not found:
1. Add to `Directory.Packages.props` inside the existing `<ItemGroup>`:
   ```xml
   <PackageVersion Include="System.Diagnostics.DiagnosticSource" Version="9.0.5" />
   ```
2. Add to `src/Dexpace.Sdk.Core/Dexpace.Sdk.Core.csproj` inside an `<ItemGroup>`:
   ```xml
   <PackageReference Include="System.Diagnostics.DiagnosticSource" />
   ```
3. Run `dotnet restore /Users/omar/dexpace/dotnet-sdk` then re-run the build.

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj
```

Expected: 4 new tests pass, total 38 (or 34 + 4 if Task A was committed), 0 failed.

- [ ] **Step 6: Commit**

```bash
git -C /Users/omar/dexpace/dotnet-sdk add \
    src/Dexpace.Sdk.Core/Diagnostics/DexpaceDiagnostics.cs \
    tests/Dexpace.Sdk.Core.Tests/Diagnostics/DexpaceDiagnosticsTests.cs
# Include package changes if they were needed:
# git -C /Users/omar/dexpace/dotnet-sdk add Directory.Packages.props src/Dexpace.Sdk.Core/Dexpace.Sdk.Core.csproj
git -C /Users/omar/dexpace/dotnet-sdk commit -m "feat: add Dexpace diagnostics ActivitySource and Meter"
```

---

## Task C: UrlRedactor

**Files:**
- Create: `src/Dexpace.Sdk.Core/Diagnostics/UrlRedactor.cs`
- Create: `tests/Dexpace.Sdk.Core.Tests/Diagnostics/UrlRedactorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Dexpace.Sdk.Core.Tests/Diagnostics/UrlRedactorTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj
```

Expected: build error — `UrlRedactor` not found.

- [ ] **Step 3: Create the implementation**

Create `src/Dexpace.Sdk.Core/Diagnostics/UrlRedactor.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Text;
using System.Web;

namespace Dexpace.Sdk.Core.Diagnostics;

/// <summary>
/// Produces a log-safe string form of a <see cref="Uri"/> by stripping userinfo and
/// replacing the values of known-sensitive query parameters with <c>REDACTED</c>.
/// </summary>
/// <remarks>
/// Sensitive parameter names are matched case-insensitively. The default set covers the most
/// common credential-bearing parameters; callers may supply a custom set instead.
/// Non-sensitive parameters and all path/host/scheme components are preserved verbatim.
/// </remarks>
public sealed class UrlRedactor
{
    /// <summary>
    /// Default set of query parameter names whose values are redacted.
    /// </summary>
    public static readonly IReadOnlyCollection<string> DefaultSensitiveParams =
    [
        "access_token",
        "token",
        "code",
        "sig",
        "signature",
        "api_key",
        "apikey",
        "password",
    ];

    private readonly HashSet<string> _sensitiveParams;

    /// <summary>
    /// Initializes a <see cref="UrlRedactor"/> using <see cref="DefaultSensitiveParams"/>.
    /// </summary>
    public UrlRedactor()
        : this(DefaultSensitiveParams)
    {
    }

    /// <summary>
    /// Initializes a <see cref="UrlRedactor"/> with a caller-supplied set of sensitive
    /// parameter names (case-insensitive).
    /// </summary>
    /// <param name="sensitiveParams">
    /// The query parameter names whose values should be replaced with <c>REDACTED</c>.
    /// </param>
    public UrlRedactor(IEnumerable<string> sensitiveParams)
    {
        ArgumentNullException.ThrowIfNull(sensitiveParams);
        _sensitiveParams = new HashSet<string>(sensitiveParams, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a log-safe representation of <paramref name="uri"/>: userinfo is always stripped;
    /// sensitive query parameter values are replaced with <c>REDACTED</c>.
    /// </summary>
    /// <param name="uri">The URI to redact.</param>
    /// <returns>A safe string representation.</returns>
    public string Redact(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var query = uri.Query;

        // Build the base URL without userinfo and without the query string.
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
        };
        var baseUrl = builder.Uri.GetLeftPart(UriPartial.Path);

        if (string.IsNullOrEmpty(query))
        {
            return baseUrl;
        }

        // Parse, redact, and re-serialize the query string.
        var parsed = HttpUtility.ParseQueryString(query);
        var sb = new StringBuilder();

        foreach (string? key in parsed)
        {
            if (key is null)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            var value = _sensitiveParams.Contains(key) ? "REDACTED" : parsed[key];
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(value is null ? string.Empty : Uri.EscapeDataString(value));
        }

        return sb.Length == 0 ? baseUrl : $"{baseUrl}?{sb}";
    }
}
```

- [ ] **Step 4: Attempt build — `System.Web` availability check**

`HttpUtility.ParseQueryString` lives in `System.Web`. On net8+ it is in-box. Build to verify:

```bash
dotnet build /Users/omar/dexpace/dotnet-sdk -c Release
```

If build fails with `CS0234` on `System.Web`, replace the `HttpUtility.ParseQueryString` approach with the manual query-string parser below (drop `using System.Web;`, use this helper instead):

```csharp
// Drop-in replacement for HttpUtility.ParseQueryString — no System.Web needed.
private static IEnumerable<(string Key, string Value)> ParseQueryParams(string query)
{
    var span = query.AsSpan().TrimStart('?');
    foreach (var segment in span.Split('&'))
    {
        var part = span[segment];
        var eq = part.IndexOf('=');
        if (eq < 0) continue;
        yield return (
            Uri.UnescapeDataString(part[..eq].ToString()),
            Uri.UnescapeDataString(part[(eq + 1)..].ToString()));
    }
}
```

And replace the `ParseQueryString`/`foreach` block in `Redact` with:

```csharp
var sb = new StringBuilder();
foreach (var (key, value) in ParseQueryParams(query))
{
    if (sb.Length > 0) sb.Append('&');
    var redactedValue = _sensitiveParams.Contains(key) ? "REDACTED" : value;
    sb.Append(Uri.EscapeDataString(key));
    sb.Append('=');
    sb.Append(Uri.EscapeDataString(redactedValue));
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj
```

Expected: 6 new tests pass, 0 failed, total grows by 6.

- [ ] **Step 6: Commit**

```bash
git -C /Users/omar/dexpace/dotnet-sdk add \
    src/Dexpace.Sdk.Core/Diagnostics/UrlRedactor.cs \
    tests/Dexpace.Sdk.Core.Tests/Diagnostics/UrlRedactorTests.cs
git -C /Users/omar/dexpace/dotnet-sdk commit -m "feat: add UrlRedactor for log-safe URLs"
```

---

## Task D: PipelineContext

**Files:**
- Create: `src/Dexpace.Sdk.Core/Pipeline/PipelineContext.cs`
- Create: `tests/Dexpace.Sdk.Core.Tests/Pipeline/PipelineContextTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Dexpace.Sdk.Core.Tests/Pipeline/PipelineContextTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj
```

Expected: build error — `PipelineContext` not found.

- [ ] **Step 3: Create the implementation**

Create `src/Dexpace.Sdk.Core/Pipeline/PipelineContext.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Diagnostics;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;

namespace Dexpace.Sdk.Core.Pipeline;

/// <summary>
/// Carries all per-call mutable state as it flows through the pipeline delegate chain.
/// </summary>
/// <remarks>
/// One instance is created per client call and passed to every policy in the chain. Policies
/// read and replace <see cref="Request"/> (for redirect/auth rewriting), stash cross-policy
/// coordination data in the property bag (see <see cref="GetProperty{T}"/> /
/// <see cref="SetProperty{T}"/>), and observe <see cref="Response"/> once the transport has
/// responded.
/// </remarks>
public sealed class PipelineContext
{
    private Dictionary<string, object?>? _properties;

    /// <summary>
    /// Initializes a new <see cref="PipelineContext"/> for a single client call.
    /// </summary>
    /// <param name="request">The initial request. Policies may replace it during the call.</param>
    /// <param name="options">A snapshot of the client options for this call.</param>
    /// <param name="cancellationToken">
    /// An optional cancellation token that can abort the call.
    /// </param>
    public PipelineContext(
        Request request,
        DexpaceClientOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        Request = request;
        Options = options;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// The current request. Policies (e.g. redirect, auth) may replace this during the call.
    /// </summary>
    public Request Request { get; set; }

    /// <summary>
    /// The response from the transport, or <see langword="null"/> before the transport responds.
    /// Set by the pipeline after the outermost transport send completes.
    /// </summary>
    public Response? Response { get; internal set; }

    /// <summary>
    /// The active SDK tracing span, or <see langword="null"/> when no <see cref="ActivitySource"/>
    /// listener is registered (near-zero overhead when unobserved).
    /// </summary>
    public Activity? Activity { get; internal set; }

    /// <summary>
    /// A snapshot of the client options that applies to this call.
    /// </summary>
    public DexpaceClientOptions Options { get; }

    /// <summary>
    /// A token that can cancel the in-flight operation.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// The zero-based retry attempt counter. <c>0</c> on the initial send;
    /// incremented by the retry policy before each subsequent attempt.
    /// </summary>
    public int AttemptNumber { get; internal set; }

    /// <summary>
    /// Retrieves a typed value from the cross-policy property bag.
    /// </summary>
    /// <typeparam name="T">The expected type of the stored value.</typeparam>
    /// <param name="key">The key used when the value was stored.</param>
    /// <returns>
    /// The stored value cast to <typeparamref name="T"/>, or <see langword="default"/>
    /// if the key is absent or the stored value cannot be cast.
    /// </returns>
    public T? GetProperty<T>(string key)
    {
        if (_properties is null || !_properties.TryGetValue(key, out var value))
        {
            return default;
        }

        return value is T typed ? typed : default;
    }

    /// <summary>
    /// Stores a typed value in the cross-policy property bag.
    /// Overwrites any existing value for <paramref name="key"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value to store.</typeparam>
    /// <param name="key">A key that identifies the value within this call.</param>
    /// <param name="value">The value to store.</param>
    public void SetProperty<T>(string key, T value)
    {
        _properties ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        _properties[key] = value;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj
```

Expected: 10 new tests pass, 0 failed.

- [ ] **Step 5: Final build verification (0 warnings)**

```bash
dotnet build /Users/omar/dexpace/dotnet-sdk -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git -C /Users/omar/dexpace/dotnet-sdk add \
    src/Dexpace.Sdk.Core/Pipeline/PipelineContext.cs \
    tests/Dexpace.Sdk.Core.Tests/Pipeline/PipelineContextTests.cs
git -C /Users/omar/dexpace/dotnet-sdk commit -m "feat: add PipelineContext for per-call pipeline state"
```

---

## Final verification

- [ ] **Run full build one last time**

```bash
dotnet build /Users/omar/dexpace/dotnet-sdk -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Run the full Core test suite on net10.0**

```bash
dotnet test -c Release -f net10.0 /Users/omar/dexpace/dotnet-sdk/tests/Dexpace.Sdk.Core.Tests/Dexpace.Sdk.Core.Tests.csproj
```

Expected: all prior tests pass, plus ~24 new ones (4+4+6+10). 0 failures.
