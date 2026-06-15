# Serde + System.Text.Json Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the `ISerde` serialization seam in `Core` and a trim/AOT-safe `System.Text.Json` implementation, plus the request/response body conveniences and the typed-error accessor that depend on it.

**Architecture:** `Core` declares a generic, context-backed `ISerde` (no third-party dependency). A new `Dexpace.Sdk.Serialization.SystemTextJson` package implements it over source-generated `JsonTypeInfo<T>` — no runtime reflection. Body conveniences (`RequestBody.FromValue<T>`, `ResponseBody.ReadValueAsync<T>`) and `HttpResponseException.GetErrorAsync<T>` route through `ISerde`.

**Tech Stack:** C# (`net8.0` for `Core`; `net8.0;net10.0` for the new package), System.Text.Json (in-box, source generators), xUnit.

**Scope notes (deliberately excluded):**
- **Strong-naming/signing** and **retargeting the existing libraries to `net8.0;net10.0`** are a separate "conventions" slice — this plan multi-targets and AOT-validates only the *new* package, leaving `Core`/`SystemNet` at `net8.0`.
- **`AddSystemTextJsonSerde` DI helper** lands with the DI integration slice (10).
- **`Optional<T>`/PATCH** is deferred (issue #1).
- The **error-body buffering** that makes `GetErrorAsync<T>` work post-throw is the policies slice (5); here the accessor reads whatever replayable body the exception already carries.

---

## File Structure

**New files:**
- `src/Dexpace.Sdk.Serialization.SystemTextJson/Dexpace.Sdk.Serialization.SystemTextJson.csproj` — the package.
- `src/Dexpace.Sdk.Serialization.SystemTextJson/SystemTextJsonSerde.cs` — the `ISerde` implementation.
- `src/Dexpace.Sdk.Core/Serialization/ISerde.cs` — the seam.
- `src/Dexpace.Sdk.Core/Serialization/ResponseBodySerdeExtensions.cs` — `ReadValueAsync<T>`.
- `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests.csproj`
- `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/TestModels.cs` — sample models + source-gen context.
- `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/SystemTextJsonSerdeTests.cs`
- `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/BodyConvenienceTests.cs`

**Modified files:**
- `Dexpace.Sdk.sln` — add the two new projects.
- `src/Dexpace.Sdk.Core/Http/Request/RequestBody.cs` — add `FromValue<T>`.
- `src/Dexpace.Sdk.Core/Errors/TransportExceptions.cs` — add `HttpResponseException.GetErrorAsync<T>`.

---

## Task 1: Scaffold the System.Text.Json package and its test project

**Files:**
- Create: `src/Dexpace.Sdk.Serialization.SystemTextJson/Dexpace.Sdk.Serialization.SystemTextJson.csproj`
- Create: `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests.csproj`
- Modify: `Dexpace.Sdk.sln`

- [ ] **Step 1: Create the library project file**

`src/Dexpace.Sdk.Serialization.SystemTextJson/Dexpace.Sdk.Serialization.SystemTextJson.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <RootNamespace>Dexpace.Sdk.Serialization.SystemTextJson</RootNamespace>
    <AssemblyName>Dexpace.Sdk.Serialization.SystemTextJson</AssemblyName>
    <PackageId>Dexpace.Sdk.Serialization.SystemTextJson</PackageId>
    <Description>
      System.Text.Json implementation of the Dexpace.Sdk.Core ISerde serialization seam,
      built on source-generated JsonSerializerContext metadata for trim- and AOT-safety.
    </Description>
    <PackageTags>http;sdk;json;serialization;dexpace</PackageTags>
    <IsPackable>true</IsPackable>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Dexpace.Sdk.Core\Dexpace.Sdk.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Dexpace.Sdk.Serialization.SystemTextJson.Tests" />
  </ItemGroup>

</Project>
```

> System.Text.Json is in-box on `net8.0`/`net10.0`; no `PackageReference` (and no `Directory.Packages.props` entry) is needed.

- [ ] **Step 2: Create the test project file**

`tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <RootNamespace>Dexpace.Sdk.Serialization.SystemTextJson.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Dexpace.Sdk.Core\Dexpace.Sdk.Core.csproj" />
    <ProjectReference Include="..\..\src\Dexpace.Sdk.Serialization.SystemTextJson\Dexpace.Sdk.Serialization.SystemTextJson.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Add both projects to the solution**

Run:
```bash
dotnet sln Dexpace.Sdk.sln add \
  src/Dexpace.Sdk.Serialization.SystemTextJson/Dexpace.Sdk.Serialization.SystemTextJson.csproj \
  tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests.csproj
```
Expected: `Project ... added to the solution.` (twice)

- [ ] **Step 4: Verify the solution restores and builds**

Run: `dotnet build -c Release`
Expected: PASS (the new projects compile as empty assemblies; no warnings).

- [ ] **Step 5: Commit**

```bash
git add Dexpace.Sdk.sln src/Dexpace.Sdk.Serialization.SystemTextJson tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests
git commit -m "chore: scaffold Dexpace.Sdk.Serialization.SystemTextJson project"
```

---

## Task 2: Declare the `ISerde` seam in Core

**Files:**
- Create: `src/Dexpace.Sdk.Core/Serialization/ISerde.cs`

- [ ] **Step 1: Write the interface**

`src/Dexpace.Sdk.Core/Serialization/ISerde.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Buffers;
using Dexpace.Sdk.Core.Http.Common;

namespace Dexpace.Sdk.Core.Serialization;

/// <summary>
/// Serializes values into, and deserializes them out of, request/response payloads. The seam is
/// generic and serializer-agnostic; implementations are responsible for resolving type metadata
/// (a System.Text.Json implementation ships separately).
/// </summary>
public interface ISerde
{
    /// <summary>The media type stamped on bodies created from values (for example, application/json).</summary>
    MediaType DefaultMediaType { get; }

    /// <summary>Serializes <paramref name="value"/> to <paramref name="destination"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when serialization finishes.</returns>
    /// <exception cref="Errors.SerializationException">Serialization failed.</exception>
    ValueTask SerializeAsync<T>(Stream destination, T value, CancellationToken cancellationToken = default);

    /// <summary>Deserializes a value of type <typeparamref name="T"/> from <paramref name="source"/>.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="source">The stream to read from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized value, or <see langword="null"/>.</returns>
    /// <exception cref="Errors.DeserializationException">Deserialization failed.</exception>
    ValueTask<T?> DeserializeAsync<T>(Stream source, CancellationToken cancellationToken = default);

    /// <summary>Serializes <paramref name="value"/> synchronously to <paramref name="destination"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="destination">The buffer writer to write to.</param>
    /// <param name="value">The value to serialize.</param>
    /// <exception cref="Errors.SerializationException">Serialization failed.</exception>
    void Serialize<T>(IBufferWriter<byte> destination, T value);

    /// <summary>Deserializes a value of type <typeparamref name="T"/> from a UTF-8 buffer.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="utf8">The UTF-8 encoded payload.</param>
    /// <returns>The deserialized value, or <see langword="null"/>.</returns>
    /// <exception cref="Errors.DeserializationException">Deserialization failed.</exception>
    T? Deserialize<T>(ReadOnlySpan<byte> utf8);
}
```

- [ ] **Step 2: Build Core**

Run: `dotnet build -c Release src/Dexpace.Sdk.Core/Dexpace.Sdk.Core.csproj`
Expected: PASS (no missing-doc warnings; the interface is fully documented).

- [ ] **Step 3: Commit**

```bash
git add src/Dexpace.Sdk.Core/Serialization/ISerde.cs
git commit -m "feat: add ISerde serialization seam to core"
```

---

## Task 3: `SystemTextJsonSerde` async round-trip

**Files:**
- Create: `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/TestModels.cs`
- Create: `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/SystemTextJsonSerdeTests.cs`
- Create: `src/Dexpace.Sdk.Serialization.SystemTextJson/SystemTextJsonSerde.cs`

- [ ] **Step 1: Write the test models and source-gen context**

`tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/TestModels.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Text.Json.Serialization;

namespace Dexpace.Sdk.Serialization.SystemTextJson.Tests;

public sealed record Widget(string Name, int Size);

public sealed record ApiError(string Code, string Message);

[JsonSerializable(typeof(Widget))]
[JsonSerializable(typeof(ApiError))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
```

- [ ] **Step 2: Write the failing async round-trip test**

`tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/SystemTextJsonSerdeTests.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Http.Common;
using Xunit;

namespace Dexpace.Sdk.Serialization.SystemTextJson.Tests;

public sealed class SystemTextJsonSerdeTests
{
    private static SystemTextJsonSerde Serde() => new(TestJsonContext.Default);

    [Fact]
    public async Task SerializeAsync_then_DeserializeAsync_round_trips()
    {
        var serde = Serde();
        var widget = new Widget("gizmo", 42);

        using var stream = new MemoryStream();
        await serde.SerializeAsync(stream, widget);
        stream.Position = 0;
        var result = await serde.DeserializeAsync<Widget>(stream);

        Assert.Equal(widget, result);
    }

    [Fact]
    public void DefaultMediaType_is_application_json_utf8()
    {
        Assert.Equal(CommonMediaTypes.ApplicationJsonUtf8, Serde().DefaultMediaType);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests`
Expected: FAILS to compile — `The type or namespace name 'SystemTextJsonSerde' could not be found`.

- [ ] **Step 4: Implement `SystemTextJsonSerde`**

`src/Dexpace.Sdk.Serialization.SystemTextJson/SystemTextJsonSerde.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dexpace.Sdk.Core.Errors;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Serialization;

namespace Dexpace.Sdk.Serialization.SystemTextJson;

/// <summary>
/// A <see cref="ISerde"/> implementation backed by System.Text.Json. Type metadata is resolved from
/// a source-generated <see cref="JsonSerializerContext"/>, keeping serialization trim- and
/// NativeAOT-safe with no runtime reflection.
/// </summary>
public sealed class SystemTextJsonSerde : ISerde
{
    private readonly JsonSerializerOptions _options;

    /// <summary>Initializes a new instance from explicit options.</summary>
    /// <param name="options">
    /// Options whose <see cref="JsonSerializerOptions.TypeInfoResolver"/> is set (typically a
    /// source-generated <see cref="JsonSerializerContext"/>).
    /// </param>
    /// <exception cref="ArgumentException">The options have no type-info resolver.</exception>
    public SystemTextJsonSerde(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TypeInfoResolver is null)
        {
            throw new ArgumentException(
                "The JsonSerializerOptions must have a TypeInfoResolver (for example, a source-generated "
                + "JsonSerializerContext) for AOT-safe serialization.",
                nameof(options));
        }

        options.MakeReadOnly();
        _options = options;
    }

    /// <summary>Initializes a new instance from a source-generated context.</summary>
    /// <param name="context">The source-generated serializer context.</param>
    public SystemTextJsonSerde(JsonSerializerContext context)
        : this((context ?? throw new ArgumentNullException(nameof(context))).Options)
    {
    }

    /// <inheritdoc/>
    public MediaType DefaultMediaType => CommonMediaTypes.ApplicationJsonUtf8;

    /// <inheritdoc/>
    public async ValueTask SerializeAsync<T>(Stream destination, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var info = GetTypeInfo<T>(forSerialize: true);
        await JsonSerializer.SerializeAsync(destination, value, info, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<T?> DeserializeAsync<T>(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var info = GetTypeInfo<T>(forSerialize: false);
        return await JsonSerializer.DeserializeAsync(source, info, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Serialize<T>(IBufferWriter<byte> destination, T value) =>
        throw new NotSupportedException("Implemented in a later task.");

    /// <inheritdoc/>
    public T? Deserialize<T>(ReadOnlySpan<byte> utf8) =>
        throw new NotSupportedException("Implemented in a later task.");

    private JsonTypeInfo<T> GetTypeInfo<T>(bool forSerialize)
    {
        if (_options.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> info)
        {
            return info;
        }

        return forSerialize
            ? throw new SerializationException(TypeInfoMessage<T>())
            : throw new DeserializationException(TypeInfoMessage<T>());
    }

    private static string TypeInfoMessage<T>() =>
        $"No JsonTypeInfo is registered for '{typeof(T)}'. Add it to a source-generated "
        + "JsonSerializerContext supplied to SystemTextJsonSerde.";
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests`
Expected: PASS (both tests, on `net8.0` and `net10.0`).

- [ ] **Step 6: Commit**

```bash
git add src/Dexpace.Sdk.Serialization.SystemTextJson/SystemTextJsonSerde.cs tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/TestModels.cs tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/SystemTextJsonSerdeTests.cs
git commit -m "feat: add SystemTextJsonSerde with async serialize/deserialize"
```

---

## Task 4: Synchronous serialize/deserialize

**Files:**
- Modify: `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/SystemTextJsonSerdeTests.cs`
- Modify: `src/Dexpace.Sdk.Serialization.SystemTextJson/SystemTextJsonSerde.cs:Serialize,Deserialize`

- [ ] **Step 1: Write the failing sync round-trip test**

Add to `SystemTextJsonSerdeTests.cs`:

```csharp
    [Fact]
    public void Serialize_then_Deserialize_sync_round_trips()
    {
        var serde = Serde();
        var widget = new Widget("sprocket", 7);

        var buffer = new ArrayBufferWriter<byte>();
        serde.Serialize(buffer, widget);
        var result = serde.Deserialize<Widget>(buffer.WrittenSpan);

        Assert.Equal(widget, result);
    }
```

Add `using System.Buffers;` to the file's usings.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~Serialize_then_Deserialize_sync_round_trips"`
Expected: FAIL — `System.NotSupportedException : Implemented in a later task.`

- [ ] **Step 3: Implement the sync methods**

In `SystemTextJsonSerde.cs`, replace the two `NotSupportedException` bodies with:

```csharp
    /// <inheritdoc/>
    public void Serialize<T>(IBufferWriter<byte> destination, T value)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var info = GetTypeInfo<T>(forSerialize: true);
        using var writer = new Utf8JsonWriter(destination);
        JsonSerializer.Serialize(writer, value, info);
    }

    /// <inheritdoc/>
    public T? Deserialize<T>(ReadOnlySpan<byte> utf8)
    {
        var info = GetTypeInfo<T>(forSerialize: false);
        return JsonSerializer.Deserialize(utf8, info);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~Serialize_then_Deserialize_sync_round_trips"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Dexpace.Sdk.Serialization.SystemTextJson/SystemTextJsonSerde.cs tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/SystemTextJsonSerdeTests.cs
git commit -m "feat: add synchronous serialize/deserialize to SystemTextJsonSerde"
```

---

## Task 5: Error mapping (unknown type, malformed JSON)

**Files:**
- Modify: `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/SystemTextJsonSerdeTests.cs`
- Modify: `src/Dexpace.Sdk.Serialization.SystemTextJson/SystemTextJsonSerde.cs` (wrap JSON failures)

- [ ] **Step 1: Write the failing error tests**

Add to `SystemTextJsonSerdeTests.cs`:

```csharp
    private sealed record Unregistered(string Value);

    [Fact]
    public void Deserialize_unknown_type_throws_DeserializationException()
    {
        var serde = Serde();
        var ex = Assert.Throws<DeserializationException>(
            () => serde.Deserialize<Unregistered>("{}"u8));
        Assert.Contains("Unregistered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_malformed_json_throws_DeserializationException()
    {
        var serde = Serde();
        Assert.Throws<DeserializationException>(() => serde.Deserialize<Widget>("{ not json"u8));
    }
```

Add `using Dexpace.Sdk.Core.Errors;` to the file's usings.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~Deserialize_malformed_json_throws_DeserializationException"`
Expected: FAIL — a raw `System.Text.Json.JsonException` is thrown instead of `DeserializationException`.

(The unknown-type test already passes from Task 3's `GetTypeInfo` mapping; the malformed-json test drives this task.)

- [ ] **Step 3: Wrap System.Text.Json failures**

In `SystemTextJsonSerde.cs`, wrap each `JsonSerializer` call so `JsonException` maps to the SDK type. Replace the four method bodies' serializer calls as follows:

```csharp
    /// <inheritdoc/>
    public async ValueTask SerializeAsync<T>(Stream destination, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var info = GetTypeInfo<T>(forSerialize: true);
        try
        {
            await JsonSerializer.SerializeAsync(destination, value, info, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new SerializationException($"Failed to serialize '{typeof(T)}' to JSON.", ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<T?> DeserializeAsync<T>(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var info = GetTypeInfo<T>(forSerialize: false);
        try
        {
            return await JsonSerializer.DeserializeAsync(source, info, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new DeserializationException($"Failed to deserialize JSON to '{typeof(T)}'.", ex);
        }
    }

    /// <inheritdoc/>
    public void Serialize<T>(IBufferWriter<byte> destination, T value)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var info = GetTypeInfo<T>(forSerialize: true);
        using var writer = new Utf8JsonWriter(destination);
        try
        {
            JsonSerializer.Serialize(writer, value, info);
        }
        catch (JsonException ex)
        {
            throw new SerializationException($"Failed to serialize '{typeof(T)}' to JSON.", ex);
        }
    }

    /// <inheritdoc/>
    public T? Deserialize<T>(ReadOnlySpan<byte> utf8)
    {
        var info = GetTypeInfo<T>(forSerialize: false);
        try
        {
            return JsonSerializer.Deserialize(utf8, info);
        }
        catch (JsonException ex)
        {
            throw new DeserializationException($"Failed to deserialize JSON to '{typeof(T)}'.", ex);
        }
    }
```

- [ ] **Step 4: Run the error tests to verify they pass**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~Deserialize"`
Expected: PASS (unknown-type and malformed-json both throw the SDK exception types).

- [ ] **Step 5: Commit**

```bash
git add src/Dexpace.Sdk.Serialization.SystemTextJson/SystemTextJsonSerde.cs tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/SystemTextJsonSerdeTests.cs
git commit -m "feat: map System.Text.Json failures to SDK serialization exceptions"
```

---

## Task 6: `RequestBody.FromValue<T>`

**Files:**
- Create: `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/BodyConvenienceTests.cs`
- Modify: `src/Dexpace.Sdk.Core/Http/Request/RequestBody.cs`

- [ ] **Step 1: Write the failing test**

`tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/BodyConvenienceTests.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
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
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~FromValue_produces_replayable_json_body"`
Expected: FAILS to compile — `'RequestBody' does not contain a definition for 'FromValue'`.

- [ ] **Step 3: Implement `FromValue<T>`**

In `src/Dexpace.Sdk.Core/Http/Request/RequestBody.cs`, add `using System.Buffers;` and `using Dexpace.Sdk.Core.Serialization;` to the usings, then add this static factory after `FromStream`:

```csharp
    /// <summary>
    /// Creates a replayable body by serializing <paramref name="value"/> with <paramref name="serde"/>.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="serde">The serializer.</param>
    /// <param name="contentType">The media type, or <see langword="null"/> for the serde's default.</param>
    /// <returns>A replayable <see cref="RequestBody"/>.</returns>
    public static RequestBody FromValue<T>(T value, ISerde serde, MediaType? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(serde);
        var buffer = new ArrayBufferWriter<byte>();
        serde.Serialize(buffer, value);
        return FromBytes(buffer.WrittenMemory, contentType ?? serde.DefaultMediaType);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~FromValue_produces_replayable_json_body"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Dexpace.Sdk.Core/Http/Request/RequestBody.cs tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/BodyConvenienceTests.cs
git commit -m "feat: add RequestBody.FromValue serde convenience"
```

---

## Task 7: `ResponseBody.ReadValueAsync<T>`

**Files:**
- Create: `src/Dexpace.Sdk.Core/Serialization/ResponseBodySerdeExtensions.cs`
- Modify: `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/BodyConvenienceTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `BodyConvenienceTests.cs` (and add `using System.Text; using Dexpace.Sdk.Core.Errors; using Dexpace.Sdk.Core.Http.Response; using Dexpace.Sdk.Core.Serialization;` to the usings):

```csharp
    [Fact]
    public async Task ReadValueAsync_deserializes_the_body()
    {
        var json = Encoding.UTF8.GetBytes("""{"name":"bolt","size":3}""");
        var body = ResponseBody.FromBytes(json, CommonMediaTypes.ApplicationJson);

        var widget = await body.ReadValueAsync<Widget>(Serde());

        Assert.Equal(new Widget("bolt", 3), widget);
    }

    [Fact]
    public async Task ReadValueAsync_is_single_use()
    {
        var json = Encoding.UTF8.GetBytes("""{"name":"bolt","size":3}""");
        var body = ResponseBody.FromBytes(json, CommonMediaTypes.ApplicationJson);

        await body.ReadValueAsync<Widget>(Serde());
        await Assert.ThrowsAsync<StreamConsumedException>(
            async () => await body.ReadValueAsync<Widget>(Serde()));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~ReadValueAsync"`
Expected: FAILS to compile — `'ResponseBody' does not contain a definition for 'ReadValueAsync'`.

- [ ] **Step 3: Implement the extension**

`src/Dexpace.Sdk.Core/Serialization/ResponseBodySerdeExtensions.cs`:

```csharp
// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Http.Response;

namespace Dexpace.Sdk.Core.Serialization;

/// <summary>Serialization conveniences over <see cref="ResponseBody"/>.</summary>
public static class ResponseBodySerdeExtensions
{
    /// <summary>Reads and deserializes the body as <typeparamref name="T"/> using <paramref name="serde"/>.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="body">The response body (read once).</param>
    /// <param name="serde">The serializer.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The deserialized value, or <see langword="null"/>.</returns>
    /// <exception cref="Errors.StreamConsumedException">The body has already been read.</exception>
    /// <exception cref="Errors.DeserializationException">Deserialization failed.</exception>
    public static async ValueTask<T?> ReadValueAsync<T>(
        this ResponseBody body, ISerde serde, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(serde);

        await using var stream = await body.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        return await serde.DeserializeAsync<T>(stream, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~ReadValueAsync"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Dexpace.Sdk.Core/Serialization/ResponseBodySerdeExtensions.cs tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/BodyConvenienceTests.cs
git commit -m "feat: add ResponseBody.ReadValueAsync serde convenience"
```

---

## Task 8: `HttpResponseException.GetErrorAsync<T>`

**Files:**
- Modify: `src/Dexpace.Sdk.Core/Errors/TransportExceptions.cs`
- Modify: `tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/BodyConvenienceTests.cs`

> Note: `Response.Body` is `ResponseBody` (never null — empty bodies are buffered). Constructor: `new Response(Status status, Headers? headers = null, ResponseBody? body = null, Protocol protocol = Protocol.Http11)`. Confirmed against `src/Dexpace.Sdk.Core/Http/Response/Response.cs`.

- [ ] **Step 1: Write the failing tests**

Add to `BodyConvenienceTests.cs`:

```csharp
    [Fact]
    public async Task GetErrorAsync_deserializes_the_buffered_error_body()
    {
        var json = Encoding.UTF8.GetBytes("""{"code":"rate_limited","message":"slow down"}""");
        var response = new Response(Status.TooManyRequests, Headers.Empty,
            ResponseBody.FromBytes(json, CommonMediaTypes.ApplicationJson));
        var ex = new HttpResponseException(response);

        var error = await ex.GetErrorAsync<ApiError>(Serde());

        Assert.Equal(new ApiError("rate_limited", "slow down"), error);
    }

    [Fact]
    public async Task GetErrorAsync_throws_when_body_already_consumed()
    {
        var json = Encoding.UTF8.GetBytes("""{"code":"x","message":"y"}""");
        var response = new Response(Status.BadRequest, Headers.Empty,
            ResponseBody.FromBytes(json, CommonMediaTypes.ApplicationJson));
        var ex = new HttpResponseException(response);

        await ex.GetErrorAsync<ApiError>(Serde());
        await Assert.ThrowsAsync<ResponseNotReadException>(
            async () => await ex.GetErrorAsync<ApiError>(Serde()));
    }
```

Add `using Dexpace.Sdk.Core.Http.Response;` if not already present.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~GetErrorAsync"`
Expected: FAILS to compile — `'HttpResponseException' does not contain a definition for 'GetErrorAsync'`.

- [ ] **Step 3: Implement `GetErrorAsync<T>`**

In `src/Dexpace.Sdk.Core/Errors/TransportExceptions.cs`, add `using Dexpace.Sdk.Core.Serialization;` to the usings, then add this method to the `HttpResponseException` class (after the `Status` property):

```csharp
    /// <summary>
    /// Deserializes the error response body as <typeparamref name="T"/> using <paramref name="serde"/>.
    /// </summary>
    /// <typeparam name="T">The error model type.</typeparam>
    /// <param name="serde">The serializer.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The deserialized error model (possibly <see langword="null"/>).</returns>
    /// <exception cref="ResponseNotReadException">The error body has already been consumed.</exception>
    /// <exception cref="DeserializationException">Deserialization failed.</exception>
    public async ValueTask<T?> GetErrorAsync<T>(ISerde serde, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serde);
        try
        {
            await using var stream = await Response.Body.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            return await serde.DeserializeAsync<T>(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (StreamConsumedException ex)
        {
            throw new ResponseNotReadException(
                "The error response body has already been consumed and cannot be deserialized.", ex);
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test -c Release tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests --filter "FullyQualifiedName~GetErrorAsync"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Dexpace.Sdk.Core/Errors/TransportExceptions.cs tests/Dexpace.Sdk.Serialization.SystemTextJson.Tests/BodyConvenienceTests.cs
git commit -m "feat: add HttpResponseException.GetErrorAsync typed-error accessor"
```

---

## Task 9: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution under the lint gate**

Run: `dotnet build -c Release`
Expected: PASS — no warnings (warnings are errors), including the trim/AOT analyzer on the new package and the missing-doc gate.

- [ ] **Step 2: Run the whole test suite**

Run: `dotnet test -c Release`
Expected: PASS — all existing tests plus the new serde tests, across `net8.0` and `net10.0`.

- [ ] **Step 3: Verify formatting**

Run: `dotnet format --verify-no-changes`
Expected: PASS (no diff). If it reports changes, run `dotnet format`, review, and `git commit -m "style: apply dotnet format"`.

- [ ] **Step 4: Final confirmation**

Confirm the working tree is clean (`git status`) and the branch contains the serde commits.

---

## Self-Review

**Spec coverage (against `2026-06-14-serde-slice-design.md`):**
- `ISerde` generic + context-backed seam in Core → Tasks 2–5. ✓
- `SystemTextJsonSerde`, AOT-safe via source-gen context, unknown-type + JSON-failure mapping → Tasks 3, 5. ✓
- `DefaultMediaType` → Task 3. ✓
- `RequestBody.FromValue<T>` (replayable, default media type) → Task 6. ✓
- `ResponseBody.ReadValueAsync<T>` (single-use) → Task 7. ✓
- `HttpResponseException.GetErrorAsync<T>` (+ `ResponseNotReadException` on consumed body) → Task 8. ✓
- New multi-target + AOT-validated project; STJ referenced only there → Tasks 1, 9. ✓
- Deferred (correctly out of scope): `AddSystemTextJsonSerde` (DI slice), `Optional<T>` (#1), error-body buffering (policies slice), strong-naming + existing-project retargeting (conventions slice).

**Placeholder scan:** No TBD/TODO. The one `NotSupportedException` is an intentional, committed red-state in Task 3 that Task 4 turns green. ✓

**Type consistency:** `ISerde` members (`SerializeAsync`/`DeserializeAsync`/`Serialize`/`Deserialize`/`DefaultMediaType`) are used identically in `SystemTextJsonSerde`, the conveniences, and `GetErrorAsync`. `RequestBody.FromBytes(ReadOnlyMemory<byte>, MediaType?)`, `ResponseBody.OpenReadAsync`, `ResponseBody.FromBytes`, `HttpResponseException.Response`, `StreamConsumedException`, `ResponseNotReadException`, `SerializationException`, `DeserializationException`, and `CommonMediaTypes.ApplicationJson(Utf8)` all match the current source. ✓

**Verified against source:** the `Response` constructor and the non-nullable `Response.Body` are confirmed in `Response.cs`; `GetErrorAsync` deserializes the body directly without a null check (an empty buffered body surfaces as a `DeserializationException`, which is the intended "no parseable error" signal).
