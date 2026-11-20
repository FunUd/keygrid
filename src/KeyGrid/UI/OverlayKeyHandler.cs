using System.Windows.Input;
using KeyGrid.Core.Abstractions;

namespace KeyGrid.UI;

internal sealed class OverlayKeyHandler
{
    private readonly bool _isPrimary;
    private ClickKind? _pendingClickKind;

    /// <summary>
    /// Whether repeated arrow keys should dispatch movement. Enabled only while
    /// the Stage 2 cursor is active, so holding an arrow key moves continuously
    /// without changing the repeat policy in Stage 1 or Scroll Mode.
    /// </summary>
    public bool AllowArrowKeyRepeat { get; set; }

    public event Action<char>? KeyPressed;
    public event Action<int, int>? MoveRequested;
    public event Action<ClickKind>? ConfirmRequested;
    public event Action? BackspacePressed;
    public event Action? CancelRequested;
    public event Action? ContinuousModeToggleRequested;
    public event Action? RangeStartRequested;
    public event Action? DragStartRequested;
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
                // R is also a Stage 1 column key. The manager decides whether this
                // press starts a range or is part of a Stage 1 selection.
                RangeStartRequested?.Invoke();
                return true;
            case Key.D when modifiers == ModifierKeys.None:
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

    public bool HandleKeyUp(Key key, ModifierKeys modifiers)
    {
        if (!_isPrimary)
        {
            return false;
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
