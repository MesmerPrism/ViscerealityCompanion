namespace ViscerealityCompanion.Core.Services;

public static class LocalAgentWorkspaceLayout
{
    public const string BundledCliExecutableFileName = "Viscereality CLI.exe";
    public const string LegacyBundledCliExecutableFileName = "viscereality.exe";
    public const string BundledCliDllFileName = "viscereality.dll";

    public static string RootPath => CompanionOperatorDataLayout.LocalAgentWorkspaceRootPath;

    public static string BundledCliRootPath => Path.Combine(RootPath, "cli", "current");
    public static string BundledCliExecutablePath => Path.Combine(BundledCliRootPath, BundledCliExecutableFileName);
    public static string LegacyBundledCliExecutablePath => Path.Combine(BundledCliRootPath, LegacyBundledCliExecutableFileName);
    public static string BundledCliDllPath => Path.Combine(BundledCliRootPath, BundledCliDllFileName);
    public static string BundledCliLslDllPath => LslRuntimeLayout.GetLocalDllPath(BundledCliRootPath);
    public static string BundledCliRuntimeLslDllPath => LslRuntimeLayout.GetRuntimeDllPath(BundledCliRootPath);
}
