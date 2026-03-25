namespace ViscerealityCompanion.Core.Services;

public static class LslMonitorServiceFactory
{
    public static ILslMonitorService CreateDefault()
        => OperatingSystem.IsWindows()
            ? new WindowsLslMonitorService()
            : new PreviewLslMonitorService();
}
