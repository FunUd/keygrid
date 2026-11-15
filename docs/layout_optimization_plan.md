# KeyGrid Label and Grid Optimization Plan

Status: Revised proposal after technical review. No implementation has been done yet.

Related documents:

- [spec.md](../spec.md)
- [docs/implementation_plan.md](implementation_plan.md)

## 1. Goal

Improve two things without increasing the number of stages or adding more
keystrokes:

1. **Findability** - make the two-letter Stage 1 labels easier to scan and read.
2. **Input ease** - make the key sequences easier to type and less error-prone.

The main levers are:

- a coarser default grid target (20 DIP instead of 10 physical pixels),
- disjoint left-hand column keys and right-hand row keys,
- clearer progressive label presentation,
- correct per-monitor DPI handling and label ownership.

The existing two-stage, four-keystroke interaction remains unchanged.

## 2. Important correction found in review

### 2.1 Stage 2 currently does not implement the accuracy stated in the specification

`SelectionManager.SetupStage2Divisions()` calls `GridCalculator.CalculateDivisions()`
again for the selected cell. That recalculates Stage 2 column and row counts from
the selected cell size, instead of reusing the Stage 1 counts. The code then uses
a square-root estimate for each dimension.

That is not the same as spec.md section 4.1, which derives the final size as
`ceil(ceil(W / C) / C) x ceil(ceil(H / R) / R)` and assumes Stage 2 uses the same
`C` and `R` as Stage 1. In practice, at 1920x1080 with the current 10 px target:

| | Stage 1 | Stage 2 | Final cell |
|---|---:|---:|---:|
| Current code at 1920x1080 | 14 x 11 | 4 x 4 | about 35 x 25 px |
| Spec formula with same counts | 14 x 11 | 14 x 11 | about 10 x 9 px |

The current code is therefore **less precise than the specification** at common
resolutions, while also producing many Stage 1 labels.

The proposed 20 DIP target must not be evaluated against the current Stage 2
implementation. If Stage 2 keeps recalculating counts, a "20 DIP" target produces
roughly 48 x 45 px final cells at 1920x1080, not 20 DIP. The plan therefore makes
the Stage 2 division semantics a prerequisite.

### 2.2 Decision: Stage 2 reuses the Stage 1 division counts

Stage 2 will:

- reuse the `ColumnCount` and `RowCount` chosen for Stage 1,
- recalculate only the boundaries for the selected cell,
- use the same key mapping as Stage 1. Phase 2 replaces the shared mapping with
  dedicated column-key and row-key arrays; the same-count rule still applies.

This is required for three reasons:

1. It matches the final-size formula in spec.md section 4.1.
2. It keeps the final cell at or below the chosen target in the normal case.
3. It keeps one key allocation and one learned mapping for both stages. A
   recalculation produces a different, smaller key set in Stage 2 (currently
   4 x 4 at 1920x1080), which is harder to learn, harder to test, and easy to
   drift from the Stage 1 allocation. A future Stage 2 that tries to meet the
   target by linear recalculation could also exceed the remaining key budget;
   reusing `C` and `R` avoids that design trap.

This correction must be implemented together with the target change. Fixing
same-count Stage 2 while keeping the old 10 px target would produce 14 x 11
rulers in a 1920x1080 cell, leaving only about 10 DIP per ruler label. That is
too dense for the current ruler typography. The 20 DIP target restores readable
ruler spacing.

### 2.3 Corrected current-versus-proposed sizes

The table below uses maximum boundary sizes. "Current" means the current
behavior at the existing 10 px target, with Stage 2 recalculating counts.
"Proposed" means same-count Stage 2 with a 20 DIP target on the monitor's DPI
scale.

| Display / DPI | Current Stage 1 | Current final cell | Proposed Stage 1 | Proposed final cell |
|---|---:|---:|---:|---:|
| 1920x1080 @ 100% | 14 x 11 (154) | about 35 x 25 px | 10 x 8 (80) | about 20 x 17 px |
| 1920x1080 @ 150% | 14 x 11 (154) | about 35 x 25 px | 8 x 6 (48) | about 30 x 30 px (20 x 20 DIP) |
| 1920x1080 @ 200% | 14 x 11 (154) | about 35 x 25 px (17.5 x 12.5 DIP) | 7 x 6 (42) | about 40 x 30 px (20 x 15 DIP) |
| 2560x1440 @ 100% | 16 x 12 (192) | about 40 x 30 px | 12 x 9 (108) | about 18 x 18 px |
| 3840x2160 @ 100% | 20 x 15 (300) | about 39 x 36 px | 14 x 11 (154) | about 20 x 18 px |
| 3840x2160 @ 200% | 20 x 15 (300) | about 39 x 36 px (about 20 x 18 DIP) | 10 x 8 (80) | about 39 x 34 px (about 20 x 17 DIP) |

The proposed layout is **more precise than the current actual behavior at
common 100-150% configurations**, similar at 4K 200%, and intentionally coarser
than the current 1920x1080 200% actual cell. At 200% the current physical 10 px
target is only about 5 DIP, which is extremely fine but produces many labels.
The specification's theoretical 10 px target would require many more labels; 20
DIP is an intentional trade-off in favor of findability.

### 2.4 Review disposition

Accepted and reflected in this plan:

- the Stage 2 division-count recalculation bug and the same-count fix,
- the mixed-DPI policy caveat and the primary-monitor reference,
- keeping Stage 2 rulers instead of in-cell labels,
- exact key allocation, membership-based lookup, and overflow behavior,
- one owner monitor for labels and rulers,
- explicit spec/test/documentation impact,
- deferring precision mode until the simple design is measured,
- off-center badge placement with a real clearance from the click target,
- glyph caching and actual `pixelsPerDip`,
- correcting the inaccurate "20 DIP everywhere" claim.

Not adopted as part of this plan:

- Deferring the target change entirely. The target change is the main reduction
  in Stage 1 label count; the Stage 2 correction removes the precision risk. If
  the schedule requires a smaller first step, display-only changes can ship
  first, but key allocation cannot: the current 2560x1440 grid is 16 x 12
  (`16 + 12 = 28`) and the current 4K grid is 20 x 15 (`20 + 15 = 35`); neither
  fits 26 disjoint keys. The target/`C + R <= 26` change is a prerequisite for
  the key split.
- Using the maximum DPI scale as the default. It remains a fallback for
  high-DPI secondary ruler legibility.
- An always-on center marker. The marker is limited to the early-Enter state.
- Two-character labels inside Stage 2 sub-cells.
- Alphabetical A-Z ordering.

## 3. Grid target and DPI policy

### 3.1 Target definition

- The default target is **20 DIP in each dimension**, not necessarily a square
  20 x 20 cell. The actual cell may be wider or taller.
- Convert the target to physical pixels using the effective DPI scale of the
  primary monitor:

  ```text
  targetPixels = 20.0 * primaryDpiScale
  ```

- Use `T * C^2 >= W` and `T * R^2 >= H` to choose the smallest counts that meet
  the target.
- Enforce `C + R <= 26` so columns and rows never share a key.
- If the two independent minimum counts violate `C + R <= 26`, relax the target
  by choosing the `(C, R)` pair that minimizes the worst-case final size ratio:

  ```text
  max(ceil(ceil(W / C) / C) / T,
      ceil(ceil(H / R) / R) / T)
  ```

  Tie-break with fewer total cells (`C * R`), then fewer total keys (`C + R`),
  then smaller `C`. The chosen pair is deterministic and testable.

### 3.2 Why the primary monitor's DPI scale

Mixed-DPI setups cannot have a uniform 20 DIP target with one global grid. The
default should favor the monitor that owns keyboard focus and the primary
interaction overlay.

Decision:

- Use the **primary monitor's** effective DPI scale as the reference.
- Document that secondary monitors may have finer or coarser final cells.
- Keep the maximum-DPI policy as a fallback if manual testing shows that
  Stage 2 rulers on a higher-DPI secondary monitor are too dense.
- Do not claim "20 DIP everywhere" in the specification or README.

The current `(0,0)` primary-monitor heuristic in `OverlayManager` must be
replaced with the `MONITORINFOF_PRIMARY` flag. This also fixes focus routing on
topologies where the primary monitor is not at the origin.

### 3.3 Proposed single-monitor results

These values use the monitor's own DPI scale (which is the primary scale in a
single-monitor setup).

| Resolution | DPI | Columns | Rows | Stage 1 labels | Final cell (px) | Final cell (DIP) |
|---|---:|---:|---:|---:|---:|---:|
| 1920x1080 | 100% | 10 | 8 | 80 | 20 x 17 | 20.0 x 17.0 |
| 1920x1080 | 125% | 9 | 7 | 63 | 24 x 23 | 19.2 x 18.4 |
| 1920x1080 | 150% | 8 | 6 | 48 | 30 x 30 | 20.0 x 20.0 |
| 1920x1080 | 175% | 8 | 6 | 48 | 30 x 30 | 17.1 x 17.1 |
| 1920x1080 | 200% | 7 | 6 | 42 | 40 x 30 | 20.0 x 15.0 |
| 2560x1440 | 100% | 12 | 9 | 108 | 18 x 18 | 18.0 x 18.0 |
| 2560x1440 | 150% | 10 | 7 | 70 | 26 x 30 | 17.3 x 20.0 |
| 2560x1440 | 200% | 8 | 6 | 48 | 40 x 40 | 20.0 x 20.0 |
| 3840x2160 | 100% | 14 | 11 | 154 | 20 x 18 | 20.0 x 18.0 |
| 3840x2160 | 125% | 13 | 10 | 130 | 23 x 22 | 18.4 x 17.6 |
| 3840x2160 | 150% | 12 | 9 | 108 | 27 x 27 | 18.0 x 18.0 |
| 3840x2160 | 175% | 11 | 8 | 88 | 32 x 34 | 18.3 x 19.4 |
| 3840x2160 | 200% | 10 | 8 | 80 | 39 x 34 | 19.5 x 17.0 |

Notes:

- A 4K display at 100% still has 154 Stage 1 labels. That is half of the current
  300, but it may still feel dense. Start with 20 DIP; if the manual benchmark
  shows that 154 labels are still too crowded, switch the constant to 24 DIP
  (about 130 labels at 4K 100%) before adding any new mechanism.
- 7680x2160 with a 20 DIP target cannot satisfy `C + R <= 26`. The relaxation
  selects 17 x 9 = 153 labels with a final cell of about 27 x 27 px
  (ratio about 1.35). This is the documented relaxed case.
- 7680x4320 at 20 DIP relaxes further to 15 x 11 = 165 labels, with a final cell
  of about 35 x 36 px (ratio about 1.8). It still uses exactly the 26 available
  keys, so it is a useful upper-bound test.

### 3.4 Mixed-DPI examples

Assumption: monitors are side by side, so the virtual screen is 5760x2160.

| Primary | Secondary | Target | Chosen grid | Notes |
|---|---|---:|---:|---|
| 3840x2160 @ 200% | 1920x1080 @ 100% | 40 px | 12 x 8 (96) | Primary final about 20 x 17 DIP; secondary final about 40 x 34 DIP (coarse). |
| 1920x1080 @ 100% | 3840x2160 @ 200% | 20 px | 16 x 10 (160) | Relaxed by `C + R`; primary final about 23 x 22 DIP, secondary about 11.5 x 11 DIP (finer, ruler font must shrink). |

The second case is the main reason to keep the maximum-DPI policy as a fallback.
Decide between primary-scale and maximum-scale after measuring Stage 2 ruler
legibility on the higher-DPI secondary monitor.

### 3.5 DPI plumbing

- Add a pure `MonitorInfo` value (bounds, DPI scale, is-primary) to the core
  abstraction layer.
- Extend `IMonitorProvider` to return monitor info, or add a parallel
  `GetMonitorInfos()` method with a clear deprecation path for
  `GetMonitorRects()`.
- Implement effective DPI retrieval in the Win32 layer with
  `GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI)`.
- Fall back to 96 DPI (scale 1.0) if retrieval fails, and log the failure. Do not
  silently guess.
- Pass `targetPixels` into `SelectionManager.StartSelection`; the core remains
  free of Win32 and WPF types.
- On `WM_DPICHANGED` or `WM_DISPLAYCHANGE` during selection, cancel and return
  to standby as required by spec.md section 5.3 / 11.2. Ensure the messages are
  handled by the overlay windows or the provider's message window; a
  message-only window may not receive all DPI notifications.
- Keep click-coordinate conversion in physical pixels. DPI is used only for
  choosing the target size and for DIP rendering.

## 4. Key allocation

### 4.1 Blocks

```text
Left block  (columns): Q W E R T A S D F G Z X C V B   (15 keys)
Right block (rows):    Y U I O P H J K L N M             (11 keys)
```

The blocks are derived from the physical keyboard layout, not alphabetical
order. The user can type a column key with the left hand and a row key with the
right hand in typical grids (`C <= 15`, `R <= 11`). On overflow grids such as
17 x 9, the hand separation is best-effort and must be documented as such.

### 4.2 Allocation algorithm

For valid `C >= 1`, `R >= 1`, and `C + R <= 26`:

1. **Columns**
   - If `C <= 15`, use the first `C` keys of the left block.
   - If `C > 15`, use all 15 left keys, then the first `C - 15` keys of the
     right block.
2. **Rows**
   - If columns did not borrow from the right block:
     - Use the first `R` right keys if `R <= 11`.
     - If `R > 11`, use all 11 right keys, then the unused left keys in left
       block order. (`C + R <= 26` guarantees there are enough unused left
       keys.)
   - If columns borrowed from the right block, `R <= 10` is guaranteed by
     `C + R <= 26`; use the remaining right keys in their original order.

At most one dimension overflows its natural block. The allocation is
deterministic and never shares a key between columns and rows.

Examples:

| C x R | Column keys | Row keys |
|---:|---|---|
| 10 x 8 | Q W E R T A S D F G | Y U I O P H J K |
| 14 x 11 | Q W E R T A S D F G Z X C V | Y U I O P H J K L N M |
| 17 x 9 | Q W E R T A S D F G Z X C V B Y U | I O P H J K L N M |
| 7 x 6 | Q W E R T A S | Y U I O P H |
| 14 x 12 (edge case) | Q W E R T A S D F G Z X C V | Y U I O P H J K L N M B |

### 4.3 Key resolution and state

- `KeyLayout` exposes `Allocate(columnCount, rowCount)` returning the two key
  arrays. The old single shared `KeySequence` lookup must not remain the input
  path; keep it only if a test needs it for comparison, and mark it obsolete.
- Add membership-based resolution:
  - `TryGetColumnIndex(key, out index)`
  - `TryGetRowIndex(key, out index)`
- `SelectionManager` stores the allocated `ColumnKeys` and `RowKeys` for the
  whole session. Stage 2 uses the same arrays (and the same counts).
- A column key is never accepted as a row key and vice versa. For example, at
  10 x 8, `Q` is a valid column key but must be rejected as the second key.
- Backspace replay must use the same allocation. Because the allocation is
  deterministic from `(C, R)`, replaying the history is sufficient; add a test
  that proves it.
- If a future design allows Stage 2 counts to differ, use prefixes of the
  session key arrays. Never reassign key identities between stages; that would
  break Backspace and the learned key positions.

### 4.4 Keyboard-layout caveat

The blocks assume a QWERTY or JIS letter layout, where `Q W E R T ...` are the
same physical row. On AZERTY or other remapped layouts, the character-based
labels may not match the physical positions. Document this in README and add a
manual test with the Japanese IME ON/OFF. A scan-code-based remapping is a
future enhancement, not part of this work unit.

## 5. Label and ruler presentation

### 5.1 Stage 1 before any key

- Draw a two-character label in every valid cell.
- Left character: column key, cyan.
- Right character: row key, amber.
- Use a semi-transparent dark pill behind both characters.
- Use the actual `pixelsPerDip` value from `VisualTreeHelper.GetDpi(this)` when
  creating `FormattedText`; the current hard-coded `1.0` must be removed.
- Target font size is 18-20 DIP. Clamp down only for small visible fragments.
- Do not use an alphabetical A-Z order. The physical block order is intentional.

### 5.2 Stage 1 after the column key

- Keep the selected column highlighted.
- Hide or strongly dim labels in other columns.
- In the active column, show only the row key, in amber, at a larger size.
- Keep the same visual rhythm for every stage: first choose a column, then a
  row.

### 5.3 Stage 2

- **Do not add two-character labels inside Stage 2 sub-cells.** Stage 2 keeps
  the existing outside rulers.
- Horizontal ruler: sub-column keys, cyan.
- Vertical ruler: sub-row keys, amber.
- After the third key, highlight the selected sub-column segment and suppress
  the other sub-column labels; keep the vertical ruler visible.
- The fourth key clicks immediately; no confirmation UI.
- Because Stage 2 now reuses Stage 1 counts, the rulers contain `C` and `R`
  labels, not the previously recalculated `C2` and `R2`. Size the ruler font from
  the actual segment spacing and strip thickness, clamped to a readable minimum.
  This is mainly needed for mixed-DPI secondary monitors.

### 5.4 Label placement and click target

- Place the Stage 1 badge slightly above the cell center, not directly over the
  center. A practical rule is:
  - `labelCenterY = cellCenterY - (badgeHeight / 2 + 6 DIP)`,
  - clamp the badge inside the visible cell,
  - fall back to a smaller font or the center if the cell is too short.
- The badge should leave a 4-8 DIP clearance above the cell center when the cell
  is large enough. With a 28 DIP badge, that is roughly 20 DIP above the center,
  not 12 DIP.
- Do not draw a center marker in every cell. Draw a small crosshair at the
  resolved early-click point only while `Enter` is available: after the Stage 1
  row key (two keys), before the Stage 2 sub-column key. This helps the optional
  early click without adding clutter.
- In Stage 2, the rulers stay outside the selected cell and must not cover the
  click target. Preserve the existing edge-flip and inside fallback behavior.

### 5.5 Colors, contrast, and accessibility

Proposed palette:

| Element | Color |
|---|---|
| Column key | Cyan (`#00E5FF` or similar) |
| Row key | Amber (`#FFC857` or similar) |
| Badge background | Dark neutral, high opacity |
| Active badge border | White or bright cyan |
| Active column highlight | Cyan, low alpha |
| Selected cell highlight | Neutral white/blue, low alpha |

Requirements:

- Position (left/right character, horizontal/vertical ruler) must carry the
  same meaning as color. Do not rely on color alone.
- Check the overlay against light, dark, and grayscale content.
- Keep the grid lines and active highlights from washing out the labels.
- Avoid using green as a semantic color if it conflicts with the amber row key.

### 5.6 Monitor ownership

The current renderer draws a label at the center of each visible fragment. A cell
that spans two monitors can therefore show the same label twice. Fix this:

- Define one owner monitor per cell: the monitor containing the cell's reference
  point; if the reference point is in a gap, use the monitor with the largest
  intersection area; tie-break deterministically.
- Draw the Stage 1 label only on the owner monitor, using the visible fragment
  on that monitor for placement.
- Draw Stage 2 rulers only on the owner monitor.
- Add unit tests for a cell spanning two monitors and for a cell spanning a gap.
- Keep the behavior deterministic; do not depend on overlay enumeration order.

### 5.7 Rendering performance

- Cache glyph layouts per character, brush, font size, and `pixelsPerDip` rather
  than creating `FormattedText` for every label on every render. There are at
  most 26 column keys and 26 row keys.
- Reuse the cached glyph widths for badge centering.
- Measure the 4K 100% case with 154 labels. The existing performance target is
  100 ms per key update; aim to stay well below it.
- Do not add continuous animation or screen-content sampling.

## 6. Implementation phases

### Phase 1: Grid semantics and sizing (must be one coherent work unit)

This combines the review's Stage 2 correction with the 20 DIP target. Do not
release same-count Stage 2 alone at the old 10 px target; the rulers would be too
dense.

- Fix `SelectionManager.SetupStage2Divisions()` to reuse Stage 1 counts and
  recalculate only the selected cell boundaries.
- Add the target parameter to `GridCalculator.CalculateDivisions()` and implement
  the `C + R <= 26` relaxation.
- Add DPI scale and primary-monitor information to the monitor provider.
- Choose the target from the primary monitor's DPI scale.
- Replace the `(0,0)` primary-overlay heuristic with the primary-monitor flag.
- Handle DPI/display changes during selection.
- Update spec.md glossary, 4.1, 4.2, 10, 11.2, 13, 14.1, and 15.
- Update tests for divisions, same-count Stage 2, final cell size, DPI, and
  relaxation.
- Validate with `dotnet test` and a manual 1920/4K 100/150/200% smoke test.

### Phase 2: Key allocation split

Depends on Phase 1. The current 2560x1440 grid needs 16 + 12 = 28 keys and the
current 4K grid needs 20 + 15 = 35 keys; neither can be split into disjoint
column and row sets. Do not start this phase until `C + R <= 26` is enforced.

- Implement `KeyLayout.Allocate()` and membership-based lookup.
- Store per-session column/row key arrays in `SelectionManager`.
- Update Stage 1 and Stage 2 input resolution.
- Update OverlayRenderElement label/ruler lookup to use the session arrays.
- Rewrite tests that currently use `Q`/`W`/`E` as both column and row keys.
- Update spec.md sections 3.3, 13.1, 13.3, 14.1, and 15, plus README examples.
- Validate all cross-set rejection and Backspace replay cases.

### Phase 3: Label and ruler presentation

- Add the cyan/amber two-character label rendering and the stage-aware
  one-character emphasis.
- Move badges above the cell center with clamping; add the early-Enter marker.
- Add Stage 2 ruler color and dynamic font sizing.
- Add monitor ownership for labels and rulers.
- Fix `pixelsPerDip` and add glyph caching.
- Update spec.md section 5.2 and the manual checks in 14.2.
- Validate contrast, multi-monitor boundary cases, and rendering time.

### Phase 4: Manual UX benchmark and tuning

- Compare the new layout with the current behavior on 1920x1080 and 3840x2160
  at 100/150/200% DPI.
- Include mixed-DPI cases in both monitor orders.
- Measure time to target, Backspace count, wrong-key count, and subjective
  readability.
- Tune only the constants first: 20 vs 24 DIP, primary vs maximum DPI scale,
  and the label offset. Do not add a precision mode until the simple version has
  been measured.

## 7. Spec and documentation checklist

Update these in the same work unit as the corresponding code change:

| Document / section | Required change |
|---|---|
| spec.md glossary | Replace "10 physical px" with "20 DIP on the primary monitor's effective DPI" and explain mixed-DPI relativity. |
| spec.md 3.3 | Replace the shared 26-key sequence with left/right blocks, the allocation rule, overflow behavior, and Stage 2 reuse. Update the 1920 example. |
| spec.md 4.1 | Replace the 10 px formula/table with the 20 DIP target, primary DPI conversion, `C + R <= 26`, and the relaxed 7680 example. |
| spec.md 4.2 | State explicitly that Stage 2 reuses Stage 1 `C` and `R` and only recalculates boundaries. Remove the old 10 px/3-division example or update it. |
| spec.md 5.2 | Keep Stage 2 rulers; change the center-label wording, add cyan/amber, progressive disclosure, off-center placement, early-Enter marker, and owner-monitor rule. |
| spec.md 10 | Update the target, key allocation, and state that precision mode is not part of this version. |
| spec.md 11.1 | Keep performance targets; add the 154-label 4K check to validation. |
| spec.md 11.2 | Ensure DPI/display changes cancel selection through `WM_DPICHANGED` and `WM_DISPLAYCHANGE`. |
| spec.md 13.1 / 13.3 | Update `KeyLayout`, `SelectionManager`, and `IMonitorProvider` responsibilities; add monitor DPI/primary information. |
| spec.md 14.1 | Replace old division and 10 px tests; add same-count Stage 2, cross-set rejection, owner monitor, DPI relaxation, and 125/175/200% cases. |
| spec.md 14.2 | Add mixed-DPI ruler legibility, off-center label, grayscale/contrast, and monitor-boundary checks. |
| spec.md 15 | Update the acceptance layouts and target-size checks. |
| README.md | Update the key examples and usage; replace shared-key examples such as `QQ`, `WE`, and `ED` with disjoint examples such as `QY`, `WU`, and `RI`; explain the left/right blocks, 20 DIP target, mixed-DPI limitation, and QWERTY/JIS assumption. |
| docs/implementation_plan.md | Update the test matrix and key/grid descriptions that still assume the shared sequence and 10 px target. Preserve completed historical phases. |

## 8. Test plan

### 8.1 Core unit and property tests

- `GridCalculator`:
  - minimum counts for 1920x1080, 2560x1440, 3840x2160, 7680x2160, and 8K,
  - `C + R <= 26` for all returned pairs,
  - final size <= target when the key budget permits,
  - relaxed case is minimax and deterministic,
  - 125/150/175/200% target values,
  - replace the old 26 x 26 clamp expectation with a relaxed pair whose counts
    are each at most 26 and whose sum is at most 26,
  - boundaries still partition without gaps or overlaps.
- `KeyLayout`:
  - exact left/right blocks,
  - allocation for all valid `(C, R)` pairs,
  - unique keys, correct lengths, overflow behavior,
  - case-insensitive lookup, invalid keys rejected.
- `SelectionManager`:
  - Stage 2 counts equal Stage 1 counts,
  - four-key final size matches the formula,
  - cross-set keys are rejected,
  - Backspace replay preserves allocation and bounds,
  - Enter only after the first two keys,
  - click target resolution unchanged.
- `Stage2LabelLayout`:
  - rulers remain outside the selected cell when possible,
  - 14 x 11 and 17 x 9 label centers fit within the strip,
  - dynamic font sizing fits the minimum segment spacing,
  - edge flipping and inside fallback still work.
- `MonitorOwnership`:
  - cell wholly inside one monitor,
  - cell spanning two monitors,
  - cell spanning a gap,
  - deterministic tie-breaking.

### 8.2 Integration / manual tests

- Single monitor 1920x1080, 2560x1440, 3840x2160.
- 100, 125, 150, 175, 200% DPI.
- Mixed DPI in both orders; primary at a non-zero origin.
- Negative-origin and L-shaped layouts.
- 14 x 11 (4K 100%) and 10 x 8 (1920 100%) label scans.
- 7680x2160 relaxed layout.
- Stage 2 rulers near screen edges, with the selected cell spanning monitors.
- Early Enter, Backspace at each step, Esc, focus loss, IME ON/OFF.
- Rendering time for 154 labels at 4K 100%.
- Verify no duplicate label appears on monitor boundaries.

### 8.3 Commands

```powershell
dotnet test --nologo
```

A docs-only revision of this plan does not require code tests. The current
baseline is 80 passing tests; record the new test count after each phase.

## 9. Acceptance criteria

- `C + R <= 26` for every selectable grid.
- No key is shared between columns and rows.
- Stage 2 uses the same `C` and `R` as Stage 1; a four-key selection meets the
  final-size target in the normal case and the documented relaxed cases on
  7680x2160 and 7680x4320.
- 1920x1080 at 100% uses 10 x 8 = 80 Stage 1 labels and about a 20 x 17 px final
  cell.
- 3840x2160 at 100% uses 14 x 11 = 154 Stage 1 labels and about a 20 x 18 px
  final cell.
- The 7680x2160 relaxed case uses 17 x 9 = 153 labels and a final cell of about
  27 x 27 px.
- Stage 1 labels are readable at the tested DPI scales; Stage 2 rulers remain
  outside the selected cell when space permits, use the specified inside
  fallback near screen edges, and do not cover the click target.
- A cell spanning monitors shows exactly one label/ruler set on its owner
  monitor.
- The app still completes a four-key click in the existing state-machine order
  and preserves Backspace/Enter/Esc behavior.
- All existing and new tests pass.

## 10. Explicit non-goals and deferred items

- **Do not add a third stage or a precision mode in this work unit.** A 10 DIP
  target with the current ruler UI is not readable enough, and the settings are
  fixed in this version. If finer precision is needed later, design a third
  stage or a different Stage 2 presentation and add it with a setting.
- **Do not add number/punctuation keys.** Prefer a coarser grid over a larger,
  less memorable key set.
- **Do not switch to alphabetical A-Z order.** It does not follow the physical
  keyboard and is harder to type.
- **Do not add two-character labels inside Stage 2 sub-cells.** Keep rulers.
- **Do not claim uniform 20 DIP across mixed-DPI monitors.** The target is
  relative to the primary monitor.
- **Do not use the old `(0,0)` primary-monitor heuristic.**

## 11. Risks and open decisions

1. **Primary vs maximum DPI scale.** Primary scale favors the main work area;
   maximum scale favors ruler readability on high-DPI secondary monitors. Start
   with primary scale and decide after the mixed-DPI benchmark.
2. **20 vs 24 DIP.** 20 DIP gives 154 labels at 4K 100%; 24 DIP gives about 130.
   Tune the constant after a real findability test.
3. **Ruler density after same-count Stage 2.** At 20 DIP the spacing is readable
   on single monitors, but mixed-DPI secondary monitors can be tighter. Dynamic
   ruler font sizing is required, and the minimum readable size needs a visual
   check.
4. **Off-center label position.** The 4-8 DIP clearance above the click target
   is a starting point; tune it based on readability, not by moving the label
   back over the target.
5. **Early Enter precision.** A coarser grid makes the early click less precise.
   The center marker and the Stage 2 path mitigate this, but benchmark it.
6. **QWERTY/JIS assumption.** Alternate keyboard layouts may require remapping;
   document the limitation now and treat remapping as future work.
7. **Performance.** Two-color labels and 154 labels can increase render cost;
   glyph caching and the 100 ms key-update budget are part of acceptance.
