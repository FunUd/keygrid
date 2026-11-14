using System.Runtime.InteropServices;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;

namespace KeyGrid.Interop;

/// <summary>
/// Dispatches left click events using Win32 SendInput with MOUSEEVENTF_VIRTUALDESK.
/// </summary>
public class Win32ClickSender : IClickSender
{
    public bool SendLeftClick(PixelPoint target, PixelRect virtualScreen)
    {
        var (normX, normY) = CoordinateConverter.ToNormalizedVirtualDesktop(target, virtualScreen);

        // Sequence: Move -> LeftDown -> LeftUp
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
                               NativeMethods.MOUSEEVENTF_LEFTDOWN |
                               NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        // 3. Left button up
        inputs[2].type = NativeMethods.INPUT_MOUSE;
        inputs[2].mi.dx = normX;
        inputs[2].mi.dy = normY;
        inputs[2].mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE |
                               NativeMethods.MOUSEEVENTF_LEFTUP |
                               NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return sent == inputs.Length;
    }
}
