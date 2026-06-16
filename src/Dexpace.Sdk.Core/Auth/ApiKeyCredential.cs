// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Http.Common;

namespace Dexpace.Sdk.Core.Auth;

/// <summary>
/// A credential that authenticates requests by stamping a static API key into an HTTP header.
/// </summary>
/// <remarks>
/// By default the key is sent in the <c>Authorization</c> header with no scheme prefix, i.e.
/// the header value is exactly <see cref="Key"/>. Specify a scheme to add a prefix, e.g.
/// <c>"Bearer"</c> produces <c>Authorization: Bearer &lt;key&gt;</c>. Pass a custom header
/// name to use a non-standard header such as <c>X-Api-Key</c>.
/// </remarks>
public sealed class ApiKeyCredential
{
    /// <summary>
    /// Initializes an <see cref="ApiKeyCredential"/>.
    /// </summary>
    /// <param name="key">The API key value. Must not be null or empty.</param>
    /// <param name="header">
    /// The header to stamp. Defaults to <see cref="HttpHeaderName.WellKnown.Authorization"/>.
    /// </param>
    /// <param name="scheme">
    /// An optional scheme prefix (e.g. <c>"Bearer"</c>). When <see langword="null"/> the key
    /// is used as the entire header value.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    public ApiKeyCredential(string key, HttpHeaderName? header = null, string? scheme = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
        {
            throw new ArgumentException("API key must not be empty.", nameof(key));
        }

        Key = key;
        HeaderName = header ?? HttpHeaderName.WellKnown.Authorization;
        Scheme = scheme;
    }

    /// <summary>The raw API key value.</summary>
    public string Key { get; }

    /// <summary>The HTTP header into which the key is stamped.</summary>
    public HttpHeaderName HeaderName { get; }

    /// <summary>
    /// The optional scheme prefix. When non-<see langword="null"/>, the header value is
    /// <c>"&lt;Scheme&gt; &lt;Key&gt;"</c>; otherwise the header value is exactly <see cref="Key"/>.
    /// </summary>
    public string? Scheme { get; }
}
