using System.Windows.Threading;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using KeyGrid.Core.Services;
using KeyGrid.Interop;
using KeyGrid.Services;

namespace KeyGrid.UI;

/// <summary>
/// Manages creation, event routing, rendering synchronization,
/// and teardown for overlay windows across all monitors.
/// </summary>
public class OverlayManager
{
    private readonly IMonitorProvider _monitorProvider;
    private readonly SelectionManager _selectionManager;
    private readonly ClickExecutor _clickExecutor;
    private readonly List<OverlayWindow> _overlays = new();
    private readonly FocusLossPolicy _focusLossPolicy = new();
    private OverlayWindow? _primaryOverlay;
    private int _isClosing;
    private bool _isFocusLossPending = false;

    public event Action<string>? ErrorOccurred;

    public bool IsActive => _overlays.Count > 0;

    private bool IsClosing => Volatile.Read(ref _isClosing) != 0;

    public OverlayManager(
        IMonitorProvider monitorProvider,
        SelectionManager selectionManager,
        ClickExecutor clickExecutor)
    {
        _monitorProvider = monitorProvider;
        _selectionManager = selectionManager;
        _clickExecutor = clickExecutor;

        _monitorProvider.Changed += OnMonitorConfigurationChanged;
    }

    public void ShowOverlays()
    {
        CloseOverlays();

        var monitorRects = _monitorProvider.GetMonitorRects();
        if (monitorRects.Count == 0)
        {
            ErrorOccurred?.Invoke("No active displays found.");
            return;
        }

        if (!_selectionManager.StartSelection(monitorRects))
        {
            ErrorOccurred?.Invoke("Failed to initialize selection area.");
            return;
        }

        _focusLossPolicy.Reset();

        // Identify primary monitor: typically (0,0) or the first monitor
        var primaryRect = monitorRects.FirstOrDefault(r => r.Left == 0 && r.Top == 0);
        if (primaryRect.IsEmpty)
        {
            primaryRect = monitorRects[0];
        }

        foreach (var rect in monitorRects)
        {
            bool isPrimary = (rect == primaryRect);
            var overlay = new OverlayWindow(rect, isPrimary);

            if (isPrimary)
            {
                _primaryOverlay = overlay;
                overlay.KeyPressed += OnKeyPressed;
                overlay.EnterPressed += OnEnterPressed;
                overlay.BackspacePressed += OnBackspacePressed;
                overlay.CancelRequested += OnCancelRequested;
                overlay.FocusLost += OnFocusLost;
            }

            _overlays.Add(overlay);
            overlay.Show();
            overlay.UpdateRenderState(_selectionManager);
        }
    }

    private void OnKeyPressed(char key)
    {
        if (!_selectionManager.ProcessKey(key))
        {
            return;
        }

        _focusLossPolicy.Reset();

        // The 4th keystroke completes stage 2 and resolves the target.
        // Dispatch the click immediately instead of waiting for a confirmation screen.
        if (_selectionManager.State == SelectionState.ExecutingClick)
        {
            ExecuteConfirmedClick();
            return;
        }

        UpdateAllOverlays();
    }

    private void OnEnterPressed()
    {
        if (_selectionManager.ProcessEnter())
        {
            _focusLossPolicy.Reset();
            ExecuteConfirmedClick();
        }
    }

    private void OnBackspacePressed()
    {
        if (_selectionManager.ProcessBackspace())
        {
            _focusLossPolicy.Reset();
            UpdateAllOverlays();
        }
    }

    private void OnCancelRequested()
    {
        if (IsClosing) return;
        _selectionManager.Cancel();
        CloseOverlays();
    }

    private void OnFocusLost()
    {
        if (IsClosing || !IsActive || _isFocusLossPending)
        {
            return;
        }

        // Defer handling until the focus transition has completed. Reclaiming focus
        // synchronously inside WM_KILLFOCUS can be overridden by the incoming window.
        _isFocusLossPending = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(HandleFocusLoss));
    }

    private void HandleFocusLoss()
    {
        try
        {
            if (IsClosing || !IsActive)
            {
                return;
            }

            nint foreground = NativeMethods.GetForegroundWindow();
            uint foregroundPid = 0;
            if (foreground != nint.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(foreground, out foregroundPid);
            }
            bool foregroundIsVisible = foreground != nint.Zero && NativeMethods.IsWindowVisible(foreground);

            var action = _focusLossPolicy.Decide(
                foregroundPid,
                (uint)Environment.ProcessId,
                foregroundIsVisible);

            if (action == FocusLossAction.ReactivateOverlay
                && _primaryOverlay != null
                && _primaryOverlay.TryReactivate())
            {
                FileLogger.LogInfo(
                    $"Focus loss recovered (foreground PID {foregroundPid}, visible={foregroundIsVisible}).");
                return;
            }

            FileLogger.LogInfo(
                $"Focus loss cancelled selection (foreground PID {foregroundPid}, visible={foregroundIsVisible}).");
            _selectionManager.Cancel();
            CloseOverlays();
        }
        finally
        {
            // Keep the flag set while handling so focus transitions caused by
            // TryReactivate do not schedule duplicate handling.
            _isFocusLossPending = false;
        }
    }

    private void OnMonitorConfigurationChanged()
    {
        if (IsActive && !IsClosing)
        {
            // spec 5.3: Discard selection on monitor / DPI change
            _selectionManager.Cancel();
            CloseOverlays();
        }
    }

    private void UpdateAllOverlays()
    {
        foreach (var overlay in _overlays)
        {
            overlay.UpdateRenderState(_selectionManager);
        }
    }

    private void ExecuteConfirmedClick()
    {
        if (!_selectionManager.TargetCoordinate.HasValue)
        {
            CloseOverlays();
            _selectionManager.CompleteClick();
            return;
        }

        var target = _selectionManager.TargetCoordinate.Value;
        var virtualScreen = _selectionManager.VirtualScreenBoundingBox;

        bool success = _clickExecutor.ExecuteClick(
            target,
            virtualScreen,
            hideOverlaysAction: CloseOverlays,
            waitAfterHideAction: () => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render));

        _selectionManager.CompleteClick();

        if (!success)
        {
            ErrorOccurred?.Invoke($"Failed to dispatch click to ({target.X}, {target.Y}). The target window may require higher integrity level (UIPI).");
        }
    }

    public void CloseOverlays()
    {
        // Overlay teardown is normally UI-thread bound, but WM_DISPLAYCHANGE and
        // focus transitions can enter here re-entrantly. Interlocked guarantees that
        // only the first caller runs the teardown sequence.
        if (Interlocked.Exchange(ref _isClosing, 1) != 0)
        {
            return;
        }

        try
        {
            foreach (var overlay in _overlays)
            {
                try
                {
                    overlay.Close();
                }
                catch (InvalidOperationException)
                {
                    // Ignore if window was already closed or closing
                }
            }
            _overlays.Clear();
            _primaryOverlay = null;
        }
        finally
        {
            Volatile.Write(ref _isClosing, 0);
        }
    }
}
