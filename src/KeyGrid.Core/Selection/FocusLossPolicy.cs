namespace KeyGrid.Core.Selection;

/// <summary>
/// Action to take when the primary overlay loses keyboard focus.
/// </summary>
public enum FocusLossAction
{
    /// <summary>
    /// The focus loss was not a user-initiated switch; reclaim focus and keep the selection.
    /// </summary>
    ReactivateOverlay,

    /// <summary>
    /// A visible foreign window received focus; discard the selection and close the overlays.
    /// </summary>
    CancelSelection
}

/// <summary>
/// Decides how a focus loss is handled.
///
/// Windows can move focus to a hidden helper window (for example the IME/TSF windows
/// "MSCTFIME UI" and "CiceroUIWndFrame") shortly after a topmost window is shown.
/// Treating that as a cancellation makes the overlay disappear about a second after it
/// opens, so hidden windows and windows owned by this process are treated as transient.
/// Visible foreign windows are treated as intentional focus changes and are never
/// reclaimed, preventing the overlay from pulling focus back from another application.
/// </summary>
public sealed class FocusLossPolicy
{
    public FocusLossAction Decide(uint foregroundProcessId, uint currentProcessId, bool foregroundIsVisible)
    {
        bool isOwnProcess = foregroundProcessId == currentProcessId;
        bool isHiddenHelper = !foregroundIsVisible;

        return isOwnProcess || isHiddenHelper
            ? FocusLossAction.ReactivateOverlay
            : FocusLossAction.CancelSelection;
    }
}
