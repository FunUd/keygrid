namespace KeyGrid.Core.Selection;

/// <summary>
/// Represents the operating state of KeyGrid according to spec.md Section 8.1.
/// </summary>
public enum SelectionState
{
    /// <summary>
    /// Waiting for hotkey. Overlay is hidden.
    /// </summary>
    Standby,

    /// <summary>
    /// Overlay is visible, actively receiving keystrokes (0 to 3 keystrokes in history).
    /// </summary>
    Selecting,

    /// <summary>
    /// 4 keystrokes completed (stage 2 complete). Waiting for Enter confirmation.
    /// Crosshair marker is displayed.
    /// </summary>
    AwaitingConfirmation,

    /// <summary>
    /// Enter pressed. Overlay is hidden and click is being dispatched via SendInput.
    /// </summary>
    ExecutingClick
}
