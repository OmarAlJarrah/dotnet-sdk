// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Client;

namespace Dexpace.Sdk.Core.Pipeline;

/// <summary>
/// The "next" continuation passed to each <see cref="HttpPipelinePolicy.ProcessAsync"/> call.
/// Advances the policy index and ultimately invokes the transport.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PipelineRunner"/> is a <c>readonly struct</c> so it carries zero allocation per
/// policy hop. A policy may call <see cref="RunAsync"/> more than once (e.g. retry, redirect)
/// because the runner is immutable — each call re-advances from the same index with its own
/// in-flight state.
/// </para>
/// <para>
/// Callers must not retain or share a <see cref="PipelineRunner"/> beyond the duration of
/// <see cref="HttpPipelinePolicy.ProcessAsync"/>.
/// </para>
/// </remarks>
public readonly struct PipelineRunner
{
    private readonly HttpPipelinePolicy[] _policies;
    private readonly int _index;
    private readonly IAsyncHttpClient _transport;

    /// <summary>
    /// Initializes a runner. Called by the pipeline entry point and recursively by
    /// <see cref="RunAsync"/>.
    /// </summary>
    /// <param name="policies">The ordered (sorted-by-stage) policy array.</param>
    /// <param name="index">The index of the next policy to invoke.</param>
    /// <param name="transport">The terminal transport invoked when all policies have run.</param>
    internal PipelineRunner(HttpPipelinePolicy[] policies, int index, IAsyncHttpClient transport)
    {
        _policies = policies;
        _index = index;
        _transport = transport;
    }

    /// <summary>
    /// Runs the remainder of the pipeline starting at the current index, then invokes the
    /// transport if no earlier policy short-circuited.
    /// </summary>
    /// <param name="context">The mutable context for the current call.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the pipeline tail has run.</returns>
    public async ValueTask RunAsync(PipelineContext context)
    {
        if (_index >= _policies.Length)
        {
            context.Response = await _transport
                .ExecuteAsync(context.Request, context.CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var next = new PipelineRunner(_policies, _index + 1, _transport);
        await _policies[_index].ProcessAsync(context, next).ConfigureAwait(false);
    }
}
