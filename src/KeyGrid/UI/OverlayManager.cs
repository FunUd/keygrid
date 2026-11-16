using System.Windows.Threading;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Calculation;
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
    private readonly Func<PixelRect, bool, double, IOverlayWindowSurface> _overlayFactory;
    private readonly IWindowFocusService _windowFocusService;
    private readonly List<IOverlayWindowSurface> _overlays = new();
    private readonly FocusLossPolicy _focusLossPolicy = new();
    private IReadOnlyList<PixelRect> _activeMonitorRects = Array.Empty<PixelRect>();
    private IOverlayWindowSurface? _primaryOverlay;
    private nint _originatingForegroundWindow;
    private int _isClosing;
    private int? _focusLossPendingGeneration;
    private bool _isDisplayChangePending = false;
    private int _selectionGeneration;
    private int _confirmationAttempt;

    public event Action<string>? ErrorOccurred;

    public bool IsActive => _overlays.Count > 0;

    private bool IsClosing => Volatile.Read(ref _isClosing) != 0;

    public OverlayManager(
        IMonitorProvider monitorProvider,
        SelectionManager selectionManager,
        ClickExecutor clickExecutor)
        : this(
            monitorProvider,
            selectionManager,
            clickExecutor,
            (monitorRect, isPrimary, dpiScale) => new OverlayWindow(monitorRect, isPrimary, dpiScale),
            new Win32WindowFocusService())
    {
    }

    internal OverlayManager(
        IMonitorProvider monitorProvider,
        SelectionManager selectionManager,
        ClickExecutor clickExecutor,
        Func<PixelRect, bool, double, IOverlayWindowSurface> overlayFactory,
        IWindowFocusService windowFocusService)
    {
        _monitorProvider = monitorProvider;
        _selectionManager = selectionManager;
        _clickExecutor = clickExecutor;
        _overlayFactory = overlayFactory;
        _windowFocusService = windowFocusService;

        _monitorProvider.Changed += OnMonitorConfigurationChanged;
    }

    public void ShowOverlays()
    {
        if (!IsActive)
        {
            _originatingForegroundWindow = _windowFocusService.GetForegroundWindow();
            FileLogger.LogInfo(
                $"Selection opening: originating foreground HWND={FormatWindowHandle(_originatingForegroundWindow)}.");
        }

        int generation = ++_selectionGeneration;
        _focusLossPendingGeneration = null;
        _isDisplayChangePending = false;

        CloseOverlaysWithoutRestore();
        _selectionManager.Cancel();

        var monitors = _monitorProvider.GetMonitors()
            .Where(m => !m.Bounds.IsEmpty)
            .ToList();
        if (monitors.Count == 0)
        {
            ErrorOccurred?.Invoke("No active displays found.");
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
            return;
        }

        int primaryIndex = monitors.FindIndex(m => m.IsPrimary);
        if (primaryIndex < 0)
        {
            primaryIndex = 0;
            FileLogger.LogWarning("No monitor reported as primary; using the first enumerated monitor.");
        }

        var monitorRects = monitors.Select(m => m.Bounds).ToList();
        _activeMonitorRects = monitorRects;
        if (!_selectionManager.StartSelection(monitorRects))
        {
            ErrorOccurred?.Invoke("Failed to initialize selection area.");
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
            return;
        }

        FileLogger.LogInfo(
            $"Selection started on {monitors.Count} monitor(s) with " +
            $"Stage 1 grid {_selectionManager.ColumnCount}x{_selectionManager.RowCount} and " +
            $"Stage 2 grid {SelectionManager.Stage2ColumnCount}x{SelectionManager.Stage2RowCount}.");

        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            bool isPrimary = i == primaryIndex;
            var overlay = _overlayFactory(monitor.Bounds, isPrimary, monitor.DpiScale);
            overlay.DisplayConfigurationChanged += () => OnOverlayDisplayConfigurationChanged(generation);

            if (isPrimary)
            {
                _primaryOverlay = overlay;
                overlay.KeyPressed += OnKeyPressed;
                overlay.MoveRequested += OnMoveRequested;
                overlay.ConfirmRequested += OnConfirmRequested;
                overlay.BackspacePressed += OnBackspacePressed;
                overlay.CancelRequested += OnCancelRequested;
                overlay.FocusLost += () => OnFocusLost(generation);
            }

            _overlays.Add(overlay);
            overlay.Show();
            overlay.UpdateRenderState(_selectionManager, _activeMonitorRects);
        }

        if (_primaryOverlay == null || !_primaryOverlay.TryReactivate())
        {
            FileLogger.LogWarning("Could not focus the primary overlay after showing all overlays.");
            _selectionManager.Cancel();
            CloseOverlaysWithoutRestore();
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
            ErrorOccurred?.Invoke("Failed to focus the selection overlay.");
            return;
        }

        // Defer enabling DPI notifications until the overlays have completed their
        // initial per-monitor placement. The expected-DPI check in the overlay also
        // filters normal startup WM_DPICHANGED messages.
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (generation != _selectionGeneration || IsClosing || !IsActive)
                {
                    return;
                }

                foreach (var overlay in _overlays)
                {
                    overlay.RefreshExpectedDpi();
                    overlay.EnableDpiChangeNotifications();
                }
            }));
    }

    private void OnKeyPressed(char key)
    {
        if (!_selectionManager.ProcessKey(key))
        {
            return;
        }

        UpdateAllOverlays();
    }

    private void OnMoveRequested(int columnDelta, int rowDelta)
    {
        if (_selectionManager.MoveStage2Cursor(columnDelta, rowDelta))
        {
            UpdateAllOverlays();
        }
    }

    private void OnConfirmRequested(ClickKind clickKind)
    {
        int attempt = ++_confirmationAttempt;
        FileLogger.LogInfo(
            $"Confirmation #{attempt} received (kind={clickKind}, state={_selectionManager.State}, " +
            $"historyLength={_selectionManager.History.Count}).");

        if (!_selectionManager.ProcessEnter())
        {
            FileLogger.LogInfo($"Confirmation #{attempt} ignored because the selection is not confirmable.");
            return;
        }

        FileLogger.LogInfo(
            $"Confirmation #{attempt} accepted (target={_selectionManager.TargetCoordinate}).");
        ExecuteConfirmedClick(clickKind, attempt);
    }

    private void OnBackspacePressed()
    {
        if (_selectionManager.ProcessBackspace())
        {
            UpdateAllOverlays();
        }
    }

    private void OnCancelRequested()
    {
        if (IsClosing) return;
        _selectionManager.Cancel();
        CloseOverlays();
    }

    private void OnFocusLost(int generation)
    {
        if (generation != _selectionGeneration
            || IsClosing
            || !IsActive
            || _focusLossPendingGeneration == generation)
        {
            return;
        }

        // Defer handling until the focus transition has completed. Reclaiming focus
        // synchronously inside WM_KILLFOCUS can be overridden by the incoming window.
        _focusLossPendingGeneration = generation;
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => HandleFocusLoss(generation)));
    }

    private void HandleFocusLoss(int generation)
    {
        try
        {
            if (generation != _selectionGeneration || IsClosing || !IsActive)
            {
                return;
            }

            nint foreground = _windowFocusService.GetForegroundWindow();
            uint foregroundPid = _windowFocusService.GetWindowProcessId(foreground);
            bool foregroundIsVisible = foreground != nint.Zero && _windowFocusService.IsWindowVisible(foreground);

            var action = _focusLossPolicy.Decide(
                foregroundPid,
                (uint)Environment.ProcessId,
                foregroundIsVisible);

            FileLogger.LogInfo(
                $"Focus loss evaluated (foreground={FormatWindowHandle(foreground)}, " +
                $"pid={foregroundPid}, visible={foregroundIsVisible}, action={action}).");

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
            if (action == FocusLossAction.CancelSelection)
            {
                CloseOverlaysWithoutRestore();
                _originatingForegroundWindow = nint.Zero;
                FileLogger.LogInfo("Kept the externally activated foreground window active.");
            }
            else
            {
                CloseOverlays();
            }
        }
        finally
        {
            // Keep the flag set while handling so focus transitions caused by
            // TryReactivate do not schedule duplicate handling.
            if (_focusLossPendingGeneration == generation)
            {
                _focusLossPendingGeneration = null;
            }
        }
    }

    private void OnOverlayDisplayConfigurationChanged(int generation)
    {
        if (generation != _selectionGeneration || IsClosing || !IsActive || _isDisplayChangePending)
        {
            return;
        }

        // Do not close the overlay window from inside its own window procedure.
        // Defer teardown until the DPI/display message has been fully handled.
        _isDisplayChangePending = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() => HandleOverlayDisplayConfigurationChanged(generation)));
    }

    private void HandleOverlayDisplayConfigurationChanged(int generation)
    {
        try
        {
            if (generation != _selectionGeneration || IsClosing || !IsActive)
            {
                return;
            }

            FileLogger.LogInfo("Overlay window received a display or DPI change; cancelling selection.");
            OnMonitorConfigurationChanged();
        }
        finally
        {
            if (generation == _selectionGeneration)
            {
                _isDisplayChangePending = false;
            }
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
            overlay.UpdateRenderState(_selectionManager, _activeMonitorRects);
        }
    }

    private void ExecuteConfirmedClick(ClickKind clickKind, int attempt)
    {
        if (!_selectionManager.TargetCoordinate.HasValue)
        {
            FileLogger.LogWarning($"Confirmation #{attempt} was accepted without a target coordinate.");
            CloseOverlays();
            _selectionManager.CompleteClick();
            return;
        }

        var target = _selectionManager.TargetCoordinate.Value;
        var virtualScreen = _selectionManager.VirtualScreenBoundingBox;

        bool success;
        bool foregroundRestored = false;
        FileLogger.LogInfo(
            $"Click attempt #{attempt} started (kind={clickKind}, target=({target.X}, {target.Y}), " +
            $"virtualScreen={virtualScreen}).");
        try
        {
            success = _clickExecutor.ExecuteClick(
                target,
                virtualScreen,
                clickKind,
                hideOverlaysAction: () =>
                {
                    FileLogger.LogInfo($"Click attempt #{attempt}: closing overlays.");
                    CloseOverlaysWithoutRestore();
                },
                prepareClickAction: () =>
                {
                    FileLogger.LogInfo($"Click attempt #{attempt}: waiting for overlay render.");
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    FileLogger.LogInfo($"Click attempt #{attempt}: overlay render wait completed.");
                    FileLogger.LogInfo(
                        $"Click attempt #{attempt}: restoring originating foreground window before SendInput.");
                    foregroundRestored = RestoreOriginatingForegroundWindow();
                    return foregroundRestored;
                });
            FileLogger.LogInfo(
                $"Click attempt #{attempt}: click execution returned success={success} " +
                $"(foregroundRestored={foregroundRestored}).");
        }
        catch
        {
            FileLogger.LogWarning(
                $"Click attempt #{attempt} threw before completing; restoring originating foreground window.");
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
            throw;
        }

        if (success)
        {
            _originatingForegroundWindow = nint.Zero;
            nint foreground = _windowFocusService.GetForegroundWindow();
            FileLogger.LogInfo(
                $"Click attempt #{attempt}: keeping post-click foreground window " +
                $"{FormatWindowHandle(foreground)}.");
        }
        else
        {
            FileLogger.LogInfo(
                $"Click attempt #{attempt}: restoring originating foreground window after unsuccessful click execution.");
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
        }

        _selectionManager.CompleteClick();
        FileLogger.LogInfo($"Click attempt #{attempt} completed; selection returned to standby.");

        if (!foregroundRestored)
        {
            ErrorOccurred?.Invoke("Could not restore the window that was active before selection. The click was not sent.");
        }
        else if (!success)
        {
            ErrorOccurred?.Invoke($"Failed to dispatch click to ({target.X}, {target.Y}). The target window may require higher integrity level (UIPI).");
        }
    }

    public void CloseOverlays()
    {
        CloseOverlaysWithoutRestore();
        RestoreOriginatingForegroundWindow();
        _originatingForegroundWindow = nint.Zero;
    }

    private void CloseOverlaysWithoutRestore()
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
            int overlayCount = _overlays.Count;
            foreach (var overlay in _overlays)
            {
                bool isPrimary = ReferenceEquals(overlay, _primaryOverlay);
                FileLogger.LogInfo($"Closing {(isPrimary ? "primary" : "secondary")} overlay.");
                try
                {
                    overlay.Close();
                    FileLogger.LogInfo(
                        $"Close returned for {(isPrimary ? "primary" : "secondary")} overlay.");
                }
                catch (InvalidOperationException ex)
                {
                    FileLogger.LogWarning(
                        $"Could not close {(isPrimary ? "primary" : "secondary")} overlay: {ex.Message}");
                }
            }
            _overlays.Clear();
            _primaryOverlay = null;
            FileLogger.LogInfo($"Overlay teardown finished (requested={overlayCount}, active={IsActive}).");
        }
        finally
        {
            Volatile.Write(ref _isClosing, 0);
        }
    }

    private bool RestoreOriginatingForegroundWindow()
    {
        nint target = _originatingForegroundWindow;
        if (target == nint.Zero)
        {
            FileLogger.LogWarning("No originating foreground window is available to restore.");
            return false;
        }

        nint foregroundBeforeRestore = _windowFocusService.GetForegroundWindow();
        bool restored = foregroundBeforeRestore == target
            || _windowFocusService.TryRestoreForegroundWindow(target);
        nint foregroundAfterRestore = _windowFocusService.GetForegroundWindow();
        bool succeeded = restored && foregroundAfterRestore == target;
        FileLogger.LogInfo(
            $"Foreground restore result={succeeded} (target={FormatWindowHandle(target)}, " +
            $"before={FormatWindowHandle(foregroundBeforeRestore)}, after={FormatWindowHandle(foregroundAfterRestore)}).");

        if (succeeded)
        {
            FileLogger.LogInfo("The foreground window that was active before selection is active.");
        }
        else
        {
            FileLogger.LogWarning(
                "Could not restore the foreground window that was active before selection; it may no longer be available.");
        }

        return succeeded;
    }

    private static string FormatWindowHandle(nint window) => $"0x{window.ToInt64():X}";
}
