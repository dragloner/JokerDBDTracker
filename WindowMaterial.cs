using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JokerDBDTracker;

internal static class WindowMaterial
{
    private const int ImmersiveDarkModeAttribute = 20;
    private const int CornerPreferenceAttribute = 33;
    private const int SystemBackdropAttribute = 38;
    private const int RoundedCorners = 2;
    private const int MicaBackdrop = 2;
    public static void Attach(Window window) => window.SourceInitialized += (_, _) =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(window).Handle;
        var dark = 1;
        var corners = RoundedCorners;
        var backdrop = MicaBackdrop;
        _ = DwmSetWindowAttribute(handle, ImmersiveDarkModeAttribute, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(handle, CornerPreferenceAttribute, ref corners, sizeof(int));
        _ = DwmSetWindowAttribute(handle, SystemBackdropAttribute, ref backdrop, sizeof(int));
    };
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
