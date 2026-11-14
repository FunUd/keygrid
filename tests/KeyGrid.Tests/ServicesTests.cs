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
        public bool ReturnResult { get; set; } = true;

        public bool SendLeftClick(PixelPoint target, PixelRect virtualScreen)
        {
            LastTarget = target;
            LastVirtualScreen = virtualScreen;
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
            hideOverlaysAction: () => overlayHidden = true,
            waitAfterHideAction: null);

        Assert.True(result);
        Assert.True(overlayHidden);
        Assert.Equal(target, fakeSender.LastTarget);
        Assert.Equal(screen, fakeSender.LastVirtualScreen);
    }

    [Fact]
    public void ClickExecutor_WhenSendInputFails_ReturnsFalse()
    {
        var fakeSender = new FakeClickSender { ReturnResult = false };
        var executor = new ClickExecutor(fakeSender);

        bool result = executor.ExecuteClick(
            new PixelPoint(100, 100),
            new PixelRect(0, 0, 1920, 1080),
            hideOverlaysAction: () => { },
            waitAfterHideAction: null);

        Assert.False(result);
    }
}
