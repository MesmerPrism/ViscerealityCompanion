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
        => $"am start -n {Quote(component)}";

    public static string BuildMonkeyLaunchCommand(string packageId)
        => $"monkey -p {Quote(packageId)} -c android.intent.category.LAUNCHER 1";

    public static string BuildTaskLockCommand(int taskId)
        => $"am task lock {taskId}";

    public static string BuildTaskLockStopCommand()
        => "am task lock stop";

    public static string BuildHomeLaunchCommand(string homeComponent)
        => $"am start -W -a android.intent.action.MAIN -c android.intent.category.HOME -n {Quote(homeComponent)}";

    public static string BuildBroadcastActionCommand(string action)
        => $"am broadcast -a {Quote(action)}";

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
        var primaryComponent = ParsePrimaryForegroundComponent(output);
        var visibleComponents = ParseVisibleActivityComponents(output);
        if (!string.IsNullOrWhiteSpace(primaryComponent))
        {
            return BuildSnapshot(
                primaryComponent,
                visibleComponents.Count > 0
                    ? visibleComponents
                    : [primaryComponent]);
        }

        if (visibleComponents.Count > 0)
        {
            return BuildSnapshot(visibleComponents[0], visibleComponents);
        }

        return null;
    }

    private static string? ParsePrimaryForegroundComponent(string output)
    {
        foreach (var pattern in PrimaryForegroundPatterns())
        {
            var match = pattern.Match(output);
            if (match.Success)
            {
                var packageId = match.Groups["package"].Value;
                var activityName = match.Groups["activity"].Value;
                return BuildComponent(packageId, activityName);
            }
        }

        return string.Empty;
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

    public static int? ParseRecentTaskId(string output, string packageId)
    {
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        var currentTask = default(RecentTaskCandidate?);

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var headerMatch = RecentTaskHeaderPattern().Match(line);
            if (headerMatch.Success)
            {
                if (TryResolveTaskId(currentTask, packageId, out var resolvedTaskId))
                {
                    return resolvedTaskId;
                }

                currentTask = new RecentTaskCandidate(
                    HeaderTaskId: int.TryParse(headerMatch.Groups["id"].Value, out var headerTaskId) ? headerTaskId : null,
                    AffinityPackageId: NormalizeRecentTaskPackage(headerMatch.Groups["package"].Value),
                    ExplicitTaskId: null,
                    ActivityComponent: null);
                continue;
            }

            if (currentTask is null)
            {
                continue;
            }

            var explicitTaskIdMatch = RecentTaskIdPattern().Match(line);
            if (explicitTaskIdMatch.Success &&
                int.TryParse(explicitTaskIdMatch.Groups["id"].Value, out var explicitTaskId))
            {
                currentTask = currentTask with { ExplicitTaskId = explicitTaskId };
                continue;
            }

            var componentMatch = RecentTaskComponentPattern().Match(line);
            if (componentMatch.Success)
            {
                currentTask = currentTask with { ActivityComponent = componentMatch.Groups["component"].Value };
            }
        }

        if (TryResolveTaskId(currentTask, packageId, out var trailingTaskId))
        {
            return trailingTaskId;
        }

        return null;
    }

    public static string Quote(string value)
        => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    public static string Collapse(string value)
        => string.Join(" ", value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    [GeneratedRegex(@"(?:ResumedActivity:|Resumed:\s+ActivityRecord\{[^}]*\s)(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern1();

    [GeneratedRegex(@"mCurrentFocus=Window\{[^}]*\s(?<package>[A-Za-z0-9_\.]+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern2();

    [GeneratedRegex(@"mFocusedApp=.*\s(?<package>[A-Za-z0-9_\.]+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern3();

    [GeneratedRegex(@"mFocusedWindow=Window\{[^}]*\s(?<package>[A-Za-z0-9_\.]+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern4();

    [GeneratedRegex(@"mTopFullscreenOpaqueOrDimmingWindowState.*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern5();

    [GeneratedRegex(@"mTopFullscreenOpaqueWindowState.*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern6();

    [GeneratedRegex(@"mCurrentFocus=Window\{[^}]*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+){2,})\}", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern7();

    [GeneratedRegex(@"mResumedActivity:?\s*ActivityRecord\{[^}]*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern8();

    [GeneratedRegex(@"topResumedActivity=ActivityRecord\{[^}]*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)\s+t\d+\}", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern9();

    [GeneratedRegex(@"Window\s+#\d+\s+Window\{[^}]*\su\d+\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)\}:", RegexOptions.Compiled)]
    private static partial Regex PrimaryForegroundPattern10();

    [GeneratedRegex(@"topResumedActivity=ActivityRecord\{[^}]*\s(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/(?<activity>[A-Za-z0-9_\.$]+)\s+t\d+\}", RegexOptions.Compiled)]
    private static partial Regex VisibleActivityPattern();

    [GeneratedRegex(@"level:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BatteryLevelPattern();

    [GeneratedRegex(@"^\*\s+Recent\s+#\d+:\s+Task\{[^}]*\s#(?<id>\d+)\s+type=[^\r\n]*\sA=\d+:(?<package>[^\s\}]+)", RegexOptions.Compiled)]
    private static partial Regex RecentTaskHeaderPattern();

    [GeneratedRegex(@"\btaskId=(?<id>\d+)\b", RegexOptions.Compiled)]
    private static partial Regex RecentTaskIdPattern();

    [GeneratedRegex(@"\bmActivityComponent=(?<component>[^\s]+)", RegexOptions.Compiled)]
    private static partial Regex RecentTaskComponentPattern();

    private static IReadOnlyList<Regex> PrimaryForegroundPatterns()
        => [
            PrimaryForegroundPattern1(),
            PrimaryForegroundPattern2(),
            PrimaryForegroundPattern3(),
            PrimaryForegroundPattern4(),
            PrimaryForegroundPattern5(),
            PrimaryForegroundPattern6(),
            PrimaryForegroundPattern8(),
            PrimaryForegroundPattern9(),
            PrimaryForegroundPattern10(),
            PrimaryForegroundPattern7()
        ];

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

    private static bool TryResolveTaskId(RecentTaskCandidate? candidate, string packageId, out int taskId)
    {
        if (candidate is null)
        {
            taskId = default;
            return false;
        }

        var matchesAffinity = string.Equals(candidate.AffinityPackageId, packageId, StringComparison.OrdinalIgnoreCase);
        var matchesComponent =
            !string.IsNullOrWhiteSpace(candidate.ActivityComponent) &&
            candidate.ActivityComponent.StartsWith($"{packageId}/", StringComparison.OrdinalIgnoreCase);

        if (!matchesAffinity && !matchesComponent)
        {
            taskId = default;
            return false;
        }

        if (candidate.ExplicitTaskId.HasValue)
        {
            taskId = candidate.ExplicitTaskId.Value;
            return true;
        }

        if (candidate.HeaderTaskId.HasValue)
        {
            taskId = candidate.HeaderTaskId.Value;
            return true;
        }

        taskId = default;
        return false;
    }

    private static string NormalizeRecentTaskPackage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var suffixIndex = value.IndexOf(':');
        return suffixIndex >= 0 ? value[..suffixIndex] : value;
    }

    private sealed record RecentTaskCandidate(
        int? HeaderTaskId,
        string AffinityPackageId,
        int? ExplicitTaskId,
        string? ActivityComponent);
}
