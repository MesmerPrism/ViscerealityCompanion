using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App;

internal static class PackagedTaskbarShortcutRepairService
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;
    private const string TaskbarPinnedRelativePath = @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";
    private const string ShortcutNamePrefix = "Viscereality Companion";
    private const string MainApplicationId = "App";

    public static void TryRepairLegacyPinnedTaskbarShortcut()
        => TryRepairLegacyPinnedTaskbarShortcut(StartupUpdateDiagnostics.Write);

    internal static void TryRepairLegacyPinnedTaskbarShortcut(Action<string>? log)
    {
        var packageFamilyName = TryReadCurrentPackageFamilyName();
        if (!IsCurrentReleasePackageFamilyName(packageFamilyName))
        {
            log?.Invoke("Taskbar shortcut repair skipped because the current process is not running under the public release package family.");
            return;
        }

        var taskbarPinnedDirectory = TryResolveTaskbarPinnedDirectory();
        if (string.IsNullOrWhiteSpace(taskbarPinnedDirectory) || !Directory.Exists(taskbarPinnedDirectory))
        {
            log?.Invoke("Taskbar shortcut repair skipped because the pinned taskbar shortcut directory was not found.");
            return;
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            log?.Invoke("Taskbar shortcut repair skipped because WScript.Shell is unavailable.");
            return;
        }

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return;
            }

            foreach (var shortcutPath in Directory.EnumerateFiles(taskbarPinnedDirectory, "*.lnk"))
            {
                var shortcutName = Path.GetFileName(shortcutPath);
                if (!ShouldInspectShortcutName(shortcutName))
                {
                    continue;
                }

                if (!TryReadShortcut(shell, shortcutPath, out var targetPath, out var arguments) ||
                    !ShouldRepairShortcut(shortcutName, targetPath, arguments))
                {
                    continue;
                }

                WriteShortcut(shell, shortcutPath, packageFamilyName!);
                log?.Invoke($"Taskbar shortcut repair updated {shortcutPath} to target {packageFamilyName}.");
            }
        }
        catch (Exception exception)
        {
            log?.Invoke($"Taskbar shortcut repair failed: {exception.Message}");
        }
        finally
        {
            ReleaseComObject(shell);
        }
    }

    internal static bool IsCurrentReleasePackageFamilyName(string? packageFamilyName)
        => !string.IsNullOrWhiteSpace(packageFamilyName) &&
           packageFamilyName.StartsWith($"{PackagedAppIdentity.ReleasePackageName}_", StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldInspectShortcutName(string shortcutName)
        => shortcutName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
           shortcutName.StartsWith(ShortcutNamePrefix, StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldRepairShortcut(string shortcutName, string? targetPath, string? arguments)
    {
        if (!ShouldInspectShortcutName(shortcutName))
        {
            return false;
        }

        return ContainsLegacyPackageReference(targetPath) || ContainsLegacyPackageReference(arguments);
    }

    internal static string BuildAppsFolderArguments(string packageFamilyName)
        => $@"shell:AppsFolder\{packageFamilyName}!{MainApplicationId}";

    private static bool ContainsLegacyPackageReference(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.IndexOf(PackagedAppIdentity.LegacyPreviewPackageName, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool TryReadShortcut(object shell, string shortcutPath, out string targetPath, out string arguments)
    {
        object? shortcut = null;
        try
        {
            dynamic shellDispatch = shell;
            shortcut = shellDispatch.CreateShortcut(shortcutPath);
            dynamic shortcutDispatch = shortcut;
            targetPath = Convert.ToString(shortcutDispatch.TargetPath) ?? string.Empty;
            arguments = Convert.ToString(shortcutDispatch.Arguments) ?? string.Empty;
            return true;
        }
        catch
        {
            targetPath = string.Empty;
            arguments = string.Empty;
            return false;
        }
        finally
        {
            ReleaseComObject(shortcut);
        }
    }

    private static void WriteShortcut(object shell, string shortcutPath, string packageFamilyName)
    {
        object? shortcut = null;
        try
        {
            dynamic shellDispatch = shell;
            shortcut = shellDispatch.CreateShortcut(shortcutPath);

            if (shortcut is null)
            {
                return;
            }

            dynamic shortcutDispatch = shortcut;
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            shortcutDispatch.TargetPath = Path.Combine(windowsDirectory, "explorer.exe");
            shortcutDispatch.Arguments = BuildAppsFolderArguments(packageFamilyName);
            shortcutDispatch.WorkingDirectory = windowsDirectory;
            shortcutDispatch.Description = "Launch Viscereality Companion";

            var iconSource = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(iconSource))
            {
                shortcutDispatch.IconLocation = $"{iconSource},0";
            }

            shortcutDispatch.Save();
        }
        finally
        {
            ReleaseComObject(shortcut);
        }
    }

    private static string? TryReadCurrentPackageFamilyName()
    {
        var length = 0;
        var initialResult = GetCurrentPackageFamilyName(ref length, null);
        if (initialResult == AppModelErrorNoPackage)
        {
            return null;
        }

        if (initialResult != ErrorInsufficientBuffer || length <= 0)
        {
            return null;
        }

        var builder = new StringBuilder(length);
        var result = GetCurrentPackageFamilyName(ref length, builder);
        return result == 0 ? builder.ToString() : null;
    }

    private static string? TryResolveTaskbarPinnedDirectory()
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfilePath))
        {
            return Path.Combine(userProfilePath, "AppData", "Roaming", TaskbarPinnedRelativePath);
        }

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(appDataPath)
            ? null
            : Path.Combine(appDataPath, TaskbarPinnedRelativePath);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, StringBuilder? packageFamilyName);
}
