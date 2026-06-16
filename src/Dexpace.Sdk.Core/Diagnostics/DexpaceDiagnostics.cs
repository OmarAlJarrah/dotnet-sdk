// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Dexpace.Sdk.Core.Internal;

namespace Dexpace.Sdk.Core.Diagnostics;

/// <summary>
/// Central home for the SDK's OpenTelemetry instrumentation objects.
/// </summary>
/// <remarks>
/// Consumers wire up collection by subscribing to <see cref="ActivitySource"/> (tracing) and
/// <see cref="Meter"/> (metrics) via their chosen OTel SDK — no SDK-internal configuration needed.
/// When no listener is active, <c>ActivitySource.StartActivity</c> returns
/// <see langword="null"/> and the hot path allocates nothing for tracing.
/// </remarks>
public static class DexpaceDiagnostics
{
    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> used by the SDK for distributed tracing.
    /// Name: <c>"Dexpace.Sdk"</c>.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("Dexpace.Sdk", SdkVersion.Value);

    /// <summary>
    /// The <see cref="System.Diagnostics.Metrics.Meter"/> used by the SDK for metrics.
    /// Name: <c>"Dexpace.Sdk"</c>.
    /// </summary>
    public static readonly Meter Meter = new("Dexpace.Sdk", SdkVersion.Value);
}
