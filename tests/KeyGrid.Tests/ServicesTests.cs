using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Services;
using Xunit;

namespace KeyGrid.Tests;

public class ServicesTests
{
    private class FakeClickSender : IClickSender
    {
        public PixelPoint? LastTarget { get; private set; }
        public PixelRect? LastVirtualScreen { get; private set; }
        public ClickKind? LastClickKind { get; private set; }
        public bool ReturnResult { get; set; } = true;
        public List<string> Calls { get; } = new();

        public bool SendClick(PixelPoint target, PixelRect virtualScreen, ClickKind clickKind)
        {
            Calls.Add("send");
            LastTarget = target;
            LastVirtualScreen = virtualScreen;
            LastClickKind = clickKind;
            return ReturnResult;
        }
    }

    [Fact]
    public void ClickExecutor_HidesOverlaysBeforeSendingClick()
    {
        var fakeSender = new FakeClickSender();
        var executor = new ClickExecutor(fakeSender);

        bool overlayHidden = false;
        var target = new PixelPoint(500, 400);
        var screen = new PixelRect(0, 0, 1920, 1080);

        bool result = executor.ExecuteClick(
            target,
            screen,
            ClickKind.Right,
            hideOverlaysAction: () => overlayHidden = true,
            waitAfterHideAction: null);

        Assert.True(result);
        Assert.True(overlayHidden);
        Assert.Equal(target, fakeSender.LastTarget);
        Assert.Equal(screen, fakeSender.LastVirtualScreen);
        Assert.Equal(ClickKind.Right, fakeSender.LastClickKind);
    }

    [Fact]
    public void ClickExecutor_WaitsForRenderAfterHidingAndBeforeSending()
    {
        var fakeSender = new FakeClickSender();
        var executor = new ClickExecutor(fakeSender);
        var calls = fakeSender.Calls;

        bool result = executor.ExecuteClick(
            new PixelPoint(500, 400),
            new PixelRect(0, 0, 1920, 1080),
            ClickKind.Left,
            hideOverlaysAction: () => calls.Add("hide"),
            waitAfterHideAction: () => calls.Add("wait"));

        Assert.True(result);
        Assert.Equal(new[] { "hide", "wait", "send" }, calls);
    }

    [Fact]
    public void ClickExecutor_WhenSendInputFails_ReturnsFalse()
    {
        var fakeSender = new FakeClickSender { ReturnResult = false };
        var executor = new ClickExecutor(fakeSender);

        bool result = executor.ExecuteClick(
            new PixelPoint(100, 100),
            new PixelRect(0, 0, 1920, 1080),
            ClickKind.Left,
            hideOverlaysAction: () => { },
            waitAfterHideAction: null);

        Assert.False(result);
    }

    [Fact]
    public void ClickExecutor_WhenRenderWaitThrows_DoesNotSendClick()
    {
        var fakeSender = new FakeClickSender();
        var executor = new ClickExecutor(fakeSender);

        Assert.Throws<InvalidOperationException>(() => executor.ExecuteClick(
            new PixelPoint(100, 100),
            new PixelRect(0, 0, 1920, 1080),
            ClickKind.Left,
            hideOverlaysAction: () => { },
            waitAfterHideAction: () => throw new InvalidOperationException("Render wait failed.")));

        Assert.DoesNotContain("send", fakeSender.Calls);
    }
}
