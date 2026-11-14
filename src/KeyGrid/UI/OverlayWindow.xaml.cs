using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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

    private nint _originalHImc = nint.Zero;
    private bool _imeDisabled = false;

    public OverlayWindow(PixelRect monitorRect, bool isPrimary)
    {
        InitializeComponent();
        MonitorRect = monitorRect;
        IsPrimary = isPrimary;

        Focusable = isPrimary;
        IsHitTestVisible = true; // Absorbs mouse input to protect background apps
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

            Activate();
            Focus();
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

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_KILLFOCUS = 0x0008;

        if (IsPrimary && msg == WM_KILLFOCUS)
        {
            // Focus lost: spec.md 5.3 requires discarding selection
            CancelRequested?.Invoke();
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
