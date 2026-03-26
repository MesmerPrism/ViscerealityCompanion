using ViscerealityCompanion.Core.Models;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class QuestShellOverlayClassifierTests
{
    [Fact]
    public void TryClassify_SystemuxAnytimeUi_IsNotOverlay()
    {
        var result = QuestShellOverlayClassifier.TryClassify(
            "com.oculus.systemux",
            "com.oculus.systemux/com.oculus.panelapp.anytimeui.AnytimeUIActivity");

        Assert.NotNull(result);
        Assert.Equal("Meta system UI layer active", result!.Label);
        Assert.False(result.IsOverlay);
        Assert.Equal(OperationOutcomeKind.Preview, result.Level);
    }

    [Fact]
    public void TryClassify_SystemuxWithoutKnownOverlay_IsNotPopup()
    {
        var result = QuestShellOverlayClassifier.TryClassify(
            "com.oculus.systemux",
            "com.oculus.systemux/com.oculus.systemux.SomeBackgroundActivity");

        Assert.NotNull(result);
        Assert.Equal("Meta system UI layer active", result!.Label);
        Assert.False(result.IsOverlay);
        Assert.Equal(OperationOutcomeKind.Preview, result.Level);
    }

    [Fact]
    public void TryClassify_SystemuxPanelOverlay_IsOverlay()
    {
        var result = QuestShellOverlayClassifier.TryClassify(
            "com.oculus.systemux",
            "com.oculus.systemux/com.oculus.panelapp.notifications.NotificationsActivity");

        Assert.NotNull(result);
        Assert.Equal("Meta popup open", result!.Label);
        Assert.True(result.IsOverlay);
        Assert.Equal(OperationOutcomeKind.Warning, result.Level);
    }

    [Fact]
    public void TryClassify_VrShellControlBar_IsOverlay()
    {
        var result = QuestShellOverlayClassifier.TryClassify(
            "com.oculus.vrshell",
            "com.oculus.vrshell/com.oculus.panelapp.controlbar.ControlBarActivity");

        Assert.NotNull(result);
        Assert.Equal("Meta control bar open", result!.Label);
        Assert.True(result.IsOverlay);
    }

    [Fact]
    public void TryClassify_VrShellHome_IsNotOverlay()
    {
        var result = QuestShellOverlayClassifier.TryClassify(
            "com.oculus.vrshell",
            "com.oculus.vrshell/com.oculus.vrshell.HomeActivity");

        Assert.NotNull(result);
        Assert.Equal("Quest Home active", result!.Label);
        Assert.False(result.IsOverlay);
    }
}
