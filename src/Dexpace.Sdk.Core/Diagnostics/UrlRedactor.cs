// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Text;

namespace Dexpace.Sdk.Core.Diagnostics;

/// <summary>
/// Produces a log-safe string form of a <see cref="Uri"/> by stripping userinfo and
/// replacing the values of known-sensitive query parameters with <c>REDACTED</c>.
/// </summary>
/// <remarks>
/// Sensitive parameter names are matched case-insensitively. The default set covers the most
/// common credential-bearing parameters; callers may supply a custom set instead.
/// Non-sensitive parameters and all path/host/scheme components are preserved verbatim.
/// </remarks>
public sealed class UrlRedactor
{
    /// <summary>
    /// Default set of query parameter names whose values are redacted.
    /// </summary>
    public static readonly IReadOnlyCollection<string> DefaultSensitiveParams =
    [
        "access_token",
        "token",
        "code",
        "sig",
        "signature",
        "api_key",
        "apikey",
        "password",
    ];

    private readonly HashSet<string> _sensitiveParams;

    /// <summary>
    /// Initializes a <see cref="UrlRedactor"/> using <see cref="DefaultSensitiveParams"/>.
    /// </summary>
    public UrlRedactor()
        : this(DefaultSensitiveParams)
    {
    }

    /// <summary>
    /// Initializes a <see cref="UrlRedactor"/> with a caller-supplied set of sensitive
    /// parameter names (case-insensitive).
    /// </summary>
    /// <param name="sensitiveParams">
    /// The query parameter names whose values should be replaced with <c>REDACTED</c>.
    /// </param>
    public UrlRedactor(IEnumerable<string> sensitiveParams)
    {
        ArgumentNullException.ThrowIfNull(sensitiveParams);
        _sensitiveParams = new HashSet<string>(sensitiveParams, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a log-safe representation of <paramref name="uri"/>: userinfo is always stripped;
    /// sensitive query parameter values are replaced with <c>REDACTED</c>.
    /// </summary>
    /// <param name="uri">The URI to redact.</param>
    /// <returns>A safe string representation.</returns>
    public string Redact(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var query = uri.Query;

        // Build the base URL without userinfo and without the query string.
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
        };
        var baseUrl = builder.Uri.GetLeftPart(UriPartial.Path);

        if (string.IsNullOrEmpty(query))
        {
            return baseUrl;
        }

        // Parse, redact, and re-serialize the query string without System.Web dependency.
        var sb = new StringBuilder();
        foreach (var (key, value) in ParseQueryParams(query))
        {
            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            var redactedValue = _sensitiveParams.Contains(key) ? "REDACTED" : value;
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(redactedValue));
        }

        return sb.Length == 0 ? baseUrl : $"{baseUrl}?{sb}";
    }

    private static IEnumerable<(string Key, string Value)> ParseQueryParams(string query)
    {
        var raw = query.TrimStart('?');
        foreach (var part in raw.Split('&'))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }

            yield return (
                Uri.UnescapeDataString(part[..eq]),
                Uri.UnescapeDataString(part[(eq + 1)..]));
        }
    }
}
