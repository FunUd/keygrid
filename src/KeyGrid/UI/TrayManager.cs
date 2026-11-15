using System.Runtime.InteropServices;
using KeyGrid.Interop;
using KeyGrid.Services;

namespace KeyGrid.UI;

/// <summary>
/// Manages the system tray icon, context menu, and balloon notifications according to spec.md Section 9.
/// </summary>
public class TrayManager : IDisposable
{
    private const uint TrayIconId = 1001;
    private const int ExitMenuCommand = 1;
    private readonly nint _hWnd;
    private bool _iconCreated = false;
    private bool _hotkeyFailed = false;
    private bool _menuOpen = false;

    public event Action? ExitRequested;

    public TrayManager(nint hWnd)
    {
        _hWnd = hWnd;
        InitializeTrayIcon();
    }

    public void SetHotkeyStatus(bool registered)
    {
        _hotkeyFailed = !registered;
        UpdateTooltip(registered ? "KeyGrid (Ctrl+Shift+Space)" : "KeyGrid (ホットキー登録失敗)");
    }

    private void InitializeTrayIcon()
    {
        var nid = new NativeMethods.NOTIFYICONDATA();
        nid.cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>();
        nid.hWnd = _hWnd;
        nid.uID = TrayIconId;
        nid.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        nid.uCallbackMessage = NativeMethods.WM_TRAYICON;
        nid.hIcon = NativeMethods.LoadIcon(nint.Zero, NativeMethods.IDI_APPLICATION);
        nid.szTip = "KeyGrid";

        _iconCreated = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref nid);
    }

    public void UpdateTooltip(string tooltip)
    {
        if (!_iconCreated) return;

        var nid = new NativeMethods.NOTIFYICONDATA();
        nid.cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>();
        nid.hWnd = _hWnd;
        nid.uID = TrayIconId;
        nid.uFlags = NativeMethods.NIF_TIP;
        nid.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref nid);
    }

    public void ShowBalloon(string title, string message, bool isError = false)
    {
        if (!_iconCreated) return;

        var nid = new NativeMethods.NOTIFYICONDATA();
        nid.cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>();
        nid.hWnd = _hWnd;
        nid.uID = TrayIconId;
        nid.uFlags = NativeMethods.NIF_INFO;
        nid.szInfoTitle = title.Length > 63 ? title[..63] : title;
        nid.szInfo = message.Length > 255 ? message[..255] : message;
        nid.dwInfoFlags = isError ? NativeMethods.NIIF_ERROR : NativeMethods.NIIF_INFO;

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref nid);
    }

    public void HandleTrayMessage(nint lParam)
    {
        // The notification message is carried in LOWORD(lParam). Masking makes the
        // extraction explicit and independent of pointer width/sign extension.
        int msg = (int)((long)lParam & 0xFFFF);
        if (msg == NativeMethods.WM_RBUTTONUP)
        {
            ShowContextMenu();
        }
    }

    private void ShowContextMenu()
    {
        if (_menuOpen)
        {
            return;
        }

        // A native TrackPopupMenuEx menu is used instead of a WPF ContextMenu.
        // The message window that owns the tray icon is not an activating UI window,
        // so WPF's popup did not reliably receive the outside click that should
        // dismiss the menu, leaving it on screen until Exit was selected.
        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == nint.Zero)
        {
            FileLogger.LogError("Failed to create the tray context menu.");
            return;
        }

        _menuOpen = true;
        try
        {
            bool populated =
                NativeMethods.AppendMenu(
                    menu,
                    NativeMethods.MF_STRING | NativeMethods.MF_GRAYED,
                    nint.Zero,
                    "KeyGrid v1.2") &&
                NativeMethods.AppendMenu(
                    menu,
                    NativeMethods.MF_STRING | NativeMethods.MF_GRAYED,
                    nint.Zero,
                    _hotkeyFailed ? "状態: ホットキー登録失敗" : "起動: Ctrl + Shift + Space") &&
                NativeMethods.AppendMenu(
                    menu,
                    NativeMethods.MF_SEPARATOR,
                    nint.Zero,
                    null) &&
                NativeMethods.AppendMenu(
                    menu,
                    NativeMethods.MF_STRING,
                    ExitMenuCommand,
                    "終了 (&Exit)");

            if (!populated)
            {
                FileLogger.LogError("Failed to populate the tray context menu.");
                return;
            }

            NativeMethods.GetCursorPos(out var pt);

            // TrackPopupMenuEx only dismisses on an outside click when its owner
            // is the foreground window. Remember the previous foreground window
            // so focus can be handed back when the menu closes without a click.
            nint previousForeground = NativeMethods.GetForegroundWindow();
            bool foregroundSet = NativeMethods.SetForegroundWindow(_hWnd);
            if (!foregroundSet)
            {
                // A tray notification does not always grant the usual foreground
                // permission. Attaching to the foreground thread lets the hidden
                // owner become foreground so the native menu can dismiss normally.
                foregroundSet = NativeMethods.EnsureForegroundAndFocus(_hWnd);
            }
            if (!foregroundSet)
            {
                FileLogger.LogWarning("Failed to bring the tray menu owner to the foreground.");
            }

            int command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                pt.X,
                pt.Y,
                _hWnd,
                nint.Zero);

            // Flush the menu's dismissal message so the popup cannot linger.
            NativeMethods.PostMessage(_hWnd, NativeMethods.WM_NULL, nint.Zero, nint.Zero);

            // Restore the previous foreground only if the hidden menu owner still
            // owns the foreground. If the menu was dismissed by clicking another
            // window, that window must keep focus.
            nint currentForeground = NativeMethods.GetForegroundWindow();
            if ((currentForeground == _hWnd || currentForeground == nint.Zero) &&
                previousForeground != nint.Zero &&
                previousForeground != _hWnd)
            {
                bool restored = NativeMethods.SetForegroundWindow(previousForeground);
                if (!restored)
                {
                    restored = NativeMethods.EnsureForegroundAndFocus(previousForeground);
                }
                if (!restored)
                {
                    FileLogger.LogWarning("Failed to restore the foreground window after closing the tray menu.");
                }
            }

            if (command == ExitMenuCommand)
            {
                ExitRequested?.Invoke();
            }
        }
        finally
        {
            _menuOpen = false;
            if (!NativeMethods.DestroyMenu(menu))
            {
                FileLogger.LogWarning("Failed to destroy the tray context menu.");
            }
        }
    }

    public void Dispose()
    {
        if (_iconCreated)
        {
            var nid = new NativeMethods.NOTIFYICONDATA();
            nid.cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>();
            nid.hWnd = _hWnd;
            nid.uID = TrayIconId;

            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref nid);
            _iconCreated = false;
        }
    }
}
