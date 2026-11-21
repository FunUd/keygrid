using System.Runtime.InteropServices;
using KeyGrid.Services;

namespace KeyGrid.Interop;

/// <summary>
/// Modifier state snapshot attached to a low-level keyboard event. The bits let
/// the consumer distinguish plain keys from system combinations without querying
/// keyboard state again.
/// </summary>
internal static class KeyboardHookModifiers
{
    public const uint Control = 0x1;
    public const uint Alt = 0x2;
    public const uint Shift = 0x4;
    public const uint Win = 0x8;
}

/// <summary>
/// A single key event observed by <see cref="ILowLevelKeyboardHook"/>.
/// </summary>
internal readonly struct KeyboardHookEvent
{
    public KeyboardHookEvent(uint virtualKey, bool isKeyUp, uint modifierMask)
    {
        VirtualKey = virtualKey;
        IsKeyUp = isKeyUp;
        ModifierMask = modifierMask;
    }

    public uint VirtualKey { get; }
    public bool IsKeyUp { get; }
    public uint ModifierMask { get; }
}

/// <summary>
/// Observes keyboard input independently of window focus. Used while the scroll
/// overlays are hidden so Scroll Mode keeps receiving keys after the target
/// window takes the foreground.
/// </summary>
internal interface ILowLevelKeyboardHook
{
    /// <summary>
    /// Raised for each non-injected key event. Return true to consume the event
    /// so it never reaches the foreground application.
    /// </summary>
    event Func<KeyboardHookEvent, bool>? KeyEvent;

    bool IsInstalled { get; }
    void Install();
    void Uninstall();
}

/// <summary>
/// WH_KEYBOARD_LL implementation. The callback runs on the installing thread's
/// message loop, which keeps it compatible with the WPF dispatcher without
/// marshaling. Must be installed and uninstalled on that same thread.
/// </summary>
internal sealed class Win32LowLevelKeyboardHook : ILowLevelKeyboardHook
{
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private nint _hookHandle;

    public event Func<KeyboardHookEvent, bool>? KeyEvent;

    public bool IsInstalled => _hookHandle != nint.Zero;

    public void Install()
    {
        if (IsInstalled)
        {
            return;
        }

        // LL hooks are not injected into other processes, but a global hook still
        // needs a module handle for the callback association.
        nint module = Marshal.GetHINSTANCE(typeof(Win32LowLevelKeyboardHook).Module);
        _hookProc = HookProc;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            module,
            dwThreadId: 0);
        if (_hookHandle == nint.Zero)
        {
            FileLogger.LogWarning(
                $"SetWindowsHookEx(WH_KEYBOARD_LL) failed (error={Marshal.GetLastWin32Error()}). " +
                "Scroll Mode will not receive keys while the overlays are hidden.");
            _hookProc = null;
        }
    }

    public void Uninstall()
    {
        if (_hookHandle == nint.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = nint.Zero;
        _hookProc = null;
    }

    private nint HookProc(int nCode, nuint wParam, nint lParam)
    {
        if (nCode >= 0 && lParam != nint.Zero)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            // Injected events come from SendInput and other automation; let them
            // through untouched so the hook cannot fight other input tools.
            if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                bool isKeyUp = wParam == (nuint)NativeMethods.WM_KEYUP_MSG
                    || wParam == (nuint)NativeMethods.WM_SYSKEYUP_MSG;
                bool isKeyDown = wParam == (nuint)NativeMethods.WM_KEYDOWN_MSG
                    || wParam == (nuint)NativeMethods.WM_SYSKEYDOWN_MSG;
                if (isKeyDown || isKeyUp)
                {
                    var keyEvent = new KeyboardHookEvent(data.vkCode, isKeyUp, ReadModifierMask());
                    bool consume = KeyEvent?.Invoke(keyEvent) == true;
                    if (consume)
                    {
                        return 1;
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static uint ReadModifierMask()
    {
        // GetAsyncKeyState reports the high bit while the key is held.
        uint mask = 0;
        if ((NativeMethods.GetAsyncKeyState((int)NativeMethods.VK_CONTROL) & 0x8000) != 0)
        {
            mask |= KeyboardHookModifiers.Control;
        }
        if ((NativeMethods.GetAsyncKeyState((int)NativeMethods.VK_MENU) & 0x8000) != 0)
        {
            mask |= KeyboardHookModifiers.Alt;
        }
        if ((NativeMethods.GetAsyncKeyState((int)NativeMethods.VK_SHIFT) & 0x8000) != 0)
        {
            mask |= KeyboardHookModifiers.Shift;
        }
        if ((NativeMethods.GetAsyncKeyState((int)NativeMethods.VK_LWIN) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState((int)NativeMethods.VK_RWIN) & 0x8000) != 0)
        {
            mask |= KeyboardHookModifiers.Win;
        }

        return mask;
    }
}
