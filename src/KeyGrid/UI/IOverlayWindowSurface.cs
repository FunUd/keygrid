using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;

namespace KeyGrid.UI;

public interface IOverlayWindowSurface
{
    event Action<char>? KeyPressed;
    event Action<int, int>? MoveRequested;
    event Action<ClickKind>? ConfirmRequested;
    event Action? BackspacePressed;
    event Action? CancelRequested;
    event Action? ContinuousModeToggleRequested;
    event Action? RangeStartRequested;
    event Action<int>? PageScrollRequested;
    event Action? FocusLost;
    event Action? DisplayConfigurationChanged;

    nint Handle { get; }
    bool TryReactivate();
    void UpdateRenderState(
        SelectionManager selectionManager,
        IReadOnlyList<PixelRect> monitorRects,
        bool isContinuousMode,
        PixelPoint? rangeStartPoint = null,
        bool isScrollMode = false);
    void RefreshExpectedDpi();
    void EnableDpiChangeNotifications();
    void Show();
    void HideForInput();
    void ShowForInput();
    void Close();
}
