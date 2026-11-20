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
    // SendInput queues the click asynchronously. Keep the overlays hidden briefly
    // so a re-shown topmost overlay cannot receive the button-up event.
    private const int ContinuousModeClickSettleMilliseconds = 50;

    // PageUp/PageDown scroll by five wheel notches; arrow keys scroll by one.
    private const int ScrollNotchesPerPage = 5;

    /// <summary>
    /// Phase of the composite range-then-drag workflow started by the R+D chord.
    /// The range phase keeps the captured anchor while the user selects the range
    /// endpoint; the drag phase reuses that endpoint as the drag anchor.
    /// </summary>
    private enum RangeDragPhase
    {
        None,
        Range,
        Drag
    }

    private readonly IMonitorProvider _monitorProvider;
    private readonly SelectionManager _selectionManager;
    private readonly ClickExecutor _clickExecutor;
    private readonly Func<PixelRect, bool, double, IOverlayWindowSurface> _overlayFactory;
    private readonly IWindowFocusService _windowFocusService;
    private readonly List<IOverlayWindowSurface> _overlays = new();
    private readonly FocusLossPolicy _focusLossPolicy = new();
    private IReadOnlyList<PixelRect> _activeMonitorRects = Array.Empty<PixelRect>();
    private PixelRect _virtualScreenBounds = PixelRect.Empty;
    private IOverlayWindowSurface? _primaryOverlay;
    private nint _originatingForegroundWindow;
    private int _isClosing;
    private int? _focusLossPendingGeneration;
    private bool _isDisplayChangePending = false;
    private bool _continuousMode;
    private PixelPoint? _rangeStartPoint;
    private PixelPoint? _dragStartPoint;
    private bool _scrollMode;
    private RangeDragPhase _rangeDragPhase = RangeDragPhase.None;
    private bool _isInputInProgress;
    private int _selectionGeneration;
    private int _confirmationAttempt;

    public event Action<string>? ErrorOccurred;

    public bool IsActive => _overlays.Count > 0;

    /// <summary>
    /// Whether a successful confirmation keeps the selection session alive.
    /// The flag belongs to the current overlay session and is reset on exit.
    /// </summary>
    internal bool IsContinuousMode => _continuousMode;

    /// <summary>
    /// Whether a two-point range anchor has been set for the current session.
    /// </summary>
    internal bool HasRangeSelection => _rangeStartPoint.HasValue;

    /// <summary>
    /// Whether a two-point drag anchor has been set for the current session.
    /// </summary>
    internal bool HasDragSelection => _dragStartPoint.HasValue;

    /// <summary>
    /// Whether Scroll Mode is active for the current Stage 2 selection.
    /// </summary>
    internal bool IsScrollMode => _scrollMode;

    /// <summary>
    /// Whether the composite range-then-drag workflow is active, either while
    /// selecting the range endpoint or the drag drop target.
    /// </summary>
    internal bool IsRangeDragWorkflow => _rangeDragPhase != RangeDragPhase.None;

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
        // A hotkey start or explicit restart always begins in normal mode.
        _continuousMode = false;
        _rangeDragPhase = RangeDragPhase.None;
        ShowOverlaysCore();
    }

    private void ShowOverlaysCore()
    {
        // Every new Stage 1 session starts without sub-mode state. Continuous mode
        // itself is preserved by the caller when the session is intentionally kept open.
        // The workflow's drag anchor is the exception: it is installed before the
        // reopen so the new Stage 1 session renders it immediately.
        if (_rangeDragPhase == RangeDragPhase.None)
        {
            _rangeStartPoint = null;
            _dragStartPoint = null;
        }

        _scrollMode = false;
        _isInputInProgress = false;

        if (!IsActive)
        {
            _originatingForegroundWindow = _windowFocusService.GetForegroundWindow();
            FileLogger.LogDebug(
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
            _continuousMode = false;
            ErrorOccurred?.Invoke("No active displays found.");
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
            return;
        }

        // Calculate virtual desktop bounds from all monitors for SendInput normalization
        _virtualScreenBounds = PixelRect.BoundingBox(monitors.Select(m => m.Bounds));

        MonitorInfo dpiReferenceMonitor;
        var fromWindow = _originatingForegroundWindow != nint.Zero
            ? _monitorProvider.GetMonitorForWindow(_originatingForegroundWindow)
            : null;
        if (fromWindow.HasValue)
        {
            dpiReferenceMonitor = fromWindow.Value;
        }
        else
        {
            int primaryIdx = monitors.FindIndex(m => m.IsPrimary);
            dpiReferenceMonitor = primaryIdx >= 0 ? monitors[primaryIdx] : monitors[0];
        }
        double dpiReferenceScale = dpiReferenceMonitor.DpiScale;

        var monitorRects = monitors.Select(m => m.Bounds).ToList();
        _activeMonitorRects = monitorRects;
        if (!_selectionManager.StartSelection(monitorRects, dpiReferenceScale))
        {
            _continuousMode = false;
            ErrorOccurred?.Invoke("Failed to initialize selection area.");
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

        int targetPx = (int)Math.Round(GridCalculator.Stage1CellTargetDip * dpiReferenceScale);
        int cellWidthPx = (_selectionManager.CurrentBoundingBox.Width + _selectionManager.ColumnCount - 1) / _selectionManager.ColumnCount;
        int cellHeightPx = (_selectionManager.CurrentBoundingBox.Height + _selectionManager.RowCount - 1) / _selectionManager.RowCount;
        int maxDev = Math.Max(Math.Abs(cellWidthPx - targetPx), Math.Abs(cellHeightPx - targetPx));
        if (targetPx > 0 && (double)maxDev / targetPx > 0.25)
        {
            FileLogger.LogWarning(
                $"Stage 1 cell deviation ({(double)maxDev / targetPx:P1}) exceeds 25% of target ({targetPx} px). Coarse grid selected.");
        }

        FileLogger.LogInfo(
            $"Selection started across {monitors.Count} monitor(s) with virtual desktop {_virtualScreenBounds}, " +
            $"DPI reference monitor at {dpiReferenceMonitor.Bounds} ({dpiReferenceScale:P0}), " +
            $"Stage 1 grid {_selectionManager.ColumnCount}x{_selectionManager.RowCount} " +
            $"(cell ~{cellWidthPx / dpiReferenceScale:F0}x{cellHeightPx / dpiReferenceScale:F0} DIP), " +
            $"Stage 2 window {SelectionManager.Stage2WindowDip} DIP, continuousMode={_continuousMode}.");

        // Create an overlay for each monitor; wire input only on the primary overlay
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
                overlay.ContinuousModeToggleRequested += OnContinuousModeToggleRequested;
                overlay.RangeStartRequested += OnRangeStartRequested;
                overlay.DragStartRequested += OnDragStartRequested;
                overlay.RangeDragWorkflowRequested += OnRangeDragWorkflowRequested;
                overlay.PageScrollRequested += OnPageScrollRequested;
                overlay.FocusLost += () => OnFocusLost(generation);
            }

            _overlays.Add(overlay);
            overlay.Show();
            overlay.UpdateRenderState(
                _selectionManager,
                _activeMonitorRects,
                _continuousMode,
                _rangeStartPoint,
                _scrollMode,
                IsRangeDragWorkflow,
                _dragStartPoint);
        }

        if (_primaryOverlay == null || !_primaryOverlay.TryReactivate())
        {
            FileLogger.LogWarning("Could not focus the primary overlay after showing all overlays.");
            _continuousMode = false;
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
                // A synthetic input operation intentionally hides the overlays and
                // temporarily moves focus, so skip the check while it is in progress.
                if (!_isInputInProgress && !IsPrimaryForegroundAndFocused())
                {
                    FileLogger.LogWarning(
                        "Primary overlay is not foreground and focused after startup; re-evaluating focus.");
                    OnFocusLost(generation);
                }
            }));
    }

    private void OnKeyPressed(char key)
    {
        if (_scrollMode)
        {
            FileLogger.LogDebug("Key input ignored while Scroll Mode is active.");
            return;
        }

        if (!_selectionManager.ProcessKey(key))
        {
            return;
        }

        UpdateAllOverlays();
    }

    private void OnRangeStartRequested()
    {
        if (IsClosing
            || !IsActive
            || _isInputInProgress
            || _scrollMode
            || _selectionManager.State != SelectionState.Selecting)
        {
            return;
        }

        if (_selectionManager.CurrentStage == 2)
        {
            // R is unused in Stage 2. Set the anchor once; Esc clears it. Repeating
            // R must not silently move an anchor that the user may already be relying on.
            if (_rangeStartPoint.HasValue
                || _dragStartPoint.HasValue
                || !_selectionManager.TryGetStage2Target(out var startPoint))
            {
                return;
            }

            _rangeStartPoint = startPoint;
            ReturnToStage1();
            FileLogger.LogDebug($"Range start set at ({startPoint.X}, {startPoint.Y}).");
            UpdateAllOverlays();
            return;
        }

        // In Stage 1, R remains an ordinary column key. This path preserves that
        // behavior for unmodified R while allowing the range action in Stage 2.
        if (_selectionManager.ProcessKey('R'))
        {
            UpdateAllOverlays();
        }
    }

    private void OnDragStartRequested()
    {
        if (IsClosing
            || !IsActive
            || _isInputInProgress
            || _scrollMode
            || _selectionManager.State != SelectionState.Selecting)
        {
            return;
        }

        if (_selectionManager.CurrentStage == 2)
        {
            if (_dragStartPoint.HasValue
                || _rangeStartPoint.HasValue
                || !_selectionManager.TryGetStage2Target(out var startPoint))
            {
                return;
            }

            _dragStartPoint = startPoint;
            ReturnToStage1();
            FileLogger.LogDebug($"Drag start set at ({startPoint.X}, {startPoint.Y}).");
            UpdateAllOverlays();
            return;
        }

        if (_selectionManager.ProcessKey('D'))
        {
            UpdateAllOverlays();
        }
    }

    private void OnRangeDragWorkflowRequested()
    {
        if (IsClosing
            || !IsActive
            || _isInputInProgress
            || _scrollMode
            || _rangeDragPhase != RangeDragPhase.None
            || _rangeStartPoint.HasValue
            || _dragStartPoint.HasValue
            || _selectionManager.State != SelectionState.Selecting
            || _selectionManager.CurrentStage != 2
            || !_selectionManager.TryGetStage2Target(out var startPoint))
        {
            return;
        }

        // The chord captures the current Stage 2 target as the range anchor and
        // returns to the full Stage 1 grid. The range endpoint becomes the drag
        // anchor once the range selection succeeds.
        _rangeStartPoint = startPoint;
        _rangeDragPhase = RangeDragPhase.Range;
        ReturnToStage1();
        FileLogger.LogDebug($"Range-drag workflow started; range anchor at ({startPoint.X}, {startPoint.Y}).");
        UpdateAllOverlays();
    }

    private void ReturnToStage1()
    {
        while (_selectionManager.History.Count > 0)
        {
            if (!_selectionManager.ProcessBackspace())
            {
                throw new InvalidOperationException("The selection could not return to Stage 1.");
            }
        }
    }

    private void OnMoveRequested(int columnDelta, int rowDelta)
    {
        if (_scrollMode)
        {
            if (columnDelta != 0)
            {
                ScrollCurrentTarget(columnDelta, horizontal: true);
            }
            else if (rowDelta != 0)
            {
                // Windows wheel input is positive upward; arrow row deltas are positive downward.
                ScrollCurrentTarget(-rowDelta, horizontal: false);
            }

            return;
        }

        if (_selectionManager.MoveStage2Cursor(columnDelta, rowDelta))
        {
            UpdateAllOverlays();
        }
    }

    private void OnPageScrollRequested(int direction)
    {
        if (!CanScrollCurrentTarget())
        {
            return;
        }

        if (!_scrollMode)
        {
            _scrollMode = true;
            FileLogger.LogInfo("Scroll Mode entered from Page key.");
            UpdateAllOverlays();
        }

        // PageDown (+1) scrolls down, PageUp (-1) scrolls up.
        ScrollCurrentTarget(
            direction > 0 ? -ScrollNotchesPerPage : ScrollNotchesPerPage,
            horizontal: false);
    }

    private bool CanScrollCurrentTarget()
    {
        return !IsClosing
            && IsActive
            && !_isInputInProgress
            && _selectionManager.State == SelectionState.Selecting
            && _selectionManager.CurrentStage == 2
            && !_rangeStartPoint.HasValue
            && !_dragStartPoint.HasValue
            && _selectionManager.TryGetStage2Target(out _);
    }

    private void ScrollCurrentTarget(int notches, bool horizontal)
    {
        if (!_selectionManager.TryGetStage2Target(out var target))
        {
            return;
        }

        ExecuteScrollOperation(target, notches, horizontal);
    }

    private void OnContinuousModeToggleRequested()
    {
        if (IsClosing
            || !IsActive
            || _scrollMode
            || IsRangeDragWorkflow
            || _rangeStartPoint.HasValue
            || _dragStartPoint.HasValue
            || _selectionManager.State != SelectionState.Selecting)
        {
            return;
        }

        _continuousMode = !_continuousMode;
        FileLogger.LogInfo(
            $"Continuous mode {(_continuousMode ? "enabled" : "disabled")} " +
            $"(stage={_selectionManager.CurrentStage}, historyLength={_selectionManager.History.Count}).");
        UpdateAllOverlays();
    }

    private void OnConfirmRequested(ClickKind clickKind)
    {
        int attempt = ++_confirmationAttempt;
        FileLogger.LogInfo(
            $"Confirmation #{attempt} received (kind={clickKind}, state={_selectionManager.State}, " +
            $"historyLength={_selectionManager.History.Count}).");

        if (_scrollMode)
        {
            FileLogger.LogDebug(
                $"Confirmation #{attempt} ignored while Scroll Mode is active; press Esc to return to Stage 2.");
            return;
        }

        if (_dragStartPoint.HasValue)
        {
            if (clickKind != ClickKind.Left)
            {
                FileLogger.LogDebug(
                    $"Confirmation #{attempt} ignored because drag selection uses unmodified Enter.");
                return;
            }

            ExecuteDragSelection(attempt);
            return;
        }

        if (_rangeStartPoint.HasValue)
        {
            if (clickKind != ClickKind.Left)
            {
                FileLogger.LogDebug(
                    $"Confirmation #{attempt} ignored because range selection uses unmodified Enter.");
                return;
            }

            ExecuteRangeSelection(attempt);
            return;
        }

        if (!_selectionManager.ProcessEnter())
        {
            FileLogger.LogDebug($"Confirmation #{attempt} ignored because the selection is not confirmable.");
            return;
        }

        FileLogger.LogDebug(
            $"Confirmation #{attempt} accepted (target={_selectionManager.TargetCoordinate}).");
        ExecuteConfirmedClick(clickKind, attempt);
    }

    private void OnBackspacePressed()
    {
        if (_scrollMode)
        {
            return;
        }

        if (_selectionManager.ProcessBackspace())
        {
            UpdateAllOverlays();
        }
    }

    private void OnCancelRequested()
    {
        if (IsClosing || _isInputInProgress) return;

        // Esc cancels an active sub-mode before it exits the selection session.
        if (_scrollMode)
        {
            _scrollMode = false;
            FileLogger.LogInfo("Scroll Mode exited by Esc; Stage 2 selection remains active.");
            UpdateAllOverlays();
            return;
        }

        if (_rangeStartPoint.HasValue)
        {
            _rangeStartPoint = null;
            // Clearing the anchor also ends the composite workflow; a second
            // Esc then exits the selection session as usual.
            _rangeDragPhase = RangeDragPhase.None;
            FileLogger.LogInfo("Range start cleared by Esc; selection remains active.");
            UpdateAllOverlays();
            return;
        }

        if (_dragStartPoint.HasValue)
        {
            _dragStartPoint = null;
            _rangeDragPhase = RangeDragPhase.None;
            FileLogger.LogInfo("Drag start cleared by Esc; selection remains active.");
            UpdateAllOverlays();
            return;
        }

        _selectionManager.Cancel();
        CloseOverlays();
    }

    private void OnFocusLost(int generation)
    {
        if (generation != _selectionGeneration
            || IsClosing
            || !IsActive
            || _isInputInProgress
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
                || _isInputInProgress
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

            FileLogger.LogDebug(
                $"Focus loss evaluated (foreground={FormatWindowHandle(foreground)}, " +
                $"focused={FormatWindowHandle(focused)}, pid={foregroundPid}, " +
                $"visible={foregroundIsVisible}, action={action}).");

            if (action == FocusLossAction.ReactivateOverlay
                && !primaryIsForeground
                && primaryOverlay.TryReactivate())
            {
                LockPrimaryForeground();
                FileLogger.LogInfo("Focus loss recovered by reactivating the primary overlay.");
                FileLogger.LogDebug(
                    $"Focus loss recovery details (foreground PID {foregroundPid}, " +
                    $"visible={foregroundIsVisible}).");
                return;
            }

            FileLogger.LogInfo("Focus loss cancelled selection.");
            FileLogger.LogDebug(
                $"Focus loss cancellation details (foreground PID {foregroundPid}, " +
                $"visible={foregroundIsVisible}).");
            _continuousMode = false;
            _rangeStartPoint = null;
            _dragStartPoint = null;
            _scrollMode = false;
            _rangeDragPhase = RangeDragPhase.None;
            _isInputInProgress = false;
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
            _rangeStartPoint = null;
            _dragStartPoint = null;
            _scrollMode = false;
            _rangeDragPhase = RangeDragPhase.None;
            _isInputInProgress = false;
            _selectionManager.Cancel();
            CloseOverlays();
        }
    }

    private void UpdateAllOverlays()
    {
        foreach (var overlay in _overlays)
        {
            overlay.UpdateRenderState(
                _selectionManager,
                _activeMonitorRects,
                _continuousMode,
                _rangeStartPoint,
                _scrollMode,
                IsRangeDragWorkflow,
                _dragStartPoint);
        }
    }

    private void ExecuteConfirmedClick(ClickKind clickKind, int attempt)
    {
        if (!_selectionManager.TargetCoordinate.HasValue)
        {
            FileLogger.LogWarning($"Confirmation #{attempt} was accepted without a target coordinate.");
            _continuousMode = false;
            CloseOverlays();
            _selectionManager.CompleteClick();
            return;
        }

        bool continueSelection = _continuousMode;
        var target = _selectionManager.TargetCoordinate.Value;
        var virtualScreen = _virtualScreenBounds;

        bool success;
        bool clickTargetReady = false;
        FileLogger.LogInfo($"Click attempt #{attempt} started (kind={clickKind}).");
        FileLogger.LogDebug(
            $"Click attempt #{attempt} target details (target=({target.X}, {target.Y}), " +
            $"virtualScreen={virtualScreen}).");
        try
        {
            success = _clickExecutor.ExecuteClick(
                target,
                virtualScreen,
                clickKind,
                hideOverlaysAction: () =>
                {
                    FileLogger.LogDebug($"Click attempt #{attempt}: closing overlays.");
                    CloseOverlaysWithoutRestore();
                },
                prepareClickAction: () =>
                {
                    FileLogger.LogDebug($"Click attempt #{attempt}: waiting for overlay render.");
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    FileLogger.LogDebug($"Click attempt #{attempt}: overlay render wait completed.");

                    // Resolve the click target after the overlays are hidden. Hiding
                    // does not activate another window, so WindowFromPoint now
                    // reports the same window a physical click would hit.
                    nint windowAtTarget = _windowFocusService.GetWindowFromPoint(target);
                    nint clickTargetWindow = _windowFocusService.GetRootWindow(windowAtTarget);
                    FileLogger.LogDebug(
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
                    FileLogger.LogDebug(
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
            _continuousMode = false;
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
            throw;
        }

        if (success)
        {
            _originatingForegroundWindow = nint.Zero;
            nint foreground = _windowFocusService.GetForegroundWindow();
            FileLogger.LogDebug(
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
        if (continueSelection)
        {
            // Rebuild the overlay session from Stage 1. The click target remains
            // the foreground window until the new primary overlay reclaims focus.
            FileLogger.LogInfo(
                $"Click attempt #{attempt} completed in continuous mode; reopening Stage 1.");
            Thread.Sleep(ContinuousModeClickSettleMilliseconds);
            ShowOverlaysCore();
            if (!IsActive)
            {
                _continuousMode = false;
            }
        }
        else
        {
            FileLogger.LogInfo($"Click attempt #{attempt} completed; selection returned to standby.");
        }

        if (!clickTargetReady)
        {
            ErrorOccurred?.Invoke(
                "Could not confirm the window under the selected coordinate. The click was not sent.");
        }
        else if (!success)
        {
            ErrorOccurred?.Invoke(
                "Failed to dispatch click. The target window may require a higher integrity level (UIPI).");
        }
    }

    private void ExecuteDragSelection(int attempt)
    {
        if (!_selectionManager.TryGetStage2Target(out var end))
        {
            FileLogger.LogDebug($"Drag #{attempt} ignored because the Stage 2 target is not confirmable.");
            return;
        }

        var start = _dragStartPoint!.Value;
        var virtualScreen = _virtualScreenBounds;
        bool continueSelection = _continuousMode;
        string? preparationFailure = null;
        bool success;

        FileLogger.LogInfo($"Drag #{attempt} started.");
        FileLogger.LogDebug(
            $"Drag #{attempt} target details (start=({start.X}, {start.Y}), " +
            $"end=({end.X}, {end.Y}), virtualScreen={virtualScreen}).");

        try
        {
            success = _clickExecutor.ExecuteDrag(
                start,
                end,
                virtualScreen,
                hideOverlaysAction: HideOverlaysForInput,
                prepareClickAction: () =>
                {
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

                    if (!_activeMonitorRects.Any(rect => rect.Contains(start))
                        || !_activeMonitorRects.Any(rect => rect.Contains(end)))
                    {
                        preparationFailure =
                            "The drag start or drop target is outside all monitor areas.";
                        return false;
                    }

                    nint startWindow = _windowFocusService.GetWindowFromPoint(start);
                    nint startRoot = _windowFocusService.GetRootWindow(startWindow);
                    nint endWindow = _windowFocusService.GetWindowFromPoint(end);
                    nint endRoot = _windowFocusService.GetRootWindow(endWindow);

                    FileLogger.LogDebug(
                        $"Drag #{attempt}: window context before activation " +
                        $"(startWindow={FormatWindowHandle(startWindow)}, startRoot={FormatWindowHandle(startRoot)}, " +
                        $"endWindow={FormatWindowHandle(endWindow)}, endRoot={FormatWindowHandle(endRoot)}, " +
                        $"foreground={FormatWindowHandle(_windowFocusService.GetForegroundWindow())}).");

                    if (startRoot == nint.Zero)
                    {
                        preparationFailure =
                            "Could not find a window under the drag start point.";
                        return false;
                    }

                    if (endRoot == nint.Zero)
                    {
                        preparationFailure =
                            "Could not find a window under the drag drop target.";
                        return false;
                    }

                    bool activated = RestoreForegroundWindow(startRoot, "Drag start");
                    nint startAfterActivation = _windowFocusService.GetWindowFromPoint(start);
                    nint endAfterActivation = _windowFocusService.GetWindowFromPoint(end);
                    bool startStillHit = _windowFocusService.IsWindowOwnedBy(
                        startAfterActivation,
                        startRoot);
                    nint endRootAfterActivation = _windowFocusService.GetRootWindow(endAfterActivation);

                    if (!activated || !startStillHit || endRootAfterActivation == nint.Zero)
                    {
                        preparationFailure =
                            "Could not activate the drag start or verify the drop target.";
                        return false;
                    }

                    return true;
                });
        }
        catch
        {
            _isInputInProgress = false;
            _dragStartPoint = null;
            _rangeDragPhase = RangeDragPhase.None;
            _selectionManager.Cancel();
            CloseOverlaysWithoutRestore();
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
            throw;
        }

        if (preparationFailure != null)
        {
            FileLogger.LogWarning($"Drag #{attempt} aborted: {preparationFailure}");
            if (!ShowOverlaysAfterInput())
            {
                return;
            }

            _dragStartPoint = null;
            _rangeDragPhase = RangeDragPhase.None;
            UpdateAllOverlays();
            ErrorOccurred?.Invoke(preparationFailure);
            return;
        }

        _isInputInProgress = false;
        _dragStartPoint = null;
        _rangeDragPhase = RangeDragPhase.None;

        if (success)
        {
            _originatingForegroundWindow = nint.Zero;
            FileLogger.LogInfo($"Drag #{attempt} completed successfully.");
        }
        else
        {
            FileLogger.LogWarning($"Drag #{attempt} SendInput failed.");
            if (!ShowOverlaysAfterInput())
            {
                return;
            }

            UpdateAllOverlays();
            ErrorOccurred?.Invoke(
                "Failed to dispatch drag. The drag start or target window may require a higher integrity level (UIPI).");
            return;
        }

        _selectionManager.CompleteClick();
        CloseOverlaysWithoutRestore();

        if (continueSelection)
        {
            FileLogger.LogInfo($"Drag #{attempt} completed in continuous mode; reopening Stage 1.");
            Thread.Sleep(ContinuousModeClickSettleMilliseconds);
            ShowOverlaysCore();
            if (!IsActive)
            {
                _continuousMode = false;
            }
        }
        else
        {
            FileLogger.LogInfo($"Drag #{attempt} finished; selection returned to standby.");
        }
    }

    private void ExecuteRangeSelection(int attempt)
    {
        if (!_selectionManager.TryGetStage2Target(out var end))
        {
            FileLogger.LogDebug($"Range #{attempt} ignored because the Stage 2 target is not confirmable.");
            return;
        }

        var start = _rangeStartPoint!.Value;
        var virtualScreen = _virtualScreenBounds;
        bool continueSelection = _continuousMode;
        bool wasRangeDragWorkflow = _rangeDragPhase == RangeDragPhase.Range;
        string? preparationFailure = null;
        bool success;

        FileLogger.LogInfo($"Range #{attempt} started.");
        FileLogger.LogDebug(
            $"Range #{attempt} target details (start=({start.X}, {start.Y}), " +
            $"end=({end.X}, {end.Y}), virtualScreen={virtualScreen}).");

        try
        {
            success = _clickExecutor.ExecuteRangeClick(
                start,
                end,
                virtualScreen,
                hideOverlaysAction: HideOverlaysForInput,
                prepareClickAction: () =>
                {
                    FileLogger.LogDebug($"Range #{attempt}: waiting for overlay render.");
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

                    // Hiding the overlays makes WindowFromPoint report what a physical
                    // click would hit. Both endpoints must belong to the same top-level
                    // window before any synthetic input is dispatched.
                    nint startWindow = _windowFocusService.GetWindowFromPoint(start);
                    nint endWindow = _windowFocusService.GetWindowFromPoint(end);
                    nint startRoot = _windowFocusService.GetRootWindow(startWindow);
                    nint endRoot = _windowFocusService.GetRootWindow(endWindow);

                    FileLogger.LogDebug(
                        $"Range #{attempt}: start window={FormatWindowHandle(startWindow)} " +
                        $"(root={FormatWindowHandle(startRoot)}), " +
                        $"end window={FormatWindowHandle(endWindow)} " +
                        $"(root={FormatWindowHandle(endRoot)}).");

                    if (startRoot == nint.Zero || endRoot == nint.Zero)
                    {
                        preparationFailure =
                            "Could not find a window under a range endpoint. The range was cancelled.";
                        return false;
                    }

                    if (startRoot != endRoot)
                    {
                        preparationFailure =
                            "Range endpoints must be in the same top-level window. The range was cancelled.";
                        return false;
                    }

                    bool activated = RestoreForegroundWindow(startRoot, "Range target");
                    nint startAfterActivation = _windowFocusService.GetWindowFromPoint(start);
                    nint endAfterActivation = _windowFocusService.GetWindowFromPoint(end);
                    bool startStillHit = _windowFocusService.IsWindowOwnedBy(
                        startAfterActivation,
                        startRoot);
                    bool endStillHit = _windowFocusService.IsWindowOwnedBy(
                        endAfterActivation,
                        endRoot);

                    FileLogger.LogDebug(
                        $"Range #{attempt}: target activation result={activated}, " +
                        $"start matches={startStillHit}, end matches={endStillHit}.");

                    if (!startStillHit || !endStillHit)
                    {
                        preparationFailure =
                            "The window under a range endpoint changed. The range was cancelled.";
                        return false;
                    }

                    return true;
                });
        }
        catch
        {
            _isInputInProgress = false;
            _rangeStartPoint = null;
            _rangeDragPhase = RangeDragPhase.None;
            _selectionManager.Cancel();
            CloseOverlaysWithoutRestore();
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
            throw;
        }

        if (preparationFailure != null)
        {
            // No click was sent. Restore the same Stage 2 selection so the user can
            // adjust the endpoints or try again.
            FileLogger.LogWarning($"Range #{attempt} aborted: {preparationFailure}");
            if (!ShowOverlaysAfterInput())
            {
                return;
            }

            _rangeStartPoint = null;
            _rangeDragPhase = RangeDragPhase.None;
            UpdateAllOverlays();
            ErrorOccurred?.Invoke(preparationFailure);
            return;
        }

        _isInputInProgress = false;
        _rangeStartPoint = null;

        // A successful range inside the composite workflow reuses the exact
        // endpoint as the drag anchor and reopens Stage 1 for the drop target.
        bool reopenForDrag = wasRangeDragWorkflow && success;
        if (reopenForDrag)
        {
            _dragStartPoint = end;
            _rangeDragPhase = RangeDragPhase.Drag;
        }
        else
        {
            _rangeDragPhase = RangeDragPhase.None;
        }

        if (success)
        {
            _originatingForegroundWindow = nint.Zero;
            FileLogger.LogInfo($"Range #{attempt} completed successfully.");
        }
        else
        {
            FileLogger.LogInfo(
                $"Range #{attempt} failed; restoring the originating foreground window.");
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
        }

        _selectionManager.CompleteClick();
        CloseOverlaysWithoutRestore();

        if (continueSelection || reopenForDrag)
        {
            FileLogger.LogInfo(
                reopenForDrag
                    ? $"Range #{attempt} completed in the range-drag workflow; reopening Stage 1 for the drag target."
                    : $"Range #{attempt} completed in continuous mode; reopening Stage 1.");
            Thread.Sleep(ContinuousModeClickSettleMilliseconds);
            ShowOverlaysCore();
            if (!IsActive)
            {
                _continuousMode = false;
                _rangeDragPhase = RangeDragPhase.None;
            }
        }
        else
        {
            FileLogger.LogInfo($"Range #{attempt} finished; selection returned to standby.");
        }

        if (!success)
        {
            ErrorOccurred?.Invoke(
                "Failed to dispatch range clicks. The target window may require a higher integrity level (UIPI).");
        }
    }

    private void ExecuteScrollOperation(PixelPoint target, int notches, bool horizontal)
    {
        if (notches == 0)
        {
            return;
        }

        var virtualScreen = _virtualScreenBounds;
        string? preparationFailure = null;
        bool success;

        FileLogger.LogInfo($"Scroll starting (notches={notches}, horizontal={horizontal}).");
        FileLogger.LogDebug($"Scroll target details (target=({target.X}, {target.Y})).");

        try
        {
            success = _clickExecutor.ExecuteScroll(
                target,
                virtualScreen,
                notches,
                horizontal,
                hideOverlaysAction: HideOverlaysForInput,
                prepareClickAction: () =>
                {
                    FileLogger.LogDebug("Scroll: waiting for overlay render.");
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

                    nint window = _windowFocusService.GetWindowFromPoint(target);
                    nint root = _windowFocusService.GetRootWindow(window);
                    FileLogger.LogDebug(
                        $"Scroll: window={FormatWindowHandle(window)}, " +
                        $"root={FormatWindowHandle(root)}.");

                    if (root == nint.Zero)
                    {
                        preparationFailure =
                            "Could not find a window under the scroll target. Scroll Mode was cancelled.";
                        return false;
                    }

                    bool activated = RestoreForegroundWindow(root, "Scroll target");
                    nint windowAfterActivation = _windowFocusService.GetWindowFromPoint(target);
                    bool stillHit = _windowFocusService.IsWindowOwnedBy(
                        windowAfterActivation,
                        root);
                    FileLogger.LogDebug(
                        $"Scroll: target activation result={activated}, " +
                        $"window after activation={FormatWindowHandle(windowAfterActivation)}, " +
                        $"matchesTarget={stillHit}.");

                    if (!stillHit)
                    {
                        preparationFailure =
                            "The window under the scroll target changed. Scroll Mode was cancelled.";
                        return false;
                    }

                    return true;
                });
        }
        catch
        {
            _scrollMode = false;
            ShowOverlaysAfterInput();
            throw;
        }

        if (preparationFailure != null)
        {
            _scrollMode = false;
            FileLogger.LogWarning($"Scroll aborted: {preparationFailure}");
            if (!ShowOverlaysAfterInput())
            {
                return;
            }

            UpdateAllOverlays();
            ErrorOccurred?.Invoke(preparationFailure);
            return;
        }

        if (!success)
        {
            _scrollMode = false;
            FileLogger.LogWarning("Scroll input was not dispatched; leaving Scroll Mode.");
        }

        if (!ShowOverlaysAfterInput())
        {
            _scrollMode = false;
            return;
        }

        UpdateAllOverlays();
        if (!success)
        {
            ErrorOccurred?.Invoke(
                "Failed to dispatch scroll input. The target window may require a higher integrity level (UIPI).");
        }
    }

    /// <summary>
    /// Temporarily hides every overlay without tearing down the selection. The
    /// surfaces are kept alive so range validation failures and scroll operations
    /// can return to the same Stage 2 state.
    /// </summary>
    private void HideOverlaysForInput()
    {
        _isInputInProgress = true;
        _windowFocusService.UnlockForeground();
        foreach (var overlay in _overlays)
        {
            overlay.HideForInput();
        }
    }

    /// <summary>
    /// Restores surfaces hidden by <see cref="HideOverlaysForInput"/> and reclaims
    /// focus for the primary overlay. Returns false after cancelling the session if
    /// the overlay cannot be focused again.
    /// </summary>
    private bool ShowOverlaysAfterInput()
    {
        if (IsClosing || _overlays.Count == 0)
        {
            _isInputInProgress = false;
            return false;
        }

        foreach (var overlay in _overlays)
        {
            overlay.ShowForInput();
        }

        if (_primaryOverlay == null || !_primaryOverlay.TryReactivate())
        {
            _isInputInProgress = false;
            _continuousMode = false;
            _rangeStartPoint = null;
            _scrollMode = false;
            _rangeDragPhase = RangeDragPhase.None;
            _selectionManager.Cancel();
            CloseOverlaysWithoutRestore();
            RestoreOriginatingForegroundWindow();
            _originatingForegroundWindow = nint.Zero;
            ErrorOccurred?.Invoke("Failed to restore the selection overlay.");
            return false;
        }

        LockPrimaryForeground();
        _isInputInProgress = false;
        return true;
    }

    public void CloseOverlays()
    {
        _continuousMode = false;
        _rangeStartPoint = null;
        _dragStartPoint = null;
        _scrollMode = false;
        _rangeDragPhase = RangeDragPhase.None;
        _isInputInProgress = false;
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
                FileLogger.LogDebug($"Closing {(isPrimary ? "primary" : "secondary")} overlay.");
                try
                {
                    overlay.Close();
                    FileLogger.LogDebug(
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
        FileLogger.LogDebug(
            $"{description} foreground restore result={succeeded} " +
            $"(target={FormatWindowHandle(target)}, " +
            $"before={FormatWindowHandle(foregroundBeforeRestore)}, " +
            $"after={FormatWindowHandle(foregroundAfterRestore)}).");
        return succeeded;
    }

    private static string FormatWindowHandle(nint window) => $"0x{window.ToInt64():X}";
}
