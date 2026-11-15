using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;

namespace KeyGrid.UI;

public interface IOverlayWindowSurface
{
    event Action<char>? KeyPressed;
    event Action<int, int>? MoveRequested;
    event Action? EnterPressed;
    event Action? BackspacePressed;
    event Action? CancelRequested;
    event Action? FocusLost;
    event Action? DisplayConfigurationChanged;

    bool TryReactivate();
    void UpdateRenderState(SelectionManager selectionManager, IReadOnlyList<PixelRect> monitorRects);
    void RefreshExpectedDpi();
    void EnableDpiChangeNotifications();
    void Show();
    void Close();
}
