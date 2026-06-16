// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Response;

namespace Dexpace.Sdk.Core.Pagination;

/// <summary>
/// A single page of results returned by a paginated operation.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Values"/> contains the deserialized items for this page.
/// <see cref="Status"/> and <see cref="Headers"/> are the HTTP metadata captured from the
/// response before it was disposed; they are immutable and safe to retain after iteration.
/// </para>
/// </remarks>
public sealed class Page<T>
{
    /// <summary>Creates a page.</summary>
    /// <param name="values">The items on this page.</param>
    /// <param name="status">The HTTP status of the response that produced this page.</param>
    /// <param name="headers">The HTTP response headers for this page.</param>
    public Page(IReadOnlyList<T> values, Status status, Headers headers)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(headers);
        Values = values;
        Status = status;
        Headers = headers;
    }

    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Values { get; }

    /// <summary>The HTTP status code of the response that produced this page.</summary>
    public Status Status { get; }

    /// <summary>The HTTP response headers for this page.</summary>
    public Headers Headers { get; }
}
