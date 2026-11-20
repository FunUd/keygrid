using System.Windows.Input;
using KeyGrid.Core.Abstractions;

namespace KeyGrid.UI;

internal sealed class OverlayKeyHandler
{
    private readonly bool _isPrimary;
    private ClickKind? _pendingClickKind;
    private Key? _pendingChordKey;

    /// <summary>
    /// Whether repeated arrow keys should dispatch movement. Enabled only while
    /// the Stage 2 cursor is active, so holding an arrow key moves continuously
    /// without changing the repeat policy in Stage 1 or Scroll Mode.
    /// </summary>
    public bool AllowArrowKeyRepeat { get; set; }

    /// <summary>
    /// Whether an overlapping R+D key-down pair requests the range-then-drag
    /// workflow. Enabled only in eligible Stage 2 state; while enabled, a lone
    /// R or D is deferred to key-up so the chord can be recognized first.
    /// </summary>
    public bool AllowRangeDragChord { get; set; }

    public event Action<char>? KeyPressed;
    public event Action<int, int>? MoveRequested;
    public event Action<ClickKind>? ConfirmRequested;
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

        if (isRepeat && !(AllowArrowKeyRepeat && IsArrowKey(key)))
        {
            return true;
        }

        // Any key other than the chord pair breaks the overlap, so a later
        // key-up must not complete a chord across unrelated input.
        if (key is not (Key.R or Key.D))
        {
            _pendingChordKey = null;
        }

        switch (key)
        {
            case Key.Tab:
                // Only plain Tab toggles continuous mode; modified Tab combinations
                // are consumed so they cannot move focus away from the overlay.
                if (modifiers == ModifierKeys.None)
                {
                    ContinuousModeToggleRequested?.Invoke();
                }

                return true;
            case Key.Escape:
                // A pending Enter must not fire after the selection was cancelled
                // or a sub-mode was exited.
                _pendingClickKind = null;
                CancelRequested?.Invoke();
                return true;
            case Key.Back:
                BackspacePressed?.Invoke();
                return true;
            case Key.PageUp when modifiers == ModifierKeys.None:
                // Page keys can enter Scroll Mode, where Enter is ignored. Drop a
                // pending confirmation so releasing a held Enter cannot click later.
                _pendingClickKind = null;
                PageScrollRequested?.Invoke(-1);
                return true;
            case Key.PageDown when modifiers == ModifierKeys.None:
                _pendingClickKind = null;
                PageScrollRequested?.Invoke(1);
                return true;
            case Key.Enter:
                // Consume the key down so Windows does not deliver it to another window
                // when the overlay closes. Confirmation runs on key up. Capture the
                // modifiers now because the user may release a modifier before Enter.
                _pendingClickKind = modifiers switch
                {
                    ModifierKeys.None => ClickKind.Left,
                    ModifierKeys.Shift => ClickKind.Right,
                    ModifierKeys.Control => ClickKind.Middle,
                    _ => null
                };
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
            default:
                if (key >= Key.A && key <= Key.Z)
                {
                    KeyPressed?.Invoke((char)('A' + (key - Key.A)));
                }

                return true;
        }
    }

    private static bool IsArrowKey(Key key)
    {
        return key is Key.Left or Key.Right or Key.Up or Key.Down;
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

        if (key != Key.Enter)
        {
            return false;
        }

        ClickKind? clickKind = _pendingClickKind;
        _pendingClickKind = null;
        if (clickKind.HasValue)
        {
            ConfirmRequested?.Invoke(clickKind.Value);
        }

        // Consume Enter even when the key down was not seen, so a stale key up
        // cannot reach the window that receives focus after the overlay closes.
        return true;
    }
}
