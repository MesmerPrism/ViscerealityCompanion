using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed record WindowsInstallFootprintCleanupResult(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<string> SkippedPaths)
{
    public int RemovedShortcutCount
        => RemovedPaths.Count(path => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase));

    public int RemovedLegacyCliCount
        => RemovedPaths.Count(path =>
            string.Equals(
                Path.GetFileName(path),
                LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName,
                StringComparison.OrdinalIgnoreCase));

    public int RemovedWorkspaceCount
        => RemovedPaths.Count(path =>
            !string.IsNullOrWhiteSpace(path) &&
            Path.GetFileName(Path.TrimEndingDirectorySeparator(path))
                .Equals("agent-workspace", StringComparison.OrdinalIgnoreCase));
}

public sealed class WindowsInstallFootprintCleanupService
{
    private readonly Func<WindowsInstallFootprintSnapshot> _snapshotProvider;
    private readonly Action<string> _deleteFile;
    private readonly Action<string> _deleteDirectory;

    public WindowsInstallFootprintCleanupService(
        Func<WindowsInstallFootprintSnapshot>? snapshotProvider = null,
        Action<string>? deleteFile = null,
        Action<string>? deleteDirectory = null)
    {
        _snapshotProvider = snapshotProvider ?? WindowsEnvironmentAnalysisService.SnapshotInstallFootprint;
        _deleteFile = deleteFile ?? File.Delete;
        _deleteDirectory = deleteDirectory ?? (path => Directory.Delete(path, recursive: true));
    }

    public WindowsInstallFootprintCleanupResult Cleanup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsInstallFootprintCleanupResult(
                OperationOutcomeKind.Preview,
                "Install-footprint cleanup is Windows-only.",
                "The packaged-family, shortcut, and CLI export cleanup flow only applies on Windows.",
                [],
                []);
        }

        var snapshot = _snapshotProvider();
        var shortcutPathsToRemove = WindowsInstallFootprintRules.GetUnexpectedShortcutPaths(snapshot);
        var legacyCliPathsToRemove = WindowsInstallFootprintRules.GetLegacyCliExecutablePathsSafeToRemove(snapshot);
        var staleWorkspaceRootsToRemove = WindowsInstallFootprintRules.GetStaleUnpackagedAgentWorkspaceRootsSafeToRemove(snapshot);

        var removedPaths = new List<string>();
        var skippedPaths = new List<string>();
        foreach (var path in shortcutPathsToRemove
                     .Concat(legacyCliPathsToRemove)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _deleteFile(path);
                removedPaths.Add(path);
            }
            catch (Exception exception)
            {
                skippedPaths.Add($"{path} ({exception.Message})");
            }
        }

        foreach (var path in staleWorkspaceRootsToRemove)
        {
            try
            {
                _deleteDirectory(path);
                removedPaths.Add(path);
            }
            catch (Exception exception)
            {
                skippedPaths.Add($"{path} ({exception.Message})");
            }
        }

        var untouchedAdvisories = BuildUntouchedAdvisories(snapshot, legacyCliPathsToRemove, staleWorkspaceRootsToRemove);
        var hasWarnings = skippedPaths.Count > 0 || untouchedAdvisories.Count > 0;
        var summary = removedPaths.Count switch
        {
            > 0 when hasWarnings => "Removed some stale Windows install-footprint artifacts, but manual follow-up remains.",
            > 0 => "Removed stale Windows install-footprint artifacts.",
            _ when hasWarnings => "No safe install-footprint cleanup was applied.",
            _ => "Windows install footprint already looked clean."
        };

        var detailLines = new List<string>
        {
            $"Removed shortcuts: {removedPaths.Count(path => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))}.",
            $"Removed legacy generic CLI exports: {removedPaths.Count(path => string.Equals(Path.GetFileName(path), LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName, StringComparison.OrdinalIgnoreCase))}.",
            $"Removed stale unpackaged workspaces: {removedPaths.Count(path => WindowsInstallFootprintRules.IsUnpackagedAgentWorkspaceRoot(snapshot, path))}."
        };

        if (removedPaths.Count > 0)
        {
            detailLines.Add($"Removed artifacts:{Environment.NewLine}{RenderPathList(removedPaths)}");
        }

        if (skippedPaths.Count > 0)
        {
            detailLines.Add($"Skipped artifacts:{Environment.NewLine}{RenderPathList(skippedPaths)}");
        }

        if (untouchedAdvisories.Count > 0)
        {
            detailLines.Add($"Still manual: {string.Join(" ", untouchedAdvisories)}");
        }

        return new WindowsInstallFootprintCleanupResult(
            hasWarnings ? OperationOutcomeKind.Warning : OperationOutcomeKind.Success,
            summary,
            string.Join(Environment.NewLine, detailLines),
            removedPaths,
            skippedPaths);
    }

    private static IReadOnlyList<string> BuildUntouchedAdvisories(
        WindowsInstallFootprintSnapshot snapshot,
        IReadOnlyList<string> removableLegacyCliPaths,
        IReadOnlyList<string> removableWorkspaceRoots)
    {
        var advisories = new List<string>();
        var legacyPreviewInstalls = snapshot.PackagedInstalls
            .Where(static install => PackagedAppIdentity.IsLegacyPreviewPackageName(install.PackageName))
            .ToArray();
        if (snapshot.PackagedInstalls.Count > 1)
        {
            advisories.Add("Installed packaged families remain unchanged; this button only removes Windows-side shortcut and CLI leftovers.");
        }

        if (legacyPreviewInstalls.Length > 0)
        {
            advisories.Add("The legacy preview package family is still installed and must be retired through the packaged-install path instead of this cleanup button.");
        }

        var untouchedLegacyCliPaths = snapshot.LegacyCliExecutablePaths
            .Except(removableLegacyCliPaths, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (untouchedLegacyCliPaths.Length > 0)
        {
            advisories.Add("Some legacy generic CLI exports were left in place because no branded `Viscereality CLI.exe` sibling was found in the same export folder.");
        }

        if (snapshot.UnpackagedAgentWorkspaceExists && removableWorkspaceRoots.Count == 0)
        {
            advisories.Add("The unpackaged `agent-workspace` mirror was left in place because no packaged install was detected to supersede it.");
        }

        return advisories;
    }

    private static string RenderPathList(IEnumerable<string> paths)
    {
        var ordered = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return ordered.Length == 0
            ? "- none"
            : string.Join(Environment.NewLine, ordered.Select(static path => $"- {path}"));
    }
}

internal static class WindowsInstallFootprintRules
{
    internal static readonly HashSet<string> ExpectedLauncherFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Viscereality Companion.lnk",
        "Viscereality Companion (Local Dev).lnk"
    };

    internal static IReadOnlyList<string> GetAllShortcutPaths(WindowsInstallFootprintSnapshot snapshot)
        => snapshot.DesktopShortcutPaths
            .Concat(snapshot.StartMenuShortcutPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static IReadOnlyList<string> GetUnexpectedShortcutPaths(WindowsInstallFootprintSnapshot snapshot)
        => GetAllShortcutPaths(snapshot)
            .Where(static path => !ExpectedLauncherFileNames.Contains(Path.GetFileName(path)))
            .ToArray();

    internal static IReadOnlyList<string> GetLegacyCliExecutablePathsSafeToRemove(WindowsInstallFootprintSnapshot snapshot)
    {
        var brandedCliDirectories = snapshot.BrandedCliExecutablePaths
            .Select(Path.GetDirectoryName)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return snapshot.LegacyCliExecutablePaths
            .Where(path =>
                string.Equals(
                    Path.GetFileName(path),
                    LocalAgentWorkspaceLayout.LegacyBundledCliExecutableFileName,
                    StringComparison.OrdinalIgnoreCase))
            .Where(path => brandedCliDirectories.Contains(Path.GetDirectoryName(path) ?? string.Empty))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<string> GetStaleUnpackagedAgentWorkspaceRootsSafeToRemove(WindowsInstallFootprintSnapshot snapshot)
    {
        if (!snapshot.UnpackagedAgentWorkspaceExists ||
            snapshot.PackagedInstalls.Count == 0 ||
            string.IsNullOrWhiteSpace(snapshot.UnpackagedAgentWorkspaceRootPath))
        {
            return [];
        }

        return
        [
            snapshot.UnpackagedAgentWorkspaceRootPath
        ];
    }

    internal static bool IsUnpackagedAgentWorkspaceRoot(WindowsInstallFootprintSnapshot snapshot, string path)
        => !string.IsNullOrWhiteSpace(path) &&
           string.Equals(
               Path.TrimEndingDirectorySeparator(path),
               Path.TrimEndingDirectorySeparator(snapshot.UnpackagedAgentWorkspaceRootPath),
               StringComparison.OrdinalIgnoreCase);
}
