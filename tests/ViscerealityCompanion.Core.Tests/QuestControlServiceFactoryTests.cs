using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class QuestControlServiceFactoryTests
{
    [Fact]
    public void ResolveExecutablePath_prefers_first_existing_candidate_after_normalization()
    {
        var candidates = new[]
        {
            "",
            null,
            "  \"C:\\\\missing\\\\adb.exe\"  ",
            ".\\tools\\..\\managed\\adb.exe",
            "C:\\\\other\\\\adb.exe"
        };

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(".\\managed\\adb.exe")
        };

        var resolved = AdbExecutableLocator.ResolveExecutablePath(candidates, existing.Contains);

        Assert.Equal(Path.GetFullPath(".\\managed\\adb.exe"), resolved);
    }

    [Fact]
    public void ResolveExecutablePath_returns_null_when_no_candidates_exist()
    {
        var resolved = AdbExecutableLocator.ResolveExecutablePath(
            ["C:\\\\missing\\\\adb.exe", null, ""],
            _ => false);

        Assert.Null(resolved);
    }
}
