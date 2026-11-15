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
/// Recovery is bounded to avoid fighting a permanently stolen focus.
/// </summary>
public sealed class FocusLossPolicy
{
    public const int MaxConsecutiveRecoveries = 3;

    private int _consecutiveRecoveries;

    public FocusLossAction Decide(uint foregroundProcessId, uint currentProcessId, bool foregroundIsVisible)
    {
        bool isOwnProcess = foregroundProcessId == currentProcessId;
        bool isHiddenHelper = !foregroundIsVisible;

        // Own process windows and invisible helpers (IME/TSF) are always transient.
        // They must not consume the recovery budget: otherwise frequent IME focus
        // changes would exhaust the budget (or eventually overflow the counter)
        // even though these losses are never deliberate user switches.
        if (isOwnProcess || isHiddenHelper)
        {
            return FocusLossAction.ReactivateOverlay;
        }

        // For visible foreign windows we still attempt recovery up to
        // MaxConsecutiveRecoveries times, because the first focus loss after the
        // overlay opens is typically caused by Windows returning focus to the
        // previously active window before SetForegroundWindow completes – not by
        // a deliberate user switch.
        if (_consecutiveRecoveries < MaxConsecutiveRecoveries)
        {
            _consecutiveRecoveries++;
            return FocusLossAction.ReactivateOverlay;
        }

        return FocusLossAction.CancelSelection;
    }

    /// <summary>
    /// Resets the recovery budget after the user successfully interacts with the overlay.
    /// </summary>
    public void Reset()
    {
        _consecutiveRecoveries = 0;
    }
}
