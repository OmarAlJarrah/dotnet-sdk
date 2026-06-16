// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Text;

namespace Dexpace.Sdk.Core.Auth;

/// <summary>
/// Describes the parameters of a token request: the OAuth 2.0 scopes required and an optional
/// claims challenge string.
/// </summary>
/// <remarks>
/// The shape intentionally mirrors <c>Azure.Core.TokenRequestContext</c> to ease the
/// <c>Dexpace.Sdk.Auth.AzureIdentity</c> adapter.
/// </remarks>
public readonly struct TokenRequestContext
{
    /// <summary>
    /// Initializes a <see cref="TokenRequestContext"/> with the specified scopes and no claims.
    /// </summary>
    /// <param name="scopes">The OAuth 2.0 scopes for which a token is required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scopes"/> is <see langword="null"/>.</exception>
    public TokenRequestContext(IReadOnlyList<string> scopes)
        : this(scopes, null)
    {
    }

    /// <summary>
    /// Initializes a <see cref="TokenRequestContext"/> with the specified scopes and claims.
    /// </summary>
    /// <param name="scopes">The OAuth 2.0 scopes for which a token is required.</param>
    /// <param name="claims">
    /// An optional claims challenge returned by the resource in a previous <c>401</c> response.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="scopes"/> is <see langword="null"/>.</exception>
    public TokenRequestContext(IReadOnlyList<string> scopes, string? claims)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        Scopes = scopes;
        Claims = claims;
        CacheKey = BuildCacheKey(scopes, claims);
    }

    /// <summary>The OAuth 2.0 scopes for which a token is required.</summary>
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>An optional claims challenge to include in the token request.</summary>
    public string? Claims { get; }

    /// <summary>
    /// A stable string key derived from <see cref="Scopes"/> and <see cref="Claims"/>.
    /// Suitable for use as a dictionary key in a token cache.
    /// </summary>
    public string CacheKey { get; }

    private static string BuildCacheKey(IReadOnlyList<string> scopes, string? claims)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < scopes.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(scopes[i]);
        }

        if (claims is not null)
        {
            sb.Append('|');
            sb.Append(claims);
        }

        return sb.ToString();
    }
}
