using PdfSharp.Fonts;

namespace ViscerealityCompanion.Core.Services;

internal static class PdfReportBootstrap
{
    private static readonly Lazy<bool> Initialization = new(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureInitialized()
        => _ = Initialization.Value;

    private static bool Initialize()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }

        return true;
    }
}
