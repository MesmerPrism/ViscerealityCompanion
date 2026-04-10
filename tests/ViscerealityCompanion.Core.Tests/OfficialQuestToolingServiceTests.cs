using System.Security.Cryptography;
using System.Text;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class OfficialQuestToolingServiceTests
{
    [Fact]
    public void IntegrityMatchesSha512_accepts_matching_payload()
    {
        var payload = Encoding.UTF8.GetBytes("hzdb payload");
        var integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(payload));

        Assert.True(OfficialQuestToolingService.IntegrityMatchesSha512(payload, integrity));
    }

    [Fact]
    public void ChecksumMatchesSha1_accepts_matching_payload()
    {
        var payload = Encoding.UTF8.GetBytes("platform-tools payload");
        var checksum = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant();

        Assert.True(OfficialQuestToolingService.ChecksumMatchesSha1(payload, checksum));
    }

    [Fact]
    public void ParsePlatformToolsRevision_reads_source_properties_revision()
    {
        var revision = OfficialQuestToolingService.ParsePlatformToolsRevision("""
            Pkg.Desc=Android SDK Platform-Tools
            Pkg.Revision=37.0.0
            """);

        Assert.Equal("37.0.0", revision);
    }

    [Theory]
    [InlineData(null, "C:\\tools\\hzdb.exe", "1.0.1", true)]
    [InlineData("", "C:\\tools\\hzdb.exe", "1.0.1", true)]
    [InlineData("1.0.0", "C:\\tools\\hzdb.exe", "1.0.1", true)]
    [InlineData("1.0.1", "C:\\missing\\hzdb.exe", "1.0.1", true)]
    [InlineData("1.0.1", "C:\\tools\\hzdb.exe", "1.0.1", false)]
    public void NeedsInstall_matches_expected_conditions(string? installedVersion, string targetPath, string availableVersion, bool expected)
    {
        var result = OfficialQuestToolingService.NeedsInstall(
            installedVersion,
            targetPath,
            availableVersion,
            path => !path.Contains("missing", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expected, result);
    }
}
