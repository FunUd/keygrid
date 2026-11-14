using KeyGrid.Core.Selection;
using Xunit;

namespace KeyGrid.Tests;

public class FocusLossPolicyTests
{
    private const uint CurrentProcessId = 100;
    private const uint ForegroundProcessId = 200;

    [Fact]
    public void VisibleForeignWindow_CancelsSelection()
    {
        var policy = new FocusLossPolicy();

        var action = policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true);

        Assert.Equal(FocusLossAction.CancelSelection, action);
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
    public void RepeatedTransientFocusLoss_StopsRecoveringAfterLimit()
    {
        var policy = new FocusLossPolicy();

        for (int i = 0; i < FocusLossPolicy.MaxConsecutiveRecoveries; i++)
        {
            Assert.Equal(
                FocusLossAction.ReactivateOverlay,
                policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false));
        }

        Assert.Equal(
            FocusLossAction.CancelSelection,
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false));
    }

    [Fact]
    public void Reset_RestoresRecoveryBudget()
    {
        var policy = new FocusLossPolicy();
        for (int i = 0; i < FocusLossPolicy.MaxConsecutiveRecoveries; i++)
        {
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false);
        }

        policy.Reset();

        Assert.Equal(
            FocusLossAction.ReactivateOverlay,
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false));
    }
}
