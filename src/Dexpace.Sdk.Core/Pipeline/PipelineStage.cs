// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

namespace Dexpace.Sdk.Core.Pipeline;

/// <summary>
/// Identifies where in the pipeline chain a policy is inserted.
/// Policies execute in ascending numeric order (outermost first on the way in;
/// innermost first on the way out).
/// </summary>
/// <remarks>
/// <para>
/// Numbers are sparse to leave room for future stages without breaking existing values.
/// </para>
/// <para>
/// <b>Pillar stages</b> — <see cref="Operation"/>, <see cref="Redirect"/>, <see cref="Retry"/>,
/// <see cref="Auth"/>, and <see cref="Diagnostics"/> — admit exactly one policy each.
/// Adding a second policy to a pillar stage is a configuration error detected at
/// pipeline build time.
/// </para>
/// <para>
/// <b>Non-pillar stages</b> — <see cref="PerCall"/> and <see cref="PerAttempt"/> — may hold
/// multiple policies, which execute in the order they were registered.
/// </para>
/// </remarks>
public enum PipelineStage
{
    /// <summary>
    /// Outermost stage. Runs once per logical operation — opens the operation span and applies
    /// the overall deadline. Pillar: at most one policy.
    /// </summary>
    Operation = 100,

    /// <summary>
    /// Redirect-following stage. Runs outside the retry loop so each hop triggers a full retry
    /// sequence. Pillar: at most one policy.
    /// </summary>
    Redirect = 200,

    /// <summary>
    /// Per-call stage (non-pillar). Policies here run once per logical call, above the retry
    /// boundary — suitable for stable cross-attempt concerns such as idempotency keys and
    /// client identity headers.
    /// </summary>
    PerCall = 250,

    /// <summary>
    /// Retry stage. Wraps everything below it so that each retry attempt re-executes all
    /// inner stages. Pillar: at most one policy.
    /// </summary>
    Retry = 300,

    /// <summary>
    /// Per-attempt stage (non-pillar). Policies here run on every attempt inside the retry
    /// loop — suitable for per-attempt concerns such as a fresh <c>Date</c> header.
    /// </summary>
    PerAttempt = 400,

    /// <summary>
    /// Auth stage. Placed inside the retry loop so a token refresh applies to the next
    /// retry attempt. Pillar: at most one policy.
    /// </summary>
    Auth = 500,

    /// <summary>
    /// Diagnostics stage. Closest to the transport wire; wraps the per-attempt span,
    /// metrics, and structured log events. Pillar: at most one policy.
    /// </summary>
    Diagnostics = 600,
}
