// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Text;

namespace Dexpace.Sdk.Core.Auth;

/// <summary>
/// A credential that authenticates requests using the HTTP Basic scheme (RFC 7617).
/// </summary>
/// <remarks>
/// The credential stores the username and password in plain text. Call <see cref="ToBase64"/>
/// to obtain the Base64-encoded <c>username:password</c> token suitable for the
/// <c>Authorization: Basic &lt;token&gt;</c> header value.
/// </remarks>
public sealed class BasicCredential
{
    /// <summary>
    /// Initializes a <see cref="BasicCredential"/> with the given username and password.
    /// </summary>
    /// <param name="username">The username. Must not be <see langword="null"/>.</param>
    /// <param name="password">The password. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="username"/> or <paramref name="password"/> is <see langword="null"/>.
    /// </exception>
    public BasicCredential(string username, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);
        Username = username;
        Password = password;
    }

    /// <summary>The username.</summary>
    public string Username { get; }

    /// <summary>The password.</summary>
    public string Password { get; }

    /// <summary>
    /// Returns the Base64-encoded UTF-8 <c>username:password</c> token for use in the
    /// <c>Authorization: Basic &lt;token&gt;</c> header value.
    /// </summary>
    /// <returns>The Base64-encoded credentials.</returns>
    public string ToBase64()
    {
        var bytes = Encoding.UTF8.GetBytes($"{Username}:{Password}");
        return Convert.ToBase64String(bytes);
    }
}
