using System.Text.RegularExpressions;

namespace ViscerealityCompanion.Core.Services;

internal static partial class AdbShellSupport
{
    internal sealed record ForegroundAppSnapshot(
        string PackageId,
        string ActivityName,
        string PrimaryComponent,
        IReadOnlyList<string> VisibleComponents);

    public static string BuildForceStopCommand(string packageId)
        => $"am force-stop {Quote(packageId)}";

    public static string BuildExplicitLaunchCommand(string component)
        => $"am start -W -n {Quote(component)}";

    public static string BuildMonkeyLaunchCommand(string packageId)
        => $"monkey -p {Quote(packageId)} -c android.intent.category.LAUNCHER 1";

    public static string BuildOpenUrlCommand(string url, string? browserPackageId)
    {
        var packageArg = string.IsNullOrWhiteSpace(browserPackageId) ? string.Empty : $" -p {Quote(browserPackageId)}";
        return $"am start -W -a android.intent.action.VIEW -d {Quote(url)}{packageArg}";
    }

    public static string? ParseForegroundPackage(string output)
        => ParseForegroundSnapshot(output)?.PackageId;

    public static string? ParseForegroundComponent(string output)
        => ParseForegroundSnapshot(output)?.PrimaryComponent;

    public static ForegroundAppSnapshot? ParseForegroundSnapshot(string output)
    {
        var visibleComponents = ParseVisibleActivityComponents(output);
        if (visibleComponents.Count > 0)
        {
            return BuildSnapshot(visibleComponents[0], visibleComponents);
        }

        foreach (var pattern in ForegroundPatterns())
        {
            var match = pattern.Match(output);
            if (match.Success)
            {
                var packageId = match.Groups["package"].Value;
                var activityName = match.Groups["activity"].Value;
                var component = BuildComponent(packageId, activityName);
                return new ForegroundAppSnapshot(packageId, activityName, component, [component]);
            }
        }

        return null;
    }

    public static IReadOnlyList<string> ParseVisibleActivityComponents(string output)
    {
        var matches = VisibleActivityPattern().Matches(output);
        if (matches.Count == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new List<string>(matches.Count);

        foreach (Match match in matches)
        {
            var packageId = match.Groups["package"].Value;
            var activityName = match.Groups["activity"].Value;
            var component = BuildComponent(packageId, activityName);
            if (seen.Add(component))
            {
                components.Add(component);
            }
        }

        return components;
    }

    public static IReadOnlyList<string> ParseInstalledPackages(string output)
        => output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["package:".Length..].Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static int? ParseBatteryLevel(string output)
    {
        var match = BatteryLevelPattern().Match(output);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    public static string Quote(string value)
        => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    public static string Collapse(string value)
        => string.Join(" ", value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    [GeneratedRegex(@"(?:topResumedActivity|mResumedActivity|ResumedActivity).*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex ForegroundPattern1();

    [GeneratedRegex(@"mCurrentFocus=Window\{[^}]*\s(?<package>[A-Za-z0-9_\.]+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex ForegroundPattern2();

    [GeneratedRegex(@"mFocusedApp=.*\s(?<package>[A-Za-z0-9_\.]+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex ForegroundPattern3();

    [GeneratedRegex(@"mTopFullscreenOpaqueOrDimmingWindowState.*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex ForegroundPattern4();

    [GeneratedRegex(@"mCurrentFocus=Window\{[^}]*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+){2,})\}", RegexOptions.Compiled)]
    private static partial Regex ForegroundPattern5();

    [GeneratedRegex(@"Window\s+#\d+\s+Window\{[^}]*\su\d+\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)\}:", RegexOptions.Compiled)]
    private static partial Regex ForegroundPattern6();

    [GeneratedRegex(@"topResumedActivity=ActivityRecord\{[^}]*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)\s+t\d+\}", RegexOptions.Compiled)]
    private static partial Regex VisibleActivityPattern();

    [GeneratedRegex(@"level:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BatteryLevelPattern();

    private static IReadOnlyList<Regex> ForegroundPatterns()
        => [ForegroundPattern1(), ForegroundPattern2(), ForegroundPattern3(), ForegroundPattern4(), ForegroundPattern5(), ForegroundPattern6()];

    private static ForegroundAppSnapshot? BuildSnapshot(string component, IReadOnlyList<string> visibleComponents)
    {
        var slashIndex = component.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= component.Length - 1)
        {
            return null;
        }

        var packageId = component[..slashIndex];
        var activityName = component[(slashIndex + 1)..];
        return new ForegroundAppSnapshot(packageId, activityName, component, visibleComponents);
    }

    private static string BuildComponent(string packageId, string activityName)
        => string.IsNullOrWhiteSpace(activityName)
            ? packageId
            : $"{packageId}/{activityName}";
}
