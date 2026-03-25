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
    {
        var candidates = new List<string>();

        void AddCandidate(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(path);
            }
        }

        AddCandidate(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android",
            "Sdk",
            "platform-tools",
            "adb.exe"));

        foreach (var envVar in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                AddCandidate(Path.Combine(value, "platform-tools", "adb.exe"));
            }
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in pathEntries)
        {
            AddCandidate(Path.Combine(entry, "adb.exe"));
            AddCandidate(Path.Combine(entry, "adb"));
        }

        return candidates
            .FirstOrDefault(candidate => File.Exists(candidate));
    }
}
