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
