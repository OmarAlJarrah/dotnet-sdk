// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

namespace Dexpace.Sdk.Core.Auth;

/// <summary>
/// Base class for credential implementations that produce <see cref="AccessToken"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses implement <see cref="GetTokenAsync"/> and may optionally override
/// <see cref="GetToken"/> for a non-blocking synchronous path. The default
/// <see cref="GetToken"/> implementation is a blocking bridge over
/// <see cref="GetTokenAsync"/>; override it when a truly synchronous code path exists.
/// </para>
/// <para>
/// Tokens are typically obtained through an <c>AccessTokenCache</c> rather than calling
/// this type directly, so that caching, proactive refresh, and single-flight behaviour are
/// applied automatically.
/// </para>
/// </remarks>
public abstract class TokenCredential
{
    /// <summary>
    /// Asynchronously obtains an <see cref="AccessToken"/> for the requested context.
    /// </summary>
    /// <param name="context">The scopes and optional claims for the token request.</param>
    /// <param name="ct">A token to cancel the request.</param>
    /// <returns>
    /// A <see cref="ValueTask{AccessToken}"/> that resolves to the token on success.
    /// </returns>
    public abstract ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Synchronously obtains an <see cref="AccessToken"/> for the requested context.
    /// </summary>
    /// <remarks>
    /// The default implementation is a blocking bridge over <see cref="GetTokenAsync"/>.
    /// Override this method when a non-blocking synchronous path is available.
    /// </remarks>
    /// <param name="context">The scopes and optional claims for the token request.</param>
    /// <param name="ct">A token to cancel the request.</param>
    /// <returns>The access token.</returns>
    public virtual AccessToken GetToken(TokenRequestContext context, CancellationToken ct = default)
        => GetTokenAsync(context, ct).AsTask().GetAwaiter().GetResult();
}
