using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.Core.Services;

public sealed record QuestShellOverlayClassification(
    string Label,
    string Detail,
    OperationOutcomeKind Level,
    bool IsOverlay);

public static class QuestShellOverlayClassifier
{
    public static QuestShellOverlayClassification? TryClassify(string? packageId, string? component)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        var activityName = ParseActivityName(component);

        if (string.Equals(packageId, "com.oculus.systemux", StringComparison.OrdinalIgnoreCase))
        {
            if (activityName.Contains(".panelapp.", StringComparison.OrdinalIgnoreCase) &&
                !activityName.Contains("AnytimeUIActivity", StringComparison.OrdinalIgnoreCase))
            {
                return new QuestShellOverlayClassification(
                    "Meta popup open",
                    "A Meta system overlay is visible on the headset.",
                    OperationOutcomeKind.Warning,
                    true);
            }

            return new QuestShellOverlayClassification(
                "Meta system UI layer active",
                "Quest system UI is active, but the reported component is not treated as a popup on its own.",
                OperationOutcomeKind.Preview,
                false);
        }

        if (string.Equals(packageId, "com.oculus.vrshell", StringComparison.OrdinalIgnoreCase))
        {
            if (activityName.Contains("FocusPlaceholderActivity", StringComparison.OrdinalIgnoreCase))
            {
                return new QuestShellOverlayClassification(
                    "Meta shell placeholder active",
                    "Quest shell focus moved into FocusPlaceholderActivity. This usually means a Meta menu or control-bar transition intercepted the runtime, and if it persists while the study app keeps publishing quest_twin_state the menu is likely blocked or not visibly surfaced.",
                    OperationOutcomeKind.Warning,
                    true);
            }

            if (activityName.Contains("ControlBarActivity", StringComparison.OrdinalIgnoreCase))
            {
                return new QuestShellOverlayClassification(
                    "Meta control bar open",
                    "The Quest control bar is visible over the current scene.",
                    OperationOutcomeKind.Warning,
                    true);
            }

            if (activityName.Contains("ToastsActivity", StringComparison.OrdinalIgnoreCase) ||
                activityName.Contains(".panelapp.", StringComparison.OrdinalIgnoreCase))
            {
                return new QuestShellOverlayClassification(
                    "Meta shell panel open",
                    "A Meta shell panel is visible over the current scene.",
                    OperationOutcomeKind.Warning,
                    true);
            }

            return new QuestShellOverlayClassification(
                "Quest Home active",
                "The headset is on the Meta home shell rather than in a Viscereality APK.",
                OperationOutcomeKind.Warning,
                false);
        }

        if (string.Equals(packageId, "com.oculus.browser", StringComparison.OrdinalIgnoreCase))
        {
            return new QuestShellOverlayClassification(
                "Meta Browser active",
                "The Meta browser is the current foreground app.",
                OperationOutcomeKind.Preview,
                false);
        }

        return null;
    }

    private static string ParseActivityName(string? component)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            return string.Empty;
        }

        var slashIndex = component.IndexOf('/');
        return slashIndex >= 0 && slashIndex < component.Length - 1
            ? component[(slashIndex + 1)..]
            : string.Empty;
    }
}
