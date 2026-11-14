using System.Runtime.InteropServices;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Geometry;

namespace KeyGrid.Interop;

/// <summary>
/// Provides monitor bounds using Win32 EnumDisplayMonitors API in physical pixels.
/// </summary>
public class Win32MonitorProvider : IMonitorProvider
{
    public event Action? Changed;

    public IReadOnlyList<PixelRect> GetMonitorRects()
    {
        var rects = new List<PixelRect>();

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
                    rects.Add(new PixelRect(left, top, width, height));
                }
            }
            return true;
        };

        NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);

        // Fallback to Primary screen metrics if EnumDisplayMonitors returns none
        if (rects.Count == 0)
        {
            int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            if (w > 0 && h > 0)
            {
                rects.Add(new PixelRect(x, y, w, h));
            }
        }

        return rects;
    }

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
