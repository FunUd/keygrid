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
    event Action? DragStartRequested;
    event Action? RangeDragWorkflowRequested;
    event Action<int>? PageScrollRequested;
    event Action<int>? MonitorSelectRequested;
    event Action? FocusLost;
    event Action? DisplayConfigurationChanged;

    nint Handle { get; }
    bool TryReactivate();
    void UpdateRenderState(
        SelectionManager selectionManager,
        IReadOnlyList<PixelRect> monitorRects,
        bool isContinuousMode,
        PixelPoint? rangeStartPoint = null,
        bool isScrollMode = false,
        bool isRangeDragWorkflow = false,
        PixelPoint? dragStartPoint = null,
        string? monitorBadgeLabel = null);
    void RefreshExpectedDpi();
    void EnableDpiChangeNotifications();
    void Show();
    void HideForInput();
    void ShowForInput();
    void Close();
}
