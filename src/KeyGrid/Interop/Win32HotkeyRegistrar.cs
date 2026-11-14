using KeyGrid.Core.Abstractions;

namespace KeyGrid.Interop;

/// <summary>
/// Registers and unregisters system-wide hotkeys via Win32 API.
/// </summary>
public class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    public bool Register(nint hWnd, int id, uint modifiers, uint vk)
    {
        return NativeMethods.RegisterHotKey(hWnd, id, modifiers, vk);
    }

    public bool Unregister(nint hWnd, int id)
    {
        return NativeMethods.UnregisterHotKey(hWnd, id);
    }
}
