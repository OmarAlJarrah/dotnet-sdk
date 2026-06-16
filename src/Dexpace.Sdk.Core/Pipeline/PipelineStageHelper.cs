// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

namespace Dexpace.Sdk.Core.Pipeline;

/// <summary>
/// Internal helpers for <see cref="PipelineStage"/> pillar classification.
/// </summary>
internal static class PipelineStageHelper
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="stage"/> is a pillar stage that
    /// admits at most one policy.
    /// </summary>
    internal static bool IsPillar(PipelineStage stage) => stage switch
    {
        PipelineStage.Operation => true,
        PipelineStage.Redirect => true,
        PipelineStage.Retry => true,
        PipelineStage.Auth => true,
        PipelineStage.Diagnostics => true,
        _ => false,
    };

    /// <summary>
    /// The set of all pillar stages, used for cardinality validation during
    /// <see cref="PipelineBuilder.Build"/>.
    /// </summary>
    internal static readonly PipelineStage[] PillarStages =
    [
        PipelineStage.Operation,
        PipelineStage.Redirect,
        PipelineStage.Retry,
        PipelineStage.Auth,
        PipelineStage.Diagnostics,
    ];
}
