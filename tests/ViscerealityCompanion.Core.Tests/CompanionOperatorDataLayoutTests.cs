using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class CompanionOperatorDataLayoutTests
{
    [Fact]
    public void ResolveRootPath_UsesHostVisiblePackagedRoot_WhenPackageFamilyIsKnown()
    {
        var rootPath = CompanionOperatorDataLayout.ResolveRootPath(
            @"C:\Users\joelp\AppData\Local",
            "MesmerPrism.ViscerealityCompanion_8wekyb3d8bbwe",
            overrideRoot: null);

        Assert.Equal(
            @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_8wekyb3d8bbwe\LocalCache\Local\ViscerealityCompanion",
            rootPath);
    }

    [Fact]
    public void ResolveRootPath_PrefersExplicitOverrideRoot()
    {
        var rootPath = CompanionOperatorDataLayout.ResolveRootPath(
            @"C:\Users\joelp\AppData\Local",
            "MesmerPrism.ViscerealityCompanion_8wekyb3d8bbwe",
            @"D:\ViscerealityData");

        Assert.Equal(@"D:\ViscerealityData", rootPath);
    }

    [Fact]
    public void RemapToResolvedRootPath_RemapsLegacyBareLocalAppDataPath()
    {
        const string localAppDataPath = @"C:\Users\joelp\AppData\Local";
        const string resolvedRootPath = @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_8wekyb3d8bbwe\LocalCache\Local\ViscerealityCompanion";
        const string legacySessionPath = @"C:\Users\joelp\AppData\Local\ViscerealityCompanion\study-data\sussex-university\participant-P001\session-20260413T132014Z";

        var remappedPath = CompanionOperatorDataLayout.RemapToResolvedRootPath(
            legacySessionPath,
            localAppDataPath,
            resolvedRootPath);

        Assert.Equal(
            @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.ViscerealityCompanion_8wekyb3d8bbwe\LocalCache\Local\ViscerealityCompanion\study-data\sussex-university\participant-P001\session-20260413T132014Z",
            remappedPath);
    }

    [Fact]
    public void TryReadPackagedFamilyNameFromProcessPath_ParsesWindowsAppsDirectory()
    {
        var familyName = CompanionOperatorDataLayout.TryReadPackagedFamilyNameFromProcessPath(
            @"C:\Program Files\WindowsApps\MesmerPrism.ViscerealityCompanion_0.1.46.0_x64__8wekyb3d8bbwe\ViscerealityCompanion.App\ViscerealityCompanion.exe");

        Assert.Equal("MesmerPrism.ViscerealityCompanion_8wekyb3d8bbwe", familyName);
    }

    [Fact]
    public void TryResolveLegacyPackagedRoot_UsesLegacyPreviewRoot_ForPublicReleasePackage()
    {
        var legacyRoot = CompanionOperatorDataLayout.TryResolveLegacyPackagedRoot(
            @"C:\Users\joelp\AppData\Local",
            "MesmerPrism.ViscerealityCompanion_8wekyb3d8bbwe",
            path => string.Equals(
                path,
                @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.ViscerealityCompanionPreview_8wekyb3d8bbwe\LocalCache\Local\ViscerealityCompanion",
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(
            @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.ViscerealityCompanionPreview_8wekyb3d8bbwe\LocalCache\Local\ViscerealityCompanion",
            legacyRoot);
    }
}
