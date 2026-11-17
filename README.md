# KeyGrid

KeyGrid is a lightweight, resident Windows 11 utility that enables mouse-free left, right, and middle clicking using keyboard navigation. It overlays an adaptive two-stage grid across all active displays. Choose a coarse cell with two letter keys, then position a 5×5 cursor with the arrow keys and use `Enter`, `Shift+Enter`, or `Ctrl+Enter` to click.

---

## Features

- **Keyboard-Driven Precision**: Select a coarse region with two disjoint column/row letter keys, navigate a 5×5 sub-grid with arrow keys, and use `Enter`, `Shift+Enter`, or `Ctrl+Enter` to left-, right-, or middle-click.
- **Multi-Monitor Support**: Automatically maps across all active displays, handles non-standard topologies (L-shaped, vertical offsets, gaps), and operates with Per-Monitor DPI Awareness V2.
- **Background Resident Utility**: Runs silently in the system tray with near-zero CPU and memory overhead while idle.
- **IME & Repeat Protection**: Automatically suppresses IME during grid selection and ignores accidental key repeats.
- **Safe Fallback**: Excludes gaps between disjoint monitors, snapping center reference points to the closest valid display pixel.
- **Continuous Mode**: Press `Tab` while the grid is visible to keep it open after each click and return to Stage 1 for the next selection. Press `Tab` again for normal mode, or `Esc` to exit.
- **Undo & Cancellation**: Step back through Stage 1 with `Backspace` or cancel immediately with `Esc`.

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

### 3. Automatic Clicking

- Press **`Enter`** in Stage 2 to resolve the highlighted cell, hide the overlay, and dispatch a left click.
- Press **`Shift+Enter`** in Stage 2 to dispatch a right click at the highlighted cell.
- Press **`Ctrl+Enter`** in Stage 2 to dispatch a middle (wheel) click at the highlighted cell.
- **Confirm Without Moving**: Use `Enter`, `Shift+Enter`, or `Ctrl+Enter` as soon as Stage 2 appears to click its initially highlighted, center-most valid cell.
- The same confirmation keys work immediately after the two Stage 1 keys. Other modifier combinations with `Enter` are ignored.
- The click kind is captured when `Enter` is pressed. If the target should not see the modifier held during the click (for example `Ctrl`+middle-click), release the modifier before releasing `Enter`; the selected click kind does not change.
- **Continuous Mode**: Press `Tab` while the grid is visible. After each click, the grid returns to Stage 1 for the next selection. A `連続モード` badge appears on the primary monitor while the mode is on. Press `Tab` again for normal mode, or `Esc` to exit.

### 4. Correcting and Cancelling

| Key | Action |
|---|---|
| **`Backspace`** | Returns to the previous Stage 1 selection step and recalculates the active selection area. Arrow movement is not part of the undo history. |
| **`Tab`** | Toggles continuous mode while the grid is visible. In continuous mode, each click returns to Stage 1 instead of exiting. |
| **`Esc`** | Cancels the selection mode (including continuous mode) and returns to standby. |
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
| Column or row keys feel physically shifted | The key blocks assume QWERTY/JIS physical rows. On AZERTY and other remapped letter layouts, labels use characters rather than physical scan-code positions. |
| The click is not delivered | The target may run at a higher integrity level (for example, elevated). Windows UIPI blocks synthetic input to such windows; run both applications at the same integrity level. |
| A second launch exits immediately | KeyGrid is already running. Only one instance is allowed. |
| More diagnostics are needed | See `%LOCALAPPDATA%\KeyGrid\logs\keygrid_YYYYMMDD.log`. Logs are retained for 7 days. |

---

## License

This project is licensed under the terms specified in the [LICENSE](LICENSE) file.
