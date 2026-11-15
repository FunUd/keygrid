# KeyGrid

KeyGrid is a lightweight, resident Windows 11 utility that enables mouse-free left clicking using keyboard navigation. It overlays an adaptive 2-stage grid across all active displays, letting you pinpoint any on-screen coordinate with four keystrokes.

---

## Features

- **Keyboard-Driven Precision**: Reach and left-click any on-screen location in 4 keystrokes (Column → Row → Column → Row).
- **Multi-Monitor & Mixed-DPI Support**: Automatically maps across all active displays, handles non-standard topologies (L-shaped, vertical offsets, gaps), and operates with Per-Monitor DPI Awareness V2. The 20 DIP grid target is derived from the primary monitor's effective DPI, so secondary monitors in a mixed-DPI setup can have slightly finer or coarser final cells.
- **Background Resident Utility**: Runs silently in the system tray with near-zero CPU and memory overhead while idle.
- **IME & Repeat Protection**: Automatically suppresses IME during grid selection and ignores accidental key repeats.
- **Safe Fallback**: Excludes gaps between disjoint monitors, snapping center reference points to the closest valid display pixel.
- **Undo & Cancellation**: Step back keystrokes with `Backspace` (recalculating the exact bounding box) or cancel immediately with `Esc` or by clicking outside.

---

## Requirements

- **Operating System**: Windows 11 (x64)
- **Runtime / SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download) (or .NET 10 Desktop Runtime)
- **Privileges**: Standard user permissions (Administrator privileges not required)

---

## Building from Source

Clone the repository and build using the .NET CLI:

```powershell
# Restore dependencies and build the entire solution
dotnet build

# Run the comprehensive test suite (100+ unit and integration tests)
dotnet test
```

### Publishing a Release Binary

To publish the application for release:

```powershell
dotnet publish src/KeyGrid/KeyGrid.csproj -c Release -o .dist
```

The resulting executable will be available at `.dist\KeyGrid.exe`.

---

## Launching KeyGrid

### Running in Development

```powershell
dotnet run --project src/KeyGrid/KeyGrid.csproj
```

### Running the Published Binary

Double-click `.dist\KeyGrid.exe` or execute it from PowerShell:

```powershell
.\.dist\KeyGrid.exe
```

The publish output folder is portable; no installer is required. Keep the folder in a permanent location and create a shortcut if desired.

KeyGrid runs as a background process and displays an icon in the Windows system notification tray. If KeyGrid is already running, starting a second instance will display a notification and exit immediately.

---

## Basic Usage

### 1. Activating the Grid

Press the global hotkey:

$$\text{Ctrl} + \text{Shift} + \text{Space}$$

Full-screen transparent overlays will appear across all connected monitors. In Stage 1, each grid section displays a 2-letter label (Column key + Row key, e.g., `QQ`, `WE`, `RI`) directly in the cell. In Stage 2, small key rulers appear just outside the zoomed cell, keeping the sub-cell centers clear.

The grid targets a 20 DIP final cell on the primary monitor's effective DPI. Typical Stage 1 counts are 10×8 at 1920×1080 100%, 12×9 at 2560×1440 100%, and 14×11 at 3840×2160 100%. On mixed-DPI systems, the target is not uniform across monitors; a higher-DPI secondary display may show finer cells.

### 2. Selecting Coordinates (2 Stages)

KeyGrid uses an ordered subset of 26 keys:

```
Q  W  E  R  T  Y  U  I  O  P  A  S  D  F  G  H  J  K  L  Z  X  C  V  B  N  M
```

- **Stage 1**:
  1. Look directly at your target on the screen and read the 2-letter label in that cell (e.g., `RI`).
  2. Press the first letter (`R`). The active column highlights.
  3. Press the second letter (`I`). The grid immediately zooms into that cell.
- **Stage 2**:
  4. Read the sub-column key from the ruler just above or below the zoomed cell (e.g., `E`). The ruler flips to the opposite side near a screen edge.
  5. Press the third letter (`E`). The chosen sub-column and its ruler segment highlight.
  6. Read the sub-row key from the ruler just left or right of the cell (e.g., `I`), then press the fourth letter (`I`).

### 3. Automatic Clicking

- When the fourth keystroke completes Stage 2, KeyGrid immediately resolves the click coordinate, hides the overlay, and dispatches the left click. No confirmation screen is shown.
- **Early Click**: You can also press **`Enter`** immediately after completing Stage 1 (after the first 2 keystrokes) to click the center of the Stage 1 cell without performing Stage 2.

### 4. Correcting and Cancelling

| Key | Action |
|---|---|
| **`Backspace`** | Undoes the last keystroke and recalculates the active selection area. Available up to the third keystroke; the fourth keystroke clicks immediately. |
| **`Esc`** | Cancels the selection mode and returns to standby. |
| **Focus Loss** | Switching to a visible application cancels selection. Transient focus loss to hidden Windows/IME helper windows is recovered automatically. |

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
| Cells look finer or coarser on a secondary monitor | The 20 DIP target follows the primary monitor's effective DPI. This is expected in mixed-DPI setups; make the desired monitor primary to tune the grid for it. |
| The click is not delivered | The target may run at a higher integrity level (for example, elevated). Windows UIPI blocks synthetic input to such windows; run both applications at the same integrity level. |
| A second launch exits immediately | KeyGrid is already running. Only one instance is allowed. |
| More diagnostics are needed | See `%LOCALAPPDATA%\KeyGrid\logs\keygrid_YYYYMMDD.log`. Logs are retained for 7 days. |

---

## License

This project is licensed under the terms specified in the [LICENSE](LICENSE) file.
