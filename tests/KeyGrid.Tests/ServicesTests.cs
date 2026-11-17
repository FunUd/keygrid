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

        public PixelPoint? LastRangeStart { get; private set; }
        public PixelPoint? LastRangeEnd { get; private set; }

        public bool SendClick(PixelPoint target, PixelRect virtualScreen, ClickKind clickKind)
        {
            Calls.Add("send");
            LastTarget = target;
            LastVirtualScreen = virtualScreen;
            LastClickKind = clickKind;
            return ReturnResult;
        }

        public bool SendRangeClick(PixelPoint start, PixelPoint end, PixelRect virtualScreen)
        {
            Calls.Add("send-range");
            LastRangeStart = start;
            LastRangeEnd = end;
            LastVirtualScreen = virtualScreen;
            return ReturnResult;
        }
    }

    [Theory]
    [InlineData(ClickKind.Left)]
    [InlineData(ClickKind.Right)]
    [InlineData(ClickKind.Middle)]
    public void ClickExecutor_HidesOverlaysBeforeSendingClick(ClickKind clickKind)
    {
        var fakeSender = new FakeClickSender();
        var executor = new ClickExecutor(fakeSender);

        bool overlayHidden = false;
        var target = new PixelPoint(500, 400);
        var screen = new PixelRect(0, 0, 1920, 1080);

        bool result = executor.ExecuteClick(
            target,
            screen,
            clickKind,
            hideOverlaysAction: () => overlayHidden = true,
            prepareClickAction: null);

        Assert.True(result);
        Assert.True(overlayHidden);
        Assert.Equal(target, fakeSender.LastTarget);
        Assert.Equal(screen, fakeSender.LastVirtualScreen);
        Assert.Equal(clickKind, fakeSender.LastClickKind);
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
            prepareClickAction: () =>
            {
                calls.Add("wait");
                return true;
            });

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
            prepareClickAction: null);

        Assert.False(result);
    }

    [Fact]
    public void ClickExecutor_RangeHidesWaitsThenSendsBothEndpoints()
    {
        var fakeSender = new FakeClickSender();
        var executor = new ClickExecutor(fakeSender);
        var start = new PixelPoint(100, 200);
        var end = new PixelPoint(900, 800);
        var screen = new PixelRect(0, 0, 1920, 1080);

        bool result = executor.ExecuteRangeClick(
            start,
            end,
            screen,
            hideOverlaysAction: () => fakeSender.Calls.Add("hide"),
            prepareClickAction: () =>
            {
                fakeSender.Calls.Add("wait");
                return true;
            });

        Assert.True(result);
        Assert.Equal(new[] { "hide", "wait", "send-range" }, fakeSender.Calls);
        Assert.Equal(start, fakeSender.LastRangeStart);
        Assert.Equal(end, fakeSender.LastRangeEnd);
        Assert.Equal(screen, fakeSender.LastVirtualScreen);
    }

    [Fact]
    public void ClickExecutor_RangeWhenPreparationFails_DoesNotSendClick()
    {
        var fakeSender = new FakeClickSender();
        var executor = new ClickExecutor(fakeSender);

        bool result = executor.ExecuteRangeClick(
            new PixelPoint(100, 200),
            new PixelPoint(900, 800),
            new PixelRect(0, 0, 1920, 1080),
            hideOverlaysAction: () => fakeSender.Calls.Add("hide"),
            prepareClickAction: () => false);

        Assert.False(result);
        Assert.DoesNotContain("send-range", fakeSender.Calls);
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
            prepareClickAction: () => throw new InvalidOperationException("Render wait failed.")));

        Assert.DoesNotContain("send", fakeSender.Calls);
    }

    [Fact]
    public void ClickExecutor_WhenPreparationFails_DoesNotSendClick()
    {
        var fakeSender = new FakeClickSender();
        var executor = new ClickExecutor(fakeSender);

        bool result = executor.ExecuteClick(
            new PixelPoint(100, 100),
            new PixelRect(0, 0, 1920, 1080),
            ClickKind.Left,
            hideOverlaysAction: () => { },
            prepareClickAction: () => false);

        Assert.False(result);
        Assert.DoesNotContain("send", fakeSender.Calls);
    }
}
