using ViscerealityCompanion.App;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class AppBuildIdentityTests
{
    [Fact]
    public void TryReadPackagedVersionFromProcessPath_ParsesWindowsAppsVersion()
    {
        var processPath = @"C:\Program Files\WindowsApps\MesmerPrism.ViscerealityCompanion_0.1.32.0_x64__8wekyb3d8bbwe\ViscerealityCompanion.App\ViscerealityCompanion.exe";

        var version = AppBuildIdentity.TryReadPackagedVersionFromProcessPath(processPath);

        Assert.Equal("0.1.32.0", version);
    }

    [Fact]
    public void TryReadPackagedVersionFromProcessPath_ReturnsNullOutsideWindowsApps()
    {
        var processPath = @"C:\Users\tillh\source\repos\ViscerealityCompanion\src\ViscerealityCompanion.App\bin\Release\net10.0-windows\ViscerealityCompanion.exe";

        var version = AppBuildIdentity.TryReadPackagedVersionFromProcessPath(processPath);

        Assert.Null(version);
    }
}
