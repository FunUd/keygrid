using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using KeyGrid.Core.Services;
using KeyGrid.UI;

namespace KeyGrid.UI.Tests;

public class OverlayManagerTests
{
    [Fact]
    public void ShowOverlays_CreatesSurfacePerMonitorAndMarksOnlyPrimaryForInput()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = new FakeMonitorProvider(
                new MonitorInfo(new PixelRect(-800, 0, 800, 600), 1.0, false),
                new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.5, true));
            var factory = new FakeOverlayFactory();
            var manager = CreateManager(monitorProvider, factory, new FakeWindowFocusService(), new FakeClickSender());

            manager.ShowOverlays();

            Assert.True(manager.IsActive);
            Assert.Equal(2, factory.Windows.Count);
            Assert.False(factory.Windows[0].IsPrimary);
            Assert.True(factory.Windows[1].IsPrimary);
            Assert.Equal(1.0, factory.Windows[0].DpiScale);
            Assert.Equal(1.5, factory.Windows[1].DpiScale);
            Assert.All(factory.Windows, window =>
            {
                Assert.Equal(1, window.ShowCount);
                Assert.Equal(1, window.RenderUpdateCount);
            });
            Assert.Equal(1, factory.Windows[1].TryReactivateCount);
            Assert.Equal(0, factory.Windows[0].TryReactivateCount);
            UiTestThread.DrainDispatcher();

            Assert.All(factory.Windows, window =>
            {
                Assert.Equal(1, window.RefreshExpectedDpiCount);
                Assert.True(window.DpiNotificationsEnabled);
            });
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);

            manager.CloseOverlays();
            Assert.False(manager.IsActive);
            Assert.All(factory.Windows, window => Assert.Equal(1, window.CloseCount));
        });
    }

    [Fact]
    public void ShowOverlays_WhenPrimaryCannotAcquireFocus_ClosesOverlays()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory { TryReactivateResult = false };
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, new FakeClickSender());
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            manager.ShowOverlays();

            var primary = Assert.Single(factory.Windows);
            Assert.Equal(1, primary.TryReactivateCount);
            Assert.Equal(1, primary.CloseCount);
            Assert.False(manager.IsActive);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Equal("Failed to focus the selection overlay.", error);
        });
    }

    [Fact]
    public void ShowOverlays_WithoutActiveMonitorsRaisesError()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var manager = CreateManager(
                new FakeMonitorProvider(),
                factory,
                new FakeWindowFocusService(),
                new FakeClickSender());
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            manager.ShowOverlays();

            Assert.Equal("No active displays found.", error);
            Assert.False(manager.IsActive);
            Assert.Empty(factory.Windows);
        });
    }

    [Fact]
    public void PrimarySurfaceEvents_UpdateSelectionAndCloseBeforeSendingClick()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = new FakeMonitorProvider(
                new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.0, true));
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            clickSender.WasClickTargetActivated = () =>
                focus.ForegroundWindow == new nint(0x500);
            clickSender.AreAllOverlaysClosed = () =>
                factory.Windows.Count > 0 && factory.Windows.All(window => window.CloseCount > 0);
            var manager = CreateManager(monitorProvider, factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            int afterColumnUpdateCount = primary.RenderUpdateCount;
            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            Assert.Single(manager.SelectionManager.History);
            Assert.Equal(afterColumnUpdateCount, primary.RenderUpdateCount);

            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            primary.RaiseMove(1, 0);
            Assert.Equal(afterColumnUpdateCount + 2, primary.RenderUpdateCount);

            primary.RaiseConfirm(ClickKind.Left);

            Assert.False(manager.IsActive);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Equal(1, primary.CloseCount);
            Assert.True(clickSender.Called);
            Assert.Equal(ClickKind.Left, clickSender.LastClickKind);
            Assert.True(clickSender.OverlaysWereClosedBeforeSend);
            Assert.Equal(new PixelRect(0, 0, 1920, 1080), clickSender.LastVirtualScreen);
            Assert.NotNull(clickSender.LastTarget);
            Assert.Null(error);
            Assert.True(clickSender.ClickTargetWasActivatedBeforeSend);
            Assert.Equal(new nint(0x500), focus.ForegroundWindow);
        });
    }

    [Fact]
    public void PrimarySurfaceSendFailure_RestoresOriginatingForegroundAndRaisesError()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender { ReturnResult = false };
            clickSender.WasClickTargetActivated = () =>
                focus.ForegroundWindow == new nint(0x500);
            var manager = CreateManager(monitorProvider, factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseConfirm(ClickKind.Left);

            Assert.False(manager.IsActive);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.True(clickSender.ClickTargetWasActivatedBeforeSend);
            Assert.Equal(new nint(71), focus.ForegroundWindow);
            Assert.Contains("Failed to dispatch click", error);
        });
    }

    [Fact]
    public void PrimarySurface_WhenNoWindowIsUnderTarget_DoesNotSendClickAndRestoresOrigin()
    {
        UiTestThread.Run(() =>
        {
            var focus = new FakeWindowFocusService
            {
                ForegroundWindow = new nint(71),
                WindowAtPoint = nint.Zero
            };
            var factory = new FakeOverlayFactory();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseConfirm(ClickKind.Left);

            Assert.False(manager.IsActive);
            Assert.False(clickSender.Called);
            Assert.Equal(new nint(71), focus.ForegroundWindow);
            Assert.Contains("The click was not sent", error);
        });
    }

    [Fact]
    public void PrimarySurface_WhenTargetChangesAfterActivation_DoesNotSendClickAndRestoresOrigin()
    {
        UiTestThread.Run(() =>
        {
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            focus.WindowAtPointResults.Enqueue(new nint(0x500));
            focus.WindowAtPointResults.Enqueue(new nint(0x600));
            var factory = new FakeOverlayFactory();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseConfirm(ClickKind.Left);

            Assert.False(manager.IsActive);
            Assert.False(clickSender.Called);
            Assert.Equal(new nint(71), focus.ForegroundWindow);
            Assert.Contains("The click was not sent", error);
        });
    }

    [Theory]
    [InlineData(ClickKind.Right)]
    [InlineData(ClickKind.Middle)]
    public void PrimarySurfaceConfirmWithModifiedEnterDispatchesRequestedClickKind(
        ClickKind clickKind)
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            clickSender.AreAllOverlaysClosed = () =>
                factory.Windows.Count > 0 && factory.Windows.All(window => window.CloseCount > 0);
            var manager = CreateManager(monitorProvider, factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseConfirm(clickKind);

            Assert.False(manager.IsActive);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.True(clickSender.Called);
            Assert.Equal(clickKind, clickSender.LastClickKind);
            Assert.True(clickSender.OverlaysWereClosedBeforeSend);
            Assert.NotNull(clickSender.LastTarget);
        });
    }

    [Fact]
    public void ShowOverlaysWhileActive_RestartsSelectionAndClosesPreviousSurfaces()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = new FakeMonitorProvider(
                new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.0, true));
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            var oldWindow = Assert.Single(factory.Windows);
            Assert.True(manager.SelectionManager.ProcessKey('Q'));

            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();

            Assert.Equal(2, factory.Windows.Count);
            Assert.Equal(1, oldWindow.CloseCount);
            Assert.Empty(manager.SelectionManager.History);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.True(manager.IsActive);
            manager.CloseOverlays();
            Assert.Equal(new nint(71), focus.LastRestoredWindow);
        });
    }

    [Fact]
    public void MonitorChange_CancelsSelectionAndClosesSurfaces()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = new FakeMonitorProvider(
                new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.0, true));
            var factory = new FakeOverlayFactory();
            var manager = CreateManager(
                monitorProvider,
                factory,
                new FakeWindowFocusService(),
                new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);
            Assert.True(manager.SelectionManager.ProcessKey('Q'));

            monitorProvider.RaiseChanged();

            Assert.False(manager.IsActive);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Equal(1, primary.CloseCount);
        });
    }

    [Fact]
    public void VisibleForeignFocusLoss_CancelsSelection()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);
            primary.RaiseContinuousModeToggle();
            Assert.True(manager.IsContinuousMode);
            focus.ForegroundWindow = new nint(99);
            focus.FocusedWindow = new nint(0x777);
            focus.ForegroundProcessId = 999;
            focus.ForegroundIsVisible = true;

            primary.RaiseFocusLost();
            UiTestThread.DrainDispatcher();

            Assert.False(manager.IsActive);
            Assert.False(manager.IsContinuousMode);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Equal(1, primary.CloseCount);
            Assert.Equal(new nint(99), focus.LastProcessWindow);
            Assert.Equal(nint.Zero, focus.LastRestoredWindow);
        });
    }

    [Fact]
    public void TransientFocusLoss_ReactivatesPrimaryAndKeepsSelection()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);
            primary.RaiseContinuousModeToggle();
            Assert.True(manager.IsContinuousMode);
            Assert.True(manager.SelectionManager.ProcessKey('Q'));
            focus.ForegroundWindow = new nint(99);
            focus.FocusedWindow = new nint(0x777);
            focus.ForegroundProcessId = (uint)Environment.ProcessId;
            focus.ForegroundIsVisible = false;

            primary.RaiseFocusLost();
            UiTestThread.DrainDispatcher();

            Assert.True(manager.IsActive);
            Assert.True(manager.IsContinuousMode);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Single(manager.SelectionManager.History);
            Assert.Equal(2, primary.TryReactivateCount);
            Assert.Equal(0, primary.CloseCount);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ShowOverlays_WhenFocusMovesBeforeStartupCheck_CancelsSelection()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);

            focus.ForegroundWindow = new nint(99);
            focus.FocusedWindow = new nint(0x777);
            focus.ForegroundProcessId = 999;
            focus.ForegroundIsVisible = true;

            // The startup watchdog queues a second deferred focus check.
            UiTestThread.DrainDispatcher();
            UiTestThread.DrainDispatcher();

            Assert.False(manager.IsActive);
            Assert.Equal(1, primary.CloseCount);
            Assert.Equal(nint.Zero, focus.LastRestoredWindow);
        });
    }

    [Fact]
    public void FocusLoss_WhenPrimaryIsForegroundButFocusMoved_ReactivatesPrimary()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);
            focus.FocusedWindow = new nint(99);

            primary.RaiseFocusLost();
            UiTestThread.DrainDispatcher();

            Assert.True(manager.IsActive);
            Assert.Equal(2, primary.TryReactivateCount);
            Assert.Equal(primary.Handle, focus.ForegroundWindow);
            Assert.Equal(primary.Handle, focus.FocusedWindow);
            Assert.Equal(0, primary.CloseCount);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void FocusLoss_WhenFocusMovesAgainDuringRecovery_ReevaluatesAndCancels()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);
            focus.ForegroundWindow = nint.Zero;
            focus.FocusedWindow = nint.Zero;
            focus.ForegroundProcessId = 0;
            focus.ForegroundIsVisible = false;
            primary.AfterReactivate = () =>
            {
                focus.ForegroundWindow = new nint(99);
                focus.FocusedWindow = new nint(0x777);
                focus.ForegroundProcessId = 999;
                focus.ForegroundIsVisible = true;
            };

            primary.RaiseFocusLost();
            UiTestThread.DrainDispatcher();
            UiTestThread.DrainDispatcher();

            Assert.False(manager.IsActive);
            Assert.Equal(1, primary.CloseCount);
            Assert.Equal(2, primary.TryReactivateCount);
        });
    }

    [Fact]
    public void FocusLoss_WhenPrimaryRemainsForegroundAndFocused_DoesNotReacquire()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseFocusLost();
            UiTestThread.DrainDispatcher();

            Assert.True(manager.IsActive);
            Assert.Equal(1, primary.TryReactivateCount);
            Assert.Equal(0, primary.CloseCount);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void DisplayConfigurationChange_IsDeferredThenCancelsSelection()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var manager = CreateManager(
                monitorProvider,
                factory,
                new FakeWindowFocusService(),
                new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);
            primary.RaiseContinuousModeToggle();
            Assert.True(manager.IsContinuousMode);

            primary.RaiseDisplayConfigurationChanged();

            Assert.True(manager.IsActive);
            UiTestThread.DrainDispatcher();

            Assert.False(manager.IsActive);
            Assert.False(manager.IsContinuousMode);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Equal(1, primary.CloseCount);
        });
    }

    [Fact]
    public void Tab_TogglesContinuousModeAndUpdatesTheRenderedIndicator()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var manager = CreateManager(
                SingleMonitorProvider(),
                factory,
                new FakeWindowFocusService(),
                new FakeClickSender());
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            Assert.False(manager.IsContinuousMode);
            Assert.False(primary.LastContinuousMode);

            primary.RaiseContinuousModeToggle();
            Assert.True(manager.IsContinuousMode);
            Assert.True(primary.LastContinuousMode);

            primary.RaiseContinuousModeToggle();
            Assert.False(manager.IsContinuousMode);
            Assert.False(primary.LastContinuousMode);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ContinuousMode_Confirm_ReopensStage1AndAllowsAnotherClick()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            clickSender.WasClickTargetActivated = () =>
                focus.ForegroundWindow == new nint(0x500);
            clickSender.AreAllOverlaysClosed = () =>
                factory.Windows.Count > 0 && factory.Windows.All(window => window.CloseCount > 0);
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var firstPrimary = Assert.Single(factory.Windows);

            firstPrimary.RaiseContinuousModeToggle();
            Assert.True(manager.IsContinuousMode);

            firstPrimary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            firstPrimary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            firstPrimary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.CallCount);
            Assert.True(clickSender.OverlaysWereClosedBeforeSend);
            Assert.True(manager.IsActive);
            Assert.True(manager.IsContinuousMode);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Empty(manager.SelectionManager.History);
            Assert.Equal(1, firstPrimary.CloseCount);

            Assert.Equal(2, factory.Windows.Count);
            var secondPrimary = factory.Windows[1];
            Assert.True(secondPrimary.IsPrimary);
            Assert.True(secondPrimary.LastContinuousMode);
            Assert.Equal(1, secondPrimary.ShowCount);
            Assert.Equal(1, secondPrimary.TryReactivateCount);
            Assert.Equal(secondPrimary.Handle, focus.ForegroundWindow);

            secondPrimary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            secondPrimary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            secondPrimary.RaiseConfirm(ClickKind.Right);

            Assert.Equal(2, clickSender.CallCount);
            Assert.Equal(ClickKind.Right, clickSender.LastClickKind);
            Assert.True(manager.IsActive);
            Assert.Equal(3, factory.Windows.Count);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);

            manager.CloseOverlays();
            Assert.False(manager.IsContinuousMode);
            Assert.False(manager.IsActive);
        });
    }

    [Fact]
    public void ContinuousMode_ToggleOff_RestoresNormalExitAfterConfirmation()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            clickSender.WasClickTargetActivated = () =>
                focus.ForegroundWindow == new nint(0x500);
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseContinuousModeToggle();
            Assert.True(manager.IsContinuousMode);
            primary.RaiseContinuousModeToggle();
            Assert.False(manager.IsContinuousMode);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.CallCount);
            Assert.False(manager.IsActive);
            Assert.False(manager.IsContinuousMode);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Single(factory.Windows);
        });
    }

    [Fact]
    public void ContinuousMode_EscClosesOverlaysAndResetsTheMode()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var manager = CreateManager(
                SingleMonitorProvider(),
                factory,
                focus,
                new FakeClickSender());
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseContinuousModeToggle();
            Assert.True(manager.IsContinuousMode);
            primary.RaiseCancel();

            Assert.False(manager.IsActive);
            Assert.False(manager.IsContinuousMode);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Equal(1, primary.CloseCount);
            Assert.Equal(new nint(71), focus.ForegroundWindow);
        });
    }

    [Fact]
    public void HotkeyRestart_ResetsContinuousMode()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var manager = CreateManager(
                SingleMonitorProvider(),
                factory,
                focus,
                new FakeClickSender());
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var firstPrimary = Assert.Single(factory.Windows);
            firstPrimary.RaiseContinuousModeToggle();
            Assert.True(manager.IsContinuousMode);

            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();

            Assert.True(manager.IsActive);
            Assert.False(manager.IsContinuousMode);
            Assert.Equal(1, firstPrimary.CloseCount);
            Assert.Equal(2, factory.Windows.Count);
            Assert.False(factory.Windows[1].LastContinuousMode);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ContinuousMode_WhenClickDispatchFails_ReopensStage1AndReportsError()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender { ReturnResult = false };
            clickSender.WasClickTargetActivated = () =>
                focus.ForegroundWindow == new nint(0x500);
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseContinuousModeToggle();
            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.CallCount);
            Assert.True(manager.IsActive);
            Assert.True(manager.IsContinuousMode);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Equal(2, factory.Windows.Count);
            Assert.Contains("Failed to dispatch click", error);

            manager.CloseOverlays();
        });
    }

    private static FakeMonitorProvider SingleMonitorProvider()
    {
        return new FakeMonitorProvider(
            new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.0, true));
    }

    private static TestOverlayManager CreateManager(
        FakeMonitorProvider monitorProvider,
        FakeOverlayFactory factory,
        FakeWindowFocusService focus,
        FakeClickSender clickSender)
    {
        factory.FocusService = focus;
        var selectionManager = new SelectionManager();
        var manager = new OverlayManager(
            monitorProvider,
            selectionManager,
            new ClickExecutor(clickSender),
            factory.Create,
            focus);
        return new TestOverlayManager(manager, selectionManager);
    }

    private sealed class TestOverlayManager
    {
        public OverlayManager Manager { get; }
        public SelectionManager SelectionManager { get; }
        public bool IsActive => Manager.IsActive;
        public bool IsContinuousMode => Manager.IsContinuousMode;

        public event Action<string>? ErrorOccurred
        {
            add => Manager.ErrorOccurred += value;
            remove => Manager.ErrorOccurred -= value;
        }

        public TestOverlayManager(OverlayManager manager, SelectionManager selectionManager)
        {
            Manager = manager;
            SelectionManager = selectionManager;
        }

        public void ShowOverlays() => Manager.ShowOverlays();

        public void CloseOverlays() => Manager.CloseOverlays();
    }

    private sealed class FakeMonitorProvider(params MonitorInfo[] monitors) : IMonitorProvider
    {
        public event Action? Changed;

        public IReadOnlyList<MonitorInfo> GetMonitors() => monitors;

        public void RaiseChanged() => Changed?.Invoke();
    }

    private sealed class FakeOverlayFactory
    {
        private nint _nextHandle = new(0x1000);

        public List<FakeOverlayWindowSurface> Windows { get; } = new();
        public bool TryReactivateResult { get; set; } = true;
        public FakeWindowFocusService? FocusService { get; set; }

        public IOverlayWindowSurface Create(PixelRect rect, bool isPrimary, double dpiScale)
        {
            var window = new FakeOverlayWindowSurface(
                rect,
                isPrimary,
                dpiScale,
                _nextHandle,
                FocusService);
            _nextHandle += 0x10;
            window.TryReactivateResult = TryReactivateResult;
            Windows.Add(window);
            return window;
        }
    }

    private sealed class FakeOverlayWindowSurface(
        PixelRect monitorRect,
        bool isPrimary,
        double dpiScale,
        nint handle,
        FakeWindowFocusService? focusService) : IOverlayWindowSurface
    {
        public event Action<char>? KeyPressed;
        public event Action<int, int>? MoveRequested;
        public event Action<ClickKind>? ConfirmRequested;
        public event Action? BackspacePressed;
        public event Action? CancelRequested;
        public event Action? ContinuousModeToggleRequested;
        public event Action? FocusLost;
        public event Action? DisplayConfigurationChanged;

        public bool IsPrimary { get; } = isPrimary;
        public double DpiScale { get; } = dpiScale;
        public nint Handle { get; } = handle;
        public bool LastContinuousMode { get; private set; }
        public int ShowCount { get; private set; }
        public int CloseCount { get; private set; }
        public int RenderUpdateCount { get; private set; }
        public int RefreshExpectedDpiCount { get; private set; }
        public bool DpiNotificationsEnabled { get; private set; }
        public bool TryReactivateResult { get; set; }
        public int TryReactivateCount { get; private set; }
        public Action? AfterReactivate { get; set; }

        public bool TryReactivate()
        {
            TryReactivateCount++;
            if (TryReactivateResult && focusService != null)
            {
                focusService.ForegroundWindow = Handle;
                focusService.FocusedWindow = Handle;
                var afterReactivate = AfterReactivate;
                AfterReactivate = null;
                afterReactivate?.Invoke();
            }

            return TryReactivateResult;
        }

        public void UpdateRenderState(
            SelectionManager selectionManager,
            IReadOnlyList<PixelRect> monitorRects,
            bool isContinuousMode)
        {
            Assert.NotEqual(SelectionState.Standby, selectionManager.State);
            Assert.Contains(monitorRect, monitorRects);
            LastContinuousMode = isContinuousMode;
            RenderUpdateCount++;
        }

        public void RefreshExpectedDpi() => RefreshExpectedDpiCount++;

        public void EnableDpiChangeNotifications() => DpiNotificationsEnabled = true;

        public void Show() => ShowCount++;

        public void Close() => CloseCount++;

        public void RaiseKey(char key) => KeyPressed?.Invoke(key);

        public void RaiseMove(int columnDelta, int rowDelta) => MoveRequested?.Invoke(columnDelta, rowDelta);

        public void RaiseConfirm(ClickKind clickKind) => ConfirmRequested?.Invoke(clickKind);

        public void RaiseBackspace() => BackspacePressed?.Invoke();

        public void RaiseCancel() => CancelRequested?.Invoke();

        public void RaiseContinuousModeToggle() => ContinuousModeToggleRequested?.Invoke();

        public void RaiseFocusLost() => FocusLost?.Invoke();

        public void RaiseDisplayConfigurationChanged() => DisplayConfigurationChanged?.Invoke();
    }

    private sealed class FakeWindowFocusService : IWindowFocusService
    {
        public nint ForegroundWindow { get; set; }
        public nint FocusedWindow { get; set; }
        public uint ForegroundProcessId { get; set; }
        public bool ForegroundIsVisible { get; set; }
        public bool RestoreResult { get; set; } = true;
        public nint WindowAtPoint { get; set; } = new(0x500);
        public Queue<nint> WindowAtPointResults { get; } = new();
        public Dictionary<nint, nint> RootByWindow { get; } = new();
        public int GetForegroundWindowCalls { get; private set; }
        public int TryLockForegroundCalls { get; private set; }
        public int UnlockForegroundCalls { get; private set; }
        public nint LastProcessWindow { get; private set; }
        public nint LastRestoredWindow { get; private set; }

        public nint GetForegroundWindow()
        {
            GetForegroundWindowCalls++;
            return ForegroundWindow;
        }

        public nint GetFocusedWindow() => FocusedWindow;

        public uint GetWindowProcessId(nint window)
        {
            LastProcessWindow = window;
            return ForegroundProcessId;
        }

        public bool IsWindowVisible(nint window) => ForegroundIsVisible;

        public bool TryRestoreForegroundWindow(nint window)
        {
            LastRestoredWindow = window;
            if (RestoreResult)
            {
                ForegroundWindow = window;
                FocusedWindow = window;
            }

            return RestoreResult;
        }

        public nint GetWindowFromPoint(PixelPoint point) =>
            WindowAtPointResults.Count > 0 ? WindowAtPointResults.Dequeue() : WindowAtPoint;

        public nint GetRootWindow(nint window) =>
            RootByWindow.TryGetValue(window, out nint root) ? root : window;

        public bool IsWindowOwnedBy(nint window, nint rootWindow) =>
            window != nint.Zero
            && rootWindow != nint.Zero
            && GetRootWindow(window) == rootWindow;

        public bool TryLockForeground()
        {
            TryLockForegroundCalls++;
            return true;
        }

        public void UnlockForeground() => UnlockForegroundCalls++;
    }

    private sealed class FakeClickSender : IClickSender
    {
        public bool Called => CallCount > 0;
        public int CallCount { get; private set; }
        public bool ReturnResult { get; set; } = true;
        public bool OverlaysWereClosedBeforeSend { get; private set; }
        public bool ClickTargetWasActivatedBeforeSend { get; private set; }
        public Func<bool>? WasClickTargetActivated { get; set; }
        public Func<bool>? AreAllOverlaysClosed { get; set; }
        public PixelPoint? LastTarget { get; private set; }
        public PixelRect? LastVirtualScreen { get; private set; }
        public ClickKind? LastClickKind { get; private set; }

        public bool SendClick(PixelPoint target, PixelRect virtualScreen, ClickKind clickKind)
        {
            CallCount++;
            LastTarget = target;
            LastVirtualScreen = virtualScreen;
            LastClickKind = clickKind;
            OverlaysWereClosedBeforeSend = AreAllOverlaysClosed?.Invoke() ?? false;
            ClickTargetWasActivatedBeforeSend = WasClickTargetActivated?.Invoke() ?? false;
            return ReturnResult;
        }
    }
}
