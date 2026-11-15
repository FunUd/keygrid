using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using KeyGrid.Interop;

namespace KeyGrid.UI;

public partial class OverlayWindow : Window
{
    public PixelRect MonitorRect { get; }
    public bool IsPrimary { get; }

    public event Action<char>? KeyPressed;
    public event Action? EnterPressed;
    public event Action? BackspacePressed;
    public event Action? CancelRequested;
    public event Action? FocusLost;
    public event Action? DisplayConfigurationChanged;

    private nint _originalHImc = nint.Zero;
    private bool _imeDisabled = false;
    private bool _isClosing = false;
    private bool _dpiNotificationsEnabled = false;
    private int _expectedDpi;

    public OverlayWindow(PixelRect monitorRect, bool isPrimary, double effectiveDpiScale)
    {
        InitializeComponent();
        MonitorRect = monitorRect;
        IsPrimary = isPrimary;
        _expectedDpi = Math.Max(1, (int)Math.Round(effectiveDpiScale * 96.0));

        Focusable = isPrimary;
        IsHitTestVisible = true; // Absorbs mouse input to protect background apps
    }

    /// <summary>
    /// Reclaims foreground and keyboard focus for the primary overlay.
    /// </summary>
    public bool TryReactivate()
    {
        if (!IsPrimary || _isClosing)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        bool focused = NativeMethods.EnsureForegroundAndFocus(hwnd);
        if (focused)
        {
            Focus();
        }

        return focused;
    }

    public void UpdateRenderState(SelectionManager selectionManager)
    {
        RenderElement.UpdateState(MonitorRect, selectionManager);
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

        // Position window exactly in physical pixels
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            MonitorRect.Left,
            MonitorRect.Top,
            MonitorRect.Width,
            MonitorRect.Height,
            NativeMethods.SWP_SHOWWINDOW);

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

        if (!IsPrimary)
        {
            return;
        }

        // Ignore key repeat according to spec.md Section 7.1
        if (e.IsRepeat)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            CancelRequested?.Invoke();
            return;
        }

        if (e.Key == Key.Back)
        {
            BackspacePressed?.Invoke();
            return;
        }

        if (e.Key == Key.Enter)
        {
            EnterPressed?.Invoke();
            return;
        }

        if (e.Key >= Key.A && e.Key <= Key.Z)
        {
            char keyChar = (char)('A' + (e.Key - Key.A));
            KeyPressed?.Invoke(keyChar);
            return;
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
        base.Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_KILLFOCUS = 0x0008;

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

        if (IsPrimary && msg == WM_KILLFOCUS && !_isClosing)
        {
            // Focus loss may be transient (for example an IME/TSF helper window).
            // OverlayManager decides whether to reclaim focus or cancel.
            FocusLost?.Invoke();
        }

        return nint.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
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
