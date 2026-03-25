using System.IO;

namespace ViscerealityCompanion.App;

internal static class AppAssetLocator
{
    public static string ResolveQuestSessionKitRoot()
        => ResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_QUEST_SESSION_KIT_ROOT"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "quest-session-kit")),
            Path.Combine(AppContext.BaseDirectory, "samples", "quest-session-kit"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit"));

    public static string? TryResolveStudyShellRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_STUDY_SHELL_ROOT"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "study-shells")),
            Path.Combine(AppContext.BaseDirectory, "samples", "study-shells"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "samples",
                "study-shells"));

    private static string ResolveExistingDirectory(params string?[] candidates)
        => TryResolveExistingDirectory(candidates)
            ?? throw new DirectoryNotFoundException("Could not resolve the requested Viscereality asset directory.");

    private static string? TryResolveExistingDirectory(params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault();
}
