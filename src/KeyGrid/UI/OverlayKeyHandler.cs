using System.Windows.Input;
using KeyGrid.Core.Abstractions;

namespace KeyGrid.UI;

internal sealed class OverlayKeyHandler
{
    private readonly bool _isPrimary;
    private ClickKind? _pendingClickKind;
    private Key? _pendingChordKey;
    private Key? _pendingConfirmKey;
    private bool _pendingContinueConfirm;

    /// <summary>
    /// Whether repeated arrow keys should dispatch movement. Enabled only while
    /// the Stage 2 cursor is active, so holding an arrow key moves continuously
    /// without changing the repeat policy in Stage 1.
    /// </summary>
    public bool AllowArrowKeyRepeat { get; set; }

    /// <summary>
    /// Whether repeated Page keys should dispatch page scrolls. Enabled only
    /// while Scroll Mode is active, so holding a Page key keeps scrolling
    /// without changing the repeat policy elsewhere.
    /// </summary>
    public bool AllowPageScrollRepeat { get; set; }

    /// <summary>
    /// Whether an overlapping R+D key-down pair requests the range-then-drag
    /// workflow. Enabled only in eligible Stage 2 state; while enabled, a lone
    /// R or D is deferred to key-up so the chord can be recognized first.
    /// </summary>
    public bool AllowRangeDragChord { get; set; }

    /// <summary>
    /// Whether C should request a confirmation that also enters continuous
    /// mode. Enabled only for an eligible Stage 2 target; while disabled, C
    /// remains an ordinary Stage 1 column key.
    /// </summary>
    public bool AllowContinueConfirm { get; set; }

    public event Action<char>? KeyPressed;
    public event Action<int, int>? MoveRequested;
    public event Action<ClickKind>? ConfirmRequested;
    /// <summary>
    /// Raised on key-up of C in eligible Stage 2 state. Confirms like Enter
    /// but also enters continuous mode for the next selection.
    /// </summary>
    public event Action<ClickKind>? ConfirmAndContinueRequested;
    public event Action? BackspacePressed;
    public event Action? CancelRequested;
    public event Action? ContinuousModeToggleRequested;
    public event Action? RangeStartRequested;
    public event Action? DragStartRequested;
    /// <summary>
    /// Raised when R and D key-downs overlap in either press order. Neither
    /// individual range nor drag action is dispatched for the chord.
    /// </summary>
    public event Action? RangeDragWorkflowRequested;
    /// <summary>
    /// Raised for plain PageUp (-1) or PageDown (+1). The manager decides whether
    /// the key enters Scroll Mode or scrolls an already active session.
    /// </summary>
    public event Action<int>? PageScrollRequested;
    /// <summary>
    /// Raised for an unmodified digit key (1-9, row or numpad). The value is the
    /// 1-based monitor number in the manager's spatial ordering.
    /// </summary>
    public event Action<int>? MonitorSelectRequested;
    /// <summary>
    /// Raised for Tab (+1, next monitor) or Shift+Tab (-1, previous monitor).
    /// The manager wraps the direction around the monitor list.
    /// </summary>
    public event Action<int>? MonitorCycleRequested;

    public OverlayKeyHandler(bool isPrimary)
    {
        _isPrimary = isPrimary;
    }

    public bool HandleKeyDown(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (!_isPrimary)
        {
            return false;
        }

        if (isRepeat
            && !(AllowArrowKeyRepeat && IsArrowKey(key))
            && !(AllowPageScrollRepeat && IsPageKey(key)))
        {
            return true;
        }

        // Any non-chord, non-modifier key breaks the overlap, so a later key-up
        // must not complete a chord across unrelated input. Modifier key-downs
        // are tolerated because users can brush Alt/Shift while pressing the
        // chord keys; modified R/D presses still cannot form the chord.
        if (key is not (Key.R or Key.D) && !IsModifierKey(key))
        {
            _pendingChordKey = null;
        }

        // Digit keys select the monitor hosting the Stage 1 grid. They are
        // consumed even with modifiers held so modified digits cannot leak to
        // the window that receives focus after the overlay closes.
        if (key >= Key.D1 && key <= Key.D9)
        {
            if (modifiers == ModifierKeys.None)
            {
                MonitorSelectRequested?.Invoke(key - Key.D1 + 1);
            }

            return true;
        }

        if (key >= Key.NumPad1 && key <= Key.NumPad9)
        {
            if (modifiers == ModifierKeys.None)
            {
                MonitorSelectRequested?.Invoke(key - Key.NumPad1 + 1);
            }

            return true;
        }

        switch (key)
        {
            case Key.Tab:
                // Tab cycles the Stage 1 grid monitor; Shift+Tab cycles
                // backwards. Other modified Tab combinations are consumed so
                // they cannot move focus away from the overlay.
                if (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift)
                {
                    MonitorCycleRequested?.Invoke(modifiers == ModifierKeys.Shift ? -1 : 1);
                }

                return true;
            case Key.OemSemicolon:
                // Only plain ";" toggles continuous mode; modified presses are
                // consumed so they cannot reach the window that gets focus next.
                if (modifiers == ModifierKeys.None)
                {
                    ContinuousModeToggleRequested?.Invoke();
                }

                return true;
            case Key.Escape:
                // A pending Enter or C must not fire after the selection was
                // cancelled or a sub-mode was exited.
                _pendingClickKind = null;
                _pendingConfirmKey = null;
                _pendingContinueConfirm = false;
                CancelRequested?.Invoke();
                return true;
            case Key.Back:
                BackspacePressed?.Invoke();
                return true;
            case Key.PageUp when modifiers == ModifierKeys.None:
                // Page keys can enter Scroll Mode, where Enter is ignored. Drop a
                // pending confirmation so releasing a held Enter cannot click later.
                _pendingClickKind = null;
                _pendingConfirmKey = null;
                _pendingContinueConfirm = false;
                PageScrollRequested?.Invoke(-1);
                return true;
            case Key.PageDown when modifiers == ModifierKeys.None:
                _pendingClickKind = null;
                _pendingConfirmKey = null;
                _pendingContinueConfirm = false;
                PageScrollRequested?.Invoke(1);
                return true;
            case Key.Enter:
                // Consume the key down so Windows does not deliver it to another window
                // when the overlay closes. Confirmation runs on key up. Capture the
                // modifiers now because the user may release a modifier before Enter.
                _pendingClickKind = MapClickKind(modifiers);
                _pendingConfirmKey = Key.Enter;
                _pendingContinueConfirm = false;
                return true;
            case Key.C when AllowContinueConfirm:
                // C confirms like Enter but enters continuous mode afterwards.
                // The same modifier-to-click-kind mapping applies; the pending
                // key is tracked so a held C is not confused with Enter.
                _pendingClickKind = MapClickKind(modifiers);
                _pendingConfirmKey = Key.C;
                _pendingContinueConfirm = true;
                return true;
            case Key.Left:
                MoveRequested?.Invoke(-1, 0);
                return true;
            case Key.Right:
                MoveRequested?.Invoke(1, 0);
                return true;
            case Key.Up:
                MoveRequested?.Invoke(0, -1);
                return true;
            case Key.Down:
                MoveRequested?.Invoke(0, 1);
                return true;
            case Key.R when modifiers == ModifierKeys.None:
                if (AllowRangeDragChord)
                {
                    // Defer the single-key action to key-up so an overlapping D
                    // can still turn the press into the composite workflow.
                    if (_pendingChordKey == Key.D)
                    {
                        _pendingChordKey = null;
                        RangeDragWorkflowRequested?.Invoke();
                    }
                    else
                    {
                        _pendingChordKey = Key.R;
                    }

                    return true;
                }

                // R is also a Stage 1 column key. The manager decides whether this
                // press starts a range or is part of a Stage 1 selection.
                RangeStartRequested?.Invoke();
                return true;
            case Key.D when modifiers == ModifierKeys.None:
                if (AllowRangeDragChord)
                {
                    if (_pendingChordKey == Key.R)
                    {
                        _pendingChordKey = null;
                        RangeDragWorkflowRequested?.Invoke();
                    }
                    else
                    {
                        _pendingChordKey = Key.D;
                    }

                    return true;
                }

                // D is also a Stage 1 column key. The manager decides whether this
                // press starts drag or is part of Stage 1 selection.
                DragStartRequested?.Invoke();
                return true;
            case Key.D:
                // Consume modified D so it cannot become a Stage 1 column key.
                return true;
            case Key.F when modifiers == ModifierKeys.None && AllowRangeDragChord:
                // F is unused in Stage 2. It is a more reliable trigger for the
                // composite workflow than the R+D chord; Stage 1 keeps it as an
                // ordinary column key because AllowRangeDragChord is only set
                // for an eligible Stage 2 target.
                RangeDragWorkflowRequested?.Invoke();
                return true;
            default:
                if (key >= Key.A && key <= Key.Z)
                {
                    KeyPressed?.Invoke((char)('A' + (key - Key.A)));
                }

                return true;
        }
    }

    private static ClickKind? MapClickKind(ModifierKeys modifiers)
    {
        return modifiers switch
        {
            ModifierKeys.None => ClickKind.Left,
            ModifierKeys.Shift => ClickKind.Right,
            ModifierKeys.Control => ClickKind.Middle,
            _ => null
        };
    }

    private static bool IsArrowKey(Key key)
    {
        return key is Key.Left or Key.Right or Key.Up or Key.Down;
    }

    private static bool IsPageKey(Key key)
    {
        return key is Key.PageUp or Key.PageDown;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
    }

    /// <summary>
    /// Drops a deferred R/D press without dispatching it. Used when focus is
    /// lost or the overlay is torn down, because the matching key-up may never
    /// arrive.
    /// </summary>
    public void ClearPendingChord()
    {
        _pendingChordKey = null;
    }

    public bool HandleKeyUp(Key key, ModifierKeys modifiers)
    {
        if (!_isPrimary)
        {
            return false;
        }

        if (key is Key.R or Key.D && AllowRangeDragChord)
        {
            // Consume chord keys even when nothing is pending so a stale key-up
            // cannot leak to the window that receives focus next.
            if (_pendingChordKey == key)
            {
                _pendingChordKey = null;
                if (key == Key.R)
                {
                    RangeStartRequested?.Invoke();
                }
                else
                {
                    DragStartRequested?.Invoke();
                }
            }

            return true;
        }

        if (key is not (Key.Enter or Key.C))
        {
            return false;
        }

        // Dispatch only when the released key is the one that armed the pending
        // confirmation, so releasing a held C cannot complete (or cancel) an
        // Enter confirmation and vice versa. Both keys are consumed either way
        // so a stale key-up cannot reach the window that gets focus next.
        if (_pendingConfirmKey != key)
        {
            return true;
        }

        ClickKind? clickKind = _pendingClickKind;
        bool continueConfirm = _pendingContinueConfirm;
        _pendingClickKind = null;
        _pendingConfirmKey = null;
        _pendingContinueConfirm = false;

        if (clickKind.HasValue)
        {
            if (continueConfirm)
            {
                ConfirmAndContinueRequested?.Invoke(clickKind.Value);
            }
            else
            {
                ConfirmRequested?.Invoke(clickKind.Value);
            }
        }

        return true;
    }
}
