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

    private JsonTypeInfo<T> GetTypeInfo<T>(bool forSerialize)
    {
        try
        {
            if (_options.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> info)
            {
                return info;
            }
        }
        catch (NotSupportedException)
        {
            // Source-generated contexts throw NotSupportedException for unregistered types
            // instead of returning null; map to the SDK exception type below.
        }

        return forSerialize
            ? throw new SerializationException(TypeInfoMessage<T>())
            : throw new DeserializationException(TypeInfoMessage<T>());
    }

    private static string TypeInfoMessage<T>() =>
        $"No JsonTypeInfo is registered for '{typeof(T)}'. Add it to a source-generated "
        + "JsonSerializerContext supplied to SystemTextJsonSerde.";
}
