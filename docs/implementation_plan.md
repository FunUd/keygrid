# KeyGrid Implementation Plan & Test Strategy

This document outlines the step-by-step implementation plan and test architecture for **KeyGrid** as specified in [spec.md](../spec.md).

---

## 1. Architecture & Design Principles

KeyGrid is designed around strict separation of concerns to maximize testability, extensibility, and maintainability (as required by `AGENTS.md` and `spec.md` Section 13):

1. **Pure Domain Core (`KeyGrid.Core`)**:
   - Zero dependencies on Win32, WPF, or UI frameworks.
   - Contains all geometric, algorithmic, and state-machine logic:
     - Division count calculation (128 DIP target converted with the DPI reference monitor DPI, $C + R \le 26$).
     - Cell boundary partitioning without gaps or overlaps.
     - Multidimensional region intersection & bounding box tracking.
     - Euclidean nearest-point selection when the center falls into non-monitor space.
     - Disjoint column/row key allocation from physical keyboard blocks (max 26 keys).
     - Keystroke history, undo/recalculation, and state transitions.
     - Focus-loss recovery policy for hidden IME/TSF helper windows.
     - Fixed Stage-2 window calculation centered on the Stage-1 reference point.
     - Absolute virtual desktop coordinate normalization ($0 \dots 65535$).
   - Can be unit-tested completely in headless environments without UI threads.

2. **Hardware / OS Abstraction Layer (`KeyGrid.Core.Abstractions`)**:
   - `IMonitorProvider`: Abstract representation of monitor bounds, effective DPI scale, primary-monitor flag, and DPI/topology change events.
   - `IClickSender`: Abstract interface to send mouse clicks to the OS.
   - `IHotkeyRegistrar`: Abstract interface to register system-wide hotkeys.
   - Enables complete integration testing with mock/fake multi-monitor configurations (negative origin, DPI differences, disjoint gaps).

3. **WPF Overlay & Windows UI (`KeyGrid`)**:
   - WPF application targeting Windows 11 (`net10.0-windows` with Per-Monitor DPI Awareness V2).
   - `NativeMethods`: Strict encapsulation of Win32 P/Invoke (`User32`, `Imm32`, `Shell32`).
   - `OverlayWindow`: Lightweight transparent layered window (`WS_EX_TOPMOST`, `WS_EX_TOOLWINDOW`).
   - `OverlayManager`: Coordinates primary input overlay and secondary display-only overlays.
   - `TrayManager`: System tray presence, context menu, and balloon notifications for errors.
   - Single-instance mutex and lifecycle cleanup handler.

---

## 2. Test Architecture & Methodology

### 2.1 Test Levels

| Test Level | Scope | Tech Stack | Execution Speed |
|---|---|---|---|
| **Unit Tests** | Domain calculation, boundary formulas, state transitions, key mapping | xUnit, FluentAssertions / Assert | < 2 seconds |
| **Component / Integration Tests** | Virtual multi-monitor topology, Backspace replay, Euclidean distance tie-breaking | xUnit with Fakes (`FakeMonitorProvider`, `FakeClickSender`) | < 3 seconds |
| **Verification Spike Harness** | Win32 SendInput coordinate accuracy, IME suppression, mouse interception | Test verification window / Spike tool | Manual / Assisted |

### 2.2 Key Test Scenarios & Test Matrix

1. **`GridCalculatorTests`**:
   - **Division formula**: Verify the 128 DIP target converted by the DPI reference monitor DPI with $C + R \le 26$; check 1920x1080 (15, 8), 2560x1440 (16, 10), 3840x2160 (16, 10), and the coarsened 7680x2160 (20, 6) and 7680x4320 (16, 10) cases.
   - **Boundary partitioning theorem**: For all $L \in [1, 10000]$ and $N \in [1, 26]$, verify cell widths are strictly $\lfloor L/N \rfloor$ or $\lceil L/N \rceil$, sum of widths equals $L$, no overlapping or gaps.
   - **Two-stage convergence**: Verify the Stage 1 optimizer targets 128 DIP cells, while Stage 2 centers a fixed 600 DIP window (25x25 cells of 24 DIP each) around the Stage 1 cell's reference point. The deterministic target search chooses the minimax pair under the 26-key budget on ultra-wide displays (for example 20x6 for 7680x2160).
   - **Center point & Euclidean distance**:
     - Bounding box center: `left + (W - 1) / 2` (integer division).
     - When center falls in monitor gap: nearest coordinate in valid monitor rects; tie-breaker test (smaller Y first, then smaller X).
2. **`KeyLayoutTests`**:
   - Left column block (`Q W E R T A S D F G Z X C V B`) and right row block (`Y U I O P H J K L N M`).
   - Deterministic allocation, including C > 15 and R > 11 borrowing, for all valid count pairs.
   - Disjoint sets, case-insensitive membership, cross-set rejection, and invalid count validation.
3. **`SelectionManagerTests`**:
   - Complete state machine coverage (Standby, Selecting, ExecutingClick).
   - Keystroke history progression (0 -> 1 -> 2 -> 3 -> 4 keys), with the 4th key starting the click immediately.
   - Column/row key-set separation at both stages and Backspace replay with the same allocation.
   - Enter at step 2 (early click at the stage 1 reference point).
   - Enter ignored at steps 0, 1, 3, and after the 4th key.
   - Backspace: pops key and replays selection history to re-derive active bounding box.
   - Cancel via Esc, focus loss to a visible foreign window, or monitor change resets to Standby.
   - Hidden/own-process focus loss reactivates the overlay; repeated recovery failures cancel.
   - `FocusLossPolicyTests`: transient recovery, visible foreign cancellation, recovery limit, reset.
4. **`Stage2ContextGridTests`**:
   - The 25x25 window centers around the Stage 1 cell's reference point with exact 24 DIP cell sizes.
   - Context cells on any monitor remain selectable, while cells outside monitor areas are skipped.
   - Holding an arrow key repeats cursor movement; repeats stay ignored in Stage 1 and Scroll Mode.
   - The selected Stage 1 cell remains the dim-mask focus and is outlined; the fixed Stage 2 window uses a lighter border.
5. **`CoordinateConverterTests`**:
   - Normalization formula $X = (x - x_{min}) \times 65535 / (W - 1)$.
   - Negative origin configurations (secondary monitor placed left or top of primary).
6. **Phase 1 Spike Validation**:
   - Accuracy verification window registering click events and verifying 0px delta.

---

## 3. Work Breakdown & Implementation Checklist

### Phase 0: Project Foundation & Core Infrastructure
- [x] Initialize Solution and Project Structure:
  - [x] Create `src/KeyGrid.Core` (.NET 10 Class Library, pure logic).
  - [x] Create `src/KeyGrid` (.NET 10 WPF Application, `net10.0-windows`).
  - [x] Create `tests/KeyGrid.Tests` (.NET 10 xUnit test project).
- [x] Configure `app.manifest` in `KeyGrid` for Per-Monitor DPI Awareness V2 (`dpiAwareness` = `PerMonitorV2`).
- [x] Implement Geometric Primitives in `KeyGrid.Core`:
  - [x] `PixelPoint` (X, Y in integer physical pixels).
  - [x] `PixelRect` (Left, Top, Width, Height, Right, Bottom with half-open $[L, R) \times [T, B)$ semantics).
- [x] Setup CI / Local Test Scripts (`dotnet test`).

### Phase 1: Technical Verification (Spikes & OS Boundary Prototypes)
- [x] Implement Win32 P/Invoke Layer (`NativeMethods.cs`):
  - [x] Monitor enumeration (`EnumDisplayMonitors`, `GetMonitorInfo`).
  - [x] Window sizing & positioning (`SetWindowPos`, `SWP_NOACTIVATE`, `SWP_SHOWWINDOW`).
  - [x] Hotkey registration (`RegisterHotKey`, `UnregisterHotKey`).
  - [x] Input simulation (`SendInput`, `MOUSEEVENTF_ABSOLUTE`, `MOUSEEVENTF_VIRTUALDESK`).
  - [x] IME control (`ImmAssociateContext`, `ImmGetContext`, `ImmReleaseContext`).
- [x] Spike 1: Transparent Overlay & Mouse Blocking:
  - [x] Create topmost fullscreen window covering all displays.
  - [x] Verify background apps do not receive hover/click events.
- [x] Spike 2: Focus & Keyboard Input:
  - [x] Verify `RegisterHotKey` opens overlay and grants keyboard focus.
  - [x] Verify IME is disabled and alphabetic keys arrive as raw virtual key codes.
- [x] Spike 3: SendInput Click Accuracy:
  - [x] Implement `ClickVerificationHarness` to verify physical click coordinates match target within 0px offset across multiple DPI scalings (100%, 125%, 150%, 200%).
- [x] Spike 4: Taskbar Coverage & Dispatcher Synchronization:
  - [x] Verify overlay covers taskbar (both standard and auto-hide).
  - [x] Verify overlay hide animation/render loop completes before `SendInput` fires to prevent self-clicks.
- [x] **Go/No-Go Review**: Evaluate Phase 1 checklist against spec Section 15.

### Phase 2: Core Domain Logic & Single-Monitor Implementation
- [x] Implement `KeyLayout`:
  - [x] Disjoint left column block and right row block allocation, including overflow borrowing.
  - [x] Membership resolvers for column and row keys.
  - [x] Unit tests for `KeyLayout` and all valid count pairs.
- [x] Implement `GridCalculator`:
  - [x] Division count calculation ($C$ and $R$ bounded by 26).
  - [x] Boundary calculation ($k \times L + N - 1) / N$).
  - [x] Cell division and selection region intersection.
  - [x] Center reference point and nearest-pixel resolution.
  - [x] Unit tests for all calculator rules.
- [x] Implement `SelectionManager`:
  - [x] State transition logic (Standby, Selecting, ExecutingClick).
  - [x] History management (append keystroke, Backspace pop and recalculate).
  - [x] Enter key validation (stage 1 early click); stage 2 clicks automatically on the 4th key.
  - [x] Unit tests for `SelectionManager`.
- [x] Implement `CoordinateConverter`:
  - [x] Mapping physical pixel $(x, y)$ to normalized absolute mouse coordinates $(0 \dots 65535)$.
  - [x] Unit tests for `CoordinateConverter`.
- [x] Implement Single-Monitor Overlay UI:
  - [x] Custom rendering for grid lines and bounding rectangle.
  - [x] Stage 1 in-cell 2-letter labels and the Stage 2 focus-plus-context grid.
  - [x] Column highlight after the first Stage 1 stroke and cursor-cell highlight in Stage 2.
  - [x] Immediate click dispatch when stage 2 completes (no confirmation screen).
  - [x] Keyboard input binding for the allocated column/row sets (Backspace, Enter, Esc).
- [x] Implement Stage 2 context rendering:
  - [x] Center the fixed 25x25 window on the Stage 1 reference point using exact 24 DIP cells.
  - [x] Keep the selected Stage 1 cell bright and outline it; dim context outside it.
  - [x] Clip context cells and grid lines at monitor edges and skip invalid cells.
  - [x] Allow held-arrow repeat only while the Stage 2 cursor is active.
- [x] Implement `ClickExecutor`:
  - [x] Dispatcher flush before `SendInput`.
  - [x] Left-click dispatch via `SendInput`.
  - [x] End-to-end single monitor manual verification.

### Phase 3: Multi-Monitor & DPI Architecture
- [x] Implement `Win32MonitorProvider`:
  - [x] Enumerate all active displays and query physical pixel coordinates.
  - [x] Query effective DPI and the `MONITORINFOF_PRIMARY` flag per monitor.
  - [x] Derive the Stage 1 128 DIP target and fixed Stage 2 24 DIP cell from the foreground monitor's effective DPI, falling back to the primary and then first monitor.
  - [x] Compute virtual desktop bounding box.
  - [x] Expose monitor change events (`WM_DISPLAYCHANGE`, `WM_DPICHANGED`), including overlay-window notifications.
- [x] Multi-Monitor Selection Support in `GridCalculator`:
  - [x] Handling disjoint monitor bounds (exclude empty space between monitors).
  - [x] Euclidean nearest distance fallback when center point falls in monitor gap.
  - [x] Unit tests with virtual multi-monitor layouts (L-shaped, vertical stack, horizontal gap, negative coordinates).
- [x] Multi-Monitor Overlay Architecture:
  - [x] Primary overlay window on primary display (handles keyboard focus & input).
  - [x] Display-only secondary overlay windows on remaining displays.
  - [x] Label dispatching: draw each Stage 1 label on exactly one owning monitor overlay.
  - [x] Window positioning via Win32 `SetWindowPos` in physical pixels.

### Phase 4: System Tray, Lifecycle, and Resilience
- [x] Implement Single Instance Mutex:
  - [x] Named mutex `KeyGrid_SingleInstance_Mutex`.
  - [x] Detect existing instance and show balloon notification before exiting.
- [x] Implement `HotkeyManager`:
  - [x] Hidden message-only window or hook for `WM_HOTKEY`.
  - [x] Register `Ctrl + Shift + Space`.
  - [x] Graceful notification on hotkey conflict.
- [x] Implement `TrayManager`:
  - [x] System tray icon with context menu (Status, Exit).
  - [x] Native tray menu that dismisses on outside click or Esc.
  - [x] Notification balloon display on registration failure or UIPI send failure.
- [x] Implement Focus & System Event Watchers:
  - [x] Reclaim focus on transient loss to hidden IME/TSF or own-process windows.
  - [x] Cancel selection on focus loss to a visible foreign window.
  - [x] Cancel selection on display setting change (`WM_DISPLAYCHANGE`, `WM_DPICHANGED`).
  - [x] Ensure a second instance's notification window never steals focus from an active overlay.
  - [x] Clean restoration of IME context and window destruction.
- [x] Implement File Logging:
  - [x] File logger writing to `%LOCALAPPDATA%\KeyGrid\logs`.
  - [x] Automatic cleanup of log files older than 7 days.
- [x] UIPI & SendInput Verification:
  - [x] Detect SendInput failure and show diagnostic tray balloon.

### Phase 5: Verification, Benchmarking, and Packaging
- [x] Run full automated test suite (`dotnet test`).
- [ ] Execute Performance Benchmarks:
  - [ ] Overlay appearance latency $< 200$ ms.
  - [ ] Keypress to render update latency $< 100$ ms.
  - [ ] Idle CPU usage $< 0.1\%$, memory working set $< 100$ MB.
- [x] Full Checklist Verification against `spec.md` Section 16 (Completion Criteria).
- [x] Release Publishing:
  - [x] Configure and verify framework-dependent release publishing (`dotnet publish src/KeyGrid/KeyGrid.csproj -c Release -r win-x64 --self-contained false -o .dist`).

### Phase 6: Continuous Mode
- [x] Keep the selection session alive after a click and restart from Stage 1.
- [x] Toggle continuous mode with `Tab` and show a simple primary-monitor badge.
- [x] Reset continuous mode on `Esc`, hotkey restart, focus loss, or display change.
- [x] Reopen the grid after a failed click dispatch and report the error.
- [x] Add key-handler, overlay-manager, and render tests for the new behavior.

### Phase 7: Fixed-Size Stage 2 Grid
- [x] Add target-based Stage 1 sizing and deterministic tie-breaking under the 26-key budget.
- [x] Resolve the DPI reference monitor from the foreground window, with primary and first-monitor fallbacks.
- [x] Use virtual desktop bounds for click, range, and scroll coordinate normalization.
- [x] Use a fixed 25x25 Stage 2 window with exact 24 DIP cells and preserve the Stage 1 cell as the visual focus.
- [x] Update the specification, README, changelog, and implementation plan to the implemented geometry.
- [x] Run the complete automated test suite (`dotnet test --nologo`).
- [ ] Run the physical multi-monitor and DPI manual matrix; not run in this environment.
