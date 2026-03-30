using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ViscerealityCompanion.App;

internal static class WindowThemeHelper
{
    private const int DwmUseImmersiveDarkModeAttribute = 20;
    private const int DwmBorderColorAttribute = 34;
    private const int DwmCaptionColorAttribute = 35;
    private const int DwmTextColorAttribute = 36;

    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += OnWindowSourceInitialized;
    }

    private static void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        TryApply(window);
    }

    private static void TryApply(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source)
        {
            return;
        }

        var handle = source.Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeAttribute, ref enabled, sizeof(int));

            SetColor(handle, DwmCaptionColorAttribute, Color.FromRgb(1, 4, 8));
            SetColor(handle, DwmBorderColorAttribute, Color.FromRgb(1, 4, 8));
            SetColor(handle, DwmTextColorAttribute, Colors.White);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static void SetColor(IntPtr handle, int attribute, Color color)
    {
        var colorRef = color.R | (color.G << 8) | (color.B << 16);
        _ = DwmSetWindowAttribute(handle, attribute, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
