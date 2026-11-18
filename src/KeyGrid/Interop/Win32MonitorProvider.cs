using System.Runtime.InteropServices;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Geometry;
using KeyGrid.Services;

namespace KeyGrid.Interop;

/// <summary>
/// Provides monitor bounds and effective DPI using Win32 APIs in physical pixels.
/// </summary>
public class Win32MonitorProvider : IMonitorProvider
{
    private const double DefaultDpiScale = 1.0;

    public event Action? Changed;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.MonitorEnumProc callback = (nint hMonitor, nint hdc, ref NativeMethods.RECT lprc, nint data) =>
        {
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>();
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                int left = mi.rcMonitor.Left;
                int top = mi.rcMonitor.Top;
                int width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                if (width > 0 && height > 0)
                {
                    bool isPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                    double dpiScale = GetDpiScale(hMonitor, left, top);
                    monitors.Add(new MonitorInfo(new PixelRect(left, top, width, height), dpiScale, isPrimary));
                }
            }
            return true;
        };

        NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);

        // Fallback to Primary screen metrics if EnumDisplayMonitors returns none.
        // Per-monitor DPI is unavailable in this path, so use the documented 96 DPI fallback.
        if (monitors.Count == 0)
        {
            int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            if (w > 0 && h > 0)
            {
                FileLogger.LogWarning(
                    "EnumDisplayMonitors returned no monitors; using virtual-screen metrics at 96 DPI.");
                monitors.Add(new MonitorInfo(new PixelRect(x, y, w, h), DefaultDpiScale, true));
            }
        }

        return monitors;
    }

    private static double GetDpiScale(nint hMonitor, int left, int top)
    {
        int hResult = NativeMethods.GetDpiForMonitor(
            hMonitor,
            NativeMethods.MDT_EFFECTIVE_DPI,
            out uint dpiX,
            out _);

        if (hResult == 0 && dpiX > 0)
        {
            return dpiX / 96.0;
        }

        FileLogger.LogWarning("GetDpiForMonitor failed for a monitor; using 96 DPI.");
        FileLogger.LogDebug(
            $"GetDpiForMonitor failed for monitor at ({left}, {top}) with HRESULT 0x{hResult:X8}.");
        return DefaultDpiScale;
    }

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
