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

    public bool SendDoubleClick(PixelPoint target, PixelRect virtualScreen)
    {
        var (normX, normY) = CoordinateConverter.ToNormalizedVirtualDesktop(target, virtualScreen);
        uint commonFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
            | NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        // Sequence: Move -> Down -> Up -> Down -> Up. Both clicks share the
        // target coordinate and are dispatched back-to-back, so Windows
        // resolves them as a double-click at that position.
        var inputs = new NativeMethods.INPUT[5];
        inputs[0] = CreateMouseInput(normX, normY, commonFlags | NativeMethods.MOUSEEVENTF_MOVE);
        inputs[1] = CreateMouseInput(normX, normY, commonFlags | NativeMethods.MOUSEEVENTF_LEFTDOWN);
        inputs[2] = CreateMouseInput(normX, normY, commonFlags | NativeMethods.MOUSEEVENTF_LEFTUP);
        inputs[3] = CreateMouseInput(normX, normY, commonFlags | NativeMethods.MOUSEEVENTF_LEFTDOWN);
        inputs[4] = CreateMouseInput(normX, normY, commonFlags | NativeMethods.MOUSEEVENTF_LEFTUP);

        FileLogger.LogDebug(
            $"SendInput double-click starting (target=({target.X}, {target.Y})).");
        uint sent = _sendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        bool succeeded = sent == inputs.Length;
        if (succeeded)
        {
            FileLogger.LogInfo(
                $"SendInput double-click sequence succeeded (requested={inputs.Length}, sent={sent}).");
            return true;
        }

        FileLogger.LogWarning(
            $"SendInput double-click sequence incomplete (requested={inputs.Length}, sent={sent}, " +
            $"lastError={Marshal.GetLastWin32Error()}). Releasing synthetic left button.");

        // A partial send can leave the synthetic left button pressed. Release
        // it at the target so the pointer state cannot stick.
        var cleanup = new[]
        {
            CreateMouseInput(normX, normY, commonFlags | NativeMethods.MOUSEEVENTF_LEFTUP)
        };
        uint cleanupSent = _sendInput(
            (uint)cleanup.Length,
            cleanup,
            Marshal.SizeOf<NativeMethods.INPUT>());
        if (cleanupSent != (uint)cleanup.Length)
        {
            FileLogger.LogWarning(
                $"SendInput double-click cleanup incomplete (requested={cleanup.Length}, " +
                $"sent={cleanupSent}, lastError={Marshal.GetLastWin32Error()}).");
        }

        return false;
    }

    public bool SendDrag(PixelPoint start, PixelPoint end, PixelRect virtualScreen)
    {
        if (virtualScreen.IsEmpty)
        {
            return false;
        }

        var (startX, startY) = CoordinateConverter.ToNormalizedVirtualDesktop(start, virtualScreen);
        var (endX, endY) = CoordinateConverter.ToNormalizedVirtualDesktop(end, virtualScreen);
        uint commonFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
            | NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        FileLogger.LogDebug(
            $"SendInput drag starting (start=({start.X}, {start.Y}), end=({end.X}, {end.Y}), " +
            $"virtualScreen={virtualScreen}).");

        if (FileLogger.IsDebugEnabled)
        {
            nint foreground = NativeMethods.GetForegroundWindow();
            nint startWindow = NativeMethods.WindowFromPoint(
                new NativeMethods.POINT { X = start.X, Y = start.Y });
            nint endWindow = NativeMethods.WindowFromPoint(
                new NativeMethods.POINT { X = end.X, Y = end.Y });
            NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
            NativeMethods.GetWindowThreadProcessId(startWindow, out uint startProcessId);
            NativeMethods.GetWindowThreadProcessId(endWindow, out uint endProcessId);
            FileLogger.LogDebug(
                $"SendInput drag window context (foreground={DescribeWindow(foreground)}, foregroundPid={foregroundProcessId}, " +
                $"startWindow={DescribeWindow(startWindow)}, startPid={startProcessId}, " +
                $"endWindow={DescribeWindow(endWindow)}, endPid={endProcessId}).");
        }

        try
        {
            if (!SendSingleMouseInput(
                startX,
                startY,
                commonFlags | NativeMethods.MOUSEEVENTF_MOVE,
                "drag move-start"))
            {
                return ReleaseDragLeftButton(endX, endY, commonFlags);
            }

            if (!SendSingleMouseInput(
                startX,
                startY,
                commonFlags | NativeMethods.MOUSEEVENTF_LEFTDOWN,
                "drag left-down"))
            {
                return ReleaseDragLeftButton(endX, endY, commonFlags);
            }

            Thread.Sleep(80);

            foreach (var point in GetInterpolatedDragPoints(start, end))
            {
                var (normalizedX, normalizedY) =
                    CoordinateConverter.ToNormalizedVirtualDesktop(point, virtualScreen);
                if (!SendSingleMouseInput(
                    normalizedX,
                    normalizedY,
                    commonFlags | NativeMethods.MOUSEEVENTF_MOVE,
                    "drag interpolated move"))
                {
                    return ReleaseDragLeftButton(normalizedX, normalizedY, commonFlags);
                }

                Thread.Sleep(10);
            }

            Thread.Sleep(20);
            if (!SendSingleMouseInput(
                endX,
                endY,
                commonFlags | NativeMethods.MOUSEEVENTF_LEFTUP,
                "drag left-up"))
            {
                return ReleaseDragLeftButton(endX, endY, commonFlags);
            }

            FileLogger.LogInfo(
                $"SendInput drag succeeded (start=({start.X}, {start.Y}), " +
                $"end=({end.X}, {end.Y}), intermediatePoints={GetInterpolatedDragPoints(start, end).Count}).");
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.LogWarning(
                $"SendInput drag threw an exception. Releasing synthetic left button. " +
                $"Error={ex.Message}");
            return ReleaseDragLeftButton(endX, endY, commonFlags);
        }
    }

    private bool SendSingleMouseInput(int normalizedX, int normalizedY, uint flags, string phase)
    {
        var input = CreateMouseInput(normalizedX, normalizedY, flags);
        uint sent = _sendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent == 1)
        {
            return true;
        }

        FileLogger.LogWarning(
            $"SendInput {phase} incomplete (requested=1, sent={sent}, " +
            $"lastError={Marshal.GetLastWin32Error()}).");
        return false;
    }

    private bool ReleaseDragLeftButton(int normalizedX, int normalizedY, uint commonFlags)
    {
        try
        {
            var cleanup = CreateMouseInput(
                normalizedX,
                normalizedY,
                commonFlags | NativeMethods.MOUSEEVENTF_LEFTUP);
            uint cleanupSent = _sendInput(
                1,
                new[] { cleanup },
                Marshal.SizeOf<NativeMethods.INPUT>());
            if (cleanupSent != 1)
            {
                FileLogger.LogWarning(
                    $"SendInput drag cleanup incomplete (requested=1, sent={cleanupSent}, " +
                    $"lastError={Marshal.GetLastWin32Error()}).");
            }
        }
        catch (Exception ex)
        {
            FileLogger.LogWarning(
                $"SendInput drag cleanup threw an exception. Left button release may remain uncertain. " +
                $"Error={ex.Message}");
        }

        return false;
    }

    private static IReadOnlyList<PixelPoint> GetInterpolatedDragPoints(PixelPoint start, PixelPoint end)
    {
        int distance = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        int stepCount = Math.Clamp((distance + 119) / 120, 4, 24);
        var points = new List<PixelPoint>(stepCount);

        for (int step = 1; step <= stepCount; step++)
        {
            double ratio = (double)step / stepCount;
            int x = start.X + (int)Math.Round((end.X - start.X) * ratio);
            int y = start.Y + (int)Math.Round((end.Y - start.Y) * ratio);
            points.Add(new PixelPoint(x, y));
        }

        if (points.Count == 0 || points[^1] != end)
        {
            points.Add(end);
        }

        return points;
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
