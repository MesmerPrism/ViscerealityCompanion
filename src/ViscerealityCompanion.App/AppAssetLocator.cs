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

    public static string? TryResolveOscillatorConfigRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_OSCILLATOR_CONFIG_ROOT"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "oscillator-config")),
            Path.Combine(AppContext.BaseDirectory, "samples", "oscillator-config"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "samples",
                "oscillator-config"));

    public static string? TryResolveSussexParticleSizeTemplatePath()
        => TryResolveExistingFile(
            Environment.GetEnvironmentVariable("VISCEREALITY_SUSSEX_PARTICLE_SIZE_TEMPLATE"),
            Path.Combine(TryResolveOscillatorConfigRoot() ?? string.Empty, "llm-tuning", "sussex-particle-size-v1.template.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit",
                "LlmTuningProfiles",
                "sussex-particle-size-v1.template.json"));

    public static string? TryResolveSussexVisualTuningTemplatePath()
        => TryResolveExistingFile(
            Environment.GetEnvironmentVariable("VISCEREALITY_SUSSEX_VISUAL_TUNING_TEMPLATE"),
            Path.Combine(TryResolveOscillatorConfigRoot() ?? string.Empty, "llm-tuning", "sussex-visual-tuning-v1.template.json"));

    private static string ResolveExistingDirectory(params string?[] candidates)
        => TryResolveExistingDirectory(candidates)
            ?? throw new DirectoryNotFoundException("Could not resolve the requested Viscereality asset directory.");

    private static string? TryResolveExistingDirectory(params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault();

    private static string? TryResolveExistingFile(params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault();
}
