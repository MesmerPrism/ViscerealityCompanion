using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class HzdbServiceTests
{
    [Fact]
    public void ResolveNpxCommandPath_prefers_first_existing_normalized_candidate()
    {
        var candidates = new[]
        {
            "",
            null,
            "  \"C:\\\\missing\\\\npx.cmd\"  ",
            ".\\tools\\..\\fake\\npx.cmd",
            "C:\\\\other\\\\npx.cmd"
        };

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(".\\fake\\npx.cmd")
        };

        var resolved = WindowsHzdbService.ResolveNpxCommandPath(candidates, existing.Contains);

        Assert.Equal(Path.GetFullPath(".\\fake\\npx.cmd"), resolved);
    }

    [Fact]
    public void ResolveNpxCommandPath_returns_null_when_no_candidates_exist()
    {
        var resolved = WindowsHzdbService.ResolveNpxCommandPath(
            ["C:\\\\missing\\\\npx.cmd", null, ""],
            _ => false);

        Assert.Null(resolved);
    }
}
