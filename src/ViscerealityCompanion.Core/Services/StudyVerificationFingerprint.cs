using System.Security.Cryptography;
using System.Text;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public static class StudyVerificationFingerprint
{
    public static string Compute(
        string packageId,
        string apkSha256,
        string softwareVersion,
        string buildId,
        string deviceProfileId,
        string? displayId = null)
    {
        var normalized = string.Join(
            "|",
            Normalize(packageId),
            Normalize(apkSha256),
            Normalize(softwareVersion),
            Normalize(buildId),
            Normalize(deviceProfileId));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static bool Matches(
        StudyVerificationBaseline baseline,
        string packageId,
        string apkSha256,
        string softwareVersion,
        string buildId,
        string deviceProfileId,
        string? displayId = null)
    {
        var expected = string.IsNullOrWhiteSpace(baseline.EnvironmentHash)
            ? Compute(
                packageId,
                baseline.ApkSha256,
                baseline.SoftwareVersion,
                baseline.BuildId,
                baseline.DeviceProfileId,
                baseline.DisplayId)
            : baseline.EnvironmentHash.Trim();

        var actual = Compute(packageId, apkSha256, softwareVersion, buildId, deviceProfileId, displayId);
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
}
