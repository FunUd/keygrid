using KeyGrid.Interop;

namespace KeyGrid.UI;

internal interface IWindowFocusService
{
    nint GetForegroundWindow();
    uint GetWindowProcessId(nint window);
    bool IsWindowVisible(nint window);
    bool TryRestoreForegroundWindow(nint window);
}

internal sealed class Win32WindowFocusService : IWindowFocusService
{
    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public uint GetWindowProcessId(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        return processId;
    }

    public bool IsWindowVisible(nint window) => NativeMethods.IsWindowVisible(window);

    public bool TryRestoreForegroundWindow(nint window) =>
        NativeMethods.TryRestoreForegroundWindow(window);
}
