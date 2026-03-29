using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class StudyVerificationFingerprintTests
{
    [Fact]
    public void Compute_ReturnsStableHashForEquivalentValues()
    {
        var left = StudyVerificationFingerprint.Compute(
            "com.Viscereality.LslTwin",
            "ABC123",
            "14",
            "2921110053000610",
            "sussex-study-profile",
            "UP1A.231005.007.A1");
        var right = StudyVerificationFingerprint.Compute(
            " com.viscereality.lsltwin ",
            "abc123",
            " 14 ",
            "2921110053000610",
            "SUSSEX-STUDY-PROFILE",
            "up1a.231005.007.a1");

        Assert.Equal(left, right);
    }

    [Fact]
    public void Matches_UsesStoredBaselineIdentity()
    {
        var baseline = new StudyVerificationBaseline(
            ApkSha256: "ABC123",
            SoftwareVersion: "14",
            BuildId: "2921110053000610",
            DisplayId: "UP1A.231005.007.A1",
            DeviceProfileId: "sussex-study-profile",
            EnvironmentHash: "",
            VerifiedAtUtc: DateTimeOffset.Parse("2026-03-29T10:15:00Z"),
            VerifiedBy: "test");

        Assert.True(StudyVerificationFingerprint.Matches(
            baseline,
            "com.Viscereality.LslTwin",
            "ABC123",
            "14",
            "2921110053000610",
            "sussex-study-profile",
            "UP1A.231005.007.A1"));
    }
}
