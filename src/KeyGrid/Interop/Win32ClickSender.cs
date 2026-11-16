using System.Runtime.InteropServices;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Services;

namespace KeyGrid.Interop;

/// <summary>
/// Dispatches left or right click events using Win32 SendInput with MOUSEEVENTF_VIRTUALDESK.
/// </summary>
public class Win32ClickSender : IClickSender
{
    private readonly Func<uint, NativeMethods.INPUT[], int, uint> _sendInput;

    public Win32ClickSender()
        : this(NativeMethods.SendInput)
    {
    }

    internal Win32ClickSender(Func<uint, NativeMethods.INPUT[], int, uint> sendInput)
    {
        ArgumentNullException.ThrowIfNull(sendInput);
        _sendInput = sendInput;
    }

    public bool SendClick(PixelPoint target, PixelRect virtualScreen, ClickKind clickKind)
    {
        var (buttonDownFlag, buttonUpFlag) = clickKind switch
        {
            ClickKind.Left => (NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP),
            ClickKind.Right => (NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP),
            _ => throw new ArgumentOutOfRangeException(nameof(clickKind), clickKind, "Unsupported click kind.")
        };
        var (normX, normY) = CoordinateConverter.ToNormalizedVirtualDesktop(target, virtualScreen);

        // Sequence: Move -> ButtonDown -> ButtonUp
        var inputs = new NativeMethods.INPUT[3];

        // 1. Move to absolute normalized position on virtual desktop
        inputs[0].type = NativeMethods.INPUT_MOUSE;
        inputs[0].mi.dx = normX;
        inputs[0].mi.dy = normY;
        inputs[0].mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE |
                               NativeMethods.MOUSEEVENTF_MOVE |
                               NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        // 2. Left button down
        inputs[1].type = NativeMethods.INPUT_MOUSE;
        inputs[1].mi.dx = normX;
        inputs[1].mi.dy = normY;
        inputs[1].mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE |
                               buttonDownFlag |
                               NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        // 3. Button up
        inputs[2].type = NativeMethods.INPUT_MOUSE;
        inputs[2].mi.dx = normX;
        inputs[2].mi.dy = normY;
        inputs[2].mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE |
                               buttonUpFlag |
                               NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        nint foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
        nint windowAtTarget = NativeMethods.WindowFromPoint(
            new NativeMethods.POINT { X = target.X, Y = target.Y });
        NativeMethods.GetWindowThreadProcessId(windowAtTarget, out uint targetProcessId);
        FileLogger.LogInfo(
            $"SendInput starting (kind={clickKind}, target=({target.X}, {target.Y}), " +
            $"foreground=0x{foreground.ToInt64():X}, foregroundPid={foregroundProcessId}, " +
            $"windowAtTarget=0x{windowAtTarget.ToInt64():X}, targetPid={targetProcessId}).");

        uint sent = _sendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        FileLogger.LogInfo($"SendInput returned (requested={inputs.Length}, sent={sent}).");
        bool succeeded = sent == inputs.Length;
        if (succeeded)
        {
            FileLogger.LogInfo(
                $"SendInput click sequence succeeded (kind={clickKind}, target=({target.X}, {target.Y}), " +
                $"requested={inputs.Length}, sent={sent}).");
        }
        else
        {
            FileLogger.LogWarning(
                $"SendInput click sequence incomplete (kind={clickKind}, target=({target.X}, {target.Y}), " +
                $"requested={inputs.Length}, sent={sent}, lastError={Marshal.GetLastWin32Error()}).");
        }

        return succeeded;
    }
}
