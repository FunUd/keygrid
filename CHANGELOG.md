# Changelog

## [0.2.0] - 2026-11-20

### Changed

- Expand the Stage 2 grid to a 25×25 focus-plus-context window. The central 5×5 matches the selected Stage 1 cell, the outer margin stays at the same cell size for nearby correction, and holding an arrow key now moves the Stage 2 cursor continuously.

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
