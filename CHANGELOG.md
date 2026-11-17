# Changelog

## [Unreleased]

### Added

- Add keyboard-triggered right-click support with `Shift+Enter`.
- Add keyboard-triggered middle (wheel) click support with `Ctrl+Enter`.

### Fixed

- Prevent the grid from staying visible after focus moves to another window. Visible application activation now closes it immediately, and transient focus loss is recovered without leaving the grid behind.
- Dispatch the click to the window under the selected coordinate instead of restoring the pre-selection window, which could receive the click instead.

## [0.0.1]

### Added

- Initial release of KeyGrid, a tray-resident keyboard-driven clicking utility.
- Two-stage grid selection with multi-monitor and per-monitor DPI support.
