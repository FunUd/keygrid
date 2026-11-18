using System.Runtime.InteropServices;
using System.Text;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Services;

namespace KeyGrid.Interop;

/// <summary>
/// Dispatches left, right, or middle click events using Win32 SendInput
/// with MOUSEEVENTF_VIRTUALDESK.
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
            ClickKind.Middle => (NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP),
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

        // 2. Button down
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

        if (FileLogger.IsDebugEnabled)
        {
            nint foreground = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
            nint windowAtTarget = NativeMethods.WindowFromPoint(
                new NativeMethods.POINT { X = target.X, Y = target.Y });
            NativeMethods.GetWindowThreadProcessId(windowAtTarget, out uint targetProcessId);
            FileLogger.LogDebug(
                $"SendInput starting (kind={clickKind}, target=({target.X}, {target.Y}), " +
                $"foreground={DescribeWindow(foreground)}, foregroundPid={foregroundProcessId}, " +
                $"windowAtTarget={DescribeWindow(windowAtTarget)}, targetPid={targetProcessId}).");
        }

        uint sent = _sendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        FileLogger.LogDebug($"SendInput returned (requested={inputs.Length}, sent={sent}).");
        bool succeeded = sent == inputs.Length;
        if (succeeded)
        {
            FileLogger.LogInfo(
                $"SendInput click sequence succeeded (kind={clickKind}, " +
                $"requested={inputs.Length}, sent={sent}).");
        }
        else
        {
            FileLogger.LogWarning(
                $"SendInput click sequence incomplete (kind={clickKind}, " +
                $"requested={inputs.Length}, sent={sent}, lastError={Marshal.GetLastWin32Error()}).");
        }

        return succeeded;
    }

    public bool SendRangeClick(PixelPoint start, PixelPoint end, PixelRect virtualScreen)
    {
        var (startX, startY) = CoordinateConverter.ToNormalizedVirtualDesktop(start, virtualScreen);
        var (endX, endY) = CoordinateConverter.ToNormalizedVirtualDesktop(end, virtualScreen);
        uint moveFlags = NativeMethods.MOUSEEVENTF_MOVE
            | NativeMethods.MOUSEEVENTF_ABSOLUTE
            | NativeMethods.MOUSEEVENTF_VIRTUALDESK;
        uint buttonFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
            | NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        // A text selection starts with a normal click at the anchor and extends it
        // with Shift+left click at the endpoint. Keep Shift held only for the
        // second click so the target's own click handling is not altered.
        var inputs = new NativeMethods.INPUT[8];
        inputs[0] = CreateMouseInput(startX, startY, moveFlags);
        inputs[1] = CreateMouseInput(startX, startY, buttonFlags | NativeMethods.MOUSEEVENTF_LEFTDOWN);
        inputs[2] = CreateMouseInput(startX, startY, buttonFlags | NativeMethods.MOUSEEVENTF_LEFTUP);
        inputs[3] = CreateKeyInput(NativeMethods.VK_SHIFT, 0);
        inputs[4] = CreateMouseInput(endX, endY, moveFlags);
        inputs[5] = CreateMouseInput(endX, endY, buttonFlags | NativeMethods.MOUSEEVENTF_LEFTDOWN);
        inputs[6] = CreateMouseInput(endX, endY, buttonFlags | NativeMethods.MOUSEEVENTF_LEFTUP);
        inputs[7] = CreateKeyInput(NativeMethods.VK_SHIFT, NativeMethods.KEYEVENTF_KEYUP);

        FileLogger.LogDebug(
            $"SendInput range starting (start=({start.X}, {start.Y}), end=({end.X}, {end.Y})).");
        uint sent = _sendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool succeeded = sent == inputs.Length;
        if (succeeded)
        {
            FileLogger.LogInfo(
                $"SendInput range sequence succeeded (requested={inputs.Length}, sent={sent}).");
            return true;
        }

        FileLogger.LogWarning(
            $"SendInput range sequence incomplete (requested={inputs.Length}, sent={sent}, " +
            $"lastError={Marshal.GetLastWin32Error()}). Releasing synthetic buttons and Shift.");

        // A partial send can leave either the left button or Shift logically
        // pressed. Release both, using the endpoint so the mouse-up occurs where
        // the input stream currently is.
        var cleanup = new[]
        {
            CreateMouseInput(endX, endY, buttonFlags | NativeMethods.MOUSEEVENTF_LEFTUP),
            CreateKeyInput(NativeMethods.VK_SHIFT, NativeMethods.KEYEVENTF_KEYUP)
        };
        uint cleanupSent = _sendInput(
            (uint)cleanup.Length,
            cleanup,
            Marshal.SizeOf<NativeMethods.INPUT>());
        if (cleanupSent != (uint)cleanup.Length)
        {
            FileLogger.LogWarning(
                $"SendInput range cleanup incomplete (requested={cleanup.Length}, sent={cleanupSent}, " +
                $"lastError={Marshal.GetLastWin32Error()}).");
        }

        return false;
    }

    public bool SendScroll(PixelPoint target, PixelRect virtualScreen, int notches, bool horizontal)
    {
        if (notches == 0)
        {
            return true;
        }

        var (normalizedX, normalizedY) = CoordinateConverter.ToNormalizedVirtualDesktop(
            target,
            virtualScreen);
        uint moveFlags = NativeMethods.MOUSEEVENTF_MOVE
            | NativeMethods.MOUSEEVENTF_ABSOLUTE
            | NativeMethods.MOUSEEVENTF_VIRTUALDESK;
        uint wheelFlag = horizontal
            ? NativeMethods.MOUSEEVENTF_HWHEEL
            : NativeMethods.MOUSEEVENTF_WHEEL;
        uint wheelData = unchecked((uint)(notches * NativeMethods.WHEEL_DELTA));

        // Move first so wheel routing uses the selected target even if activation
        // left the pointer somewhere else. The wheel event itself does not need
        // absolute coordinates; it is delivered at the current cursor position.
        var inputs = new[]
        {
            CreateMouseInput(normalizedX, normalizedY, moveFlags),
            CreateMouseInput(0, 0, wheelFlag, wheelData)
        };

        FileLogger.LogDebug(
            $"SendInput scroll starting (target=({target.X}, {target.Y}), " +
            $"notches={notches}, horizontal={horizontal}).");
        uint sent = _sendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool succeeded = sent == inputs.Length;
        if (succeeded)
        {
            FileLogger.LogInfo($"SendInput scroll succeeded (requested={inputs.Length}, sent={sent}).");
            return true;
        }

        FileLogger.LogWarning(
            $"SendInput scroll incomplete (requested={inputs.Length}, sent={sent}, " +
            $"lastError={Marshal.GetLastWin32Error()}).");
        return false;
    }

    private static NativeMethods.INPUT CreateMouseInput(
        int normalizedX,
        int normalizedY,
        uint flags,
        uint mouseData = 0)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            mi = new NativeMethods.MOUSEINPUT
            {
                dx = normalizedX,
                dy = normalizedY,
                mouseData = mouseData,
                dwFlags = flags
            }
        };
    }

    private static NativeMethods.INPUT CreateKeyInput(ushort virtualKey, uint flags)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                dwFlags = flags
            }
        };
    }

    private static string DescribeWindow(nint window)
    {
        if (window == nint.Zero)
        {
            return "0x0";
        }

        var className = new StringBuilder(256);
        int classNameLength = NativeMethods.GetClassName(window, className, className.Capacity);
        string classDescription = classNameLength > 0 ? className.ToString() : "unknown";
        string boundsDescription = NativeMethods.GetWindowRect(window, out var bounds)
            ? $"[{bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}]"
            : "unknown";

        return $"0x{window.ToInt64():X} class={classDescription} bounds={boundsDescription}";
    }
}
