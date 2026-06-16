// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

namespace Dexpace.Sdk.Core.Auth;

/// <summary>
/// An access token returned by a <see cref="TokenCredential"/>, together with its expiry
/// and an optional proactive-refresh hint.
/// </summary>
public readonly struct AccessToken
{
    /// <summary>
    /// Initializes an <see cref="AccessToken"/> with an expiry and no proactive-refresh hint.
    /// </summary>
    /// <param name="token">The raw token string.</param>
    /// <param name="expiresOn">The time at which this token becomes invalid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    public AccessToken(string token, DateTimeOffset expiresOn)
        : this(token, expiresOn, null)
    {
    }

    /// <summary>
    /// Initializes an <see cref="AccessToken"/> with an expiry and an optional proactive-refresh hint.
    /// </summary>
    /// <param name="token">The raw token string.</param>
    /// <param name="expiresOn">The time at which this token becomes invalid.</param>
    /// <param name="refreshOn">
    /// An optional hint: when the clock reaches this value the token cache should proactively
    /// refresh, even though <paramref name="expiresOn"/> has not been reached.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    public AccessToken(string token, DateTimeOffset expiresOn, DateTimeOffset? refreshOn)
    {
        ArgumentNullException.ThrowIfNull(token);
        Token = token;
        ExpiresOn = expiresOn;
        RefreshOn = refreshOn;
    }

    /// <summary>The raw token value.</summary>
    public string Token { get; }

    /// <summary>The time at which this token becomes invalid.</summary>
    public DateTimeOffset ExpiresOn { get; }

    /// <summary>
    /// An optional proactive-refresh hint. When non-<see langword="null"/>, the token cache
    /// begins refreshing once the clock passes this value, even though <see cref="ExpiresOn"/>
    /// has not yet been reached.
    /// </summary>
    public DateTimeOffset? RefreshOn { get; }
}
