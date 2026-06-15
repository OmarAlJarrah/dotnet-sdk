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
