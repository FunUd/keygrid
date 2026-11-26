# Changelog

## [0.6.0] - 2026-11-27

### Added

- Add `Ctrl+Shift+Enter` in Stage 2 as a double left click at the confirmed coordinate. It follows the same confirmation rules as `Enter` (ignored in Scroll Mode and while a range or drag anchor is active) and shares the normal/continuous-mode completion behavior.
- Add Fine Mode in Stage 2, entered with `F`: arrow keys move a fine adjustment point by 5 DIP and Page keys by one Stage 2 cell (20 DIP), and all confirmation keys click the fine point. `Esc` returns to Stage 2. The point is drawn as a crosshair marker and never leaves the monitor display areas.
- Add `S` in Stage 2 as a direct trigger for the range-then-drag workflow (previously reachable only via the overlapping `R`+`D` chord, which still works). `S` remains a Stage 1 column key.
- Allow `R`, `D`, `S`, and the `R`+`D` chord inside Fine Mode to start a range, drag, or range-then-drag workflow anchored at the fine-adjusted point, then return to Stage 1.
- Allow Fine Mode (`F`) during range/drag endpoint selection, including both phases of the range-then-drag workflow: the fine point becomes the selection endpoint on `Enter`, and in the workflow it also becomes the drag anchor.
- Restore the original application icon artwork with a transparent background instead of an opaque tile, so it blends into the taskbar.

### Changed

- Raise the Stage 0/1 cell-badge fill opacity so the key labels stay legible over white backgrounds: the pill now composites to a dark surface instead of mid-gray, with a slightly stronger border and more opaque active state.
- Switch the Stage 0/1 cell key labels to Semibold so the glyphs keep enough stroke weight at small sizes.
- Redraw the range/drag anchor and Fine Mode target markers as a minimal reticle: a thin ring-and-crosshair with a center dot, rendered as a dark silhouette under a bright accent (green for anchors, amber for the fine point) — the same double-stroke approach as the grid lines, replacing the previous filled halo marker.

## [0.5.0] - 2026-11-26

### Added

- Add an adaptive Stage 0 coarse grid for monitors where the 26-key Stage 1 budget cannot keep cells near the target size. A deterministic coarse split is chosen to minimize the resulting fine-cell deviation and is selected with the usual column+row key pair, after which Stage 1 runs inside the chosen region. Monitors that already meet the target skip Stage 0 and keep the two-keystroke flow.
- Shrink the selection targets for finer click accuracy: Stage 1 now targets approximately 112 DIP cells (was 128 DIP) and Stage 2 uses a fixed 500 DIP window of 25×25 cells at 20 DIP each (was 600 DIP / 24 DIP).
- Add an application icon, used for the executable, taskbar, Alt+Tab, and the system tray.

### Changed

- Tone down the Stage 0/1 grid visuals: cell badges now use a translucent neutral fill with a thinner border, smaller regular-weight labels in muted hues, and the grid frame, highlights, grid lines, and dim mask are all lower-opacity so the overlay reads quieter over arbitrary content. Stage 2 rendering is unchanged.

## [0.4.1] - 2026-11-25

### Changed

- Reassign grid-display keys: `Tab`/`Shift+Tab` now cycle the Stage 1 grid monitor forward/backward in spatial order (digits `1`–`9` still jump directly), and `;` toggles continuous mode instead of `Tab`.
- Add `C` in Stage 2 as a confirm-and-continue key: it clicks like `Enter` (`Shift+C` right, `Ctrl+C` middle) and enters continuous mode in one step. `C` stays a normal column key in Stage 1 and is ignored while a range or drag anchor is active.

## [0.4.0] - 2026-11-24

### Changed

- Scope the Stage 1 grid to a single monitor instead of the whole virtual desktop. Each monitor now gets the same division density as a single-monitor setup, fixing unusably large cells on multi-monitor and large-display configurations. The initial grid monitor is the one hosting the foreground window (falling back to the primary monitor); press `1`–`9` during selection to move the grid to another monitor. Every monitor shows an `n/N` badge in spatial order (top-to-bottom, then left-to-right) and the grid monitor is marked `Grid n/N`. Switching monitors resets the Stage 1 keystroke history but preserves range/drag anchors, so cross-monitor drag-and-drop still works: set the anchor on one monitor, switch with a digit key, and pick the endpoint on another. Stage 2 keeps its fixed 25×25 window and may still reach into neighboring monitors at the edges.

## [0.3.2] - 2026-11-23

### Changed

- Show only the red cursor cell highlight and status badges while Scroll Mode is active. The 25×25 grid, window borders, and dim mask are hidden so the window being scrolled stays unobstructed.

## [0.3.1] - 2026-11-21

### Changed

- Skip the full-screen dim mask while Scroll Mode is active so the window being scrolled stays fully visible. The Stage 2 context window border, the cursor cell highlight, and the status badges remain.

## [0.3.0] - 2026-11-21

### Added

- Add a range-then-drag workflow: press and hold `R` and `D` together in Stage 2 to capture a range anchor, select the range endpoint, and automatically reuse that endpoint as the drag anchor for the drop-target selection. `F` in Stage 2 starts the same workflow directly. Single `R`/`D` presses and Stage 1 letter keys are unchanged.
- Keep the overlays hidden during a scroll burst: the first scroll input hides the grid and activates the target once, a low-level keyboard hook keeps feeding scroll keys while the target owns the foreground, and the Stage 2 grid returns when input pauses. This removes the per-notch hide/show flicker.

### Changed

- Arrow and Page key repeats now scroll continuously in Scroll Mode, so holding a key keeps scrolling without losing the Stage 2 selection.

### Fixed

- Keep a pending `R`+`D` chord alive when a modifier key is pressed between the chord keys.

## [0.2.0] - 2026-11-20

### Changed

- Add cross-window drag-and-drop: in Stage 2, press `D`, select the drop-target region from Stage 1, and press `Enter`.
- Return to Stage 1 after setting a range or drag anchor, preserving the anchor while selecting the second-point region.
- Target Stage 1 cells at approximately 128 DIP using a deterministic division search within the 26-key budget, and use the foreground monitor's DPI for sizing with primary/first-monitor fallbacks.
- Use a fixed 25×25 Stage 2 window with 24 DIP cells (600 DIP total). Keep the selected Stage 1 cell as the visual focus while the surrounding context provides nearby correction; the fixed click cell no longer scales with Stage 1.
- Keep all monitors covered and use the virtual desktop bounds for click, range, and scroll coordinate normalization, including layouts with negative monitor origins.
- Preserve continuous Stage 2 arrow-key movement.

## [0.1.0] - 2026-11-18

### Added

- Add a two-point range selection: press `R` in Stage 2 to set an anchor, move to the endpoint, and press `Enter` to left-click the anchor and Shift+left-click the endpoint. The action is intended for browser text selection and works with continuous mode.
- Add Scroll Mode: press `PageUp` or `PageDown` in Stage 2 to deliver wheel input to the current target. Arrow keys scroll by one notch, Page keys by five, and `Esc` returns to Stage 2.
- Add continuous mode: press `Tab` while the grid is visible to return to Stage 1 after each click; press `Tab` again for normal mode. `Esc` still exits.
- Add keyboard-triggered right-click support with `Shift+Enter`.
- Add keyboard-triggered middle (wheel) click support with `Ctrl+Enter`.
- Add `SECURITY.md` with vulnerability reporting, release verification, signing, deployment, and repository-hardening guidance.
- Add Dependabot updates for NuGet packages and GitHub Actions.

### Changed

- Include `LICENSE` in publish output and exclude PDB files from release archives.
- Show the assembly version in the tray menu instead of a hardcoded version.
- Grant foreground permission only to the KeyGrid process instead of every process.
- Reduce default diagnostic metadata in logs and error notifications, add the `KEYGRID_LOG_LEVEL` environment variable, and cap each daily log file at 5 MB.
- Harden the release workflow with pinned actions, least-privilege jobs, a checksum asset, and optional Authenticode signing.

### Security

- Add SHA-256 checksums to releases and document signature verification.
- Pin GitHub Actions to commit SHAs and restrict the release token to the release job.
- Restrict NuGet package sources to nuget.org and enable NuGet auditing.
- Mark unsigned release builds explicitly in the release notes while keeping signing optional for development builds.
- Validate the final release archive for `LICENSE` and absent PDB files regardless of signing.
- Make release publication retry-safe by replacing assets and notes when the release already exists.

### Fixed

- Release synthetic Shift and the left mouse button when a range input sequence is only partially accepted.
- Prevent the grid from staying visible after focus moves to another window. Visible application activation now closes it immediately, and transient focus loss is recovered without leaving the grid behind.
- Dispatch the click to the window under the selected coordinate instead of restoring the pre-selection window, which could receive the click instead.

## [0.0.1]

### Added

- Initial release of KeyGrid, a tray-resident keyboard-driven clicking utility.
- Two-stage grid selection with multi-monitor and per-monitor DPI support.
