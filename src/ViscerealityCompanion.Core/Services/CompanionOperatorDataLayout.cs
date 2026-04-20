using System.Text.RegularExpressions;

namespace ViscerealityCompanion.Core.Services;

public static class CompanionOperatorDataLayout
{
    public const string RootOverrideEnvironmentVariable = "VISCEREALITY_OPERATOR_DATA_ROOT";

    private static readonly Regex WindowsAppsPackageDirectoryPattern = new(
        @"^(?<name>.+?)_(?<version>\d+\.\d+\.\d+\.\d+)_[^_]+__(?<publisherid>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string RootPath => ResolveRootPath();

    public static string SessionRootPath => Path.Combine(RootPath, "session");
    public static string ToolingRootPath => Path.Combine(RootPath, "tooling");
    public static string LogsRootPath => Path.Combine(RootPath, "logs");
    public static string StudyDataRootPath => Path.Combine(RootPath, "study-data");
    public static string ScreenshotsRootPath => Path.Combine(RootPath, "screenshots");
    public static string RuntimeConfigRootPath => Path.Combine(RootPath, "runtime-config");
    public static string OscillatorConfigRootPath => Path.Combine(RootPath, "oscillator-config");
    public static string LocalAgentWorkspaceRootPath => Path.Combine(RootPath, "agent-workspace");
    public static string SussexVisualProfilesRootPath => Path.Combine(RootPath, "sussex-visual-profiles");
    public static string SussexControllerBreathingProfilesRootPath => Path.Combine(RootPath, "sussex-controller-breathing-profiles");
    public static string PerfTraceRootPath => Path.Combine(RootPath, "perf-traces");
    public static string DiagnosticsRootPath => Path.Combine(RootPath, "diagnostics");

    public static string NormalizeHostVisiblePath(string? path)
    {
        if (!TryNormalizeFullPath(path, out var normalizedPath))
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        }

        var localAppDataPath = ResolveWindowsLocalAppDataPath();
        var resolvedRoot = ResolveRootPath();
        return RemapToResolvedRootPath(normalizedPath, localAppDataPath, resolvedRoot) ?? normalizedPath;
    }

    public static bool TryResolveExistingDirectory(string? path, out string resolvedPath)
    {
        resolvedPath = NormalizeHostVisiblePath(path);
        if (TryNormalizeFullPath(path, out var originalPath) && Directory.Exists(originalPath))
        {
            resolvedPath = originalPath;
            return true;
        }

        return !string.IsNullOrWhiteSpace(resolvedPath) && Directory.Exists(resolvedPath);
    }

    public static bool TryResolveExistingFile(string? path, out string resolvedPath)
    {
        resolvedPath = NormalizeHostVisiblePath(path);
        if (TryNormalizeFullPath(path, out var originalPath) && File.Exists(originalPath))
        {
            resolvedPath = originalPath;
            return true;
        }

        return !string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath);
    }

    internal static string ResolveRootPath()
        => ResolveRootPath(
            ResolveWindowsLocalAppDataPath(),
            TryReadCurrentPackageFamilyName() ?? TryReadPackagedFamilyNameFromProcessPath(Environment.ProcessPath),
            Environment.GetEnvironmentVariable(RootOverrideEnvironmentVariable));

    internal static string ResolveRootPath(string localAppDataPath, string? packageFamilyName, string? overrideRoot)
    {
        if (TryNormalizeFullPath(overrideRoot, out var normalizedOverride))
        {
            return normalizedOverride;
        }

        var normalizedLocalAppData = NormalizeLocalAppDataPath(localAppDataPath);
        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            return Path.Combine(normalizedLocalAppData, "ViscerealityCompanion");
        }

        var legacyRoot = TryResolveLegacyPackagedRoot(normalizedLocalAppData, packageFamilyName);
        return legacyRoot ?? Path.Combine(normalizedLocalAppData, "Packages", packageFamilyName, "LocalCache", "Local", "ViscerealityCompanion");
    }

    internal static string? TryResolveLegacyPackagedRoot(string localAppDataPath, string? packageFamilyName, Func<string, bool>? directoryExists = null)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            return null;
        }

        directoryExists ??= Directory.Exists;
        if (!TrySplitPackageFamilyName(packageFamilyName, out var packageName, out var publisherId) ||
            !PackagedAppIdentity.IsReleasePackageName(packageName))
        {
            return null;
        }

        foreach (var migrationSourcePackageName in PackagedAppIdentity.GetMigrationSourcePackageNames(packageName))
        {
            var legacyFamilyName = $"{migrationSourcePackageName}_{publisherId}";
            var legacyRoot = Path.Combine(localAppDataPath, "Packages", legacyFamilyName, "LocalCache", "Local", "ViscerealityCompanion");
            if (directoryExists(legacyRoot))
            {
                return legacyRoot;
            }
        }

        return null;
    }

    private static bool TrySplitPackageFamilyName(string packageFamilyName, out string packageName, out string publisherId)
    {
        var separatorIndex = packageFamilyName.LastIndexOf('_');
        if (separatorIndex <= 0 || separatorIndex >= packageFamilyName.Length - 1)
        {
            packageName = string.Empty;
            publisherId = string.Empty;
            return false;
        }

        packageName = packageFamilyName[..separatorIndex];
        publisherId = packageFamilyName[(separatorIndex + 1)..];
        return true;
    }

    internal static string? RemapToResolvedRootPath(string? path, string localAppDataPath, string resolvedRootPath)
    {
        if (!TryNormalizeFullPath(path, out var normalizedPath) ||
            !TryNormalizeFullPath(resolvedRootPath, out var normalizedResolvedRoot))
        {
            return null;
        }

        var unpackagedRoot = ResolveRootPath(localAppDataPath, packageFamilyName: null, overrideRoot: null);
        if (PathsEqual(normalizedResolvedRoot, unpackagedRoot))
        {
            return null;
        }

        if (!TryGetRelativePathUnderRoot(unpackagedRoot, normalizedPath, out var relativePath))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(relativePath)
            ? normalizedResolvedRoot
            : Path.Combine(normalizedResolvedRoot, relativePath);
    }

    internal static string? TryReadPackagedFamilyNameFromProcessPath(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath) ||
            processPath.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        DirectoryInfo? directory;
        try
        {
            directory = new FileInfo(processPath).Directory;
        }
        catch
        {
            return null;
        }

        while (directory is not null)
        {
            var match = WindowsAppsPackageDirectoryPattern.Match(directory.Name);
            if (match.Success)
            {
                return $"{match.Groups["name"].Value}_{match.Groups["publisherid"].Value}";
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string ResolveWindowsLocalAppDataPath()
        => NormalizeLocalAppDataPath(
            Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static string NormalizeLocalAppDataPath(string? localAppDataPath)
    {
        if (TryNormalizeFullPath(localAppDataPath, out var normalized))
        {
            return normalized;
        }

        return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ViscerealityCompanion"));
    }

    private static string? TryReadCurrentPackageFamilyName()
    {
        try
        {
            var packageType = Type.GetType("Windows.ApplicationModel.Package, Windows, ContentType=WindowsRuntime");
            if (packageType is null)
            {
                return null;
            }

            var currentPackage = packageType.GetProperty("Current")?.GetValue(null);
            if (currentPackage is null)
            {
                return null;
            }

            var packageId = currentPackage.GetType().GetProperty("Id")?.GetValue(currentPackage);
            return packageId?.GetType().GetProperty("FamilyName")?.GetValue(packageId)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryNormalizeFullPath(string? path, out string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            normalizedPath = string.Empty;
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(path.Trim().Trim('"'));
            return true;
        }
        catch
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static bool TryGetRelativePathUnderRoot(string rootPath, string candidatePath, out string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(rootPath);
        if (PathsEqual(candidatePath, normalizedRoot))
        {
            relativePath = string.Empty;
            return true;
        }

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        if (candidatePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = candidatePath[rootWithSeparator.Length..];
            return true;
        }

        relativePath = string.Empty;
        return false;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);
}
