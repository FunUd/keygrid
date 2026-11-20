# Range-Then-Drag Workflow Plan

**Date**: 2026-11-21

**Status**: Implemented; real-application validation of the range endpoint as a drag origin remains a manual release gate
**Related plans**: [Drag-and-Drop Implementation Plan](./drag_and_drop_plan.md)

## Summary

Add a composite workflow that selects a range and then drags the selected content
to a drop target. The user starts it by pressing `R` and `D` simultaneously in
Stage 2. The chord captures the current target as the range anchor. After the
range is confirmed, its endpoint becomes the drag anchor; the user then selects
the drop target and confirms the drag.

The workflow reuses the existing range and drag actions. It does not introduce
another synthetic-input primitive or a new dependency.

## Feasibility and Value

### Feasibility

This is feasible with moderate UI-state and keyboard-input work:

- Range selection and drag dispatch already exist independently.
- `OverlayManager` owns the anchors and can preserve the workflow across the
  range completion and overlay reinitialization.
- The main new technical risk is recognizing an overlapping `R`+`D` key press
  without accidentally firing either single-key action first. The current key
  handler dispatches `R` and `D` on key-down and has no pressed-key state for
  this chord.

No changes to the Win32 drag sequence are expected. The composite action can
reuse `ExecuteRangeSelection` followed by `ExecuteDragSelection`.

### Value

The workflow is useful when the user needs to select content and then move that
same content, such as selecting browser text and dragging it to another
application. It avoids leaving selection mode and manually restarting the grid
between the two operations.

The selected range's endpoint is the agreed default drag anchor. This keeps the
flow concise, but some applications may treat a range endpoint as a caret
boundary rather than a point inside the selected content. Real-application
validation is therefore a release gate; if the endpoint cannot reliably start
a drag, the workflow will need a separate drag-start-point step.

## Proposed Interaction

1. In Stage 2, press and hold `R` and `D` so their key-down events overlap.
   The chord is accepted only when no range, drag, or Scroll Mode is active.
2. The chord captures the current Stage 2 target as the range anchor, enters
   Range-Then-Drag mode, and returns to the full Stage 1 grid.
3. Select the range endpoint region with Stage 1 keys, refine it with arrows,
   and press plain `Enter`. KeyGrid performs the existing range-selection
   sequence.
4. On successful range selection, KeyGrid reopens Stage 1 without clearing
   Range-Then-Drag mode. The exact range endpoint becomes the drag anchor and
   remains visible with the existing drag marker and `Drag: Enter to drop` badge.
5. Select the drop-target region with Stage 1 keys, refine it with arrows, and
   press plain `Enter` to perform the existing drag.
6. After the drag, normal mode exits and continuous mode follows its existing
   completion behavior. The composite workflow state is cleared.

Single `R` and `D` presses retain their existing behavior. When there is no
overlapping key-down, the pending single-key action is dispatched on key-up.
Stage 1 `R` and `D` remain ordinary selection keys and do not participate in
the chord.

### Cancellation and Failure

- `Esc` during the range phase clears the range anchor and composite-workflow
  state; selection remains open, and a second `Esc` exits as usual.
- A range validation or dispatch failure clears the composite-workflow state
  and follows the existing range failure recovery. The user can retry by
  starting the chord again.
- `Esc` during the drag phase clears the drag anchor and composite-workflow
  state; selection remains open.
- A drag failure follows existing drag recovery and clears the composite state.
- Focus loss, overlay teardown, and display-configuration changes clear any
  pending chord keys and composite-workflow state.
- The composite workflow cannot start or switch phases while Scroll Mode is
  active. Continuous-mode toggling is ignored while the composite workflow is
  active.

## Design and Implementation Steps

### 1. Validate the range endpoint as a drag origin

- Before implementing the composite state flow, manually reproduce the agreed
  sequence in a browser and a second target application: select a range, then
  drag from its endpoint into another location or window.
- Confirm that the pointer is still at the endpoint after range selection and
  that the application begins dragging the selected content rather than
  changing the selection.
- If this fails in a supported target, stop and ask whether to add an explicit
  D-start selection step. Do not silently substitute another anchor.

### 2. Add R+D chord recognition

**Files**: `OverlayKeyHandler.cs`, `OverlayWindow.xaml.cs`,
`IOverlayWindowSurface.cs`, and UI key-handler tests.

- Add a `RangeDragWorkflowRequested` event.
- Track pending `R`/`D` key-downs and consume the chord in either press order.
- Dispatch the existing individual `R` or `D` action on key-up if the other
  key was not pressed before release.
- Enable chord recognition only in eligible Stage 2 state. Keep Stage 1
  selection keys immediate and unchanged.
- Suppress repeats and modified chord attempts.
- Clear pending key state when focus is lost or the overlay is torn down.
- Test both chord orders, release orders, single-key fall-through, repeat and
  modifier behavior, and Stage 1 compatibility.

### 3. Add composite workflow state

**Files**: `OverlayManager.cs` and `OverlayManagerTests.cs`.

- Track whether the workflow is in its range phase or drag phase.
- On the chord event, capture the current Stage 2 target as `_rangeStartPoint`,
  return to the full Stage 1 grid, and keep the composite state active.
- Preserve the current range endpoint before completing the range action.
- After successful range dispatch, reopen the grid in Stage 1 and set the
  captured endpoint as `_dragStartPoint`.
- Keep the composite state through this reinitialization, then clear it after
  drag completion, cancellation, failure, focus loss, or display changes.
- Preserve existing behavior for standalone range and drag actions.
- Test state transitions, exact endpoint reuse, cancellation, failure recovery,
  continuous mode, and both single-key paths.

### 4. Render workflow status

**Files**: `OverlayRenderElement.cs`, `OverlayWindow.xaml.cs`, and rendering
tests.

- Show a distinct primary-overlay status during the range phase, such as
  `Range-to-Drag: Enter to select`.
- Reuse the existing range marker during selection.
- Reuse the existing drag marker and badge after the range completes.
- Verify that the marker is rendered on the monitor containing its physical
  coordinate, including non-primary monitors.

### 5. Update specifications and user documentation

**Files**: `spec.md`, `README.md`, and `CHANGELOG.md`.

- Document chord availability, two-phase state transitions, anchor reuse,
  cancellation, and failure behavior.
- Explain that single `R`/`D` actions are unchanged and that Stage 1 letters
  remain unaffected.
- Add the composite workflow to the keyboard-use instructions.

### 6. Validate behavior

- Add automated tests for key-event ordering and the full state sequence.
- Run `dotnet test --nologo` and `git diff --check`.
- After the endpoint-origin spike passes, manually validate a cross-window
  drop, both `R`/`D` press orders, monitor boundaries, continuous mode,
  cancellation, and focus loss.
- Do not claim the workflow is complete until the application recognizes the
  automatically reused range endpoint as a drag origin in the target
  applications. If it does not, stop and revise the source-point UX before
  implementing a fallback.

## Acceptance Criteria

- An overlapping `R`+`D` key-down chord in eligible Stage 2 state starts the
  composite workflow and captures the current target as the range anchor.
- A lone `R` or `D` still starts its existing action; Stage 1 `R` and `D`
  remain selection keys.
- Successful range selection keeps the workflow alive and sets the exact
  range endpoint as the drag anchor.
- The user can choose the drop target using Stage 1 and Stage 2 without losing
  the drag anchor.
- Plain `Enter` executes the drag; modified confirmations do not.
- Cancellation, failed input, focus loss, and display changes do not leave a
  stale anchor, pending key, or stuck workflow.
- Existing standalone range and drag tests and behaviors remain unchanged.
- Manual testing confirms the selected range endpoint can initiate a drag in
  the supported target applications.

## Risks

| Risk | Mitigation |
|---|---|
| Key-up deferral changes the response timing of standalone `R`/`D` actions | Limit deferral to eligible Stage 2 state; keep Stage 1 keys immediate and test single-key behavior |
| WPF does not deliver a matching key-up after focus changes | Clear pending chord state on focus loss and overlay teardown |
| Range endpoint is outside the draggable selection hit area | Treat real-application validation as a gate; revisit the drag-origin UX rather than silently sending a questionable drag |
| Range and drag state become inconsistent after overlay recreation | Keep a single explicit workflow phase and test each reset/cancel/failure path |
