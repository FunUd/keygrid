using System.Runtime.InteropServices;
using System.Windows.Controls;
using KeyGrid.Interop;

namespace KeyGrid.UI;

/// <summary>
/// Manages the system tray icon, context menu, and balloon notifications according to spec.md Section 9.
/// </summary>
public class TrayManager : IDisposable
{
    private const uint TrayIconId = 1001;
    private readonly nint _hWnd;
    private bool _iconCreated = false;
    private bool _hotkeyFailed = false;

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
        int msg = (int)lParam;
        if (msg == NativeMethods.WM_RBUTTONUP)
        {
            ShowContextMenu();
        }
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenu();

        var headerItem = new MenuItem
        {
            Header = "KeyGrid v1.2",
            IsEnabled = false
        };
        menu.Items.Add(headerItem);

        var statusItem = new MenuItem
        {
            Header = _hotkeyFailed ? "状態: ホットキー登録失敗" : "起動: Ctrl + Shift + Space",
            IsEnabled = false
        };
        menu.Items.Add(statusItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem
        {
            Header = "終了 (&Exit)"
        };
        exitItem.Click += (s, e) => ExitRequested?.Invoke();
        menu.Items.Add(exitItem);

        NativeMethods.GetCursorPos(out var pt);
        menu.IsOpen = true;
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
