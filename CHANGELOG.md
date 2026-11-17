# Changelog

## [Unreleased]

### Added

- Add a two-point range selection: press `R` in Stage 2 to set an anchor, move to the endpoint, and press `Enter` to left-click the anchor and Shift+left-click the endpoint. The action is intended for browser text selection and works with continuous mode.
- Add continuous mode: press `Tab` while the grid is visible to return to Stage 1 after each click; press `Tab` again for normal mode. `Esc` still exits.
- Add keyboard-triggered right-click support with `Shift+Enter`.
- Add keyboard-triggered middle (wheel) click support with `Ctrl+Enter`.

### Fixed

- Release synthetic Shift and the left mouse button when a range input sequence is only partially accepted.
- Prevent the grid from staying visible after focus moves to another window. Visible application activation now closes it immediately, and transient focus loss is recovered without leaving the grid behind.
- Dispatch the click to the window under the selected coordinate instead of restoring the pre-selection window, which could receive the click instead.

## [0.0.1]

### Added

- Initial release of KeyGrid, a tray-resident keyboard-driven clicking utility.
- Two-stage grid selection with multi-monitor and per-monitor DPI support.
