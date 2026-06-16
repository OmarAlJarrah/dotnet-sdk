// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Runtime.CompilerServices;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Errors;
using Dexpace.Sdk.Core.Http.Common;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;
using Dexpace.Sdk.Core.Pipeline;
using Dexpace.Sdk.Core.Serialization;

namespace Dexpace.Sdk.Core.Pagination;

/// <summary>
/// Factory methods for creating <see cref="AsyncPageable{T}"/> instances.
/// </summary>
public static class Pageable
{
    /// <summary>
    /// Creates an <see cref="AsyncPageable{T}"/> that fetches pages through
    /// <paramref name="pipeline"/>, starting with <paramref name="first"/>.
    /// </summary>
    /// <typeparam name="TPage">The deserialized page-envelope type.</typeparam>
    /// <typeparam name="T">The item type extracted from each page.</typeparam>
    /// <param name="pipeline">The pipeline used for each page request.</param>
    /// <param name="first">The initial request to send.</param>
    /// <param name="serde">The serde used to deserialize each <typeparamref name="TPage"/>.</param>
    /// <param name="options">Client options forwarded to each pipeline call.</param>
    /// <param name="selectItems">
    /// Extracts the ordered item list from a deserialized page envelope.
    /// </param>
    /// <param name="nextRequest">
    /// Given the deserialized page, the raw response (before disposal), and the current request,
    /// returns the next request to send, or <see langword="null"/> to end iteration.
    /// </param>
    /// <param name="maxPages">
    /// Maximum number of pages to fetch. <see langword="null"/> means no limit.
    /// </param>
    /// <returns>
    /// A lazy <see cref="AsyncPageable{T}"/> that fetches exactly one page per consumer advance.
    /// </returns>
    public static AsyncPageable<T> Create<TPage, T>(
        HttpPipeline pipeline,
        Request first,
        ISerde serde,
        DexpaceClientOptions options,
        Func<TPage, IReadOnlyList<T>> selectItems,
        Func<TPage, Response, Request, Request?> nextRequest,
        int? maxPages = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(serde);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selectItems);
        ArgumentNullException.ThrowIfNull(nextRequest);

        return new PipelinePageable<TPage, T>(pipeline, first, serde, options, selectItems, nextRequest, maxPages);
    }

    // ── internal implementation ────────────────────────────────────────────────────────────────

    private sealed class PipelinePageable<TPage, T>(
        HttpPipeline pipeline,
        Request first,
        ISerde serde,
        DexpaceClientOptions options,
        Func<TPage, IReadOnlyList<T>> selectItems,
        Func<TPage, Response, Request, Request?> nextRequest,
        int? maxPages) : AsyncPageable<T>
    {
        /// <inheritdoc/>
        public override IAsyncEnumerable<Page<T>> AsPages(int? pageSizeHint = null) =>
            PagesCore(CancellationToken.None);

        /// <inheritdoc/>
        public override IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            ItemsCore(cancellationToken).GetAsyncEnumerator(cancellationToken);

        // Page iterator — fetches one HTTP page per yield.
        private async IAsyncEnumerable<Page<T>> PagesCore(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var current = first;
            var fetched = 0;

            while (true)
            {
                if (maxPages.HasValue && fetched >= maxPages.Value)
                {
                    yield break;
                }

                var response = await pipeline.SendAsync(current, options, cancellationToken)
                    .ConfigureAwait(false);

                TPage page;
                Status status;
                Headers headers;
                Request? next;

                try
                {
                    page = await response.Body
                        .ReadValueAsync<TPage>(serde, cancellationToken)
                        .ConfigureAwait(false)
                        ?? throw new InvalidOperationException(
                            $"Serde returned null when deserializing page type '{typeof(TPage).FullName}'. " +
                            "The page deserialization must produce a non-null value.");

                    status = response.Status;
                    headers = response.Headers;

                    // Capture next while the response (and its headers) are still alive.
                    next = nextRequest(page, response, current);
                }
                finally
                {
                    await response.DisposeAsync().ConfigureAwait(false);
                }

                fetched++;
                yield return new Page<T>(selectItems(page), status, headers);

                if (next is null)
                {
                    yield break;
                }

                current = next;
            }
        }

        // Item iterator — flattens PagesCore.
        private async IAsyncEnumerable<T> ItemsCore(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var page in PagesCore(cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                foreach (var item in page.Values)
                {
                    yield return item;
                }
            }
        }
    }
}
