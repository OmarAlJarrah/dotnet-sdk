// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Diagnostics;
using Xunit;

namespace Dexpace.Sdk.Core.Tests.Diagnostics;

public class DexpaceDiagnosticsTests
{
    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        Assert.Equal("Dexpace.Sdk", DexpaceDiagnostics.ActivitySource.Name);
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        Assert.Equal("Dexpace.Sdk", DexpaceDiagnostics.Meter.Name);
    }

    [Fact]
    public void ActivitySource_VersionIsNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(DexpaceDiagnostics.ActivitySource.Version));
    }

    [Fact]
    public void Meter_VersionIsNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(DexpaceDiagnostics.Meter.Version));
    }
}
