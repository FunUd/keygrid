# KeyGrid Implementation Plan & Test Strategy

This document outlines the step-by-step implementation plan and test architecture for **KeyGrid** as specified in [spec.md](../spec.md).

---

## 1. Architecture & Design Principles

KeyGrid is designed around strict separation of concerns to maximize testability, extensibility, and maintainability (as required by `AGENTS.md` and `spec.md` Section 13):

1. **Pure Domain Core (`KeyGrid.Core`)**:
   - Zero dependencies on Win32, WPF, or UI frameworks.
   - Contains all geometric, algorithmic, and state-machine logic:
     - Division count calculation ($10 \times N^2 \ge L$, max 26).
     - Cell boundary partitioning without gaps or overlaps.
     - Multidimensional region intersection & bounding box tracking.
     - Euclidean nearest-point selection when the center falls into non-monitor space.
     - Key sequence mappings (QWERTY layout order, max 26 keys).
     - Keystroke history, undo/recalculation, and state transitions.
     - Absolute virtual desktop coordinate normalization ($0 \dots 65535$).
   - Can be unit-tested completely in headless environments without UI threads.

2. **Hardware / OS Abstraction Layer (`KeyGrid.Core.Abstractions`)**:
   - `IMonitorProvider`: Abstract representation of monitor bounds and DPI/topology change events.
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
   - **Division formula**: Verify $10 \times C^2 \ge W$ and $10 \times R^2 \ge H$ minimum integers; check 1920x1080 (14, 11), 2560x1440 (16, 12), 3840x2160 (20, 15), 7680x2160 (26, 15).
   - **Boundary partitioning theorem**: For all $L \in [1, 10000]$ and $N \in [1, 26]$, verify cell widths are strictly $\lfloor L/N \rfloor$ or $\lceil L/N \rceil$, sum of widths equals $L$, no overlapping or gaps.
   - **Two-stage convergence**: Verify bounding box size after 2 steps is $\le 10$ px for standard displays, and reaches theoretical limits on displays $> 6760$ px (e.g. 12px for 7680px width).
   - **Center point & Euclidean distance**:
     - Bounding box center: `left + (W - 1) / 2` (integer division).
     - When center falls in monitor gap: nearest coordinate in valid monitor rects; tie-breaker test (smaller Y first, then smaller X).
2. **`KeyLayoutTests`**:
   - Mapping 26 keys `Q W E R T Y U I O P A S D F G H J K L Z X C V B N M` to indices $0 \dots 25$.
   - Out-of-bounds keys and non-alphabetic keys ignored.
3. **`SelectionManagerTests`**:
   - Complete state machine coverage (Standby, Selecting, AwaitingConfirmation, ExecutingClick).
   - Keystroke history progression (0 -> 1 -> 2 -> 3 -> 4 keys).
   - Enter at step 2 (premature confirmation at stage 1 reference point).
   - Enter ignored at step 0, 1, 3.
   - Backspace: pops key and replays selection history to re-derive active bounding box.
   - Cancel via Esc, focus loss, or monitor change resets to Standby.
4. **`CoordinateConverterTests`**:
   - Normalization formula $X = (x - x_{min}) \times 65535 / (W - 1)$.
   - Negative origin configurations (secondary monitor placed left or top of primary).
5. **Phase 1 Spike Validation**:
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
- [ ] Implement Win32 P/Invoke Layer (`NativeMethods.cs`):
  - [ ] Monitor enumeration (`EnumDisplayMonitors`, `GetMonitorInfo`).
  - [ ] Window sizing & positioning (`SetWindowPos`, `SWP_NOACTIVATE`, `SWP_SHOWWINDOW`).
  - [ ] Hotkey registration (`RegisterHotKey`, `UnregisterHotKey`).
  - [ ] Input simulation (`SendInput`, `MOUSEEVENTF_ABSOLUTE`, `MOUSEEVENTF_VIRTUALDESK`).
  - [ ] IME control (`ImmAssociateContext`, `ImmGetContext`, `ImmReleaseContext`).
- [ ] Spike 1: Transparent Overlay & Mouse Blocking:
  - [ ] Create topmost fullscreen window covering all displays.
  - [ ] Verify background apps do not receive hover/click events.
- [ ] Spike 2: Focus & Keyboard Input:
  - [ ] Verify `RegisterHotKey` opens overlay and grants keyboard focus.
  - [ ] Verify IME is disabled and alphabetic keys arrive as raw virtual key codes.
- [ ] Spike 3: SendInput Click Accuracy:
  - [ ] Implement `ClickVerificationHarness` to verify physical click coordinates match target within 0px offset across multiple DPI scalings (100%, 125%, 150%, 200%).
- [ ] Spike 4: Taskbar Coverage & Dispatcher Synchronization:
  - [ ] Verify overlay covers taskbar (both standard and auto-hide).
  - [ ] Verify overlay hide animation/render loop completes before `SendInput` fires to prevent self-clicks.
- [ ] **Go/No-Go Review**: Evaluate Phase 1 checklist against spec Section 15.

### Phase 2: Core Domain Logic & Single-Monitor Implementation
- [ ] Implement `KeyLayout`:
  - [ ] Key index resolver (`Q` -> 0 ... `M` -> 25).
  - [ ] Unit tests for `KeyLayout`.
- [ ] Implement `GridCalculator`:
  - [ ] Division count calculation ($C$ and $R$ bounded by 26).
  - [ ] Boundary calculation ($k \times L + N - 1) / N$).
  - [ ] Cell division and selection region intersection.
  - [ ] Center reference point and nearest-pixel resolution.
  - [ ] Unit tests for all calculator rules.
- [ ] Implement `SelectionManager`:
  - [ ] State transition logic (Standby, Selecting, AwaitingConfirmation, ExecutingClick).
  - [ ] History management (append keystroke, Backspace pop and recalculate).
  - [ ] Enter key validation (stage 1 early confirm vs stage 2 confirm).
  - [ ] Unit tests for `SelectionManager`.
- [ ] Implement `CoordinateConverter`:
  - [ ] Mapping physical pixel $(x, y)$ to normalized absolute mouse coordinates $(0 \dots 65535)$.
  - [ ] Unit tests for `CoordinateConverter`.
- [ ] Implement Single-Monitor Overlay UI:
  - [ ] Custom rendering for grid lines and bounding rectangle.
  - [ ] Top column labels and left row labels.
  - [ ] Column highlight (after 1st stroke of a stage) and cell highlight (after 2nd stroke).
  - [ ] Crosshair marker display in `AwaitingConfirmation` state.
  - [ ] Keyboard input binding (Q-M keys, Backspace, Enter, Esc).
- [ ] Implement `ClickExecutor`:
  - [ ] Dispatcher flush before `SendInput`.
  - [ ] Left-click dispatch via `SendInput`.
  - [ ] End-to-end single monitor manual verification.

### Phase 3: Multi-Monitor & DPI Architecture
- [ ] Implement `Win32MonitorProvider`:
  - [ ] Enumerate all active displays and query physical pixel coordinates.
  - [ ] Compute virtual desktop bounding box.
  - [ ] Expose monitor change events (`WM_DISPLAYCHANGE`, `WM_DPICHANGED`).
- [ ] Multi-Monitor Selection Support in `GridCalculator`:
  - [ ] Handling disjoint monitor bounds (exclude empty space between monitors).
  - [ ] Euclidean nearest distance fallback when center point falls in monitor gap.
  - [ ] Unit tests with virtual multi-monitor layouts (L-shaped, vertical stack, horizontal gap, negative coordinates).
- [ ] Multi-Monitor Overlay Architecture:
  - [ ] Primary overlay window on primary display (handles keyboard focus & input).
  - [ ] Display-only secondary overlay windows on remaining displays.
  - [ ] Label dispatching: draw column/row label only on the overlay containing the target slice start.
  - [ ] Window positioning via Win32 `SetWindowPos` in physical pixels.

### Phase 4: System Tray, Lifecycle, and Resilience
- [ ] Implement Single Instance Mutex:
  - [ ] Named mutex `KeyGrid_SingleInstance_Mutex`.
  - [ ] Detect existing instance and show balloon notification before exiting.
- [ ] Implement `HotkeyManager`:
  - [ ] Hidden message-only window or hook for `WM_HOTKEY`.
  - [ ] Register `Ctrl + Shift + Space`.
  - [ ] Graceful notification on hotkey conflict.
- [ ] Implement `TrayManager`:
  - [ ] System tray icon with context menu (Status, Exit).
  - [ ] Notification balloon display on registration failure or UIPI send failure.
- [ ] Implement Focus & System Event Watchers:
  - [ ] Cancel selection on overlay focus loss (`WM_KILLFOCUS` / `Deactivated`).
  - [ ] Cancel selection on display setting change (`WM_DISPLAYCHANGE`, `WM_DPICHANGED`).
  - [ ] Clean restoration of IME context and window destruction.
- [ ] Implement File Logging:
  - [ ] File logger writing to `%LOCALAPPDATA%\KeyGrid\logs`.
  - [ ] Automatic cleanup of log files older than 7 days.
- [ ] UIPI & SendInput Verification:
  - [ ] Detect SendInput failure and show diagnostic tray balloon.

### Phase 5: Verification, Benchmarking, and Packaging
- [ ] Run full automated test suite (`dotnet test`).
- [ ] Execute Performance Benchmarks:
  - [ ] Overlay appearance latency $< 200$ ms.
  - [ ] Keypress to render update latency $< 100$ ms.
  - [ ] Idle CPU usage $< 0.1\%$, memory working set $< 100$ MB.
- [ ] Full Checklist Verification against `spec.md` Section 16 (Completion Criteria).
- [ ] Single-File Standalone Publishing:
  - [ ] Configure `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`.
  - [ ] Validate published executable on clean environment.
