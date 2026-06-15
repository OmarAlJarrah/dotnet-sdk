# DI / hosting integration — slice design

- **Date:** 2026-06-14
- **Status:** Approved; ready for implementation planning.
- **Part of:** [.NET SDK Platform Architecture & Build Plan](2026-06-14-dotnet-sdk-platform-design.md) — slice 10.

## 1. Purpose & scope

Wire the toolkit into a host's DI container the idiomatic way, tying together Options, `ILogger`,
`IHttpClientFactory`, the pipeline, the transport, and the serde.

**In scope:** the `Dexpace.Sdk.Extensions.DependencyInjection` package — `AddDexpaceClient`, a fluent
builder, options binding + validation, and `IHttpClientFactory` integration.

**Out of scope:** the per-package serde/transport sugar that lives in those packages.

## 2. Decisions

- **`AddDexpaceClient(...)` returns a fluent `IDexpaceClientBuilder`.**
- **Options bound + validated with `ValidateOnStart`** so misconfiguration fails at host startup.
- **`IHttpClientFactory` integration** — `UseHttpClientFactory(name)` constructs the transport from a
  named `HttpClient`, so an enterprise's `DelegatingHandler` / Polly chain composes underneath the SDK
  pipeline.
- **Registers the `HttpPipeline`** (assembled from the default policies plus any configured auth) as
  the injectable entry point; a thin `DexpaceClient` facade over it is optional (open item).

## 3. Surface (sketch)

```csharp
public static IDexpaceClientBuilder AddDexpaceClient(
    this IServiceCollection services, Action<DexpaceClientOptions>? configure = null);

public interface IDexpaceClientBuilder
{
    IServiceCollection Services { get; }
    IDexpaceClientBuilder BindConfiguration(string sectionName);          // env + appsettings + Key Vault
    IDexpaceClientBuilder UseTransport(Func<IServiceProvider, IAsyncHttpClient> factory);
    IDexpaceClientBuilder UseHttpClientFactory(string name);              // System.Net transport over a named HttpClient
    IDexpaceClientBuilder ConfigurePipeline(Action<PipelineBuilder> configure);
    IDexpaceClientBuilder AddBearerToken(TokenCredential credential, params string[] scopes);
    IDexpaceClientBuilder AddApiKey(string key, HttpHeaderName? header = null, string? scheme = null);
    IDexpaceClientBuilder AddBasicAuth(string username, string password);
}
```

Usage:

```csharp
services.AddSystemTextJsonSerde(MyJsonContext.Default);     // from the STJ package
services.AddDexpaceClient(o => o.Retry.MaxRetryAttempts = 5)
        .BindConfiguration("Dexpace")
        .UseHttpClientFactory("dexpace")                    // or .UseSystemNetTransport() from the transport package
        .AddBearerToken(credential, "https://api.example.com/.default");
```

## 4. Package dependencies

- References: `Core`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
  `Microsoft.Extensions.Http` (for `IHttpClientFactory`), `Microsoft.Extensions.Options`,
  `Microsoft.Extensions.Options.ConfigurationExtensions`.
- The reference transport sugar (`UseSystemNetTransport`) and serde sugar (`AddSystemTextJsonSerde`)
  live in their own packages; `UseTransport(...)` keeps the builder usable without referencing any
  specific transport.

## 5. What gets registered

- `IOptions<DexpaceClientOptions>` (+ `IValidateOptions<>`, `ValidateOnStart`).
- `ISerde` (resolved from whatever serde was registered).
- `IAsyncHttpClient` transport.
- `HttpPipeline` assembled via `DexpacePipeline.CreateDefault` plus configured auth and any
  `ConfigurePipeline` edits; `ILogger` injected from the container.

## 6. Project & repo changes

- New `src/Dexpace.Sdk.Extensions.DependencyInjection/` (multi-target, AOT-friendly registration — no
  reflection-based scanning).
- New `tests/Dexpace.Sdk.Extensions.DependencyInjection.Tests/`.
- `Directory.Packages.props`: add the `Microsoft.Extensions.*` versions used here.

## 7. Open items (resolve during planning)

- Whether to ship a thin `DexpaceClient` facade (over `HttpPipeline.SendAsync` + pagination helpers)
  or register `HttpPipeline` directly as the entry point.
- Exact location of the `UseSystemNetTransport` extension (transport package referencing the builder
  interface vs. the DI package referencing the transport).
- Keyed/named multi-client registration (more than one configured Dexpace client per container).
