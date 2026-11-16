using KeyGrid.Core.Geometry;
using KeyGrid.Interop;

namespace KeyGrid.UI;

internal interface IWindowFocusService
{
    nint GetForegroundWindow();
    nint GetFocusedWindow();
    uint GetWindowProcessId(nint window);
    bool IsWindowVisible(nint window);
    bool TryRestoreForegroundWindow(nint window);
    nint GetWindowFromPoint(PixelPoint point);
    nint GetRootWindow(nint window);
    bool IsWindowOwnedBy(nint window, nint rootWindow);
    bool TryLockForeground();
    void UnlockForeground();
}

internal sealed class Win32WindowFocusService : IWindowFocusService
{
    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public nint GetFocusedWindow() => NativeMethods.GetFocus();

    public uint GetWindowProcessId(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        return processId;
    }

    public bool IsWindowVisible(nint window) => NativeMethods.IsWindowVisible(window);

    public bool TryRestoreForegroundWindow(nint window) =>
        NativeMethods.TryRestoreForegroundWindow(window);

    public nint GetWindowFromPoint(PixelPoint point) =>
        NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y });

    public nint GetRootWindow(nint window)
    {
        if (window == nint.Zero)
        {
            return nint.Zero;
        }

        nint root = NativeMethods.GetAncestor(window, NativeMethods.GA_ROOT);
        return root != nint.Zero ? root : window;
    }

    public bool IsWindowOwnedBy(nint window, nint rootWindow)
    {
        if (window == nint.Zero || rootWindow == nint.Zero)
        {
            return false;
        }

        return window == rootWindow || GetRootWindow(window) == rootWindow;
    }

    public bool TryLockForeground() =>
        NativeMethods.LockSetForegroundWindow(NativeMethods.LSFW_LOCK);

    public void UnlockForeground() =>
        NativeMethods.LockSetForegroundWindow(NativeMethods.LSFW_UNLOCK);
}
