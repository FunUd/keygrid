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
    /// Target resolved by a confirmation key after stage 1 or in stage 2.
    /// Overlay is hidden and the click is being dispatched via SendInput.
    /// </summary>
    ExecutingClick
}
