// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Globalization;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;

namespace Dexpace.Sdk.Core.Pagination;

/// <summary>
/// Factory methods that build <c>nextRequest</c> delegates for use with
/// <see cref="Pageable.Create{TPage,T}"/>.
/// </summary>
/// <remarks>
/// Each factory returns a <see cref="Func{TPage,Response,Request,Request}"/> compatible with the
/// <c>nextRequest</c> parameter of <see cref="Pageable.Create{TPage,T}"/>. The returned delegate
/// receives the deserialized page, the raw (not-yet-disposed) response, and the current request,
/// and must return the next <see cref="Request"/> to send or <see langword="null"/> to stop
/// iteration.
/// </remarks>
public static class PaginationStrategies
{
    // ── Cursor ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a next-request delegate that drives cursor-based pagination.
    /// </summary>
    /// <typeparam name="TPage">The deserialized page-envelope type.</typeparam>
    /// <param name="nextCursor">
    /// Extracts the continuation cursor from a deserialized page. Return
    /// <see langword="null"/> or empty string to end iteration.
    /// </param>
    /// <param name="queryParameter">
    /// The URL query-string key to set to the cursor value on the next request.
    /// </param>
    /// <returns>
    /// A delegate that returns the next <see cref="Request"/> with <paramref name="queryParameter"/>
    /// set to the cursor, or <see langword="null"/> when the cursor is absent.
    /// </returns>
    public static Func<TPage, Response, Request, Request?> Cursor<TPage>(
        Func<TPage, string?> nextCursor,
        string queryParameter)
    {
        ArgumentNullException.ThrowIfNull(nextCursor);
        ArgumentNullException.ThrowIfNull(queryParameter);

        return (page, _, current) =>
        {
            var cursor = nextCursor(page);
            if (string.IsNullOrEmpty(cursor))
            {
                return null;
            }

            return current with { Url = SetQueryParameter(current.Url, queryParameter, cursor) };
        };
    }

    // ── PageNumber ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a next-request delegate that drives page-number pagination.
    /// </summary>
    /// <typeparam name="TPage">The deserialized page-envelope type.</typeparam>
    /// <param name="queryParameter">
    /// The URL query-string key that carries the page number. If absent on the current request URL
    /// the current page number is treated as 1.
    /// </param>
    /// <param name="hasMore">
    /// Returns <see langword="true"/> when the response contains more pages to fetch.
    /// </param>
    /// <returns>
    /// A delegate that increments the page-number query parameter and returns the next
    /// <see cref="Request"/>, or <see langword="null"/> when <paramref name="hasMore"/> returns
    /// <see langword="false"/>.
    /// </returns>
    public static Func<TPage, Response, Request, Request?> PageNumber<TPage>(
        string queryParameter,
        Func<TPage, bool> hasMore)
    {
        ArgumentNullException.ThrowIfNull(queryParameter);
        ArgumentNullException.ThrowIfNull(hasMore);

        return (page, _, current) =>
        {
            if (!hasMore(page))
            {
                return null;
            }

            var raw = GetQueryParameter(current.Url, queryParameter);
            var currentPage = raw is not null && int.TryParse(raw, out var n) ? n : 1;
            var nextPage = currentPage + 1;
            return current with { Url = SetQueryParameter(current.Url, queryParameter, nextPage.ToString(CultureInfo.InvariantCulture)) };
        };
    }

    // ── LinkHeader ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a next-request delegate that drives link-header pagination (RFC 8288).
    /// </summary>
    /// <param name="rel">
    /// The <c>rel</c> type to look for in the <c>Link</c> response header.
    /// Defaults to <c>"next"</c>.
    /// </param>
    /// <returns>
    /// A delegate that reads the <c>Link</c> response header, resolves the URL for the
    /// given <paramref name="rel"/>, and returns the next <see cref="Request"/>; or
    /// <see langword="null"/> if no matching entry is found or the URL cannot be resolved.
    /// </returns>
    public static Func<TPage, Response, Request, Request?> LinkHeader<TPage>(string rel = "next")
    {
        ArgumentNullException.ThrowIfNull(rel);

        return (_, response, current) =>
        {
            var linkHeaderValue = response.Headers.Get("Link");
            if (string.IsNullOrEmpty(linkHeaderValue))
            {
                return null;
            }

            var linkUrl = ParseLinkHeader(linkHeaderValue, rel);
            if (linkUrl is null)
            {
                return null;
            }

            // Resolve the link URL against the current request URL.
            if (!Uri.TryCreate(current.Url, linkUrl, out var resolved))
            {
                return null;
            }

            return current with { Url = resolved };
        };
    }

    // ── private helpers ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the raw (URI-decoded) value of the first occurrence of
    /// <paramref name="key"/> in the query string of <paramref name="uri"/>, or
    /// <see langword="null"/> when the key is absent.
    /// </summary>
    private static string? GetQueryParameter(Uri uri, string key)
    {
        var query = uri.Query;
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        // Strip leading '?'.
        var span = query.AsSpan().TrimStart('?');
        var encodedKey = Uri.EscapeDataString(key);

        foreach (var pair in new QueryPairEnumerator(span))
        {
            var eqIndex = pair.IndexOf('=');
            var pairKey = eqIndex < 0 ? pair : pair[..eqIndex];
            if (pairKey.Equals(encodedKey.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                if (eqIndex < 0)
                {
                    return string.Empty;
                }

                return Uri.UnescapeDataString(new string(pair[(eqIndex + 1)..]));
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a new <see cref="Uri"/> identical to <paramref name="uri"/> except that
    /// <paramref name="key"/> is set to <paramref name="value"/> (replacing any existing
    /// occurrence, or appending if absent).
    /// </summary>
    private static Uri SetQueryParameter(Uri uri, string key, string value)
    {
        var encodedKey = Uri.EscapeDataString(key);
        var encodedValue = Uri.EscapeDataString(value);
        var pair = $"{encodedKey}={encodedValue}";

        var existing = uri.Query.TrimStart('?');
        string newQuery;

        if (string.IsNullOrEmpty(existing))
        {
            newQuery = pair;
        }
        else
        {
            // Replace the existing key=value if present, otherwise append.
            var span = existing.AsSpan();
            var sb = new System.Text.StringBuilder();
            var replaced = false;

            foreach (var segment in new QueryPairEnumerator(span))
            {
                var eqIndex = segment.IndexOf('=');
                var segKey = eqIndex < 0 ? segment : segment[..eqIndex];

                if (!replaced && segKey.Equals(encodedKey.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    if (sb.Length > 0) { sb.Append('&'); }
                    sb.Append(pair);
                    replaced = true;
                }
                else
                {
                    if (sb.Length > 0) { sb.Append('&'); }
                    sb.Append(new string(segment));
                }
            }

            if (!replaced)
            {
                if (sb.Length > 0) { sb.Append('&'); }
                sb.Append(pair);
            }

            newQuery = sb.ToString();
        }

        var builder = new UriBuilder(uri) { Query = newQuery };
        return builder.Uri;
    }

    /// <summary>
    /// Parses the value of a <c>Link</c> header and returns the URL string for the entry
    /// whose <c>rel</c> matches <paramref name="rel"/> (case-insensitive), or
    /// <see langword="null"/> if not found.
    /// </summary>
    /// <remarks>
    /// Handles the comma-separated format:
    /// <c>&lt;https://api.example.com/items?page=2&gt;; rel="next", &lt;...&gt;; rel="prev"</c>
    /// </remarks>
    private static string? ParseLinkHeader(string headerValue, string rel)
    {
        // Split on commas that are not inside angle brackets.
        // We do a simple state-machine split because link URLs may theoretically
        // contain commas (e.g., in query parameters), but for RFC 8288 the entries
        // are comma-separated at the top level.
        var entries = SplitLinkEntries(headerValue);

        foreach (var entry in entries)
        {
            var trimmed = entry.Trim();
            if (trimmed.Length == 0) { continue; }

            // Each entry: <URL>; param=value; param=value…
            // Split on ';'
            var parts = trimmed.Split(';');
            if (parts.Length < 2) { continue; }

            // First part must be <URL>.
            var urlPart = parts[0].Trim();
            if (!urlPart.StartsWith('<') || !urlPart.EndsWith('>')) { continue; }

            var linkUrl = urlPart[1..^1].Trim();

            // Scan the remaining parts for rel="<rel>".
            var hasMatchingRel = false;
            for (var i = 1; i < parts.Length; i++)
            {
                var param = parts[i].Trim();

                // Strip optional quotes and compare.
                if (param.StartsWith("rel=", StringComparison.OrdinalIgnoreCase))
                {
                    var relValue = param["rel=".Length..].Trim().Trim('"');
                    if (string.Equals(relValue, rel, StringComparison.OrdinalIgnoreCase))
                    {
                        hasMatchingRel = true;
                        break;
                    }
                }
            }

            if (hasMatchingRel)
            {
                return linkUrl;
            }
        }

        return null;
    }

    /// <summary>
    /// Splits a <c>Link</c> header value on top-level commas (outside angle brackets).
    /// </summary>
    private static IEnumerable<string> SplitLinkEntries(string headerValue)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < headerValue.Length; i++)
        {
            switch (headerValue[i])
            {
                case '<': depth++; break;
                case '>': if (depth > 0) { depth--; } break;
                case ',' when depth == 0:
                    yield return headerValue[start..i];
                    start = i + 1;
                    break;
            }
        }

        if (start < headerValue.Length)
        {
            yield return headerValue[start..];
        }
    }

    // ── query-pair ref-struct enumerator ──────────────────────────────────────────────────────

    /// <summary>
    /// Zero-allocation enumerator over <c>key=value</c> pairs in a query string span
    /// (without the leading <c>?</c>). Each iteration returns a <see cref="ReadOnlySpan{T}"/>
    /// slice covering one <c>key=value</c> segment.
    /// </summary>
    private ref struct QueryPairEnumerator
    {
        private ReadOnlySpan<char> _remaining;
        private ReadOnlySpan<char> _current;

        internal QueryPairEnumerator(ReadOnlySpan<char> query) => _remaining = query;

        /// <summary>Returns <c>this</c> so the type works in <c>foreach</c>.</summary>
        public readonly QueryPairEnumerator GetEnumerator() => this;

        /// <summary>The current segment.</summary>
        public readonly ReadOnlySpan<char> Current => _current;

        /// <summary>Advances to the next segment.</summary>
        public bool MoveNext()
        {
            while (!_remaining.IsEmpty)
            {
                var idx = _remaining.IndexOf('&');
                if (idx < 0)
                {
                    _current = _remaining;
                    _remaining = [];
                }
                else
                {
                    _current = _remaining[..idx];
                    _remaining = _remaining[(idx + 1)..];
                }

                // Skip empty segments (e.g. leading/trailing '&').
                if (!_current.IsEmpty)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
