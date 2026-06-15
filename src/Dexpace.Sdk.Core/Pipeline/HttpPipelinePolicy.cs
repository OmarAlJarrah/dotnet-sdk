// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

namespace Dexpace.Sdk.Core.Pipeline;

/// <summary>
/// Base class for every policy in the HTTP pipeline.
/// </summary>
/// <remarks>
/// <para>
/// A policy participates in the request/response lifecycle by implementing
/// <see cref="ProcessAsync"/>. Before calling <c>next.RunAsync</c>, a policy may mutate
/// <see cref="PipelineContext.Request"/>; after the call returns, it may inspect or replace
/// <see cref="PipelineContext.Response"/>.
/// </para>
/// <para>
/// <b>Re-entrancy.</b> <see cref="PipelineRunner"/> is a value type, so a policy may call
/// <c>next.RunAsync</c> more than once — this is how redirect and retry policies work.
/// </para>
/// <para>
/// <b>Async-only in v1.</b> There is no synchronous <c>Process</c> override on this base class.
/// The sync entry point on the pipeline drives the async chain via a blocking
/// bridge; concrete policy subclasses are only required to implement the async path.
/// </para>
/// </remarks>
public abstract class HttpPipelinePolicy
{
    /// <summary>
    /// The stage at which this policy is inserted in the pipeline.
    /// </summary>
    public abstract PipelineStage Stage { get; }

    /// <summary>
    /// Asynchronously participates in processing the request/response.
    /// </summary>
    /// <param name="context">
    /// The mutable context carrying the current <see cref="PipelineContext.Request"/>,
    /// <see cref="PipelineContext.Response"/>, and ancillary state for this call.
    /// </param>
    /// <param name="continuation">
    /// The continuation that runs the remaining policies and eventually invokes the transport.
    /// Call this to forward the request; omit the call to short-circuit the chain.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes when the policy has finished.</returns>
    public abstract ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation);
}
