// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Text;

namespace Dexpace.Sdk.Core.Diagnostics;

/// <summary>
/// Produces a log-safe string form of a <see cref="Uri"/> by stripping userinfo and
/// replacing the values of known-sensitive query parameters with <c>REDACTED</c>.
/// </summary>
/// <remarks>
/// <para>
/// Sensitive parameter names are matched case-insensitively. The default set covers the most
/// common credential-bearing parameters; callers may supply a custom set instead.
/// </para>
/// <para>
/// <strong>Redaction boundary:</strong>
/// <list type="bullet">
///   <item><description>
///     <strong>Userinfo</strong> (the <c>user:password@</c> segment of an authority) is always
///     removed.
///   </description></item>
///   <item><description>
///     <strong>Sensitive query-parameter values</strong> are replaced with <c>REDACTED</c>;
///     names and non-sensitive parameters are preserved verbatim.
///   </description></item>
///   <item><description>
///     <strong>Fragment</strong> (the <c>#…</c> portion) is always dropped — fragments are
///     client-side only and carry no information relevant to logging.
///   </description></item>
///   <item><description>
///     <strong>Path segments are preserved verbatim.</strong> Callers must not embed secrets
///     inside the URL path; this class does not inspect or redact path components.
///   </description></item>
/// </list>
/// </para>
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
    /// sensitive query parameter values are replaced with <c>REDACTED</c>; the fragment is
    /// dropped. For non-absolute URIs the method operates on <see cref="Uri.OriginalString"/>
    /// and never throws.
    /// </summary>
    /// <param name="uri">The URI to redact. May be relative or absolute.</param>
    /// <returns>A safe string representation.</returns>
    public string Redact(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.IsAbsoluteUri)
        {
            return RedactAbsolute(uri);
        }

        return RedactRelative(uri.OriginalString);
    }

    // Handles fully-qualified URIs where Uri properties are safe to access.
    private string RedactAbsolute(Uri uri)
    {
        var query = uri.Query;

        // Build the base URL without userinfo and without the query string.
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        var baseUrl = builder.Uri.GetLeftPart(UriPartial.Path);

        if (string.IsNullOrEmpty(query))
        {
            return baseUrl;
        }

        var redactedQuery = RedactQuery(query);
        return redactedQuery.Length == 0 ? baseUrl : $"{baseUrl}?{redactedQuery}";
    }

    // Handles relative URI references by operating on the raw string directly.
    // Relative references have no userinfo, so only fragment dropping and query redaction apply.
    private string RedactRelative(string originalString)
    {
        // Drop the fragment first (everything from the first '#').
        var fragmentIndex = originalString.IndexOf('#', StringComparison.Ordinal);
        var withoutFragment = fragmentIndex >= 0
            ? originalString[..fragmentIndex]
            : originalString;

        // Split path from query on the first '?'.
        var queryIndex = withoutFragment.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            // No query string — return path as-is (fragment already dropped).
            return withoutFragment;
        }

        var path = withoutFragment[..queryIndex];
        var query = withoutFragment[queryIndex..]; // includes the leading '?'

        var redactedQuery = RedactQuery(query);
        return redactedQuery.Length == 0 ? path : $"{path}?{redactedQuery}";
    }

    // Redacts sensitive parameter values in a raw query string (with or without a leading '?').
    // Returns the redacted query string without the leading '?', or an empty string if there are
    // no key=value pairs after redaction.
    private string RedactQuery(string query)
    {
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

        return sb.ToString();
    }

    private static IEnumerable<(string Key, string Value)> ParseQueryParams(string query)
    {
        var raw = query.TrimStart('?');
        foreach (var part in raw.Split('&'))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);

            // Valueless params (e.g. "?flag") are intentionally skipped — a bare key
            // carries no value that could leak a secret.
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
