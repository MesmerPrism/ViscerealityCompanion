using System.IO;

namespace ViscerealityCompanion.Cli;

internal static class CliAssetLocator
{
    public static string ResolveQuestSessionKitRoot()
        => TryResolveQuestSessionKitRoot()
            ?? throw new DirectoryNotFoundException("Could not resolve the Quest Session Kit root for the CLI.");

    public static string ResolveStudyShellRoot()
        => TryResolveStudyShellRoot()
            ?? throw new DirectoryNotFoundException("Could not resolve the study-shell root for the CLI.");

    public static string? TryResolveQuestSessionKitRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_QUEST_SESSION_KIT_ROOT"),
            TryResolveRepoRelativeDirectory("samples", "quest-session-kit"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "samples",
                "quest-session-kit"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit"));

    public static string? TryResolveStudyShellRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_STUDY_SHELL_ROOT"),
            TryResolveRepoRelativeDirectory("samples", "study-shells"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "samples",
                "study-shells"));

    public static string? TryResolveOscillatorConfigRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_OSCILLATOR_CONFIG_ROOT"),
            TryResolveRepoRelativeDirectory("samples", "oscillator-config"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "samples",
                "oscillator-config"));

    public static string? TryResolveDocsRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_DOCS_ROOT"),
            TryResolveRepoRelativeDirectory("docs"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "docs"));

    private static string? TryResolveRepoRelativeDirectory(params string[] relativeSegments)
    {
        foreach (var root in EnumerateSearchRoots())
        {
            var candidate = Path.Combine([root, .. relativeSegments]);
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(seed))
            {
                continue;
            }

            DirectoryInfo? current;
            try
            {
                current = new DirectoryInfo(Path.GetFullPath(seed));
            }
            catch
            {
                continue;
            }

            while (current is not null)
            {
                if (seen.Add(current.FullName))
                {
                    yield return current.FullName;
                }

                current = current.Parent;
            }
        }
    }

    private static string? TryResolveExistingDirectory(params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault();
}
