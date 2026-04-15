using System.IO;

namespace ViscerealityCompanion.App;

internal static class AppAssetLocator
{
    private static readonly string[] BundledCliEntryPoints =
    [
        "viscereality.exe",
        "viscereality.dll"
    ];

    public static string? TryResolveQuestSessionKitRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_QUEST_SESSION_KIT_ROOT"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "quest-session-kit")),
            Path.Combine(AppContext.BaseDirectory, "samples", "quest-session-kit"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit"));

    public static string ResolveQuestSessionKitRoot()
        => TryResolveQuestSessionKitRoot()
            ?? throw new DirectoryNotFoundException("Could not resolve the requested Viscereality asset directory.");

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

    public static string? TryResolveDocsRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_DOCS_ROOT"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs")),
            Path.Combine(AppContext.BaseDirectory, "docs"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "docs"));

    public static string? TryResolveBundledCliRoot()
        => TryResolveExistingDirectoryContainingAnyFile(
            BundledCliEntryPoints,
            Environment.GetEnvironmentVariable("VISCEREALITY_BUNDLED_CLI_ROOT"),
            Path.Combine(AppContext.BaseDirectory, "cli", "current"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "cli-win-x64")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ViscerealityCompanion.Cli", "bin", "Debug", "net10.0")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ViscerealityCompanion.Cli", "bin", "Release", "net10.0")),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "artifacts",
                "cli-win-x64"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "src",
                "ViscerealityCompanion.Cli",
                "bin",
                "Debug",
                "net10.0"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "ViscerealityCompanion",
                "src",
                "ViscerealityCompanion.Cli",
                "bin",
                "Release",
                "net10.0"));

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

    public static string? TryResolveBundledSussexVisualProfilesRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_SUSSEX_VISUAL_PROFILE_BUNDLE_ROOT"),
            Path.Combine(TryResolveStudyShellRoot() ?? string.Empty, "sussex-university", "visual-profiles"));

    public static string? TryResolveBundledSussexControllerBreathingProfilesRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("VISCEREALITY_SUSSEX_CONTROLLER_BREATHING_PROFILE_BUNDLE_ROOT"),
            Path.Combine(TryResolveStudyShellRoot() ?? string.Empty, "sussex-university", "sussex-controller-breathing-profiles"),
            Path.Combine(TryResolveStudyShellRoot() ?? string.Empty, "sussex-university", "controller-breathing-profiles"));

    public static string? TryResolveSussexControllerBreathingTuningTemplatePath()
        => TryResolveExistingFile(
            Environment.GetEnvironmentVariable("VISCEREALITY_SUSSEX_CONTROLLER_BREATHING_TUNING_TEMPLATE"),
            Path.Combine(ResolveQuestSessionKitRoot(), "LlmTuningProfiles", "sussex-controller-breathing-tuning-v1.template.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit",
                "LlmTuningProfiles",
                "sussex-controller-breathing-tuning-v1.template.json"));

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

    private static string? TryResolveExistingDirectoryContainingAnyFile(IEnumerable<string> fileNames, params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault(candidate => fileNames.Any(fileName => File.Exists(Path.Combine(candidate, fileName))));
}
