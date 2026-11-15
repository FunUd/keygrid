# Test Coverage Review and Expansion Plan

Review date: 2026-11-16

## Summary

The existing suite provides solid coverage of the pure selection and geometry logic, but it does not yet adequately verify the full application behavior. The most important gap is the lack of automated coverage for WPF orchestration and Windows integration; the current coverage report measures `KeyGrid.Core`, not the UI/application project.

The solution test run passed all 124 tests: 120 core tests and 4 UI rendering tests. After the first expansion pass, a fresh Cobertura report for `KeyGrid.Tests` measured 586/602 lines (97.34%) and 249/266 branches (93.60%). These figures must not be interpreted as whole-application coverage.

## Findings

### Strengths

- `KeyLayout` allocation is tested across all valid column/row count pairs, including borrowing and disjointness.
- `SelectionManager` tests exercise the main two-stage flow, Backspace replay, cancellation, invalid keys, and several multi-monitor cases.
- Coordinate conversion covers negative virtual-screen origins, clamping, extreme coordinates, and sampled round-trip precision.
- UI tests verify label ownership/rendering and Stage 2 highlighting on an STA thread.

### Coverage and test-quality gaps

1. **The reported coverage is Core-only.** `KeyGrid.Tests` references `coverlet.collector`; `KeyGrid.UI.Tests` does not. No UI coverage report was produced. `OverlayManager`, `OverlayWindow`, `TrayManager`, `HotkeyManager`, `FileLogger`, `App`, and the Win32 interop classes have no direct tests.
2. **State-transition coverage remains partial.** Grid optimality is now checked against all valid key-budget candidates for representative dimensions, and the boundary property is exercised for lengths 1–512 and division counts 1–26. The main Core state transitions and process/visibility focus-policy matrix are covered; remaining edge cases and the app-level event flow still need broader tests.
3. **UI and Windows behavior remain largely untested.** See the phases below; `spec.md` Section 14.2 still requires real display, DPI, focus, tray, and click verification.

Selected Core coverage:

| Area | Line coverage | Branch coverage |
|---|---:|---:|
| `GridCalculator` | 94.9% | 89.8% |
| `LabelOwnership` | 96.4% | 91.2% |
| `PixelRect` | 98.5% | 97.4% |
| `CoordinateConverter` | 100.0% | 100.0% |
| `SelectionManager` | 98.0% | 94.6% |
| `ClickExecutor` | 100.0% | 100.0% |

Coverage is diagnostic rather than a completeness measure: passing the untested branches and specified scenarios matters more than reaching a percentage threshold.

## Expansion Plan

### Phase 1 — Close high-risk pure-logic gaps (initial pass complete)

- [x] Replace the ineffective coordinate tie-break test with equal-distance fixtures that independently verify smaller-Y and then smaller-X selection.
- [x] Exercise `CalculateBoundaries` for lengths 1–512 and division counts 1–26; compare first-stage divisions with every valid key-budget candidate for representative standard, portrait, ultrawide, and extreme dimensions.
- [x] Cover touching/empty rectangles, empty bounding-box inputs, empty-rectangle clamping, and one-pixel coordinate axes.
- [x] Verify click order (hide, wait, send) and that a render-wait exception prevents dispatch.
- [x] Add invalid division counts and invalid cell-index cases.
- [ ] Add boundary cases near integer-coordinate limits if those ranges are considered part of the supported input contract.

### Phase 2 — Complete the Core state-machine contract (initial pass complete)

- [x] Cover standby input, invalid movement vectors, grid-edge movement, and click completion.
- [x] Cover the applicable Core transitions for valid/invalid keys, Enter, Backspace at history lengths 0/1/2, restart, cancel, and completion.
- [x] Assert state and relevant selection data in transition tests rather than relying only on return values.
- [x] Verify focus-policy decisions for the own/foreign × visible/hidden input matrix. Test reactivation outcomes in the UI coordinator if recovery limits are introduced there.

### Phase 3 — Test UI orchestration and input behavior

- Add Windows STA component tests for `OverlayWindow` key routing, repeat suppression, secondary-overlay input behavior, and cleanup such as IME restoration.
- Test `OverlayManager` with controllable monitor and click fakes for no-monitor failures, overlay lifecycle, event routing, hotkey restart behavior, display-change cancellation, and click success/failure reporting.
- Add coverage instrumentation for `KeyGrid.UI.Tests` before using UI coverage percentages. Introduce injectable seams around native calls only where needed for deterministic tests; retain actual Win32 behavior for manual verification.

### Phase 4 — Windows integration and manual verification

- Automate the existing `spec.md` Section 14.2 click-coordinate check with a verification window, including negative-origin and mixed-DPI layouts where available.
- Maintain a repeatable manual checklist for mouse blocking, IME on/off, hotkey conflicts, focus loss/recovery, monitor/DPI changes, tray dismissal, taskbar/fullscreen behavior, sleep/RDP, and UIPI failures.
- Record OS, monitor topology, DPI scaling, expected result, and observed result for each manual run.

## Suggested acceptance criteria

- The tie-break regression tests fail if either tie-break rule is removed.
- Every Core behavior explicitly listed in `spec.md` Section 14.1 has at least one assertion that would fail if that behavior regressed.
- The solution suite passes on Windows, with Core and UI coverage reported separately.
- Manual Windows-only checks remain documented and are not represented as automated test coverage.
