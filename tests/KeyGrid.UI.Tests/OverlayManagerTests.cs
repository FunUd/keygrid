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
            clickSender.WasOriginatingForegroundRestored = () =>
                focus.LastRestoredWindow == new nint(71);
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
            Assert.True(clickSender.OriginatingForegroundWasRestoredBeforeSend);
            Assert.Equal(new nint(71), focus.LastRestoredWindow);
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
            clickSender.WasOriginatingForegroundRestored = () =>
                focus.LastRestoredWindow == new nint(71);
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
            Assert.True(clickSender.OriginatingForegroundWasRestoredBeforeSend);
            Assert.Equal(new nint(71), focus.LastRestoredWindow);
            Assert.Contains("Failed to dispatch click", error);
        });
    }

    [Fact]
    public void PrimarySurfaceConfirmWithShiftEnterDispatchesRightClick()
    {
        UiTestThread.Run(() =>
        {
            var monitorProvider = SingleMonitorProvider();
            var factory = new FakeOverlayFactory();
            var clickSender = new FakeClickSender();
            clickSender.AreAllOverlaysClosed = () =>
                factory.Windows.Count > 0 && factory.Windows.All(window => window.CloseCount > 0);
            var manager = CreateManager(monitorProvider, factory, new FakeWindowFocusService(), clickSender);
            manager.ShowOverlays();
            UiTestThread.DrainDispatcher();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseKey(manager.SelectionManager.ColumnKeys[0]);
            primary.RaiseKey(manager.SelectionManager.RowKeys[0]);
            primary.RaiseConfirm(ClickKind.Right);

            Assert.False(manager.IsActive);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.True(clickSender.Called);
            Assert.Equal(ClickKind.Right, clickSender.LastClickKind);
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
            Assert.Equal(1, focus.GetForegroundWindowCalls);

            manager.CloseOverlays();
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
            var focus = new FakeWindowFocusService
            {
                ForegroundWindow = new nint(99),
                ForegroundProcessId = 999,
                ForegroundIsVisible = true
            };
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);

            primary.RaiseFocusLost();
            UiTestThread.DrainDispatcher();

            Assert.False(manager.IsActive);
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
            var focus = new FakeWindowFocusService
            {
                ForegroundWindow = new nint(99),
                ForegroundProcessId = (uint)Environment.ProcessId,
                ForegroundIsVisible = false
            };
            var manager = CreateManager(monitorProvider, factory, focus, new FakeClickSender());
            manager.ShowOverlays();
            var primary = Assert.Single(factory.Windows);
            Assert.True(manager.SelectionManager.ProcessKey('Q'));

            primary.RaiseFocusLost();
            UiTestThread.DrainDispatcher();

            Assert.True(manager.IsActive);
            Assert.Equal(SelectionState.Selecting, manager.SelectionManager.State);
            Assert.Single(manager.SelectionManager.History);
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

            primary.RaiseDisplayConfigurationChanged();

            Assert.True(manager.IsActive);
            UiTestThread.DrainDispatcher();

            Assert.False(manager.IsActive);
            Assert.Equal(SelectionState.Standby, manager.SelectionManager.State);
            Assert.Equal(1, primary.CloseCount);
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
        public List<FakeOverlayWindowSurface> Windows { get; } = new();

        public IOverlayWindowSurface Create(PixelRect rect, bool isPrimary, double dpiScale)
        {
            var window = new FakeOverlayWindowSurface(rect, isPrimary, dpiScale);
            Windows.Add(window);
            return window;
        }
    }

    private sealed class FakeOverlayWindowSurface(
        PixelRect monitorRect,
        bool isPrimary,
        double dpiScale) : IOverlayWindowSurface
    {
        public event Action<char>? KeyPressed;
        public event Action<int, int>? MoveRequested;
        public event Action<ClickKind>? ConfirmRequested;
        public event Action? BackspacePressed;
        public event Action? CancelRequested;
        public event Action? FocusLost;
        public event Action? DisplayConfigurationChanged;

        public bool IsPrimary { get; } = isPrimary;
        public double DpiScale { get; } = dpiScale;
        public int ShowCount { get; private set; }
        public int CloseCount { get; private set; }
        public int RenderUpdateCount { get; private set; }
        public int RefreshExpectedDpiCount { get; private set; }
        public bool DpiNotificationsEnabled { get; private set; }
        public bool TryReactivateResult { get; set; } = true;
        public int TryReactivateCount { get; private set; }

        public bool TryReactivate()
        {
            TryReactivateCount++;
            return TryReactivateResult;
        }

        public void UpdateRenderState(SelectionManager selectionManager, IReadOnlyList<PixelRect> monitorRects)
        {
            Assert.NotEqual(SelectionState.Standby, selectionManager.State);
            Assert.Contains(monitorRect, monitorRects);
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

        public void RaiseFocusLost() => FocusLost?.Invoke();

        public void RaiseDisplayConfigurationChanged() => DisplayConfigurationChanged?.Invoke();
    }

    private sealed class FakeWindowFocusService : IWindowFocusService
    {
        public nint ForegroundWindow { get; set; }
        public uint ForegroundProcessId { get; set; }
        public bool ForegroundIsVisible { get; set; }
        public int GetForegroundWindowCalls { get; private set; }
        public nint LastProcessWindow { get; private set; }
        public nint LastRestoredWindow { get; private set; }

        public nint GetForegroundWindow()
        {
            GetForegroundWindowCalls++;
            return ForegroundWindow;
        }

        public uint GetWindowProcessId(nint window)
        {
            LastProcessWindow = window;
            return ForegroundProcessId;
        }

        public bool IsWindowVisible(nint window) => ForegroundIsVisible;

        public bool TryRestoreForegroundWindow(nint window)
        {
            LastRestoredWindow = window;
            return true;
        }
    }

    private sealed class FakeClickSender : IClickSender
    {
        public bool Called { get; private set; }
        public bool ReturnResult { get; set; } = true;
        public bool OverlaysWereClosedBeforeSend { get; private set; }
        public bool OriginatingForegroundWasRestoredBeforeSend { get; private set; }
        public Func<bool>? WasOriginatingForegroundRestored { get; set; }
        public Func<bool>? AreAllOverlaysClosed { get; set; }
        public PixelPoint? LastTarget { get; private set; }
        public PixelRect? LastVirtualScreen { get; private set; }
        public ClickKind? LastClickKind { get; private set; }

        public bool SendClick(PixelPoint target, PixelRect virtualScreen, ClickKind clickKind)
        {
            Called = true;
            LastTarget = target;
            LastVirtualScreen = virtualScreen;
            LastClickKind = clickKind;
            OverlaysWereClosedBeforeSend = AreAllOverlaysClosed?.Invoke() ?? false;
            OriginatingForegroundWasRestoredBeforeSend =
                WasOriginatingForegroundRestored?.Invoke() ?? false;
            return ReturnResult;
        }
    }
}
