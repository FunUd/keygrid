using System.Windows.Threading;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using KeyGrid.Core.Services;
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

        LockPrimaryForeground();

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

                // The foreground can move away immediately after SetForegroundWindow
                // without a reliable WM_KILLFOCUS. Re-check once the initial activation
                // has settled so the grid cannot stay visible without keyboard focus.
                if (!IsPrimaryForegroundAndFocused())
                {
                    FileLogger.LogWarning(
                        "Primary overlay is not foreground and focused after startup; re-evaluating focus.");
                    OnFocusLost(generation);
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
            || _selectionManager.State == SelectionState.ExecutingClick
            || _focusLossPendingGeneration == generation)
        {
            return;
        }

        // Defer handling until the focus transition has completed. WM_KILLFOCUS and
        // WM_ACTIVATE can arrive before the new foreground window is visible, so
        // handling them at Input priority can observe a stale foreground value.
        _focusLossPendingGeneration = generation;
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => HandleFocusLoss(generation)));
    }

    private void HandleFocusLoss(int generation)
    {
        try
        {
            if (generation != _selectionGeneration
                || IsClosing
                || !IsActive
                || _selectionManager.State == SelectionState.ExecutingClick)
            {
                return;
            }

            var primaryOverlay = _primaryOverlay;
            if (primaryOverlay == null)
            {
                return;
            }

            nint primaryHandle = primaryOverlay.Handle;
            nint foreground = _windowFocusService.GetForegroundWindow();
            nint focused = _windowFocusService.GetFocusedWindow();
            bool primaryIsForeground = primaryHandle != nint.Zero && foreground == primaryHandle;
            bool primaryIsFocused = primaryHandle != nint.Zero && focused == primaryHandle;

            if (primaryIsForeground)
            {
                if (primaryIsFocused)
                {
                    FileLogger.LogInfo(
                        "Focus loss ignored because the primary overlay is foreground and focused.");
                    LockPrimaryForeground();
                    return;
                }

                // The process still owns the foreground window, but keyboard focus
                // belongs to another window (for example an IME helper). Reclaim it.
                if (primaryOverlay.TryReactivate())
                {
                    LockPrimaryForeground();
                    FileLogger.LogInfo("Focus loss recovered by restoring keyboard focus to the primary overlay.");
                    return;
                }
            }

            uint foregroundPid = _windowFocusService.GetWindowProcessId(foreground);
            bool foregroundIsVisible = foreground != nint.Zero && _windowFocusService.IsWindowVisible(foreground);

            var action = _focusLossPolicy.Decide(
                foregroundPid,
                (uint)Environment.ProcessId,
                foregroundIsVisible);

            FileLogger.LogInfo(
                $"Focus loss evaluated (foreground={FormatWindowHandle(foreground)}, " +
                $"focused={FormatWindowHandle(focused)}, pid={foregroundPid}, " +
                $"visible={foregroundIsVisible}, action={action}).");

            if (action == FocusLossAction.ReactivateOverlay
                && !primaryIsForeground
                && primaryOverlay.TryReactivate())
            {
                LockPrimaryForeground();
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

            // A focus transition can occur while activation is being repaired and
            // be suppressed by the pending flag. Re-check after clearing it so the
            // overlay cannot keep input while another window owns focus.
            if (generation == _selectionGeneration
                && !IsClosing
                && IsActive
                && _selectionManager.State != SelectionState.ExecutingClick
                && !IsPrimaryForegroundAndFocused())
            {
                FileLogger.LogWarning(
                    "Focus changed while handling focus loss; re-evaluating.");
                OnFocusLost(generation);
            }
        }
    }

    private bool IsPrimaryForegroundAndFocused()
    {
        var primaryOverlay = _primaryOverlay;
        if (primaryOverlay == null)
        {
            return false;
        }

        nint primaryHandle = primaryOverlay.Handle;
        return primaryHandle != nint.Zero
            && _windowFocusService.GetForegroundWindow() == primaryHandle
            && _windowFocusService.GetFocusedWindow() == primaryHandle;
    }

    private void LockPrimaryForeground()
    {
        // LSFW_LOCK only has an effect while this process owns the foreground
        // window. Never lock an unrelated window that took focus during a race.
        if (IsPrimaryForegroundAndFocused())
        {
            _windowFocusService.TryLockForeground();
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
        bool clickTargetReady = false;
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

                    // Resolve the click target after the overlays are hidden. Hiding
                    // does not activate another window, so WindowFromPoint now
                    // reports the same window a physical click would hit.
                    nint windowAtTarget = _windowFocusService.GetWindowFromPoint(target);
                    nint clickTargetWindow = _windowFocusService.GetRootWindow(windowAtTarget);
                    FileLogger.LogInfo(
                        $"Click attempt #{attempt}: window under target is " +
                        $"{FormatWindowHandle(windowAtTarget)} " +
                        $"(root={FormatWindowHandle(clickTargetWindow)}).");
                    if (clickTargetWindow == nint.Zero)
                    {
                        FileLogger.LogWarning($"Click attempt #{attempt}: no window is under the click coordinate.");
                        clickTargetReady = false;
                        return false;
                    }

                    // Activate the target rather than the pre-selection window.
                    // Restoring the pre-selection window can raise it over the
                    // selected coordinate and make it receive the click instead.
                    bool activated = RestoreForegroundWindow(clickTargetWindow, "Click target");
                    nint windowAfterRestore = _windowFocusService.GetWindowFromPoint(target);
                    bool stillHitTarget = _windowFocusService.IsWindowOwnedBy(windowAfterRestore, clickTargetWindow);
                    clickTargetReady = stillHitTarget;
                    FileLogger.LogInfo(
                        $"Click attempt #{attempt}: target activation result={activated}, " +
                        $"window at target after activation is {FormatWindowHandle(windowAfterRestore)} " +
                        $"(matchesClickTarget={stillHitTarget}).");
                    return stillHitTarget;
                });
            FileLogger.LogInfo(
                $"Click attempt #{attempt}: click execution returned success={success} " +
                $"(clickTargetReady={clickTargetReady}).");
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

        if (!clickTargetReady)
        {
            ErrorOccurred?.Invoke(
                "Could not confirm the window under the selected coordinate. The click was not sent.");
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
            _windowFocusService.UnlockForeground();
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

        bool succeeded = RestoreForegroundWindow(target, "Originating");
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

    private bool RestoreForegroundWindow(nint target, string description)
    {
        if (target == nint.Zero)
        {
            FileLogger.LogWarning($"{description} foreground window is not available.");
            return false;
        }

        nint foregroundBeforeRestore = _windowFocusService.GetForegroundWindow();
        bool restored = foregroundBeforeRestore == target
            || _windowFocusService.TryRestoreForegroundWindow(target);
        nint foregroundAfterRestore = _windowFocusService.GetForegroundWindow();
        bool succeeded = restored && foregroundAfterRestore == target;
        FileLogger.LogInfo(
            $"{description} foreground restore result={succeeded} " +
            $"(target={FormatWindowHandle(target)}, " +
            $"before={FormatWindowHandle(foregroundBeforeRestore)}, " +
            $"after={FormatWindowHandle(foregroundAfterRestore)}).");
        return succeeded;
    }

    private static string FormatWindowHandle(nint window) => $"0x{window.ToInt64():X}";
}
