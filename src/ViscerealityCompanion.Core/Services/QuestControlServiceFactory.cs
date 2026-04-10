namespace ViscerealityCompanion.Core.Services;

public static class QuestControlServiceFactory
{
    public static IQuestControlService CreateDefault(string? initialSelector = null)
    {
        var adbPath = AdbExecutableLocator.TryLocate();
        return adbPath is null
            ? new PreviewQuestControlService()
            : new WindowsAdbQuestControlService(adbPath, initialSelector);
    }
}

internal static class AdbExecutableLocator
{
    public static string? TryLocate()
        => ResolveExecutablePath(EnumerateCandidates(), File.Exists);

    internal static string? ResolveExecutablePath(IEnumerable<string?> candidatePaths, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(candidatePaths);
        ArgumentNullException.ThrowIfNull(fileExists);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawCandidate in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(rawCandidate))
                continue;

            string candidate;
            try
            {
                candidate = Path.GetFullPath(rawCandidate.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }

            if (!seen.Add(candidate))
                continue;

            if (fileExists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string?> EnumerateCandidates()
    {
        yield return Environment.GetEnvironmentVariable("VISCEREALITY_ADB_EXE");
        yield return OfficialQuestToolingLayout.AdbExecutablePath;
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android",
            "Sdk",
            "platform-tools",
            "adb.exe");

        foreach (var envVar in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return Path.Combine(value, "platform-tools", "adb.exe");
            }
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in pathEntries)
        {
            yield return Path.Combine(entry, "adb.exe");
            yield return Path.Combine(entry, "adb");
        }
    }
}
