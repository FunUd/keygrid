using KeyGrid.Core.Abstractions;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using KeyGrid.Core.Services;
using KeyGrid.Interop;
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
    public void Click_UsesVirtualDesktopBounds_NotActiveMonitorBounds()
    {
        UiTestThread.Run(() =>
        {
            var primaryMonitor = new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.0, true);
            var secondaryMonitor = new MonitorInfo(new PixelRect(-800, 0, 800, 600), 1.5, false);
            var monitorProvider = new FakeMonitorProvider(primaryMonitor, secondaryMonitor);
            var focusService = new FakeWindowFocusService { ForegroundWindow = nint.Zero };
            var factory = new FakeOverlayFactory { FocusService = focusService };
            var clickSender = new FakeClickSender();
            clickSender.WasClickTargetActivated = () => focusService.ForegroundWindow == new nint(0x500);
            clickSender.AreAllOverlaysClosed = () =>
                factory.Windows.Count > 0 && factory.Windows.All(window => window.CloseCount > 0);

            var manager = CreateManager(monitorProvider, factory, focusService, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();

            // Select a cell (Stage 1: column A, row A -> Stage 2: move once)
            Assert.Equal(2, factory.Windows.Count);
            var primaryOverlay = factory.Windows.Single(w => w.IsPrimary);
            primaryOverlay.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primaryOverlay.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            primaryOverlay.RaiseMove(1, 0);

            // Request click
            primaryOverlay.RaiseConfirm(ClickKind.Left);

            // Verify that the virtual screen used for normalization includes both monitors
            Assert.True(clickSender.Called);
            Assert.NotNull(clickSender.LastVirtualScreen);
            var expectedVirtualScreen = new PixelRect(-800, 0, 2720, 1080);
            Assert.Equal(expectedVirtualScreen, clickSender.LastVirtualScreen.Value);
        });
    }

    [Fact]
    public void ShowOverlays_ResolvesDpiReferenceMonitorFromForegroundWindow_WithFallbacks()
    {
        UiTestThread.Run(() =>
        {
            var primaryMonitor = new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.0, true);
            var secondaryMonitor = new MonitorInfo(new PixelRect(1920, 0, 3840, 2160), 1.5, false);
            var monitorProvider = new FakeMonitorProvider(primaryMonitor, secondaryMonitor);
            var foregroundHwnd = new nint(0x1234);
            monitorProvider.SetMonitorForWindow(foregroundHwnd, secondaryMonitor);

            var focusService = new FakeWindowFocusService { ForegroundWindow = foregroundHwnd };
            var factory = new FakeOverlayFactory { FocusService = focusService };
            var clickSender = new FakeClickSender();

            var manager = CreateManager(monitorProvider, factory, focusService, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();

            // When foreground window is on secondary monitor (1.5x DPI), selection manager receives 1.5
            Assert.Equal(1.5, manager.SelectionManager.DpiReferenceScale);
            manager.CloseOverlays();

            // Now test fallback to primary when foreground window is null/unresolved
            focusService.ForegroundWindow = nint.Zero;
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();

            Assert.Equal(1.0, manager.SelectionManager.DpiReferenceScale);
            manager.CloseOverlays();

            // Now test fallback to first monitor when no primary monitor exists
            var nonPrimary1 = new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.25, false);
            var nonPrimary2 = new MonitorInfo(new PixelRect(1920, 0, 1920, 1080), 2.0, false);
            var noPrimaryProvider = new FakeMonitorProvider(nonPrimary1, nonPrimary2);
            var managerNoPrimary = CreateManager(noPrimaryProvider, factory, focusService, clickSender);
            managerNoPrimary.ShowOverlays();
            UiTestThread.DrainDispatcher();

            Assert.Equal(1.25, managerNoPrimary.SelectionManager.DpiReferenceScale);
            managerNoPrimary.CloseOverlays();
        });
    }

    [Fact]
    public void MonitorSelectDigit_MovesStage1GridToNumberedMonitor()
    {
        UiTestThread.Run(() =>
        {
            // Spatial order (top, then left): the left monitor is 1, the primary
            // monitor at the origin is 2.
            var leftMonitor = new MonitorInfo(new PixelRect(-800, 0, 800, 600), 1.0, false);
            var primaryMonitor = new MonitorInfo(new PixelRect(0, 0, 1920, 1080), 1.5, true);
            var monitorProvider = new FakeMonitorProvider(leftMonitor, primaryMonitor);
            var focus = new FakeWindowFocusService { ForegroundWindow = nint.Zero };
            var factory = new FakeOverlayFactory { FocusService = focus };
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();

            // With no resolvable foreground window, the primary monitor hosts the
            // initial grid.
            Assert.Equal(primaryMonitor.Bounds, manager.SelectionManager.ActiveMonitorRect);
            Assert.Equal(1.5, manager.SelectionManager.DpiReferenceScale);
            // Each overlay carries its own digit-key number; the primary monitor
            // is monitor 2 in spatial order.
            Assert.Equal("1/2", factory.Windows[0].LastMonitorBadgeLabel);
            Assert.Equal("2/2", factory.Windows[1].LastMonitorBadgeLabel);

            var primary = factory.Windows.Single(w => w.IsPrimary);
            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseMonitorSelect(1);

            Assert.Equal(leftMonitor.Bounds, manager.SelectionManager.ActiveMonitorRect);
            Assert.Equal(1.0, manager.SelectionManager.DpiReferenceScale);
            Assert.Empty(manager.SelectionManager.History);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            // Badge labels are positional and do not change on a monitor switch.
            Assert.Equal("1/2", factory.Windows[0].LastMonitorBadgeLabel);
            Assert.Equal("2/2", factory.Windows[1].LastMonitorBadgeLabel);

            // Out-of-range monitor numbers are ignored.
            primary.RaiseMonitorSelect(3);
            Assert.Equal(leftMonitor.Bounds, manager.SelectionManager.ActiveMonitorRect);

            manager.CloseOverlays();
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

    [Fact]
    public void Stage1_DRemainsAColumnKeyAndDoesNotStartDrag()
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

            primary.RaiseDragStart();

            Assert.False(manager.HasDragSelection);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Single(manager.SelectionManager.History);
            Assert.Equal('D', manager.SelectionManager.History[0]);
        });
    }

    [Fact]
    public void DragStart_SetsAnchorAndEscClearsItBeforeExiting()
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

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var expectedStart));

            primary.RaiseDragStart();

            Assert.True(manager.HasDragSelection);
            Assert.Equal(expectedStart, primary.LastDragStartPoint);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Empty(manager.SelectionManager.History);

            primary.RaiseDragStart();
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseBackspace();
            primary.RaiseBackspace();

            Assert.True(manager.HasDragSelection);
            Assert.Equal(expectedStart, primary.LastDragStartPoint);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Empty(manager.SelectionManager.History);

            primary.RaiseCancel();
            Assert.False(manager.HasDragSelection);
            Assert.True(manager.IsActive);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);

            primary.RaiseCancel();
            Assert.False(manager.IsActive);
        });
    }

    [Fact]
    public void DragExecution_DispatchesStartAndEndAndClosesOverlays()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var expectedStart));
            primary.RaiseDragStart();
            primary.RaiseDragStart();
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            primary.RaiseMove(1, 0);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var expectedEnd));

            focus.WindowAtPointResults.Enqueue(new nint(0x500));
            focus.WindowAtPointResults.Enqueue(new nint(0x600));
            focus.WindowAtPointResults.Enqueue(new nint(0x500));
            focus.WindowAtPointResults.Enqueue(new nint(0x600));
            clickSender.AreAllOverlaysHidden = () => primary.HideForInputCount > 0;

            primary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.DragCallCount);
            Assert.Equal(expectedStart, clickSender.LastDragStart);
            Assert.Equal(expectedEnd, clickSender.LastDragEnd);
            Assert.True(clickSender.DragOverlaysWereHidden);
            Assert.False(manager.HasDragSelection);
            Assert.False(manager.IsActive);
        });
    }

    [Fact]
    public void DragExecution_InContinuousMode_ReopensStage1AfterSuccess()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseContinuousModeToggle();
            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseDragStart();
            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);

            focus.WindowAtPointResults.Enqueue(new nint(0x500));
            focus.WindowAtPointResults.Enqueue(new nint(0x600));
            focus.WindowAtPointResults.Enqueue(new nint(0x500));
            focus.WindowAtPointResults.Enqueue(new nint(0x600));

            primary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.DragCallCount);
            Assert.True(manager.IsActive);
            Assert.True(manager.IsContinuousMode);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.False(manager.HasDragSelection);
        });
    }

    [Fact]
    public void DragExecution_WhenSendFails_RestoresStage2AndReportsError()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender { ReturnResult = false };
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseDragStart();
            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseMove(1, 0);

            focus.WindowAtPointResults.Enqueue(new nint(0x500));
            focus.WindowAtPointResults.Enqueue(new nint(0x600));
            focus.WindowAtPointResults.Enqueue(new nint(0x500));
            focus.WindowAtPointResults.Enqueue(new nint(0x600));

            string? error = null;
            manager.ErrorOccurred += message => error = message;
            primary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.DragCallCount);
            Assert.True(manager.IsActive);
            Assert.False(manager.HasDragSelection);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            Assert.Contains("Failed to dispatch drag", error);
        });
    }

    [Fact]
    public void RangeStart_SetsAnchorAndEscClearsItBeforeExiting()
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

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var expectedStart));

            primary.RaiseRangeStart();

            Assert.True(manager.HasRangeSelection);
            Assert.Equal(expectedStart, primary.LastRangeStartPoint);
            Assert.True(manager.IsActive);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Empty(manager.SelectionManager.History);

            primary.RaiseCancel();

            Assert.False(manager.HasRangeSelection);
            Assert.True(manager.IsActive);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Empty(manager.SelectionManager.History);
            Assert.Null(primary.LastRangeStartPoint);
            Assert.Equal(0, primary.CloseCount);

            primary.RaiseCancel();

            Assert.False(manager.IsActive);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Equal(1, primary.CloseCount);
        });
    }

    [Fact]
    public void RangeDragWorkflowStart_CapturesRangeAnchorAndReturnsToStage1()
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
            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var expectedStart));
            Assert.True(primary.LastAllowRangeDragChord);

            primary.RaiseRangeDragWorkflow();

            Assert.True(manager.IsRangeDragWorkflow);
            Assert.True(manager.HasRangeSelection);
            Assert.False(manager.HasDragSelection);
            Assert.Equal(expectedStart, primary.LastRangeStartPoint);
            Assert.True(primary.LastRangeDragWorkflow);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Empty(manager.SelectionManager.History);
            Assert.False(primary.LastAllowRangeDragChord);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void RangeDragWorkflow_RangeEndpointBecomesDragAnchorAndCompletes()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var firstPrimary = Assert.Single(factory.Windows);
            firstPrimary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            firstPrimary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var expectedRangeStart));
            firstPrimary.RaiseRangeDragWorkflow();
            firstPrimary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            firstPrimary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            firstPrimary.RaiseMove(1, 0);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var expectedDragStart));

            firstPrimary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.RangeCallCount);
            Assert.Equal(expectedRangeStart, clickSender.LastRangeStart);
            Assert.Equal(expectedDragStart, clickSender.LastRangeEnd);
            Assert.True(manager.IsActive);
            Assert.False(manager.HasRangeSelection);
            Assert.True(manager.HasDragSelection);
            Assert.True(manager.IsRangeDragWorkflow);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Empty(manager.SelectionManager.History);

            var secondPrimary = factory.Windows[1];
            Assert.True(firstPrimary.CloseCount > 0);
            Assert.Equal(expectedDragStart, secondPrimary.LastDragStartPoint);
            Assert.True(secondPrimary.LastRangeDragWorkflow);

            secondPrimary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            secondPrimary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var dropTarget));
            secondPrimary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.DragCallCount);
            Assert.Equal(expectedDragStart, clickSender.LastDragStart);
            Assert.Equal(dropTarget, clickSender.LastDragEnd);
            Assert.False(manager.IsRangeDragWorkflow);
            Assert.False(manager.IsActive);
        });
    }

    [Fact]
    public void RangeExecution_DispatchesAnchorAndEndpointClicksAndClosesOverlays()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            clickSender.AreAllOverlaysHidden = () => primary.HideForInputCount > 0;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var expectedStart));
            primary.RaiseRangeStart();
            primary.RaiseRangeStart();
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            primary.RaiseMove(1, 0);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var expectedEnd));
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.RangeCallCount);
            Assert.Equal(expectedStart, clickSender.LastRangeStart);
            Assert.Equal(expectedEnd, clickSender.LastRangeEnd);
            Assert.True(clickSender.RangeOverlaysWereHidden);
            Assert.Equal(0, clickSender.CallCount);
            Assert.False(manager.IsActive);
            Assert.False(manager.HasRangeSelection);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(1, primary.CloseCount);
            Assert.Equal(new nint(0x500), focus.ForegroundWindow);
            Assert.Null(error);
        });
    }

    [Fact]
    public void RangeExecution_WhenEndpointsUseDifferentWindows_AbortsAndReturnsToStage2()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            focus.WindowAtPointResults.Enqueue(new nint(0x500));
            focus.WindowAtPointResults.Enqueue(new nint(0x600));
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseRangeStart();
            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseMove(1, 0);
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(0, clickSender.RangeCallCount);
            Assert.Equal(0, clickSender.CallCount);
            Assert.True(manager.IsActive);
            Assert.False(manager.HasRangeSelection);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(1, primary.ShowForInputCount);
            Assert.Equal(0, primary.CloseCount);
            Assert.Contains("same top-level window", error);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void RangeSelection_BackspaceInEndpointStage1KeepsTheStartMarker()
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

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseRangeStart();
            Assert.True(manager.HasRangeSelection);
            var expectedStart = primary.LastRangeStartPoint;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseBackspace();
            primary.RaiseBackspace();

            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.True(manager.HasRangeSelection);
            Assert.Equal(expectedStart, primary.LastRangeStartPoint);
            Assert.Empty(manager.SelectionManager.History);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void RangeExecution_IgnoresModifiedConfirmationKeys()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(
                SingleMonitorProvider(),
                factory,
                new FakeWindowFocusService(),
                clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseRangeStart();
            primary.RaiseConfirm(ClickKind.Right);
            primary.RaiseConfirm(ClickKind.Middle);

            Assert.Equal(0, clickSender.RangeCallCount);
            Assert.Equal(0, clickSender.CallCount);
            Assert.True(manager.IsActive);
            Assert.True(manager.HasRangeSelection);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void RangeExecution_InContinuousMode_ReopensStage1()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var firstPrimary = Assert.Single(factory.Windows);

            firstPrimary.RaiseContinuousModeToggle();
            firstPrimary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            firstPrimary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            firstPrimary.RaiseRangeStart();
            firstPrimary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            firstPrimary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            firstPrimary.RaiseMove(1, 0);
            firstPrimary.RaiseConfirm(ClickKind.Left);

            Assert.Equal(1, clickSender.RangeCallCount);
            Assert.True(manager.IsActive);
            Assert.True(manager.IsContinuousMode);
            Assert.False(manager.HasRangeSelection);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(1, manager.SelectionManager.CurrentStage);
            Assert.Empty(manager.SelectionManager.History);
            Assert.Equal(2, factory.Windows.Count);
            Assert.True(factory.Windows[1].LastContinuousMode);
            Assert.Null(factory.Windows[1].LastRangeStartPoint);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void PageDown_EntersScrollModeAndScrollsTheCurrentTarget()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService { ForegroundWindow = new nint(71) };
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            clickSender.AreAllOverlaysHidden = () => primary.HideForInputCount > 0;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            Assert.True(manager.SelectionManager.TryGetStage2Target(out var target));

            primary.RaisePageScroll(1);

            Assert.True(manager.IsScrollMode);
            Assert.True(manager.IsActive);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            Assert.Equal(1, clickSender.ScrollCallCount);
            Assert.Equal(target, clickSender.LastScrollTarget);
            Assert.Equal(-5, clickSender.LastScrollNotches);
            Assert.False(clickSender.LastScrollHorizontal);
            Assert.True(clickSender.ScrollOverlaysWereHidden);
            Assert.Equal(0, clickSender.CallCount);
            Assert.Equal(0, clickSender.RangeCallCount);
            Assert.Equal(1, primary.HideForInputCount);
            // The scroll burst keeps the overlays hidden until input pauses.
            Assert.Equal(0, primary.ShowForInputCount);
            Assert.True(manager.ScrollKeyHook.IsInstalled);
            Assert.True(primary.LastScrollMode);
            // The target window owns the foreground while the burst is active.
            Assert.Equal(new nint(0x500), focus.ForegroundWindow);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ScrollMode_ArrowKeysScrollWithoutMovingTheStage2Cursor()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            int selectedColumn = manager.SelectionManager.SelectedColumnIndex;
            int selectedRow = manager.SelectionManager.SelectedRowIndex;
            primary.RaisePageScroll(1);
            Assert.Equal(1, clickSender.ScrollCallCount);

            primary.RaiseMove(0, 1);

            Assert.Equal(2, clickSender.ScrollCallCount);
            Assert.Equal(-1, clickSender.LastScrollNotches);
            Assert.False(clickSender.LastScrollHorizontal);
            Assert.Equal(selectedColumn, manager.SelectionManager.SelectedColumnIndex);
            Assert.Equal(selectedRow, manager.SelectionManager.SelectedRowIndex);

            primary.RaiseMove(1, 0);

            Assert.Equal(3, clickSender.ScrollCallCount);
            Assert.Equal(1, clickSender.LastScrollNotches);
            Assert.True(clickSender.LastScrollHorizontal);
            Assert.Equal(selectedColumn, manager.SelectionManager.SelectedColumnIndex);
            Assert.Equal(selectedRow, manager.SelectionManager.SelectedRowIndex);
            // The burst keeps the overlays hidden across all three scroll inputs.
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(0, primary.ShowForInputCount);

            // While the overlays are hidden, Esc arrives through the keyboard
            // hook and exits Scroll Mode back to Stage 2.
            Assert.True(manager.ScrollKeyHook.RaiseKey(NativeMethods.VK_ESCAPE));

            Assert.False(manager.IsScrollMode);
            Assert.True(manager.IsActive);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            Assert.False(manager.ScrollKeyHook.IsInstalled);
            Assert.Equal(1, primary.ShowForInputCount);
            Assert.False(primary.LastScrollMode);
            Assert.Equal(0, primary.CloseCount);

            primary.RaiseCancel();

            Assert.False(manager.IsActive);
            Assert.Equal(1, primary.CloseCount);
        });
    }

    [Fact]
    public void ScrollMode_IgnoresRangeStartContinuousToggleAndBackspace()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(
                SingleMonitorProvider(),
                factory,
                new FakeWindowFocusService(),
                clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaisePageScroll(1);
            Assert.True(manager.IsScrollMode);
            Assert.True(manager.ScrollKeyHook.IsInstalled);

            primary.RaiseRangeStart();
            primary.RaiseContinuousModeToggle();
            primary.RaiseBackspace();
            primary.RaiseConfirm(ClickKind.Left);

            Assert.True(manager.IsScrollMode);
            Assert.False(manager.HasRangeSelection);
            Assert.False(manager.IsContinuousMode);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            Assert.Equal(1, clickSender.ScrollCallCount);
            Assert.Equal(0, clickSender.RangeCallCount);
            Assert.Equal(0, clickSender.CallCount);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void PageScroll_IsIgnoredWhileARangeAnchorIsActive()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(
                SingleMonitorProvider(),
                factory,
                new FakeWindowFocusService(),
                clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseRangeStart();

            primary.RaisePageScroll(1);

            Assert.False(manager.IsScrollMode);
            Assert.True(manager.HasRangeSelection);
            Assert.Equal(0, clickSender.ScrollCallCount);
            Assert.True(manager.IsActive);
            Assert.False(primary.LastScrollMode);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ScrollFailure_ExitsScrollModeAndRestoresStage2()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var clickSender = new FakeClickSender { ReturnResult = false };
            var manager = CreateManager(
                SingleMonitorProvider(),
                factory,
                new FakeWindowFocusService(),
                clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaisePageScroll(1);

            Assert.Equal(1, clickSender.ScrollCallCount);
            Assert.False(manager.IsScrollMode);
            Assert.True(manager.IsActive);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(1, primary.ShowForInputCount);
            Assert.False(primary.LastScrollMode);
            Assert.Contains("Failed to dispatch scroll input", error);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ScrollPreparationFailure_DoesNotSendWheelAndRestoresStage2()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            focus.WindowAtPointResults.Enqueue(nint.Zero);
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaisePageScroll(1);

            Assert.Equal(0, clickSender.ScrollCallCount);
            Assert.False(manager.IsScrollMode);
            Assert.True(manager.IsActive);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Equal(2, manager.SelectionManager.CurrentStage);
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(1, primary.ShowForInputCount);
            Assert.Contains("Could not find a window under the scroll target", error);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ScrollBurst_KeepsOverlaysHiddenAcrossHookInput()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaisePageScroll(1);
            Assert.Equal(1, clickSender.ScrollCallCount);
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(0, primary.ShowForInputCount);

            // Arrow keys arrive through the hook while the overlays are hidden
            // and scroll without another hide/show cycle.
            Assert.True(manager.ScrollKeyHook.RaiseKey(NativeMethods.VK_DOWN));
            Assert.True(manager.ScrollKeyHook.RaiseKey(NativeMethods.VK_DOWN, isKeyUp: true));
            Assert.Equal(2, clickSender.ScrollCallCount);
            Assert.Equal(-1, clickSender.LastScrollNotches);
            Assert.False(clickSender.LastScrollHorizontal);
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(0, primary.ShowForInputCount);

            Assert.True(manager.ScrollKeyHook.RaiseKey(NativeMethods.VK_LEFT));
            Assert.Equal(3, clickSender.ScrollCallCount);
            Assert.Equal(-1, clickSender.LastScrollNotches);
            Assert.True(clickSender.LastScrollHorizontal);
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(0, primary.ShowForInputCount);

            // Page keys scroll five notches through the same path.
            Assert.True(manager.ScrollKeyHook.RaiseKey(NativeMethods.VK_NEXT));
            Assert.Equal(4, clickSender.ScrollCallCount);
            Assert.Equal(-5, clickSender.LastScrollNotches);
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(0, primary.ShowForInputCount);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ScrollBurst_IdleRestoresOverlaysButKeepsScrollMode()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaisePageScroll(1);
            Assert.Equal(1, primary.HideForInputCount);
            Assert.Equal(0, primary.ShowForInputCount);

            // Once scroll input pauses, the Stage 2 grid returns while Scroll
            // Mode stays active.
            manager.SimulateScrollBurstIdle();

            Assert.True(manager.IsScrollMode);
            Assert.True(manager.IsActive);
            Assert.Equal(1, primary.ShowForInputCount);
            Assert.True(primary.LastScrollMode);
            Assert.Equal(primary.Handle, focus.ForegroundWindow);

            // The next scroll key starts a new burst without reinstalling the hook.
            primary.RaiseMove(0, 1);
            Assert.Equal(2, clickSender.ScrollCallCount);
            Assert.Equal(-1, clickSender.LastScrollNotches);
            Assert.Equal(2, primary.HideForInputCount);
            Assert.Equal(1, primary.ShowForInputCount);
            Assert.True(manager.ScrollKeyHook.IsInstalled);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ScrollHook_PageRepeatsScrollAndOtherKeysAreConsumed()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaisePageScroll(1);
            Assert.Equal(1, clickSender.ScrollCallCount);

            // Holding PageDown keeps dispatching five-notch scrolls so the
            // burst stays alive for continuous scrolling.
            Assert.True(manager.ScrollKeyHook.RaiseKey(NativeMethods.VK_NEXT));
            Assert.Equal(2, clickSender.ScrollCallCount);
            Assert.Equal(-5, clickSender.LastScrollNotches);
            Assert.True(manager.ScrollKeyHook.RaiseKey(NativeMethods.VK_NEXT));
            Assert.Equal(3, clickSender.ScrollCallCount);
            Assert.Equal(-5, clickSender.LastScrollNotches);
            Assert.True(manager.ScrollKeyHook.RaiseKey(NativeMethods.VK_NEXT, isKeyUp: true));

            // Modified Page keys are consumed without scrolling.
            Assert.True(manager.ScrollKeyHook.RaiseKey(
                NativeMethods.VK_PRIOR,
                modifierMask: KeyboardHookModifiers.Shift));
            Assert.Equal(3, clickSender.ScrollCallCount);

            // Letters are consumed but ignored in Scroll Mode.
            Assert.True(manager.ScrollKeyHook.RaiseKey(0x41));
            Assert.Equal(3, clickSender.ScrollCallCount);

            // Ctrl-modified keys pass through so the target app still receives
            // system combinations, and the matching key-up passes too.
            Assert.False(manager.ScrollKeyHook.RaiseKey(
                0x43,
                modifierMask: KeyboardHookModifiers.Control));
            Assert.False(manager.ScrollKeyHook.RaiseKey(0x43, isKeyUp: true));
            Assert.Equal(3, clickSender.ScrollCallCount);

            manager.CloseOverlays();
        });
    }

    [Fact]
    public void ScrollBurst_TargetWindowChangeCancelsScrollMode()
    {
        UiTestThread.Run(() =>
        {
            var factory = new FakeOverlayFactory();
            var focus = new FakeWindowFocusService();
            var clickSender = new FakeClickSender();
            var manager = CreateManager(SingleMonitorProvider(), factory, focus, clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);
            string? error = null;
            manager.ErrorOccurred += message => error = message;

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaisePageScroll(1);
            Assert.Equal(1, clickSender.ScrollCallCount);

            // A different top-level window slides under the scroll target.
            focus.WindowAtPoint = new nint(0x600);
            focus.RootByWindow[new nint(0x600)] = new nint(0x700);

            Assert.True(manager.ScrollKeyHook.RaiseKey(NativeMethods.VK_DOWN));

            Assert.Equal(1, clickSender.ScrollCallCount);
            Assert.False(manager.IsScrollMode);
            Assert.False(manager.ScrollKeyHook.IsInstalled);
            Assert.Equal(1, primary.ShowForInputCount);
            Assert.Contains("window under the scroll target changed", error);

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
        var scrollKeyHook = new FakeScrollKeyHook();
        var manager = new OverlayManager(
            monitorProvider,
            selectionManager,
            new ClickExecutor(clickSender),
            factory.Create,
            focus,
            scrollKeyHook);
        return new TestOverlayManager(manager, selectionManager, scrollKeyHook);
    }

    private sealed class TestOverlayManager
    {
        public OverlayManager Manager { get; }
        public SelectionManager SelectionManager { get; }
        public FakeScrollKeyHook ScrollKeyHook { get; }
        public bool IsActive => Manager.IsActive;
        public bool IsContinuousMode => Manager.IsContinuousMode;
        public bool HasRangeSelection => Manager.HasRangeSelection;
        public bool HasDragSelection => Manager.HasDragSelection;
        public bool IsScrollMode => Manager.IsScrollMode;
        public bool IsRangeDragWorkflow => Manager.IsRangeDragWorkflow;

        public event Action<string>? ErrorOccurred
        {
            add => Manager.ErrorOccurred += value;
            remove => Manager.ErrorOccurred -= value;
        }

        public TestOverlayManager(
            OverlayManager manager,
            SelectionManager selectionManager,
            FakeScrollKeyHook scrollKeyHook)
        {
            Manager = manager;
            SelectionManager = selectionManager;
            ScrollKeyHook = scrollKeyHook;
        }

        public void ShowOverlays() => Manager.ShowOverlays();

        public void CloseOverlays() => Manager.CloseOverlays();

        public void SimulateScrollBurstIdle() => Manager.SimulateScrollBurstIdle();
    }

    private sealed class FakeMonitorProvider(params MonitorInfo[] monitors) : IMonitorProvider
    {
        private readonly Dictionary<nint, MonitorInfo> _windowToMonitor = new();

        public event Action? Changed;

        public IReadOnlyList<MonitorInfo> GetMonitors() => monitors;

        public MonitorInfo? GetMonitorForWindow(nint windowHandle)
        {
            if (windowHandle == nint.Zero)
            {
                return null;
            }

            return _windowToMonitor.TryGetValue(windowHandle, out var monitor) ? monitor : null;
        }

        public void SetMonitorForWindow(nint windowHandle, MonitorInfo monitor)
        {
            _windowToMonitor[windowHandle] = monitor;
        }

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
        public event Action? RangeStartRequested;
        public event Action? DragStartRequested;
        public event Action? RangeDragWorkflowRequested;
        public event Action<int>? PageScrollRequested;
        public event Action<int>? MonitorSelectRequested;
        public event Action? FocusLost;
        public event Action? DisplayConfigurationChanged;

        public PixelRect MonitorRect { get; } = monitorRect;
        public bool IsPrimary { get; } = isPrimary;
        public double DpiScale { get; } = dpiScale;
        public nint Handle { get; } = handle;
        public bool LastContinuousMode { get; private set; }
        public PixelPoint? LastRangeStartPoint { get; private set; }
        public bool LastScrollMode { get; private set; }
        public PixelPoint? LastDragStartPoint { get; private set; }
        public bool LastRangeDragWorkflow { get; private set; }
        public bool LastAllowRangeDragChord { get; private set; }
        public string? LastMonitorBadgeLabel { get; private set; }
        public int ShowCount { get; private set; }
        public int CloseCount { get; private set; }
        public int RenderUpdateCount { get; private set; }
        public int HideForInputCount { get; private set; }
        public int ShowForInputCount { get; private set; }
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
            bool isContinuousMode,
            PixelPoint? rangeStartPoint = null,
            bool isScrollMode = false,
            bool isRangeDragWorkflow = false,
            PixelPoint? dragStartPoint = null,
            string? monitorBadgeLabel = null)
        {
            Assert.NotEqual(SelectionState.Standby, selectionManager.State);
            Assert.Contains(monitorRect, monitorRects);
            LastContinuousMode = isContinuousMode;
            LastRangeStartPoint = rangeStartPoint;
            LastDragStartPoint = dragStartPoint;
            LastScrollMode = isScrollMode;
            LastRangeDragWorkflow = isRangeDragWorkflow;
            // Mirrors the eligibility computed by OverlayWindow.UpdateRenderState.
            LastAllowRangeDragChord = IsPrimary
                && selectionManager.CurrentStage == 2
                && !isScrollMode
                && !isRangeDragWorkflow
                && rangeStartPoint is null
                && dragStartPoint is null;
            LastMonitorBadgeLabel = monitorBadgeLabel;
            RenderUpdateCount++;
        }

        public void RefreshExpectedDpi() => RefreshExpectedDpiCount++;

        public void EnableDpiChangeNotifications() => DpiNotificationsEnabled = true;

        public void Show() => ShowCount++;

        public void HideForInput() => HideForInputCount++;

        public void ShowForInput() => ShowForInputCount++;

        public void Close() => CloseCount++;

        public void RaiseKey(char key) => KeyPressed?.Invoke(key);

        public void RaiseMove(int columnDelta, int rowDelta) => MoveRequested?.Invoke(columnDelta, rowDelta);

        public void RaiseConfirm(ClickKind clickKind) => ConfirmRequested?.Invoke(clickKind);

        public void RaiseBackspace() => BackspacePressed?.Invoke();

        public void RaiseCancel() => CancelRequested?.Invoke();

        public void RaiseContinuousModeToggle() => ContinuousModeToggleRequested?.Invoke();

        public void RaiseRangeStart() => RangeStartRequested?.Invoke();

        public void RaiseDragStart() => DragStartRequested?.Invoke();

        public void RaiseRangeDragWorkflow() => RangeDragWorkflowRequested?.Invoke();

        public void RaisePageScroll(int direction) => PageScrollRequested?.Invoke(direction);

        public void RaiseMonitorSelect(int monitorNumber) => MonitorSelectRequested?.Invoke(monitorNumber);

        public void RaiseFocusLost() => FocusLost?.Invoke();

        public void RaiseDisplayConfigurationChanged() => DisplayConfigurationChanged?.Invoke();

        public void SimulateKeyPress(char key) => RaiseKey(key);

        public void SimulateConfirm(ClickKind clickKind) => RaiseConfirm(clickKind);
    }

    private sealed class FakeScrollKeyHook : ILowLevelKeyboardHook
    {
        public event Func<KeyboardHookEvent, bool>? KeyEvent;

        public bool IsInstalled { get; private set; }
        public int InstallCount { get; private set; }
        public int UninstallCount { get; private set; }

        public void Install()
        {
            IsInstalled = true;
            InstallCount++;
        }

        public void Uninstall()
        {
            IsInstalled = false;
            UninstallCount++;
        }

        public bool RaiseKey(uint virtualKey, bool isKeyUp = false, uint modifierMask = 0)
        {
            return KeyEvent?.Invoke(new KeyboardHookEvent(virtualKey, isKeyUp, modifierMask)) == true;
        }
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
        public int RangeCallCount { get; private set; }
        public PixelPoint? LastRangeStart { get; private set; }
        public PixelPoint? LastRangeEnd { get; private set; }
        public bool RangeOverlaysWereHidden { get; private set; }
        public int DragCallCount { get; private set; }
        public PixelPoint? LastDragStart { get; private set; }
        public PixelPoint? LastDragEnd { get; private set; }
        public bool DragOverlaysWereHidden { get; private set; }
        public Func<bool>? AreAllOverlaysHidden { get; set; }
        public int ScrollCallCount { get; private set; }
        public PixelPoint? LastScrollTarget { get; private set; }
        public int LastScrollNotches { get; private set; }
        public bool LastScrollHorizontal { get; private set; }
        public bool ScrollOverlaysWereHidden { get; private set; }

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

        public bool SendRangeClick(PixelPoint start, PixelPoint end, PixelRect virtualScreen)
        {
            RangeCallCount++;
            LastRangeStart = start;
            LastRangeEnd = end;
            LastVirtualScreen = virtualScreen;
            RangeOverlaysWereHidden = AreAllOverlaysHidden?.Invoke() ?? false;
            return ReturnResult;
        }

        public bool SendDrag(PixelPoint start, PixelPoint end, PixelRect virtualScreen)
        {
            DragCallCount++;
            LastDragStart = start;
            LastDragEnd = end;
            LastVirtualScreen = virtualScreen;
            DragOverlaysWereHidden = AreAllOverlaysHidden?.Invoke() ?? false;
            return ReturnResult;
        }

        public bool SendScroll(
            PixelPoint target,
            PixelRect virtualScreen,
            int notches,
            bool horizontal)
        {
            ScrollCallCount++;
            LastScrollTarget = target;
            LastVirtualScreen = virtualScreen;
            LastScrollNotches = notches;
            LastScrollHorizontal = horizontal;
            ScrollOverlaysWereHidden = AreAllOverlaysHidden?.Invoke() ?? false;
            return ReturnResult;
        }
    }
}
