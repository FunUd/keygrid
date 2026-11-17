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
    public void SendRangeClick_EmitsNormalClickThenShiftClickAtEachEndpoint()
    {
        UiTestThread.Run(() =>
        {
            NativeMethods.INPUT[]? capturedInputs = null;
            uint capturedCount = 0;
            var sender = new Win32ClickSender((count, inputs, _) =>
            {
                capturedCount = count;
                capturedInputs = inputs.ToArray();
                return count;
            });
            var virtualScreen = new PixelRect(-1920, -200, 3840, 2160);
            var start = new PixelPoint(-100, 50);
            var end = new PixelPoint(1919, 1959);
            var (startX, startY) = CoordinateConverter.ToNormalizedVirtualDesktop(start, virtualScreen);
            var (endX, endY) = CoordinateConverter.ToNormalizedVirtualDesktop(end, virtualScreen);

            bool succeeded = sender.SendRangeClick(start, end, virtualScreen);

            Assert.True(succeeded);
            Assert.Equal(8u, capturedCount);
            Assert.NotNull(capturedInputs);
            Assert.Equal(8, capturedInputs.Length);

            uint commonFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
                | NativeMethods.MOUSEEVENTF_VIRTUALDESK;
            Assert.Equal(
                new[]
                {
                    commonFlags | NativeMethods.MOUSEEVENTF_MOVE,
                    commonFlags | NativeMethods.MOUSEEVENTF_LEFTDOWN,
                    commonFlags | NativeMethods.MOUSEEVENTF_LEFTUP,
                    commonFlags | NativeMethods.MOUSEEVENTF_MOVE,
                    commonFlags | NativeMethods.MOUSEEVENTF_LEFTDOWN,
                    commonFlags | NativeMethods.MOUSEEVENTF_LEFTUP
                },
                new[] { 0, 1, 2, 4, 5, 6 }.Select(index => capturedInputs[index].mi.dwFlags));

            Assert.Equal(NativeMethods.INPUT_KEYBOARD, capturedInputs[3].type);
            Assert.Equal(NativeMethods.VK_SHIFT, capturedInputs[3].ki.wVk);
            Assert.Equal(0u, capturedInputs[3].ki.dwFlags);
            Assert.Equal(NativeMethods.INPUT_KEYBOARD, capturedInputs[7].type);
            Assert.Equal(NativeMethods.VK_SHIFT, capturedInputs[7].ki.wVk);
            Assert.Equal(NativeMethods.KEYEVENTF_KEYUP, capturedInputs[7].ki.dwFlags);

            Assert.All(new[] { 0, 1, 2 }, index =>
            {
                Assert.Equal(startX, capturedInputs[index].mi.dx);
                Assert.Equal(startY, capturedInputs[index].mi.dy);
            });
            Assert.All(new[] { 4, 5, 6 }, index =>
            {
                Assert.Equal(endX, capturedInputs[index].mi.dx);
                Assert.Equal(endY, capturedInputs[index].mi.dy);
            });
        });
    }

    [Fact]
    public void SendRangeClick_WhenSendIsPartial_ReleasesLeftButtonAndShift()
    {
        UiTestThread.Run(() =>
        {
            uint firstCount = 0;
            uint cleanupCount = 0;
            NativeMethods.INPUT[]? cleanupInputs = null;
            int callCount = 0;
            var sender = new Win32ClickSender((count, inputs, _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    firstCount = count;
                    return count - 1;
                }

                cleanupCount = count;
                cleanupInputs = inputs.ToArray();
                return count;
            });

            bool succeeded = sender.SendRangeClick(
                new PixelPoint(10, 10),
                new PixelPoint(20, 20),
                new PixelRect(0, 0, 1920, 1080));

            Assert.False(succeeded);
            Assert.Equal(8u, firstCount);
            Assert.Equal(2u, cleanupCount);
            Assert.NotNull(cleanupInputs);
            Assert.Equal(NativeMethods.INPUT_MOUSE, cleanupInputs[0].type);
            Assert.Equal(
                NativeMethods.MOUSEEVENTF_ABSOLUTE
                | NativeMethods.MOUSEEVENTF_VIRTUALDESK
                | NativeMethods.MOUSEEVENTF_LEFTUP,
                cleanupInputs[0].mi.dwFlags);
            Assert.Equal(NativeMethods.INPUT_KEYBOARD, cleanupInputs[1].type);
            Assert.Equal(NativeMethods.VK_SHIFT, cleanupInputs[1].ki.wVk);
            Assert.Equal(NativeMethods.KEYEVENTF_KEYUP, cleanupInputs[1].ki.dwFlags);
        });
    }

    [Theory]
    [InlineData(false, -5, NativeMethods.MOUSEEVENTF_WHEEL)]
    [InlineData(true, 2, NativeMethods.MOUSEEVENTF_HWHEEL)]
    public void SendScroll_MovesPointerThenSendsWheelNotches(
        bool horizontal,
        int notches,
        uint expectedWheelFlag)
    {
        UiTestThread.Run(() =>
        {
            NativeMethods.INPUT[]? capturedInputs = null;
            uint capturedCount = 0;
            var sender = new Win32ClickSender((count, inputs, _) =>
            {
                capturedCount = count;
                capturedInputs = inputs.ToArray();
                return count;
            });
            var virtualScreen = new PixelRect(-1920, -200, 3840, 2160);
            var target = new PixelPoint(300, 400);
            var (normalizedX, normalizedY) = CoordinateConverter.ToNormalizedVirtualDesktop(
                target,
                virtualScreen);

            bool succeeded = sender.SendScroll(target, virtualScreen, notches, horizontal);

            Assert.True(succeeded);
            Assert.Equal(2u, capturedCount);
            Assert.NotNull(capturedInputs);
            Assert.Equal(NativeMethods.INPUT_MOUSE, capturedInputs[0].type);
            Assert.Equal(
                NativeMethods.MOUSEEVENTF_MOVE
                | NativeMethods.MOUSEEVENTF_ABSOLUTE
                | NativeMethods.MOUSEEVENTF_VIRTUALDESK,
                capturedInputs[0].mi.dwFlags);
            Assert.Equal(normalizedX, capturedInputs[0].mi.dx);
            Assert.Equal(normalizedY, capturedInputs[0].mi.dy);

            Assert.Equal(NativeMethods.INPUT_MOUSE, capturedInputs[1].type);
            Assert.Equal(expectedWheelFlag, capturedInputs[1].mi.dwFlags);
            Assert.Equal(unchecked((uint)(notches * NativeMethods.WHEEL_DELTA)), capturedInputs[1].mi.mouseData);
        });
    }

    [Fact]
    public void SendScroll_WhenNotchesAreZero_DoesNotCallNativeSender()
    {
        UiTestThread.Run(() =>
        {
            int callCount = 0;
            var sender = new Win32ClickSender((_, _, _) =>
            {
                callCount++;
                return 0;
            });

            bool succeeded = sender.SendScroll(
                new PixelPoint(100, 100),
                new PixelRect(0, 0, 1920, 1080),
                notches: 0,
                horizontal: false);

            Assert.True(succeeded);
            Assert.Equal(0, callCount);
        });
    }

    [Fact]
    public void SendScroll_WhenSendIsPartial_ReturnsFalse()
    {
        UiTestThread.Run(() =>
        {
            uint requested = 0;
            var sender = new Win32ClickSender((count, _, _) =>
            {
                requested = count;
                return count - 1;
            });

            bool succeeded = sender.SendScroll(
                new PixelPoint(100, 100),
                new PixelRect(0, 0, 1920, 1080),
                notches: 1,
                horizontal: false);

            Assert.False(succeeded);
            Assert.Equal(2u, requested);
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
