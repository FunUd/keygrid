using KeyGrid.Core.Selection;
using Xunit;

namespace KeyGrid.Tests;

public class FocusLossPolicyTests
{
    private const uint CurrentProcessId = 100;
    private const uint ForegroundProcessId = 200;

    [Fact]
    public void VisibleForeignWindow_CancelsSelectionImmediately()
    {
        var policy = new FocusLossPolicy();

        Assert.Equal(
            FocusLossAction.CancelSelection,
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true));
    }

    [Fact]
    public void VisibleForeignWindow_CancelsAfterTransientFocusLoss()
    {
        var policy = new FocusLossPolicy();

        Assert.Equal(
            FocusLossAction.ReactivateOverlay,
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false));
        Assert.Equal(
            FocusLossAction.CancelSelection,
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true));
    }

    [Fact]
    public void HiddenForeignWindow_ReactivatesOverlay()
    {
        var policy = new FocusLossPolicy();

        var action = policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false);

        Assert.Equal(FocusLossAction.ReactivateOverlay, action);
    }

    [Fact]
    public void OwnProcessWindow_ReactivatesOverlay()
    {
        var policy = new FocusLossPolicy();

        var action = policy.Decide(CurrentProcessId, CurrentProcessId, foregroundIsVisible: true);

        Assert.Equal(FocusLossAction.ReactivateOverlay, action);
    }

    [Fact]
    public void NoForegroundWindow_ReactivatesOverlay()
    {
        var policy = new FocusLossPolicy();

        var action = policy.Decide(foregroundProcessId: 0, CurrentProcessId, foregroundIsVisible: false);

        Assert.Equal(FocusLossAction.ReactivateOverlay, action);
    }

    [Fact]
    public void HiddenForeignWindow_AlwaysReactivates()
    {
        var policy = new FocusLossPolicy();

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(
                FocusLossAction.ReactivateOverlay,
                policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false));
        }
    }

    [Fact]
    public void OwnProcessAndHiddenWindowFocusLosses_ReactivateOverlay()
    {
        var policy = new FocusLossPolicy();

        Assert.Equal(FocusLossAction.ReactivateOverlay,
            policy.Decide(CurrentProcessId, CurrentProcessId, foregroundIsVisible: true));
        Assert.Equal(FocusLossAction.ReactivateOverlay,
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false));
    }
}
