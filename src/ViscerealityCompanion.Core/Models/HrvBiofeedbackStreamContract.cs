namespace ViscerealityCompanion.Core.Models;

public static class HrvBiofeedbackStreamContract
{
    public const string StreamName = "HRV_Biofeedback";
    public const string StreamType = "HRV";
    public const string ChannelLabel = "smoothed_fb";
    public const string ChannelUnit = "normalized";
    public const int DefaultChannelIndex = 0;
    public const int FeedbackDispatchDelayMs = 250;
}
