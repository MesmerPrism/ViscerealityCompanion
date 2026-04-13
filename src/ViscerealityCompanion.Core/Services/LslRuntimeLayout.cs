namespace ViscerealityCompanion.Core.Services;

public static class LslRuntimeLayout
{
    public static string GetLocalDllPath(string baseDirectory)
        => Path.Combine(Path.GetFullPath(baseDirectory), "lsl.dll");

    public static string GetRuntimeDllPath(string baseDirectory)
        => Path.Combine(Path.GetFullPath(baseDirectory), "runtimes", "win-x64", "native", "lsl.dll");

    public static IReadOnlyList<string> GetLocalCandidatePaths(string baseDirectory)
        => [GetLocalDllPath(baseDirectory), GetRuntimeDllPath(baseDirectory)];

    public static string? TryResolveExistingLocalPath(string baseDirectory)
        => GetLocalCandidatePaths(baseDirectory)
            .FirstOrDefault(File.Exists);
}
