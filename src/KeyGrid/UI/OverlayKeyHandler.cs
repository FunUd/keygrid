using System.Windows.Input;

namespace KeyGrid.UI;

internal sealed class OverlayKeyHandler
{
    private readonly bool _isPrimary;

    public event Action<char>? KeyPressed;
    public event Action<int, int>? MoveRequested;
    public event Action? EnterPressed;
    public event Action? BackspacePressed;
    public event Action? CancelRequested;

    public OverlayKeyHandler(bool isPrimary)
    {
        _isPrimary = isPrimary;
    }

    public bool HandleKeyDown(Key key, bool isRepeat)
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
            case Key.Escape:
                CancelRequested?.Invoke();
                return true;
            case Key.Back:
                BackspacePressed?.Invoke();
                return true;
            case Key.Enter:
                EnterPressed?.Invoke();
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
            default:
                if (key >= Key.A && key <= Key.Z)
                {
                    KeyPressed?.Invoke((char)('A' + (key - Key.A)));
                }

                return true;
        }
    }
}
