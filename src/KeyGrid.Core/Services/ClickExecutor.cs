using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Services;

/// <summary>
/// Executes the click sequence according to spec.md Section 6.3:
/// Hides overlays, waits for UI render flush, dispatches click via IClickSender,
/// and returns success/failure.
/// </summary>
public class ClickExecutor
{
    private readonly IClickSender _clickSender;

    public ClickExecutor(IClickSender clickSender)
    {
        _clickSender = clickSender;
    }

    /// <summary>
    /// Executes the final click with render synchronization.
    /// </summary>
    /// <param name="target">Target physical pixel coordinate.</param>
    /// <param name="virtualScreen">Virtual screen bounding box.</param>
    /// <param name="clickKind">Mouse button to send.</param>
    /// <param name="hideOverlaysAction">Action to hide all overlays.</param>
    /// <param name="prepareClickAction">Optional action that waits for rendering and confirms the target window is ready.</param>
    /// <returns>True if SendInput succeeded; false otherwise.</returns>
    public bool ExecuteClick(
        PixelPoint target,
        PixelRect virtualScreen,
        ClickKind clickKind,
        Action hideOverlaysAction,
        Func<bool>? prepareClickAction = null)
    {
        // 1. Hide all overlays
        hideOverlaysAction();

        // 2. Wait for visual updates to flush to avoid clicking on the overlay
        if (prepareClickAction != null && !prepareClickAction())
        {
            return false;
        }

        // 3. Dispatch click
        bool success = _clickSender.SendClick(target, virtualScreen, clickKind);
        return success;
    }

    /// <summary>
    /// Executes a two-point range selection with the same hide/render synchronization
    /// used for a normal click. The target window validation is supplied by the caller
    /// because it depends on the window hierarchy after the overlays are hidden.
    /// </summary>
    public bool ExecuteRangeClick(
        PixelPoint start,
        PixelPoint end,
        PixelRect virtualScreen,
        Action hideOverlaysAction,
        Func<bool>? prepareClickAction = null)
    {
        hideOverlaysAction();

        if (prepareClickAction != null && !prepareClickAction())
        {
            return false;
        }

        return _clickSender.SendRangeClick(start, end, virtualScreen);
    }

    /// <summary>
    /// Executes a two-point drag with the same hide/render synchronization used for clicks.
    /// </summary>
    public bool ExecuteDrag(
        PixelPoint start,
        PixelPoint end,
        PixelRect virtualScreen,
        Action hideOverlaysAction,
        Func<bool>? prepareClickAction = null)
    {
        hideOverlaysAction();

        if (prepareClickAction != null && !prepareClickAction())
        {
            return false;
        }

        return _clickSender.SendDrag(start, end, virtualScreen);
    }

    /// <summary>
    /// Executes wheel input at a target with the same hide/render synchronization
    /// used for clicks. Scroll mode uses this so the overlay cannot absorb the wheel event.
    /// </summary>
    public bool ExecuteScroll(
        PixelPoint target,
        PixelRect virtualScreen,
        int notches,
        bool horizontal,
        Action hideOverlaysAction,
        Func<bool>? prepareClickAction = null)
    {
        hideOverlaysAction();

        if (prepareClickAction != null && !prepareClickAction())
        {
            return false;
        }

        return _clickSender.SendScroll(target, virtualScreen, notches, horizontal);
    }

    /// <summary>
    /// Sends wheel input without hiding overlays or re-validating the target.
    /// Used for subsequent inputs inside an active scroll burst, where the
    /// overlays are already hidden and the target window was verified once.
    /// </summary>
    public bool SendScrollNow(
        PixelPoint target,
        PixelRect virtualScreen,
        int notches,
        bool horizontal)
    {
        return _clickSender.SendScroll(target, virtualScreen, notches, horizontal);
    }
}
