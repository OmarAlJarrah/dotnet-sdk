// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Auth;
using Dexpace.Sdk.Core.Http.Common;

namespace Dexpace.Sdk.Core.Pipeline.Policies;

/// <summary>
/// An auth-stage pipeline policy that stamps HTTP Basic authentication (RFC 7617) on every
/// outgoing request, replacing any prior <c>Authorization</c> header value.
/// </summary>
/// <remarks>
/// <para>
/// The header value is <c>"Basic &lt;base64(username:password)&gt;"</c> where the token is the
/// UTF-8 Base64 encoding returned by <see cref="BasicCredential.ToBase64"/>.
/// </para>
/// <para>
/// Credentials are withheld when the request has been redirected to a different origin; see
/// <see cref="AuthorizationPolicy"/> for the cross-origin withholding contract.
/// </para>
/// </remarks>
public sealed class BasicAuthPolicy : AuthorizationPolicy
{
    // Pre-compute the header value once — BasicCredential is immutable, so the Base64 token
    // never changes during the lifetime of this policy instance.
    private readonly string _headerValue;

    /// <summary>
    /// Initializes a <see cref="BasicAuthPolicy"/> with the given credential.
    /// </summary>
    /// <param name="credential">The Basic credential to stamp on every same-origin request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="credential"/> is <see langword="null"/>.</exception>
    public BasicAuthPolicy(BasicCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _headerValue = $"Basic {credential.ToBase64()}";
    }

    /// <inheritdoc/>
    protected override HttpHeaderName WithheldHeaderName => HttpHeaderName.WellKnown.Authorization;

    /// <inheritdoc/>
    protected override ValueTask<(string HeaderName, string HeaderValue)> GetCredentialAsync(
        PipelineContext context)
    {
        return new ValueTask<(string, string)>(
            (HttpHeaderName.WellKnown.Authorization.Original, _headerValue));
    }
}
