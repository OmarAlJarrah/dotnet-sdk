// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Auth;
using Dexpace.Sdk.Core.Http.Common;

namespace Dexpace.Sdk.Core.Pipeline.Policies;

/// <summary>
/// An auth-stage pipeline policy that obtains an OAuth 2.0 bearer token via an
/// <see cref="AccessTokenCache"/> and stamps <c>Authorization: Bearer &lt;token&gt;</c> on every
/// outgoing request, replacing any prior <c>Authorization</c> header value.
/// </summary>
/// <remarks>
/// <para>
/// The policy creates exactly one <see cref="AccessTokenCache"/> for the lifetime of the policy
/// instance, wrapping the supplied <see cref="TokenCredential"/>. All pipeline invocations share
/// that cache, so concurrent or sequential requests reuse a valid cached token and avoid redundant
/// credential calls.
/// </para>
/// <para>
/// Token acquisition is always async; the policy calls
/// <see cref="AccessTokenCache.GetAsync(TokenRequestContext,CancellationToken)"/> using the
/// <see cref="PipelineContext.CancellationToken"/> so that request cancellation propagates into
/// token fetching.
/// </para>
/// <para>
/// <b>401 re-acquisition:</b> re-acquiring a token on a <c>401</c> challenge response is deferred
/// to the challenge-handling work (to arrive with <c>ChallengeHandler</c>). This policy performs
/// a single token-get per request.
/// </para>
/// <para>
/// Credentials are withheld when the request has been redirected to a different origin; see
/// <see cref="AuthorizationPolicy"/> for the cross-origin withholding contract.
/// </para>
/// </remarks>
public sealed class BearerTokenAuthPolicy : AuthorizationPolicy
{
    private readonly AccessTokenCache _cache;
    private readonly TokenRequestContext _tokenRequestContext;

    /// <summary>
    /// Initializes a <see cref="BearerTokenAuthPolicy"/> with the given credential and scopes.
    /// </summary>
    /// <param name="credential">
    /// The token credential used to obtain bearer tokens. A single
    /// <see cref="AccessTokenCache"/> is created over this credential and shared across all
    /// requests.
    /// </param>
    /// <param name="scopes">
    /// The OAuth 2.0 scopes to request. Passed to
    /// <see cref="TokenCredential.GetTokenAsync"/> via <see cref="TokenRequestContext"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="credential"/> or <paramref name="scopes"/> is <see langword="null"/>.
    /// </exception>
    public BearerTokenAuthPolicy(TokenCredential credential, params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(scopes);

        _cache = new AccessTokenCache(credential);
        _tokenRequestContext = new TokenRequestContext(scopes);
    }

    /// <inheritdoc/>
    protected override HttpHeaderName WithheldHeaderName => HttpHeaderName.WellKnown.Authorization;

    /// <inheritdoc/>
    protected override async ValueTask<(string HeaderName, string HeaderValue)> GetCredentialAsync(
        PipelineContext context)
    {
        var token = await _cache
            .GetAsync(_tokenRequestContext, context.CancellationToken)
            .ConfigureAwait(false);

        return (HttpHeaderName.WellKnown.Authorization.Original, $"Bearer {token.Token}");
    }
}
