# Fixed-Size Grid on the Active Monitor: Feasibility and Implementation Plan

Status: proposal. No application code or `spec.md` change is included in this
work unit.

Related documents:

- [spec.md](../spec.md)
- [implementation_plan.md](implementation_plan.md)
- [layout_optimization_plan.md](layout_optimization_plan.md)

## 1. Summary

The requested behavior is feasible with two qualifications:

1. A mathematically exact, identical cell size on every display is not possible
   while the grid must tile the whole monitor with an integer number of columns
   and rows and the total column and row keys are limited to 26. The design can
   keep the Stage 1 cell close to a fixed DIP target on common displays and
   coarsen it predictably when the key budget is exhausted.
2. A fixed-size Stage 2 window cannot cover an arbitrarily large Stage 1 cell.
   With the recommended sizes, every supported display through 8K is covered.
   Displays beyond that range are documented as a limitation rather than
   changing the Stage 2 cell size.

Recommended design:

- Show exactly one overlay, on the monitor containing the foreground window at
  hotkey time, falling back to the Windows primary monitor.
- Target a Stage 1 cell size of **256 DIP** using the active monitor's effective
  DPI.
- Use a fixed Stage 2 cell of **24 DIP** in a fixed **25 x 25** window, making
  the Stage 2 window **600 x 600 DIP**.
- Center the Stage 2 window on the selected Stage 1 cell's reference point.
- Keep the full virtual-desktop rectangle for `SendInput` normalization. Only
  the grid and overlay move to the active monitor.

The last point is critical: using the active monitor rectangle as the
`MOUSEEVENTF_VIRTUALDESK` normalization rectangle would produce incorrect click
coordinates on secondary monitors.

## 2. Goals and Non-Goals

### 2.1 Goals

- The Stage 1 cell should have approximately the same apparent size regardless
  of monitor resolution.
- The Stage 2 final click cell should have the same apparent size on every
  display and in every Stage 1 configuration.
- Only the active monitor should be covered by the grid.
- The existing two-stage interaction, key budget, click kinds, range selection,
  Scroll Mode, and continuous mode should remain unchanged.
- Grid sizing and monitor selection should remain testable without Win32.

### 2.2 Non-Goals

- Exact pixel-for-pixel equality of Stage 1 cells across every possible display
  size.
- Increasing the 26-key budget or adding a third stage.
- Moving selection across monitors or supporting a selection that spans
  monitors.
- Covering inactive monitors with an invisible mouse-blocking overlay.
- Changing the Stage 2 interaction from arrow-key cursor movement.

## 3. Current Behavior

The current implementation:

- Creates one overlay per monitor and makes the Windows-primary overlay the only
  input-capable overlay.
- Divides the bounding box of all monitors, so a Stage 1 cell can span monitors
  or the empty space between them.
- Chooses Stage 1 divisions to minimize
  `max(ceil(ceil(W / C) / 5), ceil(ceil(H / R) / 5))` under `C + R <= 26`.
- Derives the Stage 2 window width and height from five times the selected
  Stage 1 cell. The central 5 x 5 therefore matches the Stage 1 cell.
- Uses the Stage 1 cell size to determine the final Stage 2 cell size. This is
  why the final cell grows with monitor resolution.

At 1920 x 1080 the final cell is about 24 x 22 physical pixels. At 4K it is
about 48 x 44, and at 8K it is about 96 x 87.

## 4. Feasibility Analysis

### 4.1 Why an exact fixed Stage 1 size is impossible

For a monitor of physical width `W` and height `H`, integer divisions produce
cells of width `ceil(W / C)` or `floor(W / C)`, and similarly for height. The
actual cell size therefore depends on the screen dimensions and on the integer
values of `C` and `R`.

Requiring both dimensions to be exactly 256 DIP at every resolution would
require fractional division counts or unused margins around the monitor. The
grid must continue to cover the monitor, so the design instead targets a fixed
size and allows a small, bounded deviation.

The 26-key limit makes the constraint stronger on very large or very wide
displays. For example, 4K at 100% needs at least 15 columns and 8 rows to keep a
cell near 256 DIP, which already consumes 23 keys. A 5120 x 2880 display needs
16 columns and 10 rows, consuming the full 26-key budget and still producing
cells around 320 x 288 DIP. At 8K the unavoidable cells are much larger.

The practical contract is therefore:

- Aim for 256 DIP.
- Use the best pair under `C + R <= 26`.
- Never exceed the key budget.
- Coarsen automatically when the budget cannot reach the target.
- Keep the Stage 2 final cell fixed independently of the Stage 1 cell size.

### 4.2 Recommended Stage 1 sizing algorithm

Constants:

```text
Stage1CellTargetDip = 256.0
MaxDivisions        = 26
targetPx            = round(Stage1CellTargetDip * activeDpiScale)
```

For every pair `(C, R)` with `C >= 1`, `R >= 1`, and `C + R <= 26`:

```text
cellWidthPx  = ceil(W / C)
cellHeightPx = ceil(H / R)
maxDeviation = max(|cellWidthPx - targetPx|, |cellHeightPx - targetPx|)
totalDeviation = |cellWidthPx - targetPx| + |cellHeightPx - targetPx|
```

Choose the candidate with the following deterministic ordering:

1. Smallest maximum deviation from the physical target.
2. Smallest total deviation.
3. Fewest keys (`C + R`).
4. Largest cell count (`C * R`).
5. Smallest column count.

The search is only about 325 candidates and remains a pure calculation. Unlike a
simple `ceil(W / target)` formula, it avoids making a cell unnecessarily small
when a nearby division count is closer to the target. For example, 1920 x 1080
at 100% selects 8 x 4 with 240 x 270 DIP cells instead of 8 x 5 with
240 x 216 DIP cells.

The same algorithm handles the fallback case. There is no separate oversized
screen mode: when the 26-key budget cannot reach the target, the minimax result
naturally becomes the coarsest acceptable pair.

A warning should be logged when the chosen maximum deviation exceeds 25% of the
target. This makes unsupported or unusually large configurations diagnosable
without changing selection behavior.

### 4.3 Representative Stage 1 results

`S1 cell` is the largest resulting cell size in DIP. The final Stage 2 cell is
always 24 DIP.

| Display | DPI | Stage 1 | Keys | S1 cell (DIP) | Max deviation |
|---|---:|---:|---:|---:|---:|
| 1920 x 1080 | 100% | 8 x 4 | 12 | 240 x 270 | 6.3% |
| 1920 x 1080 | 125% | 6 x 3 | 9 | 256 x 288 | 12.5% |
| 1920 x 1080 | 150% | 5 x 3 | 8 | 256 x 240 | 6.3% |
| 1920 x 1080 | 175% | 4 x 3 | 7 | 274 x 206 | 19.6% |
| 1920 x 1080 | 200% | 4 x 2 | 6 | 240 x 270 | 6.3% |
| 2560 x 1440 | 100% | 10 x 6 | 16 | 256 x 240 | 6.3% |
| 2560 x 1440 | 150% | 7 x 4 | 11 | 244 x 240 | 6.3% |
| 3840 x 2160 | 100% | 15 x 8 | 23 | 256 x 270 | 5.5% |
| 3840 x 2160 | 150% | 10 x 6 | 16 | 256 x 240 | 6.3% |
| 3840 x 2160 | 200% | 8 x 4 | 12 | 240 x 270 | 6.3% |
| 5120 x 1440 | 100% | 20 x 6 | 26 | 256 x 240 | 6.3% |
| 5120 x 2880 | 100% | 16 x 10 | 26 | 320 x 288 | 25.0% |
| 7680 x 2160 | 100% | 20 x 6 | 26 | 384 x 360 | 50.0% |
| 7680 x 4320 | 100% | 16 x 10 | 26 | 480 x 432 | 87.5% |
| 1080 x 1920 | 100% | 4 x 8 | 12 | 270 x 240 | 6.3% |
| 2160 x 3840 | 100% | 8 x 15 | 23 | 270 x 256 | 5.5% |

Common 1080p through 5K2K configurations remain within roughly 0 to 25% of the
target. The clear fallback cases are the 8K configurations, where the 26-key
budget makes the cells several times larger than 256 DIP.

### 4.4 Fixed Stage 2 sizing

Constants:

```text
Stage2CellDip   = 24.0
Stage2Columns   = 25
Stage2Rows      = 25
Stage2WindowDip = 25 * 24 = 600
stage2CellPx    = max(1, round(Stage2CellDip * activeDpiScale))
stage2WindowPx  = Stage2Columns * stage2CellPx
```

Use `stage2WindowPx` for both dimensions. Derive the window from the rounded
cell size rather than rounding a separate 600 DIP window size; this guarantees
that every one of the 25 cells has exactly the same physical size.

Because the window width is exactly 25 cells and the divider count is 25, the
integer boundary formula produces exact cell boundaries. The final confirmed
cell is therefore exactly `stage2CellPx x stage2CellPx` in physical pixels.

| DPI | Stage 2 cell | Stage 2 window |
|---|---:|---:|
| 100% | 24 px (24 DIP) | 600 px (600 DIP) |
| 125% | 30 px (24 DIP) | 750 px (600 DIP) |
| 150% | 36 px (24 DIP) | 900 px (600 DIP) |
| 175% | 42 px (24 DIP) | 1050 px (600 DIP) |
| 200% | 48 px (24 DIP) | 1200 px (600 DIP) |

### 4.5 Stage 2 centering

Let the selected Stage 1 cell be `focusBox`, and let its reference point be:

```text
refX = focusBox.Left + (focusBox.Width - 1) / 2
refY = focusBox.Top + (focusBox.Height - 1) / 2
```

Place the fixed window as follows, using integer division:

```text
window.Left = refX - stage2WindowPx / 2
window.Top  = refY - stage2WindowPx / 2
window.Size = stage2WindowPx x stage2WindowPx
```

With 25 cells, the center cell index is 12 in both dimensions. Under the
boundary formula, the center cell contains `refX` and `refY` for every
`stage2CellPx >= 1`:

```text
center cell x range = window.Left + 12 * cellPx ... window.Left + 13 * cellPx - 1
center cell y range = window.Top  + 12 * cellPx ... window.Top  + 13 * cellPx - 1
```

The initial cursor is `(12, 12)`. If that cell is invalid for any reason, scan
the whole 25 x 25 window and choose the valid cell with the smallest Manhattan
distance from `(12, 12)`.

The Stage 2 window is not the selected Stage 1 cell. It may cover parts of
neighboring Stage 1 cells, and it may extend beyond the active monitor. The
selected Stage 1 cell remains the visual anchor through the dim mask and focus
outline, while every part of the window that intersects the active monitor is
selectable.

### 4.6 Coverage limit

The fixed 600 DIP window covers every Stage 1 cell in the representative table:

- The largest supported cell is 480 x 432 DIP at 8K.
- 600 DIP still leaves 60 DIP of horizontal and 84 DIP of vertical margin.
- At 5K2K and 4K the coverage is larger.

For displays where the selected Stage 1 cell exceeds 600 DIP in either
dimension, the outer part of that cell cannot be reached through Stage 2. This
can only occur well beyond the 8K cases listed here. It is an accepted
limitation of keeping the Stage 2 cell fixed; supporting those displays would
require either a variable window size or a larger key budget.

## 5. Active Monitor Design

### 5.1 Determining the active monitor

The active monitor is the monitor containing the foreground window when the
selection starts.

1. Capture the foreground window handle before creating the overlay. The
   existing `_originatingForegroundWindow` already provides this value.
2. Resolve it with `MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST)`.
3. Read the monitor bounds and effective DPI with the existing monitor helpers.
4. If the handle is zero, `MonitorFromWindow` fails, or no monitor is returned,
   use the monitor marked `IsPrimary`.
5. If no monitor is marked primary, use the first valid enumerated monitor.
6. If there are no valid monitors, keep the existing error path.

`MONITOR_DEFAULTTONEAREST` is intentional. A minimized or off-screen window
still resolves to a nearby monitor instead of failing.

When the hotkey restarts an active selection, keep the original foreground
window and therefore the same active monitor. When a new selection starts after
a successful click or after the overlay has closed, capture the new foreground
window. Continuous mode therefore reopens on the monitor of the click target,
which is the same active monitor.

### 5.2 Monitor provider contract

Extend the Core abstraction:

```csharp
public interface IMonitorProvider
{
    IReadOnlyList<MonitorInfo> GetMonitors();
    MonitorInfo? GetMonitorForWindow(nint windowHandle);
    event Action? Changed;
}
```

`Win32MonitorProvider` implements `GetMonitorForWindow` with
`MonitorFromWindow`, `GetMonitorInfo`, and `GetDpiForMonitor`. It returns
`null` rather than guessing when the handle cannot be resolved. The existing
`GetMonitors()` remains responsible for enumeration and virtual-desktop
bounds.

Test fakes provide a configurable active monitor independently of the
primary-monitor flag. This is required to verify that input follows the active
monitor, not the Windows primary monitor.

### 5.3 One overlay, one input owner

Create exactly one `IOverlayWindowSurface`, positioned at the active monitor's
physical bounds with the active monitor's DPI.

- The overlay is always focusable, activated, topmost, and input-capable.
- The `IsPrimary` flag no longer represents both "input owner" and "Windows
  primary monitor". Remove it from the overlay contract and its render state.
- `TryReactivate`, IME disabling, focus-loss handling, key handling, and status
  badges apply to the single overlay unconditionally.
- Rename manager members such as `_primaryOverlay` and
  `IsPrimaryForegroundAndFocused` to monitor-neutral names such as
  `_overlay` and `IsOverlayForegroundAndFocused`.
- The overlay covers only the active monitor. Inactive monitors are neither
  dimmed nor mouse-blocked. Clicking or activating an application on another
  monitor causes the existing focus-loss policy to cancel selection.

This deliberately removes the display-only overlay role. Keeping a hidden
mouse-blocking window on inactive monitors would be a separate feature and is
not part of this plan.

### 5.4 Virtual desktop versus active monitor

Two different rectangles must be kept separate:

| Rectangle | Purpose |
|---|---|
| Active monitor bounds | Stage 1 selection area, overlay placement, Stage 2 clipping |
| Virtual desktop bounds | Normalization for `SendInput` with `MOUSEEVENTF_VIRTUALDESK` |

`OverlayManager` already enumerates all monitors. Compute and retain:

```csharp
var virtualScreenBounds = PixelRect.BoundingBox(
    monitors.Select(m => m.Bounds));
```

Pass `virtualScreenBounds` to every `ClickExecutor` call, including normal
clicks, range clicks, and Scroll Mode. Do not replace it with the active
monitor bounds.

`SelectionManager` currently exposes `VirtualScreenBoundingBox`, which is used
both as the initial selection area and as the click-normalization rectangle.
After this change it should expose the active monitor bounds instead, for
example `ActiveMonitorBounds`. The virtual desktop rectangle belongs to the
overlay orchestration layer and is not part of the selection state.

This distinction must be covered by a regression test with a secondary monitor
at a non-zero or negative origin. If the active monitor is used for
normalization, the synthetic click lands at the wrong coordinate even though
the grid is displayed correctly.

## 6. Selection and Rendering Changes

### 6.1 Selection manager

Change the entry point from a list of monitor rectangles to one monitor:

```csharp
public bool StartSelection(MonitorInfo activeMonitor)
```

Internally the selection area may remain a one-element rectangle list to avoid
an unrelated rewrite. The important invariants are:

- `CurrentSelection` starts as the active monitor bounds.
- `CurrentBoundingBox` starts as the active monitor bounds.
- Stage 1 divisions are calculated from the active monitor's width, height,
  and effective DPI.
- Stage 2 is clipped to the active monitor, not to all monitors.
- Backspace resets to the same active monitor bounds.
- No selection or Stage 2 cursor can move to another monitor.

### 6.2 Stage 1 calculation

`GridCalculator` should gain a target-based division calculation:

```csharp
(int Columns, int Rows) CalculateFirstStageDivisions(
    int widthPx,
    int heightPx,
    int targetCellPx)
```

The old `stage2Columns` and `stage2Rows` parameters are no longer part of Stage 1
sizing. The Stage 2 cell is fixed and does not influence the Stage 1 optimizer.

### 6.3 Stage 2 calculation

Replace `CalculateCenteredContextWindow` with a fixed-window calculation:

```csharp
PixelRect CalculateCenteredStage2Window(
    PixelRect focusBox,
    int cellSizePx,
    int columnCount,
    int rowCount)
```

The method computes the Stage 1 reference point, offsets by half the fixed
window size, and returns the window rectangle. It does not require the focus
box to be an integer multiple of the Stage 2 cell.

`SelectionManager.SetupStage2Divisions` then:

1. Saves `FocusSelection` for the dim mask and focus outline.
2. Computes `Stage2Window` with the fixed 600 DIP geometry.
3. Sets the grid to 25 x 25 with exact `cellSizePx` boundaries.
4. Intersects the window with the active monitor to build valid cells.
5. Selects the center cell `(12, 12)`, with the nearest-valid fallback.
6. Removes `Stage2FocusColumnCount`, `Stage2FocusRowCount`,
   `Stage2MarginColumns`, and `Stage2MarginRows`.

`ProcessEnter`, range selection, and Scroll Mode continue to resolve the
selected cell against `CurrentSelection`, so existing click behavior is
preserved.

### 6.4 Rendering

`OverlayRenderElement` changes:

- Remove the `_monitorRects` list and `IsOwnerMonitor` logic. There is only one
  monitor, so no label ownership arbitration is needed. The obsolete
  `LabelOwnership` helper and its tests can be deleted.
- Remove the `isPrimary` parameter. Status badges are drawn by the single
  overlay.
- Keep the dim mask around `FocusSelection` in Stage 2. The selected Stage 1
  cell remains bright, even though the Stage 2 window extends into neighboring
  cells.
- Replace the central 5 x 5 outline with an outline around the actual selected
  Stage 1 cell (`FocusSelection`). This preserves the visual anchor without
  implying that five Stage 2 cells equal one Stage 1 cell.
- Keep the lighter Stage 2 window border.
- Draw grid lines only for the 25 x 25 window, clipped to the active monitor.
- Draw the red selected Stage 2 cell at its exact fixed size.
- Draw the range anchor, status badges, and mode indicators on the single
  overlay.

The label font sizing logic can remain unchanged. The resulting Stage 1 cells
are much larger than the current 120 x 108 px cells at 1080p, so the existing
label fitting has sufficient room.

## 7. Overlay Lifecycle

### 7.1 Start

1. Capture the foreground window if no selection is active.
2. Enumerate monitors and compute the virtual desktop bounds.
3. Resolve the active monitor from the foreground window, then fall back to the
   primary monitor, then the first valid monitor.
4. Cancel any previous selection and close any previous overlay.
5. Start selection with the active monitor and DPI.
6. Create one overlay on the active monitor and wire all input events.
7. Show the overlay, update render state, and reactivate it.
8. Log active monitor bounds, DPI, Stage 1 counts, Stage 1 cell DIP, Stage 2
   window size, and whether a fallback or coarse result was used.

If the overlay cannot be focused, keep the current behavior: cancel, close, and
report the error.

### 7.2 Restart and continuous mode

- Hotkey restart while active: discard and restart on the same active monitor.
- Continuous mode after a click: capture the clicked window as the new
  foreground and resolve the active monitor again. The click target is on the
  active monitor, so this is normally the same monitor.
- Display or DPI change: keep the existing cancellation behavior.

### 7.3 Focus, IME, and input

- The single overlay owns focus and keyboard input.
- IME suppression and restoration remain unchanged.
- Focus loss to a visible foreign window cancels selection as before.
- Focus loss to an own-process or hidden helper window reactivates the single
  overlay as before.
- Mouse input is absorbed only on the active monitor.

## 8. Behavior Changes

These are intentional and must be reflected in the specification and README:

- Only the active monitor is covered and dimmed.
- Stage 1 cells no longer span monitors or monitor gaps.
- Stage 2 cannot move across monitors.
- Range selection cannot span monitors.
- The Stage 2 window is a fixed 600 DIP square, not five times the Stage 1
  cell.
- The central 5 x 5 is no longer the Stage 1 cell. The selected Stage 1 cell is
  shown by the dim-mask focus and outline.
- Stage 2 may include context from neighboring Stage 1 cells.
- The final click cell is 24 DIP instead of roughly 24 x 22 DIP at 1080p and
  48 x 44 at 4K.
- The active monitor is chosen from the foreground window, not from the Windows
  primary flag.
- Inactive monitors are not mouse-blocked.

## 9. Implementation Phases

### Phase 1: Active monitor and single-overlay lifecycle

Files:

- `src/KeyGrid.Core/Abstractions/IMonitorProvider.cs`
- `src/KeyGrid.Core/Abstractions/MonitorInfo.cs` (only if additional metadata is
  needed; the current fields are likely sufficient)
- `src/KeyGrid/Interop/NativeMethods.cs`
- `src/KeyGrid/Interop/Win32MonitorProvider.cs`
- `src/KeyGrid/UI/OverlayManager.cs`
- `src/KeyGrid/UI/OverlayWindow.xaml.cs`
- `src/KeyGrid/UI/IOverlayWindowSurface.cs`
- `src/KeyGrid/UI/OverlayKeyHandler.cs`

Tasks:

- Add `MonitorFromWindow` and the nearest-monitor constant.
- Add `GetMonitorForWindow` to the abstraction and Win32 implementation.
- Resolve the active monitor with primary/first fallbacks and diagnostic logs.
- Create one overlay instead of one per monitor.
- Remove the display-only/primary distinction from the overlay, key handler, and
  render state.
- Rename focus-management members to monitor-neutral names.
- Separate active monitor bounds from virtual desktop bounds.
- Pass virtual desktop bounds to all click, range, and scroll dispatch paths.
- Update UI tests for one overlay and active-monitor focus.

Validation:

- `dotnet test --nologo`
- Manual: dual monitors, foreground on each monitor, different DPI, secondary
  monitor placed left or above the primary.

### Phase 2: Fixed Stage 1 sizing

Files:

- `src/KeyGrid.Core/Calculation/GridCalculator.cs`
- `src/KeyGrid.Core/Selection/SelectionManager.cs`
- `src/KeyGrid.Core/Calculation/LabelOwnership.cs` (remove when the render
  changes make it obsolete)

Tasks:

- Add the 256 DIP target and target-based division search.
- Remove Stage 2 focus dimensions from the Stage 1 optimizer.
- Use the active monitor's effective DPI to convert the target to physical
  pixels.
- Keep the 26-key budget and deterministic tie-breaks.
- Log a warning for large deviations.
- Update unit tests for the representative table, tie-breaks, and fallback
  cases.

Validation:

- `dotnet test --nologo`
- Manual: 1080p, 1440p, 4K, 5K2K, and 8K if available at 100% and 150%.

### Phase 3: Fixed Stage 2 window and rendering

Files:

- `src/KeyGrid.Core/Calculation/GridCalculator.cs`
- `src/KeyGrid.Core/Selection/SelectionManager.cs`
- `src/KeyGrid/UI/OverlayRenderElement.cs`
- `src/KeyGrid/UI/OverlayWindow.xaml.cs`
- `src/KeyGrid/UI/OverlayManager.cs`

Tasks:

- Add the 24 DIP cell and 600 DIP window calculation.
- Center the window on the Stage 1 reference point.
- Select the center cell `(12, 12)` initially.
- Clip the window to the active monitor.
- Remove the central 5 x 5 constants, border, and semantics.
- Outline the selected Stage 1 cell in Stage 2.
- Simplify single-monitor label ownership.
- Update rendering and selection tests.

Validation:

- `dotnet test --nologo`
- Manual: verify the final red cell is 24 x 24 DIP at every tested DPI and that
  Stage 2 context remains reachable at monitor edges.

### Phase 4: Specification, documentation, and final validation

Files:

- `spec.md` (version bump, likely to 1.15)
- `README.md`
- `docs/implementation_plan.md`
- `CHANGELOG.md` if a release note is appropriate

Tasks:

- Replace all "all monitors" and "virtual screen grid" wording with active
  monitor behavior.
- Replace the Stage 1 formula and examples.
- Replace central 5 x 5 semantics in Sections 1, 3, 4, 5, 8, 10, 13, 14, 15,
  and 16.
- State that the virtual desktop rectangle remains the `SendInput`
  normalization basis.
- Update the four-key/continuous/range/scroll descriptions where they mention
  primary or secondary overlays.
- Update the implementation plan's outdated 20 DIP design.
- Update README features, activation, selection, and multi-monitor sections.
- Run the full automated test suite.
- Execute the manual matrix in Section 10.

## 10. Test Plan

### 10.1 Unit tests

`GridCalculatorTests`:

- Representative Stage 1 results at 1080p, 1440p, 4K, 5K2K, 8K, and portrait.
- `C + R <= 26` for every result.
- Fixed target search beats a simple ceiling formula on 1920 x 1080 and
  portrait displays.
- Deterministic tie-breaking.
- Fallback coarsening at 7680 x 2160 and 7680 x 4320.
- Stage 2 window is exactly `25 * cellPx` in both dimensions.
- Stage 2 boundaries are exactly `cellPx` apart.
- Centering places the center cell around the Stage 1 reference point.
- Center cell contains the reference point for odd and even `cellPx`.

`SelectionManagerTests`:

- Starts from the active monitor only.
- Stage 1 counts and cell sizes use the active DPI.
- Focus selection and Stage 2 window are correct.
- Initial cursor is `(12, 12)`.
- Context outside the Stage 1 cell is selectable.
- Backspace returns to Stage 1 and preserves the active monitor.
- Range and Scroll Mode still resolve targets correctly.
- No selection spans monitors.

`MultiMonitorIntegrationTests`:

- Foreground on a secondary monitor starts there.
- Primary monitor is used when the foreground handle is unavailable.
- Virtual desktop bounds remain separate from active monitor bounds.
- Click normalization uses the virtual desktop, including negative origins.
- No overlay or grid exists on inactive monitors.

`OverlayManagerTests`:

- Exactly one overlay is created.
- It is created at the active monitor bounds.
- It is always input-capable.
- The Windows primary flag does not control input ownership.
- Focus, hide/show, click, range, and scroll paths still work.
- Continuous mode reopens on the active monitor.
- Display changes cancel selection.

`OverlayRenderElementTests`:

- Labels render on the single active monitor.
- Status badges render on the single overlay.
- The Stage 1 cell remains the dim-mask focus in Stage 2.
- No central 5 x 5 border is drawn.
- The fixed window border and selected Stage 1 outline are drawn.
- Range anchor and selection highlight use the fixed cell geometry.

### 10.2 Manual matrix

- 1920 x 1080 at 100, 125, 150, 175, and 200%.
- 2560 x 1440 at 100 and 150%.
- 3840 x 2160 at 100, 150, and 200%.
- 5120 x 1440 at 100%.
- 5120 x 2880 at 100% if available.
- 7680 x 2160 and 7680 x 4320 if available.
- Two monitors with the foreground on each, including different DPI.
- Secondary monitor placed to the left and above the primary.
- Click on the secondary monitor and verify the pointer lands on the selected
  coordinate.
- Mouse blocking on the active monitor; inactive monitor remains interactive
  and cancels selection when focused.
- Range selection and Scroll Mode on the active monitor.
- Stage 2 at monitor edges.
- Continuous mode after clicking on the active monitor.

## 11. Acceptance Criteria

The change is complete when:

- Only one overlay is created, on the monitor containing the foreground window.
- The Windows primary monitor no longer determines input ownership.
- Stage 1 uses the active monitor's width, height, and DPI.
- Stage 1 satisfies `C + R <= 26` for every display.
- Representative Stage 1 sizes match the table in Section 4.3.
- Large-display coarsening is automatic, deterministic, and logged.
- Stage 2 is always 25 x 25 with a 24 DIP cell and a 600 DIP window.
- The initial Stage 2 cursor is the center cell.
- The central 5 x 5 semantics are removed from code, tests, and documentation.
- A normal click, range click, and scroll on a secondary monitor land at the
  correct physical coordinate.
- No selection or grid content appears on inactive monitors.
- All automated tests pass.
- The manual matrix in Section 10.2 has been executed or explicitly reported as
  unavailable.

## 12. Risks and Limitations

- **Integer tiling:** Stage 1 cells will vary slightly around the target even on
  common displays. The table shows the expected range.
- **26-key budget:** 8K and very wide displays produce substantially coarser
  cells. This is accepted.
- **Fixed Stage 2 window:** Stage 1 cells larger than 600 DIP cannot be fully
  reached in Stage 2. This is outside the supported 8K range.
- **Active-monitor heuristic:** A window spanning monitors resolves according to
  Windows `MonitorFromWindow` behavior. A minimized window resolves to the
  nearest monitor.
- **Inactive monitor interaction:** Because inactive monitors are not covered,
  clicking an application there cancels the selection. This follows from
  showing the grid only on the active monitor.
- **Test rewrite:** Existing multi-monitor and primary-overlay tests encode the
  old behavior and will need substantial updates.

## 13. Specification Impact Checklist

When implementation is approved, update these areas in the same work unit as
the behavior change:

- Section 0 terminology: active monitor, input overlay, remove display-only
  overlay.
- Section 1 purpose: active monitor and fixed DIP sizes.
- Section 2 environment: heterogeneous DPI on the active monitor.
- Section 3 basic operation: one overlay, fixed Stage 2 window.
- Section 3.3 and 3.4: examples and range wording.
- Section 4.1: new target-based division algorithm and results table.
- Section 4.2: fixed Stage 2 window and removal of central 5 x 5 identities.
- Section 4.3: active-monitor selection and virtual-desktop separation.
- Section 4.5: DPI conversion of the fixed DIP targets.
- Section 5.1 and 5.2: single overlay, input, rendering, badges.
- Section 5.3: focus and display-change behavior.
- Section 6.1 and 6.2: mouse blocking on the active monitor only.
- Section 6.3: `SendInput` uses virtual desktop bounds.
- Section 7.1 and 7.2: single input overlay and hotkey restart.
- Section 8: state transitions and primary-overlay wording.
- Section 10: fixed 256/24 DIP and 25 x 25 settings.
- Section 11: performance examples and stability guarantees.
- Section 12: `MonitorFromWindow` and virtual desktop normalization.
- Section 13: module responsibilities and interfaces.
- Section 14: unit, integration, and manual test matrix.
- Section 15: Phase 1 and Phase 3 criteria.
- Section 16: completion criteria.
