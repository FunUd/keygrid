using System.Windows.Input;
using KeyGrid.Core.Abstractions;

namespace KeyGrid.UI;

internal sealed class OverlayKeyHandler
{
    private readonly bool _isPrimary;
    private ClickKind? _pendingClickKind;

    public event Action<char>? KeyPressed;
    public event Action<int, int>? MoveRequested;
    public event Action<ClickKind>? ConfirmRequested;
    public event Action? BackspacePressed;
    public event Action? CancelRequested;
    public event Action? ContinuousModeToggleRequested;
    public event Action? RangeStartRequested;

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

        if (isRepeat)
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
                CancelRequested?.Invoke();
                return true;
            case Key.Back:
                BackspacePressed?.Invoke();
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
            default:
                if (key >= Key.A && key <= Key.Z)
                {
                    KeyPressed?.Invoke((char)('A' + (key - Key.A)));
                }

                return true;
        }
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
