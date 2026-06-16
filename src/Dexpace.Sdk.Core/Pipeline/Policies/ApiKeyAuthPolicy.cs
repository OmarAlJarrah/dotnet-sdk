// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Auth;
using Dexpace.Sdk.Core.Http.Common;

namespace Dexpace.Sdk.Core.Pipeline.Policies;

/// <summary>
/// An auth-stage pipeline policy that stamps an API-key credential header on every outgoing
/// request, replacing any prior value for that header.
/// </summary>
/// <remarks>
/// <para>
/// The header and optional scheme prefix are taken directly from the
/// <see cref="ApiKeyCredential"/> supplied at construction time:
/// </para>
/// <list type="bullet">
///   <item>
///     When <see cref="ApiKeyCredential.Scheme"/> is <see langword="null"/> the header value is
///     exactly <see cref="ApiKeyCredential.Key"/>.
///   </item>
///   <item>
///     When <see cref="ApiKeyCredential.Scheme"/> is non-<see langword="null"/> the header value
///     is <c>"&lt;Scheme&gt; &lt;Key&gt;"</c> (e.g. <c>"Bearer sk-abc123"</c>).
///   </item>
/// </list>
/// <para>
/// Credentials are withheld when the request has been redirected to a different origin; see
/// <see cref="AuthorizationPolicy"/> for the cross-origin withholding contract.
/// </para>
/// </remarks>
public sealed class ApiKeyAuthPolicy : AuthorizationPolicy
{
    private readonly ApiKeyCredential _credential;

    /// <summary>
    /// Initializes an <see cref="ApiKeyAuthPolicy"/> with the given credential.
    /// </summary>
    /// <param name="credential">The API-key credential to stamp on every same-origin request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="credential"/> is <see langword="null"/>.</exception>
    public ApiKeyAuthPolicy(ApiKeyCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
    }

    /// <inheritdoc/>
    protected override HttpHeaderName WithheldHeaderName => _credential.HeaderName;

    /// <inheritdoc/>
    protected override ValueTask<(string HeaderName, string HeaderValue)> GetCredentialAsync(
        PipelineContext context)
    {
        var headerName = _credential.HeaderName.Original;
        var headerValue = _credential.Scheme is null
            ? _credential.Key
            : $"{_credential.Scheme} {_credential.Key}";

        return new ValueTask<(string, string)>((headerName, headerValue));
    }
}
