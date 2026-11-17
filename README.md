# KeyGrid

KeyGrid is a lightweight, resident Windows 11 utility that enables mouse-free left, right, and middle clicking using keyboard navigation. It overlays an adaptive two-stage grid across all active displays. Choose a coarse cell with two letter keys, then position a 5×5 cursor with the arrow keys and use `Enter`, `Shift+Enter`, or `Ctrl+Enter` to click. A two-point range action extends browser text selections without touching the mouse, and Scroll Mode delivers wheel input while the grid remains available.

---

## Features

- **Keyboard-Driven Precision**: Select a coarse region with two disjoint column/row letter keys, navigate a 5×5 sub-grid with arrow keys, and use `Enter`, `Shift+Enter`, or `Ctrl+Enter` to left-, right-, or middle-click.
- **Multi-Monitor Support**: Automatically maps across all active displays, handles non-standard topologies (L-shaped, vertical offsets, gaps), and operates with Per-Monitor DPI Awareness V2.
- **Background Resident Utility**: Runs silently in the system tray with near-zero CPU and memory overhead while idle.
- **IME & Repeat Protection**: Automatically suppresses IME during grid selection and ignores accidental key repeats.
- **Safe Fallback**: Excludes gaps between disjoint monitors, snapping center reference points to the closest valid display pixel.
- **Continuous Mode**: Press `Tab` while the grid is visible to keep it open after each click and return to Stage 1 for the next selection. Press `Tab` again for normal mode, or `Esc` to exit.
- **Two-Point Range Selection**: In Stage 2, press `R` to set an anchor, move to the endpoint, and press `Enter`. KeyGrid left-clicks the anchor and Shift+left-clicks the endpoint, which extends a browser text selection without using the mouse.
- **Scroll Mode**: Press `PageUp` or `PageDown` in Stage 2 to scroll the window under the current target. Arrow keys scroll by one wheel notch and Page keys by five; `Esc` returns to Stage 2 without leaving the grid.
- **Undo & Cancellation**: Step back through Stage 1 with `Backspace` or cancel immediately with `Esc`. With a range anchor or Scroll Mode active, `Esc` cancels that sub-mode first.

---

## Requirements

- **Operating System**: Windows 11 (x64)
- **Build**: [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **Running a release**: .NET 10 Desktop Runtime
- **Privileges**: Standard user permissions (Administrator privileges not required)

---

## Building from Source

Clone the repository and build using the .NET CLI:

```powershell
# Restore dependencies and build the entire solution
dotnet build

# Run the unit and integration test suite
dotnet test
```

### Publishing a Release Binary

To publish and package the application for Windows x64:

```powershell
dotnet publish src/KeyGrid/KeyGrid.csproj -c Release -r win-x64 --self-contained false -o .dist\publish
$version = ([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
Compress-Archive -Path .dist\publish\* -DestinationPath ".dist\KeyGrid-$version-win-x64.zip"
```

The resulting archive contains the complete publish output, including `KeyGrid.exe`. It requires the .NET 10 Desktop Runtime on the target machine.

The application version is defined in `Directory.Build.props`, and release notes are maintained in `CHANGELOG.md`. Pushing a tag in the `vMAJOR.MINOR.PATCH` format runs the Windows test and publish workflow and creates a GitHub Release with the packaged application. The tag must match the project version and have a corresponding changelog section.

---

## Launching KeyGrid

### Running in Development

```powershell
dotnet run --project src/KeyGrid/KeyGrid.csproj
```

### Running the Published Binary

Extract the release archive, then double-click `KeyGrid.exe` or execute it from PowerShell:

```powershell
.\KeyGrid.exe
```

The extracted publish folder is portable; no installer is required. Keep it in a permanent location and create a shortcut if desired.

KeyGrid runs as a background process and displays an icon in the Windows system notification tray. If KeyGrid is already running, starting a second instance will display a notification and exit immediately.

---

## Basic Usage

### 1. Activating the Grid

Press the global hotkey:

$$\text{Ctrl} + \text{Shift} + \text{Space}$$

Full-screen overlays will appear across all connected monitors. Stage 1 displays a two-letter label (column key + row key) in each valid cell. After choosing a region, Stage 2 shows a 5×5 sub-grid with its center-most valid cell highlighted in red.

### 2. Selecting Coordinates (2 Stages)

KeyGrid splits the 26 letters into a left-hand column block and a right-hand row block. The roles never overlap, so a column key cannot be used as a row key or vice versa:

```text
Column block: Q W E R T A S D F G Z X C V B   (15 keys)
Row block:    Y U I O P H J K L N M             (11 keys)
```

If a grid needs more than 15 columns or more than 11 rows, KeyGrid borrows the remaining keys deterministically while keeping the two sets disjoint. Typical grids do not need borrowing.

The blocks assume the physical rows of a QWERTY or JIS keyboard. On layouts such as AZERTY, the displayed letters may not match the same physical key positions; character-based remapping remains a future enhancement.

1. Press the column letter shown in the target Stage 1 cell. The column highlights.
2. Press the row letter shown in that cell. The grid zooms into the selected region.
3. Use the arrow keys to move the red cursor through valid sub-cells. Cells outside monitor areas are skipped.
4. Press **`Enter`** to left-click, **`Shift+Enter`** to right-click, or **`Ctrl+Enter`** to middle-click the highlighted cell.
5. To extend a browser text selection, press **`R`** in Stage 2 to set the range start, move the cursor to the end point, and press **`Enter`**. The anchor stays marked in green while the end point moves.
6. To scroll the target window without clicking, press **`PageUp`** or **`PageDown`** in Stage 2. While Scroll Mode is active, arrow keys scroll by one notch, Page keys scroll by five, and **`Esc`** returns to Stage 2.

### 3. Automatic Clicking

- Press **`Enter`** in Stage 2 to resolve the highlighted cell, hide the overlay, and dispatch a left click.
- Press **`Shift+Enter`** in Stage 2 to dispatch a right click at the highlighted cell.
- Press **`Ctrl+Enter`** in Stage 2 to dispatch a middle (wheel) click at the highlighted cell.
- **Confirm Without Moving**: Use `Enter`, `Shift+Enter`, or `Ctrl+Enter` as soon as Stage 2 appears to click its initially highlighted, center-most valid cell.
- The same confirmation keys work immediately after the two Stage 1 keys. Other modifier combinations with `Enter` are ignored.
- The click kind is captured when `Enter` is pressed. If the target should not see the modifier held during the click (for example `Ctrl`+middle-click), release the modifier before releasing `Enter`; the selected click kind does not change.
- **Two-Point Range**: In Stage 2, press `R` to use the current target as the anchor. The primary overlay shows `Range: Enter to select`, and a green ring marks the anchor. Move to the endpoint, then press `Enter`. KeyGrid left-clicks the anchor and Shift+left-clicks the endpoint. Both endpoints must be in the same top-level window; otherwise the range is cancelled with an error and Stage 2 remains available.
- **Scroll Mode**: In Stage 2, press `PageUp` or `PageDown` to enter Scroll Mode. A `Scroll Mode` badge appears on the primary overlay. `Up`/`Down` scroll vertically by one wheel notch, `Left`/`Right` scroll horizontally, and `PageUp`/`PageDown` scroll vertically by five notches. The current Stage 2 target is scrolled without moving the selection cursor. `Esc` exits Scroll Mode and returns to Stage 2; press `Esc` again to leave selection. Scroll Mode is ignored while a range anchor is active, and `R` is ignored while Scroll Mode is active.
- **Continuous Mode**: Press `Tab` while the grid is visible. After each click, the grid returns to Stage 1 for the next selection. A `Continuous Mode` badge appears on the primary monitor while the mode is on. Press `Tab` again for normal mode, or `Esc` to exit.

### 4. Correcting and Cancelling

| Key | Action |
|---|---|
| **`Backspace`** | Returns to the previous Stage 1 selection step and recalculates the active selection area. Arrow movement is not part of the undo history. A range anchor stays fixed when the region changes. |
| **`R`** (Stage 2) | Sets the range anchor at the current target. Pressing `R` again while an anchor is active is ignored. |
| **`PageUp` / `PageDown`** (Stage 2) | Enters Scroll Mode and scrolls the current target vertically. Page keys scroll by five wheel notches; arrow keys scroll by one and also handle horizontal scrolling. |
| **`Tab`** | Toggles continuous mode while the grid is visible. In continuous mode, each click returns to Stage 1 instead of exiting. |
| **`Esc`** | Exits Scroll Mode first, then clears an active range anchor. When neither sub-mode is active, cancels the selection mode (including continuous mode) and returns to standby. |
| **Focus Loss** | Switching to a visible application cancels selection. Transient focus loss to hidden Windows/IME helper windows is recovered automatically. |

If the grid remains visible but no longer receives keyboard input, press **`Ctrl + Shift + Space`** again to restart the selection from the beginning and restore focus to the grid.

### 5. Exiting KeyGrid

Right-click the **KeyGrid** tray icon in the system notification area and select **終了 (Exit)**.

---

## Project Architecture

```
keygrid/
├── src/
│   ├── KeyGrid.Core/          # Pure domain logic (.NET 10 class library, zero Win32/WPF dependencies)
│   │   ├── Calculation/       # Grid partitioning, boundary math, coordinate normalization
│   │   ├── Geometry/          # PixelPoint, PixelRect (half-open [L, R) x [T, B) primitives)
│   │   ├── Selection/         # SelectionManager state machine and history replay
│   │   ├── Services/          # ClickExecutor dispatch synchronization
│   │   └── Abstractions/      # Hardware abstraction interfaces (IMonitorProvider, IClickSender)
│   └── KeyGrid/               # Desktop application (.NET 10 WPF / Windows 11)
│       ├── Interop/           # Win32 P/Invoke (EnumDisplayMonitors, GetDpiForMonitor, SendInput)
│       ├── UI/                # OverlayWindow, OverlayRenderElement (hardware-accelerated DrawingContext)
│       ├── Services/          # HotkeyManager, FileLogger
│       └── app.manifest       # Per-Monitor DPI Awareness V2 declaration
└── tests/
    └── KeyGrid.Tests/         # xUnit unit & integration test suite
```

---

## Logs & Diagnostics

KeyGrid maintains operational logs for troubleshooting at:

```
%LOCALAPPDATA%\KeyGrid\logs\keygrid_YYYYMMDD.log
```

Logs are retained for 7 days and automatically cleaned up on application startup.

---

## Troubleshooting

| Symptom | What to check |
|---|---|
| The hotkey does nothing | Another application may already own `Ctrl + Shift + Space`. The tray tooltip and context menu show whether hotkey registration failed. Close the conflicting application and restart KeyGrid. |
| The overlay closes immediately | Focus moved to a visible application, which deliberately cancels selection. Transient focus changes to IME/TSF helper windows are recovered automatically. |
| The grid returns after every click | Continuous mode is on. Press `Tab` while the grid is visible to return to normal mode, or `Esc` to exit. |
| A range selection does not start | `R` starts a range only in Stage 2. Complete the two Stage 1 keys first, and make sure no anchor is already active. |
| A range is cancelled with an error | Both range endpoints must be under the same top-level window. Move the endpoint to the same browser or application window and try again. |
| The page does not scroll | Scroll Mode is available only in Stage 2, and Page keys are ignored while a range anchor is active. Press `R` only after finishing scrolling, or press `Esc` to clear an existing range anchor. |
| Column or row keys feel physically shifted | The key blocks assume QWERTY/JIS physical rows. On AZERTY and other remapped letter layouts, labels use characters rather than physical scan-code positions. |
| The click is not delivered | The target may run at a higher integrity level (for example, elevated). Windows UIPI blocks synthetic input to such windows; run both applications at the same integrity level. |
| A second launch exits immediately | KeyGrid is already running. Only one instance is allowed. |
| More diagnostics are needed | See `%LOCALAPPDATA%\KeyGrid\logs\keygrid_YYYYMMDD.log`. Logs are retained for 7 days. |

---

## License

This project is licensed under the terms specified in the [LICENSE](LICENSE) file.
