using System.Runtime.InteropServices;
using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Interop;

namespace KeyGrid.UI.Tests;

public class Win32ClickSenderTests
{
    [Theory]
    [InlineData(ClickKind.Left, NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP)]
    [InlineData(ClickKind.Right, NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP)]
    [InlineData(ClickKind.Middle, NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP)]
    public void SendClick_EmitsVirtualDesktopMoveDownAndUpAtNormalizedPoint(
        ClickKind clickKind,
        uint buttonDownFlag,
        uint buttonUpFlag)
    {
        UiTestThread.Run(() =>
        {
            NativeMethods.INPUT[]? capturedInputs = null;
            uint capturedCount = 0;
            int capturedSize = 0;
            var sender = new Win32ClickSender((count, inputs, size) =>
            {
                capturedCount = count;
                capturedInputs = inputs.ToArray();
                capturedSize = size;
                return count;
            });
            var virtualScreen = new PixelRect(-1920, -200, 3840, 2160);

            bool succeeded = sender.SendClick(
                new PixelPoint(1919, 1959),
                virtualScreen,
                clickKind);

            Assert.True(succeeded);
            Assert.Equal(3u, capturedCount);
            Assert.Equal(Marshal.SizeOf<NativeMethods.INPUT>(), capturedSize);
            Assert.NotNull(capturedInputs);
            Assert.Equal(3, capturedInputs.Length);

            uint commonFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
                | NativeMethods.MOUSEEVENTF_VIRTUALDESK;
            Assert.Equal(
                new[]
                {
                    commonFlags | NativeMethods.MOUSEEVENTF_MOVE,
                    commonFlags | buttonDownFlag,
                    commonFlags | buttonUpFlag
                },
                capturedInputs.Select(input => input.mi.dwFlags));
            Assert.All(capturedInputs, input =>
            {
                Assert.Equal(NativeMethods.INPUT_MOUSE, input.type);
                Assert.Equal(CoordinateConverter.MaxSendInputValue, input.mi.dx);
                Assert.Equal(CoordinateConverter.MaxSendInputValue, input.mi.dy);
            });
        });
    }

    [Fact]
    public void SendClick_PartialSendInputResultReturnsFalse()
    {
        UiTestThread.Run(() =>
        {
            uint requestedCount = 0;
            var sender = new Win32ClickSender((count, _, _) =>
            {
                requestedCount = count;
                return count - 1;
            });

            bool succeeded = sender.SendClick(
                new PixelPoint(0, 0),
                new PixelRect(0, 0, 1920, 1080),
                ClickKind.Left);

            Assert.False(succeeded);
            Assert.Equal(3u, requestedCount);
        });
    }

    [Fact]
    public void SendClick_EmptyVirtualScreenDoesNotCallNativeSender()
    {
        UiTestThread.Run(() =>
        {
            bool called = false;
            var sender = new Win32ClickSender((_, _, _) =>
            {
                called = true;
                return 0;
            });

            Assert.Throws<ArgumentException>(() =>
                sender.SendClick(new PixelPoint(0, 0), PixelRect.Empty, ClickKind.Left));
            Assert.False(called);
        });
    }
}
