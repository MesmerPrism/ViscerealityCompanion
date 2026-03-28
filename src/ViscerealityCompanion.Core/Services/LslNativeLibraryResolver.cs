using System.Reflection;
using System.Runtime.InteropServices;

namespace ViscerealityCompanion.Core.Services;

internal static class LslNativeLibraryResolver
{
    private static readonly object Sync = new();
    private static readonly string[] CandidateLibraryPaths = BuildCandidateLibraryPaths();
    private static nint _libraryHandle;
    private static bool _resolverInstalled;

    public static void EnsureInstalled(Assembly assembly)
    {
        lock (Sync)
        {
            if (_resolverInstalled)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(assembly, ResolveLibrary);
            _resolverInstalled = true;
        }
    }

    public static bool TryLoad(out nint libraryHandle, out string detail)
    {
        lock (Sync)
        {
            if (_libraryHandle != IntPtr.Zero)
            {
                libraryHandle = _libraryHandle;
                detail = string.Empty;
                return true;
            }

            foreach (var candidate in CandidateLibraryPaths)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                if (!NativeLibrary.TryLoad(candidate, out _libraryHandle))
                {
                    continue;
                }

                libraryHandle = _libraryHandle;
                detail = candidate;
                return true;
            }

            libraryHandle = IntPtr.Zero;
            detail = $"Could not locate or load lsl.dll. Set VISCEREALITY_LSL_DLL or place lsl.dll in the app folder. Searched: {string.Join("; ", CandidateLibraryPaths)}";
            return false;
        }
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "lsl", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        return TryLoad(out var libraryHandle, out _) ? libraryHandle : IntPtr.Zero;
    }

    private static string[] BuildCandidateLibraryPaths()
    {
        var candidates = new List<string>();

        void AddIfPresent(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(Path.GetFullPath(path));
            }
        }

        AddIfPresent(Environment.GetEnvironmentVariable("VISCEREALITY_LSL_DLL"));
        AddIfPresent(Path.Combine(AppContext.BaseDirectory, "lsl.dll"));
        AddIfPresent(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "lsl.dll"));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddUserToolsLiblslCandidates(candidates, userProfile);
        AddIfPresent(Path.Combine(userProfile, "source", "repos", "AstralKarateDojo", "Assets", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));
        AddIfPresent(Path.Combine(userProfile, "source", "repos", "AstralKarateDojo-phone-monitor-shell", "Assets", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));
        AddIfPresent(Path.Combine(userProfile, "source", "repos", "UnitySixthSense", "Assets", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));
        AddIfPresent(Path.Combine(userProfile, "source", "repos", "Viscereality", "Viscereality", "Packages", "com.labstreaminglayer.lsl4unity", "Plugins", "LSL", "Windows", "x64", "lsl.dll"));

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddUserToolsLiblslCandidates(ICollection<string> candidates, string userProfile)
    {
        var userToolsRoot = Path.Combine(userProfile, "Tools", "liblsl");
        if (!Directory.Exists(userToolsRoot))
        {
            return;
        }

        foreach (var versionDirectory in Directory.EnumerateDirectories(userToolsRoot)
                     .OrderByDescending(path =>
                     {
                         var versionName = Path.GetFileName(path);
                         return Version.TryParse(versionName, out var version)
                             ? version
                             : new Version(0, 0);
                     }))
        {
            var candidate = Path.Combine(versionDirectory, "bin", "lsl.dll");
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                candidates.Add(Path.GetFullPath(candidate));
            }
        }
    }
}
