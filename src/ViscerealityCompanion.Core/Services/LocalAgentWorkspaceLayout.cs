namespace ViscerealityCompanion.Core.Services;

public static class LocalAgentWorkspaceLayout
{
    public static string RootPath => CompanionOperatorDataLayout.LocalAgentWorkspaceRootPath;

    public static string BundledCliRootPath => Path.Combine(RootPath, "cli", "current");
    public static string BundledCliExecutablePath => Path.Combine(BundledCliRootPath, "viscereality.exe");
    public static string BundledCliDllPath => Path.Combine(BundledCliRootPath, "viscereality.dll");
    public static string BundledCliLslDllPath => LslRuntimeLayout.GetLocalDllPath(BundledCliRootPath);
    public static string BundledCliRuntimeLslDllPath => LslRuntimeLayout.GetRuntimeDllPath(BundledCliRootPath);
}
