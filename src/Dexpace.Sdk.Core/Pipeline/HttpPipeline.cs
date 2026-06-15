// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Client;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Errors;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;

namespace Dexpace.Sdk.Core.Pipeline;

/// <summary>
/// The entry point for sending an HTTP request through the configured policy chain.
/// </summary>
/// <remarks>
/// <para>
/// Instances are created exclusively by <see cref="PipelineBuilder.Build"/>. The pipeline is
/// immutable after construction: the sorted policy array and transport are captured at build time.
/// </para>
/// <para>
/// <b>Sync bridge.</b> <see cref="Send"/> blocks the calling thread by driving the async chain
/// synchronously via <c>GetAwaiter().GetResult()</c>. Callers on a thread pool should prefer
/// <see cref="SendAsync"/> to avoid thread starvation.
/// </para>
/// </remarks>
public sealed class HttpPipeline
{
    private readonly HttpPipelinePolicy[] _policies;
    private readonly IAsyncHttpClient _transport;

    internal HttpPipeline(HttpPipelinePolicy[] policies, IAsyncHttpClient transport)
    {
        _policies = policies;
        _transport = transport;
    }

    /// <summary>
    /// Asynchronously sends <paramref name="request"/> through the pipeline and returns the
    /// response produced by the terminal transport.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="options">Client options that apply to this call.</param>
    /// <param name="cancellationToken">An optional token to cancel the call.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that completes with the <see cref="Response"/> once
    /// the pipeline chain has finished.
    /// </returns>
    /// <exception cref="PipelineAbortedException">
    /// No policy or the transport produced a <see cref="Response"/> by the time the chain
    /// completed (i.e. the pipeline was short-circuited without setting a response).
    /// </exception>
    public async ValueTask<Response> SendAsync(
        Request request,
        DexpaceClientOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var context = new PipelineContext(request, options, cancellationToken);
        await new PipelineRunner(_policies, 0, _transport).RunAsync(context).ConfigureAwait(false);

        return context.Response
            ?? throw new PipelineAbortedException(
                "The pipeline completed without producing a response.");
    }

    /// <summary>
    /// Synchronously sends <paramref name="request"/> through the pipeline and returns the
    /// response. Blocks the calling thread until the async chain completes.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="options">Client options that apply to this call.</param>
    /// <param name="cancellationToken">An optional token to cancel the call.</param>
    /// <returns>The <see cref="Response"/> produced by the pipeline.</returns>
    /// <exception cref="PipelineAbortedException">
    /// The pipeline completed without producing a response.
    /// </exception>
    public Response Send(
        Request request,
        DexpaceClientOptions options,
        CancellationToken cancellationToken = default) =>
        SendAsync(request, options, cancellationToken).AsTask().GetAwaiter().GetResult();
}
