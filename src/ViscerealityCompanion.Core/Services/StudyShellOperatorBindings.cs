using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public static class StudyShellOperatorBindings
{
    public static QuestAppTarget CreateQuestTarget(StudyShellDefinition study, string? apkPath = null)
        => new(
            Id: study.Id,
            Label: study.App.Label,
            PackageId: study.App.PackageId,
            ApkFile: string.IsNullOrWhiteSpace(apkPath) ? study.App.ApkPath : apkPath,
            LaunchComponent: study.App.LaunchComponent,
            BrowserPackageId: "com.oculus.browser",
            Description: study.Description,
            Tags: ["viscereality", "runtime", "lsl", "twin"],
            ApkSha256: study.App.Sha256,
            CompatibilityStatus: ApkCompatibilityStatus.Compatible,
            CompatibilityProfile: study.Label,
            CompatibilityNotes: study.App.Notes,
            VerificationBaseline: study.App.VerificationBaseline);

    public static DeviceProfile CreateDeviceProfile(StudyShellDefinition study)
        => new(
            study.DeviceProfile.Id,
            study.DeviceProfile.Label,
            study.DeviceProfile.Description,
            new Dictionary<string, string>(study.DeviceProfile.Properties, StringComparer.OrdinalIgnoreCase));
}
