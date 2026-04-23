namespace ViscerealityCompanion.Integration.Tests;

public sealed class OperationalGuardrailTests
{
    [Fact]
    public void DesktopLauncher_prefers_current_package_identities_before_legacy_preview()
    {
        var script = ReadRepoText("tools", "app", "Start-Desktop-App.ps1");

        var releaseIndex = script.IndexOf("$script:MesmerPrism_ViscerealityCompanion,", StringComparison.Ordinal);
        var devIndex = script.IndexOf("$script:MesmerPrism_ViscerealityCompanionDev,", StringComparison.Ordinal);
        var previewIndex = script.IndexOf("$script:MesmerPrism_ViscerealityCompanionPreview", StringComparison.Ordinal);

        Assert.True(releaseIndex >= 0, "Release package identity must be supported by the launcher.");
        Assert.True(devIndex >= 0, "Packaged Dev identity must remain available as a fallback launcher target.");
        Assert.True(previewIndex >= 0, "Legacy Preview identity must remain available only as the last fallback.");
        Assert.True(releaseIndex < devIndex, "Release package identity must be preferred over packaged Dev.");
        Assert.True(devIndex < previewIndex, "Packaged Dev must be preferred over legacy Preview.");
    }

    [Fact]
    public void SignedDotNetTestWrapper_records_logs_and_uses_hang_diagnostics()
    {
        var script = ReadRepoText("tools", "app", "Invoke-Signed-DotNetTest.ps1");

        Assert.Contains("Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("X509Store", script, StringComparison.Ordinal);
        Assert.Contains("still running", script, StringComparison.Ordinal);
        Assert.Contains(".exitcode", script, StringComparison.Ordinal);
        Assert.Contains("signed-dotnet-build.log", script, StringComparison.Ordinal);
        Assert.Contains("signed-dotnet-test.log", script, StringComparison.Ordinal);
        Assert.Contains("trx;LogFileName", script, StringComparison.Ordinal);
        Assert.Contains("--blame-hang", script, StringComparison.Ordinal);
        Assert.Contains("--blame-hang-timeout", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-AuthenticodeSignature", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Cert:\\", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PullRequestCi_builds_github_pages_site_with_lockfile_install()
    {
        var ciWorkflow = ReadRepoText(".github", "workflows", "ci.yml");
        var pagesWorkflow = ReadRepoText(".github", "workflows", "pages.yml");

        Assert.Contains("pull_request:", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("npm ci", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("npm run pages:build", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("npm ci", pagesWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("npm install", pagesWorkflow, StringComparison.Ordinal);
    }

    private static string ReadRepoText(params string[] segments)
    {
        return File.ReadAllText(GetRepoPath(segments));
    }

    private static string GetRepoPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ViscerealityCompanion.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
    }
}
