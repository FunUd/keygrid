# KeyGrid

KeyGrid is a lightweight, resident Windows 11 utility that enables mouse-free left clicking using keyboard navigation. It overlays an adaptive 2-stage grid across all active displays, letting you pinpoint any on-screen coordinate with four keystrokes.

---

## Features

- **Keyboard-Driven Precision**: Reach and left-click any on-screen location in 4 keystrokes (Column → Row → Column → Row).
- **Multi-Monitor & Mixed-DPI Support**: Automatically maps across all active displays, handles non-standard topologies (L-shaped, vertical offsets, gaps), and operates with Per-Monitor DPI Awareness V2.
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

# Run the comprehensive test suite (57+ unit and integration tests)
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

KeyGrid runs as a background process and displays an icon in the Windows system notification tray. If KeyGrid is already running, starting a second instance will display a notification and exit immediately.

---

## Basic Usage

### 1. Activating the Grid

Press the global hotkey:

$$\text{Ctrl} + \text{Shift} + \text{Space}$$

Full-screen transparent overlays will appear across all connected monitors, displaying column labels along the top edge and row labels along the left edge.

### 2. Selecting Coordinates (2 Stages)

KeyGrid uses an ordered subset of 26 keys:

```
Q  W  E  R  T  Y  U  I  O  P  A  S  D  F  G  H  J  K  L  Z  X  C  V  B  N  M
```

- **Stage 1**:
  1. Press the key corresponding to the desired **Column** label. The selected column band highlights.
  2. Press the key corresponding to the desired **Row** label. The grid zooms into the selected cell.
- **Stage 2**:
  3. Press the second **Column** key within the zoomed cell.
  4. Press the second **Row** key within the zoomed cell.

### 3. Confirming and Clicking

- Once Stage 2 is complete, KeyGrid enters the **Awaiting Confirmation** state and displays a red target crosshair at the resolved click coordinate.
- Press **`Enter`** to execute the left click. The overlay will hide and SendInput will dispatch the click event.
- **Early Confirmation**: You can also press **`Enter`** immediately after completing Stage 1 (after the first 2 keystrokes) to click the center of the Stage 1 cell without performing Stage 2.

### 4. Correcting and Cancelling

| Key | Action |
|---|---|
| **`Backspace`** | Undoes the last keystroke and recalculates the active selection area. |
| **`Esc`** | Cancels the selection mode and returns to standby. |
| **Focus Loss** | Switching windows or clicking outside automatically cancels selection. |

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
│       ├── Interop/           # Win32 P/Invoke (EnumDisplayMonitors, SendInput, ImmAssociateContext)
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

## License

This project is licensed under the terms specified in the [LICENSE](LICENSE) file.
