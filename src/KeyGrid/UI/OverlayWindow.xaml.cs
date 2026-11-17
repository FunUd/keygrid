using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using KeyGrid.Interop;
using KeyGrid.Services;

namespace KeyGrid.UI;

public partial class OverlayWindow : Window, IOverlayWindowSurface
{
    public PixelRect MonitorRect { get; }
    public bool IsPrimary { get; }

    public event Action<char>? KeyPressed;
    public event Action<int, int>? MoveRequested;
    public event Action<ClickKind>? ConfirmRequested;
    public event Action? BackspacePressed;
    public event Action? CancelRequested;
    public event Action? ContinuousModeToggleRequested;
    public event Action? FocusLost;
    public event Action? DisplayConfigurationChanged;

    private nint _originalHImc = nint.Zero;
    private bool _imeDisabled = false;
    private bool _isClosing = false;
    private bool _dpiNotificationsEnabled = false;
    private int _expectedDpi;
    private readonly OverlayKeyHandler _keyHandler;

    public OverlayWindow(PixelRect monitorRect, bool isPrimary, double effectiveDpiScale)
    {
        InitializeComponent();
        MonitorRect = monitorRect;
        IsPrimary = isPrimary;
        _expectedDpi = Math.Max(1, (int)Math.Round(effectiveDpiScale * 96.0));
        _keyHandler = new OverlayKeyHandler(isPrimary);
        _keyHandler.KeyPressed += key => KeyPressed?.Invoke(key);
        _keyHandler.MoveRequested += (columnDelta, rowDelta) => MoveRequested?.Invoke(columnDelta, rowDelta);
        _keyHandler.ConfirmRequested += clickKind => ConfirmRequested?.Invoke(clickKind);
        _keyHandler.BackspacePressed += () => BackspacePressed?.Invoke();
        _keyHandler.CancelRequested += () => CancelRequested?.Invoke();
        _keyHandler.ContinuousModeToggleRequested += () => ContinuousModeToggleRequested?.Invoke();

        Focusable = isPrimary;
        ShowActivated = isPrimary;
        IsHitTestVisible = true; // Absorbs mouse input to protect background apps
    }

    public nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>
    /// Reclaims foreground and keyboard focus for the primary overlay.
    /// </summary>
    public bool TryReactivate()
    {
        if (!IsPrimary || _isClosing || !IsVisible)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        nint foregroundBefore = NativeMethods.GetForegroundWindow();
        bool focused = NativeMethods.EnsureForegroundAndFocus(hwnd);
        bool wpfFocused = false;
        if (focused)
        {
            wpfFocused = Focus();
        }

        nint foregroundAfter = NativeMethods.GetForegroundWindow();
        nint focusedWindow = NativeMethods.GetFocus();
        FileLogger.LogInfo(
            $"Primary overlay focus request (hwnd=0x{hwnd.ToInt64():X}, visible={IsVisible}, active={IsActive}, " +
            $"foregroundBefore=0x{foregroundBefore.ToInt64():X}, foregroundAfter=0x{foregroundAfter.ToInt64():X}, " +
            $"focusedWindow=0x{focusedWindow.ToInt64():X}, nativeSuccess={focused}, wpfSuccess={wpfFocused}).");

        return focused;
    }

    public void UpdateRenderState(
        SelectionManager selectionManager,
        IReadOnlyList<PixelRect> monitorRects,
        bool isContinuousMode)
    {
        RenderElement.UpdateState(
            MonitorRect,
            monitorRects,
            selectionManager,
            IsPrimary,
            isContinuousMode);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        if (!IsPrimary)
        {
            exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        }
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        // Position window exactly in physical pixels. Secondary overlays must never
        // participate in activation; only the primary overlay owns keyboard input.
        uint showFlags = NativeMethods.SWP_SHOWWINDOW;
        if (!IsPrimary)
        {
            showFlags |= NativeMethods.SWP_NOACTIVATE;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            MonitorRect.Left,
            MonitorRect.Top,
            MonitorRect.Width,
            MonitorRect.Height,
            showFlags);

        if (IsPrimary)
        {
            // Disable IME according to spec.md Section 7.1
            _originalHImc = NativeMethods.ImmAssociateContext(hwnd, nint.Zero);
            _imeDisabled = true;

            TryReactivate();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        ModifierKeys modifiers = Keyboard.Modifiers;
        bool handled = _keyHandler.HandleKeyDown(e.Key, modifiers, e.IsRepeat);
        e.Handled = handled;

        if (IsPrimary)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            FileLogger.LogInfo(
                $"Primary overlay WPF keydown (category={GetKeyCategory(key)}, modifiers={modifiers}, " +
                $"repeat={e.IsRepeat}, handled={handled}).");
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        ModifierKeys modifiers = Keyboard.Modifiers;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool handled = _keyHandler.HandleKeyUp(key, modifiers);
        e.Handled = handled;

        if (IsPrimary && handled)
        {
            FileLogger.LogInfo(
                $"Primary overlay WPF keyup (category={GetKeyCategory(key)}, modifiers={modifiers}, handled={handled}).");
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        // Absorb all mouse clicks to prevent interacting with background apps
        e.Handled = true;
    }

    /// <summary>
    /// Aligns the expected DPI with the DPI WPF resolved for the overlay monitor.
    /// This also corrects the value when the monitor provider had to fall back to
    /// 96 DPI because GetDpiForMonitor failed.
    /// </summary>
    public void RefreshExpectedDpi()
    {
        int dpiX = (int)Math.Round(VisualTreeHelper.GetDpi(this).PixelsPerInchX);
        if (dpiX > 0)
        {
            _expectedDpi = dpiX;
        }
    }

    /// <summary>
    /// Starts reporting DPI changes after the initial per-monitor placement has
    /// settled. Moving a window to its target monitor during startup can produce a
    /// WM_DPICHANGED that must not cancel the new selection. Display-topology
    /// changes are always reported.
    /// </summary>
    public void EnableDpiChangeNotifications()
    {
        _dpiNotificationsEnabled = true;
    }

    public new void Close()
    {
        _isClosing = true;
        var hwnd = Handle;
        if (hwnd != nint.Zero)
        {
            // Hide without requesting activation so teardown does not promote a
            // different window before the click target is restored.
            NativeMethods.SetWindowPos(
                hwnd,
                nint.Zero,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_HIDEWINDOW
                | NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_NOMOVE
                | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER);
        }

        base.Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_ACTIVATE = 0x0006;
        const int WM_SETFOCUS = 0x0007;
        const int WM_KILLFOCUS = 0x0008;
        const int WM_ACTIVATEAPP = 0x001C;
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WA_INACTIVE = 0;

        if (!_isClosing)
        {
            if (msg == NativeMethods.WM_DISPLAYCHANGE)
            {
                // Message-only windows do not reliably receive display changes, so
                // every overlay reports them to the manager for cancellation.
                DisplayConfigurationChanged?.Invoke();
            }
            else if (msg == NativeMethods.WM_DPICHANGED && _dpiNotificationsEnabled)
            {
                int newDpi = (int)(wParam.ToInt64() & 0xFFFF);
                if (newDpi != _expectedDpi)
                {
                    // Moving a window to its target monitor during startup can send
                    // an expected WM_DPICHANGED. Only a different DPI means that the
                    // monitor configuration changed while the overlay was active.
                    DisplayConfigurationChanged?.Invoke();
                }
            }
        }

        if (IsPrimary && !_isClosing)
        {
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                uint virtualKey = unchecked((uint)wParam.ToInt64());
                FileLogger.LogInfo(
                    $"Primary overlay native keydown (category={GetVirtualKeyCategory(virtualKey)}).");
            }
            else if (msg == WM_SETFOCUS)
            {
                FileLogger.LogInfo(
                    $"Primary overlay received WM_SETFOCUS (previousFocus=0x{wParam.ToInt64():X}).");
            }
            else if (msg == WM_KILLFOCUS)
            {
                // Focus loss may be transient (for example an IME/TSF helper window).
                // OverlayManager decides whether to reclaim focus or cancel.
                FileLogger.LogInfo(
                    $"Primary overlay received WM_KILLFOCUS (newFocus=0x{wParam.ToInt64():X}).");
                FocusLost?.Invoke();
            }
            else if (msg == WM_ACTIVATEAPP && wParam == nint.Zero)
            {
                FileLogger.LogInfo(
                    $"Primary overlay received WM_ACTIVATEAPP (active=False, threadId={lParam}).");
                FocusLost?.Invoke();
            }
            else if (msg == WM_ACTIVATE)
            {
                int activation = (int)(wParam.ToInt64() & 0xFFFF);
                FileLogger.LogInfo(
                    $"Primary overlay received WM_ACTIVATE (state={activation}, otherWindow=0x{lParam.ToInt64():X}).");
                if (activation == WA_INACTIVE)
                {
                    // WM_KILLFOCUS is not guaranteed when the whole application is
                    // deactivated, so use the window-level activation transition too.
                    FocusLost?.Invoke();
                }
            }
            else if (msg == WM_ACTIVATEAPP)
            {
                FileLogger.LogInfo(
                    $"Primary overlay received WM_ACTIVATEAPP (active={wParam != nint.Zero}, threadId={lParam}).");
            }
        }

        return nint.Zero;
    }

    private static string GetKeyCategory(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return "Letter";
        }

        return key switch
        {
            Key.Enter => "Enter",
            Key.Left or Key.Right or Key.Up or Key.Down => "Arrow",
            Key.Back => "Backspace",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            _ => "Other"
        };
    }

    private static string GetVirtualKeyCategory(uint virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return "Letter";
        }

        return virtualKey switch
        {
            0x0D => "Enter",
            0x25 or 0x26 or 0x27 or 0x28 => "Arrow",
            0x08 => "Backspace",
            0x1B => "Escape",
            0x09 => "Tab",
            _ => "Other"
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        FileLogger.LogInfo($"Overlay window closed (primary={IsPrimary}).");
        base.OnClosed(e);

        if (IsPrimary && _imeDisabled)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != nint.Zero)
            {
                NativeMethods.ImmAssociateContext(hwnd, _originalHImc);
            }
            _imeDisabled = false;
        }
    }
}
