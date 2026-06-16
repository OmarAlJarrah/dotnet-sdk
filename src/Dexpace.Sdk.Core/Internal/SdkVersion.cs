// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Reflection;

namespace Dexpace.Sdk.Core.Internal;

/// <summary>
/// Shared helper that resolves the Core assembly's informational version at startup, with the
/// <c>+build</c> suffix stripped.
/// </summary>
internal static class SdkVersion
{
    /// <summary>
    /// The Core assembly version string (e.g. <c>"0.0.1-alpha.1"</c>), with any git commit hash
    /// suffix (e.g. <c>+abc123</c>) removed. Falls back to the assembly's <c>Version</c>
    /// property, and ultimately to <c>"0.0.0"</c> if neither attribute is present.
    /// </summary>
    internal static readonly string Value = BuildVersion();

    private static string BuildVersion()
    {
        var version = typeof(SdkVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(SdkVersion).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // Strip git commit hash suffix (e.g. "0.0.1-alpha.1+abc123" → "0.0.1-alpha.1").
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }
}
