// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

namespace Dexpace.Sdk.Core.Pagination;

/// <summary>
/// An async-enumerable sequence of items that is backed by a series of HTTP pages.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// <para>
/// Consumers can iterate items directly (<c>await foreach (var item in pageable)</c>) or iterate
/// pages via <see cref="AsPages"/> for access to per-page metadata such as status and headers.
/// </para>
/// <para>
/// Enumeration is lazy: the next page is fetched only when the consumer advances past the last
/// item of the current page. Each page send is an independent pipeline invocation.
/// </para>
/// </remarks>
public abstract class AsyncPageable<T> : IAsyncEnumerable<T>
{
    /// <summary>Returns an async sequence of <see cref="Page{T}"/> instances.</summary>
    /// <param name="pageSizeHint">
    /// An optional hint for the number of items per page. How (or whether) this is used depends
    /// on the concrete implementation.
    /// </param>
    /// <returns>An async sequence of pages.</returns>
    public abstract IAsyncEnumerable<Page<T>> AsPages(int? pageSizeHint = null);

    /// <summary>
    /// Returns an enumerator that iterates items from all pages in sequence.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel enumeration.</param>
    /// <returns>An <see cref="IAsyncEnumerator{T}"/> over all items across all pages.</returns>
    public abstract IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);
}
