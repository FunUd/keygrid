# Drag-and-Drop Implementation Plan

**Date**: 2026-11-21
**Source**: The drag-and-drop proposal supplied by the user
**Spec**: [spec.md](../spec.md)

## Summary

Add a cross-window and cross-monitor drag operation to KeyGrid's existing Stage 2
grid. The grid remains displayed across the full virtual desktop. A plain `D`
press in Stage 2 captures th　e resolved cursor as a drag anchor. `Enter` executes
the drag from the anchor to the current Stage 2 target. The implementation will
reuse the existing range-anchor lifecycle, status-badge, monitor-aware rendering,
overlay hiding, and partial-send recovery patterns.

This feature does not add a monitor-selection mode and does not impose the
same-top-level-window restriction used by range selection. The anchor and drop
target may belong to different windows or monitors.

## Technical Context

- **Language / runtime**: C# / .NET 10, WPF, Windows 11
- **Platform**: Win32 `SendInput` with `MOUSEEVENTF_VIRTUALDESK`
- **Primary dependencies**: Existing `SelectionManager`, `ClickExecutor`,
  `IClickSender`, `OverlayManager`, `OverlayKeyHandler`, and `OverlayRenderElement`
- **Storage**: None
- **Testing**: xUnit core/UI test projects and Windows manual integration tests
- **Target platform**: Per-Monitor DPI Awareness V2; virtual-screen coordinates
  may have negative origins
- **Constraints**:
  - Preserve the 128 DIP Stage 1 target and fixed 24 DIP / 600 DIP Stage 2 grid.
  - Preserve all-monitor overlays and primary-overlay keyboard ownership.
  - `SendInput` must release the synthetic left button even after partial failure.
  - No drag execution may send synthetic input across a monitor gap or to an
    unresolved target.
  - Administrative/UIPI restrictions must be reported, not silently ignored.

## Constitution Check

The repository constitution requires clear responsibilities, loose coupling,
Windows/Win32 isolation, specification updates with behavior changes, focused
validation, and explicit error handling. This plan:

- Keeps state in `SelectionManager`/`OverlayManager`.
- Keeps `SendInput` details in `Win32ClickSender`.
- Keeps monitor/DPI and coordinate conversion in existing Core/Interop boundaries.
- Requires implementation to update [spec.md](../spec.md) and directly related
  documentation in the same change.
- Requires focused unit tests and the manual cross-window/cross-monitor matrix.

No repository constitution or topology artifact was present during planning.

## Applied Guidelines

No matching migration guideline applies. Follow existing C# and WPF patterns:

- Reuse existing range/scroll sub-mode state transitions.
- Extend interfaces instead of introducing parallel mouse-input abstractions.
- Keep native input and cleanup logic in `Win32ClickSender`.
- Keep UI lifecycle and error reporting in `OverlayManager`.

## Requirements

The existing specification has no `REQ-XXX` identifiers. This plan assigns draft
feature requirement IDs for traceability; the implementation should add matching
IDs or equivalent section references when updating `spec.md`.

- **REQ-DND-001**: D is accepted only in Stage 2 and sets a drag anchor.
- **REQ-DND-002**: The drag grid remains all-monitor and uses the existing Stage 2 cursor.
- **REQ-DND-003**: Cross-window and cross-monitor drag targets are allowed.
- **REQ-DND-004**: Enter executes only plain unmodified Enter while drag mode is active.
- **REQ-DND-005**: The drag anchor remains visible across cursor movement and Backspace.
- **REQ-DND-006**: SendInput performs move, left-down, movement to the drop target,
  and left-up.
- **REQ-DND-007**: Partial SendInput failure releases the synthetic left button.
- **REQ-DND-008**: Drag mode follows normal/continuous mode completion rules.
- **REQ-DND-009**: Drag and range/Scroll modes are mutually exclusive.
- **REQ-DND-010**: Drag UI is monitor-aware and displays only on the appropriate overlays.
- **REQ-DND-011**: Drag failures are explicit and restore the grid or normal mode
  according to the completion state.

## State and Key Rules

- Stage 1 keeps D as a normal column key and routes it through `ProcessKey('D')`.
  D never starts drag mode before the Stage 1 history reaches stage 2.
- In Stage 2, plain, non-repeat D starts or changes `_dragStartPoint` only once.
  Modified D combinations are consumed and ignored. Repeated D is ignored.
- Plain Enter executes drag while `_dragStartPoint` is active. Shift/Ctrl Enter,
  range start, scroll start, continuous-mode toggle, and confirmation keys are
  rejected or ignored according to existing sub-mode rules.
- Backspace continues to undo the most recent Stage 1 key only after returning
  from Stage 2. An active drag anchor is preserved. There is no Backspace movement
  history inside the Stage 2 cursor.
- Esc clears `_dragStartPoint` first and keeps Stage 2 active. A second Esc exits
  selection mode normally. Drag never changes continuous mode.
- Tab, R, and Page keys are ignored while drag mode is active. Range and Scroll
  mode cannot start until the drag anchor is cleared or the drag is completed.
- A successful drag follows normal or continuous-mode completion. A target,
  activation, or SendInput failure clears the drag anchor and restores Stage 2;
  it does not call `CompleteClick` or advance continuous mode to a new Stage 1
  session. The existing continuous-mode flag is preserved for a later retry.

## Implementation Steps

### Step 1: SendInput drag primitive

- **Requirements**: REQ-DND-003, REQ-DND-006, REQ-DND-007, REQ-DND-011
- **Design inputs**: `IClickSender`, `ClickExecutor`, `Win32ClickSender`,
  existing range partial-send recovery
- **Description**:
  - Add `SendDrag(PixelPoint start, PixelPoint end, PixelRect virtualScreen)`
    to `IClickSender`.
  - Implement a Win32 sequence that:
    1. moves to the drag anchor,
    2. sends left-button down,
    3. moves through intermediate physical points to the drop target,
    4. sends left-button up.
  - Normalize every point against the virtual-screen bounding box.
  - Keep direct, interpolated movement behind a small drag-specific native
    implementation; do not change normal click or range-send behavior.
  - If SendInput is partial or throws, send an absolute left-button-up at the
    last endpoint and report failure.
  - Log start/end coordinates, virtual-screen bounds, requested input count,
    sent input count, and last Win32 error at existing diagnostic levels.
  - Implement native send and cleanup with `try/finally` semantics. Track the last
    endpoint reached by the send sequence and issue a left-up cleanup at that
    endpoint after partial sends or exceptions. Cleanup failure is logged and
    surfaced explicitly rather than converted into success.

### Step 2: Drag execution service

- **Requirements**: REQ-DND-006, REQ-DND-008, REQ-DND-011
- **Design inputs**: `ClickExecutor`, `OverlayManager`, existing
  `ExecuteRangeClick`/`ExecuteScroll`
- **Description**:
  - Add `ExecuteDrag` to `ClickExecutor` with the existing hide, render-flush,
    dispatch, and success-return pattern.
  - Add drag dispatch to `OverlayManager`.
  - Hide overlays before locating both coordinates and sending input.
  - Do not activate or validate a shared top-level window; a drag is explicitly
    allowed to cross windows.
  - Resolve the source window independently and verify that it owns the anchor
    after hiding and activating the source. Do not activate a shared top-level
    window for the target.
  - Verify that both endpoints and every emitted intermediate interpolation point
    belong to the union of monitor rectangles and not monitor gaps. Reject the
    drag before SendInput if the source or a required movement segment is invalid.
  - Restore Stage 2 on target-validation or SendInput failure if the session
    remains active, clear `_dragStartPoint`, report the error, and do not call
    `CompleteClick`. Use normal/continuous completion only on success.
  - Preserve the originating foreground window behavior for continuous restart,
    but do not send it a synthetic click in place of the drag target.

### Step 3: Stage 2 drag state and key handling

- **Requirements**: REQ-DND-001, REQ-DND-002, REQ-DND-004, REQ-DND-005,
  REQ-DND-008, REQ-DND-009
- **Design inputs**: `OverlayKeyHandler`, `SelectionManager`, `OverlayManager`,
  existing range and Scroll Mode state
- **Description**:
  - Add `DragStartRequested` to `OverlayKeyHandler` and `IOverlayWindowSurface`.
  - Map plain `D` to the drag-start event and reject Shift/Ctrl/Alt/Windows,
    repeat presses, and D presses in Stage 1.
  - In Stage 1, retain D as the existing disjoint column key because D is not a
    drag command until Stage 2.
  - Add `_dragStartPoint` to `OverlayManager`, matching `_rangeStartPoint`.
  - Pressing D in Stage 2 captures `TryGetStage2Target`; repeated D is ignored.
  - Plain Enter executes drag, while Shift/Ctrl Enter and other confirmation
    modifiers are ignored while drag is active.
  - Preserve the drag anchor through Stage 2 cursor movement and Backspace.
  - Esc first clears drag, then Esc exits selection. Do not use drag anchor state
    for Stage 1 selection state.
  - Reject drag start while range or Scroll Mode is active. Reject range/scroll/
    continuous toggle while drag is active consistently with existing sub-modes.

### Step 4: Monitor-aware drag rendering

- **Requirements**: REQ-DND-002, REQ-DND-005, REQ-DND-010
- **Design inputs**: `OverlayRenderElement`, `IOverlayWindowSurface`,
  existing `DrawRangeStartMarker`
- **Description**:
  - Draw the same green ring and crosshair at `_dragStartPoint` that range uses,
    but label it visually as drag state to avoid ambiguity.
  - Render the marker only on the monitor containing the physical anchor.
  - Show `Drag: Enter to drop` only on the primary overlay.
  - Reuse the existing status-badge ordering and overlay render-state plumbing.
  - Keep drag/range/Scroll indicators mutually exclusive.

### Step 5: Technical validation and interpolation

- **Requirements**: REQ-DND-006, REQ-DND-011
- **Design inputs**: Existing test applications and Windows SendInput behavior
- **Description**:
  - Phase 1 validation is a bounded runtime spike before the final native path
    is declared complete:
    - direct move/down/move/up,
    - intermediate-point interpolation,
    - short movement intervals while preserving deterministic input counts.
  - Validate Explorer file drag, Chrome/Edge browser text or tab drag, and VS Code
    drag behavior.
  - Cross-window drag is the primary acceptance scenario.
  - If direct two-point movement is not recognized by a representative app,
    retain interpolation. Otherwise keep the simpler direct path.
  - Confirm whether a per-step delay is required; if so, implement it in a testable
    abstraction and not as a UI-thread sleep.
  - The runtime spike is a blocking prerequisite for native implementation.
  - T003 is not complete until the spike has selected and documented one exact
    movement strategy: direct movement or a fixed/Bresenham-style interpolation
    algorithm, input count, step delay, and last-point behavior.
  - The chosen strategy must be recorded in `spec.md` and asserted deterministically
    by `Win32ClickSenderTests` before native implementation is approved.

### Step 6: Automated tests

- **Requirements**: All
- **Design inputs**: Existing unit/UI test patterns and fake senders
- **Description**:
  - Extend `IClickSender` test doubles for `SendDrag`.
  - Add Win32 tests for normalized start/end points, left-down/left-up flags,
    interpolation boundaries, and partial-send cleanup.
  - Add key-handler tests for plain D, Shift/Ctrl D, repeats, and Stage 2 only.
  - Add `OverlayManager` tests for anchor persistence, cross-window dispatch,
    cross-monitor anchors, Esc behavior, continuous mode, and SendInput failure.
  - Add rendering tests for the green drag marker and primary-only badge.
  - Add tests for partial sends at every drag sequence position, `_sendInput`
    exceptions, cleanup failure, negative virtual-screen origins, monitor-gap
    endpoint/segment rejection, plain/modified/repeated D in both stages,
    Tab/R/Page while drag is active, and continuous-mode drag failure.
  - Add manual elevated-window/UIPI cases and verify that source/target hit-testing
    is evaluated after overlays are hidden.

### Step 7: Specification and documentation

- **Requirements**: REQ-DND-001 through REQ-DND-011
- **Design inputs**: This plan and implemented behavior
- **Description**:
  - Update `spec.md` Sections 3.2, 3.4, 3.5, 6.3, 8.2, 13.3, 14, and 16.
  - Add D to the Stage 2 key table and specify that D remains a Stage 1 column key.
  - State that drag endpoints need not share a top-level window.
  - Document interpolation or direct movement as accepted behavior after the
    runtime spike.
  - Document partial-send left-button cleanup and UIPI/foreground failures.
  - Update `README.md`, `CHANGELOG.md`, and `docs/implementation_plan.md`.

## Project Structure

```text
src/
  KeyGrid.Core/
    Abstractions/IClickSender.cs
    Services/ClickExecutor.cs
  KeyGrid/
    Interop/NativeMethods.cs
    Interop/Win32ClickSender.cs
    UI/IOverlayWindowSurface.cs
    UI/OverlayKeyHandler.cs
    UI/OverlayManager.cs
    UI/OverlayRenderElement.cs
tests/
  KeyGrid.Tests/
    ServicesTests.cs
  KeyGrid.UI.Tests/
    OverlayKeyHandlerTests.cs
    OverlayManagerTests.cs
    Win32ClickSenderTests.cs
    OverlayRenderElementTests.cs
docs/
  drag_and_drop_plan.md
  checkpoints/spec-to-plan.yaml
  checkpoints/plan-to-tasks.yaml
```

## Testing Strategy

- **appType**: Desktop WPF application
- **Critical user journeys**:
  1. Drag between two Explorer windows or from Explorer to a browser.
  2. Drag a browser tab or text element to another window.
  3. Drag between left and right monitors with a negative virtual-screen origin.
  4. Enter and exit drag, including Backspace and Esc.
  5. Drag in continuous mode and recover from SendInput failure.
- **primaryValidationStack**: Automated xUnit core/UI tests plus real Windows
  applications and multiple-monitor manual validation.
- **fallbackMatrix**:
  - **native-tier**: Win32 `SendInput` and real applications
  - **automated-tier**: Fake `IClickSender`/window-focus implementations
  - **knownGaps**: Fakes cannot prove OS drag recognition or UIPI behavior.
- **Environment requirements**: Windows 11, two monitors preferred, Explorer,
  Chrome/Edge, and VS Code.
- **Test data strategy**: Use disposable temporary files and browser text/tabs;
  do not require repository changes to serve as test fixtures.
- **Acceptance criteria**:
  - Plain D works only in Stage 2.
  - The green anchor is exact and monitor-owned.
  - Enter sends move/down/intermediate moves/up.
  - Cross-window and cross-monitor targets succeed in the manual matrix.
  - No monitor-gap target or partial failure remains with the left button down.
  - Normal and continuous completion match existing click behavior.

## Task Breakdown

### Phase 1: Setup and technical validation

- [x] T001 [Plan:1.1] Add `SendDrag` to `src/KeyGrid.Core/Abstractions/IClickSender.cs` and the fake senders used by `tests/KeyGrid.Tests/ServicesTests.cs` and `tests/KeyGrid.UI.Tests/OverlayManagerTests.cs`. **Dependencies:** none.
- [x] T002 [Plan:1.1,5.1] Add a one-purpose drag primitive test harness in `tests/KeyGrid.UI.Tests/Win32ClickSenderTests.cs`; compare direct versus interpolated send sequences. **Dependencies:** T001.
- [ ] T003 [Plan:5.1] Run and diagnose the runtime drag spike against Explorer, Chrome/Edge, and VS Code; choose direct or interpolated movement and record the exact input-count, delay, and last-point behavior. **Dependencies:** T002.

### Phase 2: Foundational drag dispatch

- [x] T004 [Plan:1.1] Implement `Win32ClickSender.SendDrag` in `src/KeyGrid/Interop/Win32ClickSender.cs`, including virtual-screen normalization, interpolation/segment validation, and exception-safe left-button cleanup. **Dependencies:** T001, T003.
- [x] T005 [Plan:2.1] Implement `ClickExecutor.ExecuteDrag` in `src/KeyGrid.Core/Services/ClickExecutor.cs`. **Dependencies:** T001, T004.
- [x] T006 [Plan:2.1] Add independent source-window activation and source-hit verification to `src/KeyGrid/UI/OverlayManager.cs`, without same-window validation for the drop target. **Dependencies:** T003, T004, T005.

### Phase 3: Drag state and key handling

- [x] T007 [US1] [Plan:3.1] Add `DragStartRequested` to `src/KeyGrid/UI/OverlayKeyHandler.cs` and `src/KeyGrid/UI/IOverlayWindowSurface.cs`. **Dependencies:** T001.
- [x] T008 [US1] [Plan:3.1] Map plain Stage 2 D in `src/KeyGrid/UI/OverlayKeyHandler.cs`; preserve D as a Stage 1 key and consume modified/repeated D. **Dependencies:** T007.
- [x] T009 [US1] [Plan:3.1] Add `_dragStartPoint`, exact Esc/Backspace/Tab/R/Page lifecycle, mutual exclusion, and confirmation handling to `src/KeyGrid/UI/OverlayManager.cs`. **Dependencies:** T006, T008.
- [x] T010 [US1] [Plan:3.1,2.1] Implement cross-window/cross-monitor endpoint and path validation and dispatch in `src/KeyGrid/UI/OverlayManager.cs`. **Dependencies:** T005, T006, T009.

### Phase 4: Drag rendering

- [x] T011 [US1] [Plan:4.1] Extend the existing marker API in `src/KeyGrid/UI/OverlayRenderElement.cs` to distinguish and draw the drag anchor. **Dependencies:** T009.
- [x] T012 [US1] [Plan:4.1] Add `Drag: Enter to drop` to primary-only status badges in `src/KeyGrid/UI/OverlayRenderElement.cs`. **Dependencies:** T009.

### Phase 5: Automated and manual verification

- [ ] T013 [US1] [Plan:6.1] Add Win32 drag input, interpolation, partial-send, exception, cleanup-failure, and negative-origin tests in `tests/KeyGrid.UI.Tests/Win32ClickSenderTests.cs`. **Dependencies:** T003, T004.
- [x] T014 [US1] [Plan:6.1] Add plain-D, repeat, and modifier key tests in `tests/KeyGrid.UI.Tests/OverlayKeyHandlerTests.cs`. **Dependencies:** T008.
- [ ] T015 [US1] [Plan:6.1] Add drag anchor, Enter, Esc, Backspace, range/Scroll exclusion, gap rejection, and continuous-mode tests in `tests/KeyGrid.UI.Tests/OverlayManagerTests.cs`. **Dependencies:** T009, T010.
- [ ] T016 [US1] [Plan:6.1] Add drag marker and status-badge tests in `tests/KeyGrid.UI.Tests/OverlayRenderElementTests.cs`. **Dependencies:** T011, T012.
- [x] T017 [US1] [Plan:6.1] Add core drag dispatch coverage to `tests/KeyGrid.Tests/ServicesTests.cs`. **Dependencies:** T005.
- [ ] T018 [US1] [Plan:5.1] Execute the manual cross-window/cross-monitor/UIPI matrix and record pass/fail evidence. **Dependencies:** T010, T011, T012.

### Phase 6: Specification and polish

- [x] T019 [Plan:7.1] Update drag-related behavior and acceptance criteria in `spec.md`, including the selected interpolation decision and REQ-DND IDs. **Dependencies:** T003, T018.
- [x] T020 [Plan:7.1] Update usage and feature descriptions in `README.md`. **Dependencies:** T019.
- [x] T021 [Plan:7.1] Update `CHANGELOG.md` and `docs/implementation_plan.md`. **Dependencies:** T019.
- [x] T022 [Plan:7.1] Run `dotnet test --nologo` and `git diff --check`; update the traceability checkpoints with test/evidence links. **Dependencies:** T013-T021.

## Requirement Mapping

| REQ ID | Description | Plan Items | Implementation Evidence |
|--------|-------------|------------|-------------------------|
| REQ-DND-001 | Stage 2 plain D sets a drag anchor | 3.1 | OverlayKeyHandler, OverlayManager |
| REQ-DND-002 | Drag remains in the all-monitor Stage 2 grid | 3.1, 4.1 | OverlayManager, OverlayRenderElement |
| REQ-DND-003 | Cross-window/cross-monitor targets are allowed | 1.1, 2.1 | IClickSender, Win32ClickSender, OverlayManager |
| REQ-DND-004 | Plain Enter executes drag and modified Enter is ignored | 3.1 | OverlayKeyHandler, OverlayManager |
| REQ-DND-005 | Anchor persists across cursor movement and Backspace | 3.1, 4.1 | OverlayManager, OverlayRenderElement |
| REQ-DND-006 | SendInput down/move/up sequence is generated | 1.1, 2.1, 5.1 | Win32ClickSender, ClickExecutor |
| REQ-DND-007 | Partial SendInput releases left button | 1.1 | Win32ClickSender cleanup |
| REQ-DND-008 | Drag follows normal/continuous completion | 2.1, 3.1 | OverlayManager |
| REQ-DND-009 | Drag, range, and Scroll are mutually exclusive | 3.1 | OverlayManager state |
| REQ-DND-010 | Drag UI is rendered by the owning monitor and primary badge | 4.1 | OverlayRenderElement |
| REQ-DND-011 | Failures are explicit and recover the grid | 1.1, 2.1, 5.1 | Win32ClickSender, OverlayManager, FileLogger |
