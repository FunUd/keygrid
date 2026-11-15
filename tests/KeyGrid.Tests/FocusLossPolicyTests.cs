using KeyGrid.Core.Selection;
using Xunit;

namespace KeyGrid.Tests;

public class FocusLossPolicyTests
{
    private const uint CurrentProcessId = 100;
    private const uint ForegroundProcessId = 200;

    [Fact]
    public void VisibleForeignWindow_ReactivatesOverlayUpToLimit()
    {
        var policy = new FocusLossPolicy();

        // The first MaxConsecutiveRecoveries events from a visible foreign window should
        // trigger reactivation (the overlay just opened and Windows may return focus
        // to the previous foreground window before SetForegroundWindow completes).
        for (int i = 0; i < FocusLossPolicy.MaxConsecutiveRecoveries; i++)
        {
            Assert.Equal(
                FocusLossAction.ReactivateOverlay,
                policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true));
        }

        // Once the budget is exhausted, treat the focus loss as a deliberate user action.
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
    public void RepeatedVisibleForeignFocusLoss_StopsRecoveringAfterLimit()
    {
        var policy = new FocusLossPolicy();

        for (int i = 0; i < FocusLossPolicy.MaxConsecutiveRecoveries; i++)
        {
            Assert.Equal(
                FocusLossAction.ReactivateOverlay,
                policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true));
        }

        Assert.Equal(
            FocusLossAction.CancelSelection,
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true));
    }

    [Fact]
    public void Reset_RestoresRecoveryBudget()
    {
        var policy = new FocusLossPolicy();
        // Exhaust the budget using visible foreign window focus-loss events.
        for (int i = 0; i < FocusLossPolicy.MaxConsecutiveRecoveries; i++)
        {
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true);
        }

        policy.Reset();

        Assert.Equal(
            FocusLossAction.ReactivateOverlay,
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true));
    }

    [Fact]
    public void HiddenForeignWindow_AlwaysReactivatesRegardlessOfBudget()
    {
        var policy = new FocusLossPolicy();

        // Hidden helper windows (IME/TSF) must always trigger reactivation, even beyond the
        // visible-foreign-window recovery limit, because they are system-generated and not
        // caused by deliberate user switches.
        int attempts = FocusLossPolicy.MaxConsecutiveRecoveries + 5;
        for (int i = 0; i < attempts; i++)
        {
            Assert.Equal(
                FocusLossAction.ReactivateOverlay,
                policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false));
        }
    }

    [Fact]
    public void TransientFocusLoss_DoesNotConsumeVisibleForeignRecoveryBudget()
    {
        var policy = new FocusLossPolicy();

        // Hidden helpers and own-process windows are always transient. They must not
        // consume the budget that bounds recovery from visible foreign windows, otherwise
        // frequent IME focus changes could exhaust it and cause a spurious cancellation.
        for (int i = 0; i < 10_000; i++)
        {
            Assert.Equal(
                FocusLossAction.ReactivateOverlay,
                policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: false));
            Assert.Equal(
                FocusLossAction.ReactivateOverlay,
                policy.Decide(CurrentProcessId, CurrentProcessId, foregroundIsVisible: true));
        }

        // The full budget is still available for visible foreign windows.
        for (int i = 0; i < FocusLossPolicy.MaxConsecutiveRecoveries; i++)
        {
            Assert.Equal(
                FocusLossAction.ReactivateOverlay,
                policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true));
        }

        Assert.Equal(
            FocusLossAction.CancelSelection,
            policy.Decide(ForegroundProcessId, CurrentProcessId, foregroundIsVisible: true));
    }
}
