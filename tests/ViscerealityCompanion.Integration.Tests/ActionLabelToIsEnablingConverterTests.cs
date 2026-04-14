using System.Globalization;
using System.Windows;
using ViscerealityCompanion.App;

namespace ViscerealityCompanion.Integration.Tests;

public sealed class ActionLabelToIsEnablingConverterTests
{
    private static readonly ActionLabelToIsEnablingConverter Converter = new();

    [Theory]
    [InlineData("Start Recording", true)]
    [InlineData("Stop Recording", false)]
    [InlineData("Launch Kiosk Runtime", true)]
    [InlineData("Wake Headset To Enable Launching", true)]
    [InlineData("Clear Guardian Blocker Before Launching", true)]
    [InlineData("Exit Kiosk Runtime", false)]
    [InlineData("Particles On", true)]
    [InlineData("Particles Off", false)]
    [InlineData("Use Automatic Driver", true)]
    [InlineData("Use Controller Volume Driver", false)]
    [InlineData("Start Recording...", true)]
    [InlineData("Pause Automatic...", false)]
    public void Convert_MapsActionLabelToRequestedDirection(string label, bool expected)
    {
        var result = Converter.Convert(label, typeof(bool), parameter: null!, CultureInfo.InvariantCulture);

        Assert.IsType<bool>(result);
        Assert.Equal(expected, (bool)result);
    }

    [Fact]
    public void Convert_ReturnsUnsetValue_ForUnknownLabels()
    {
        var result = Converter.Convert("Refresh Snapshot", typeof(bool), parameter: null!, CultureInfo.InvariantCulture);

        Assert.Same(DependencyProperty.UnsetValue, result);
    }
}
