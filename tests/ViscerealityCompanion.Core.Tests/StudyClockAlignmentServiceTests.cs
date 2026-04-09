using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.Core.Tests;

public sealed class StudyClockAlignmentServiceTests
{
    [Fact]
    public void BuildProbeScheduleOffsets_UsesExactReservedProbeCountWithoutEndOfWindowExtras()
    {
        var offsets = WindowsStudyClockAlignmentService.BuildProbeScheduleOffsets(
            TimeSpan.FromSeconds(2.5),
            TimeSpan.FromMilliseconds(250));

        Assert.Equal(10, offsets.Count);
        Assert.Equal(TimeSpan.Zero, offsets[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(2250), offsets[^1]);
    }

    [Fact]
    public void BuildProbeScheduleOffsets_MatchesFullBurstWindowCount()
    {
        var offsets = WindowsStudyClockAlignmentService.BuildProbeScheduleOffsets(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(250));

        Assert.Equal(40, offsets.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(9750), offsets[^1]);
    }
}
