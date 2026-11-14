using System.Windows;
using System.Windows.Interop;
using KeyGrid.Core.Selection;
using KeyGrid.Core.Services;
using KeyGrid.Interop;
using KeyGrid.Services;
using KeyGrid.UI;

namespace KeyGrid;

public partial class App : Application
{
    private const string MutexName = "KeyGrid_SingleInstance_Mutex";
    private Mutex? _singleInstanceMutex;
    private HwndSource? _hwndSource;
    private Win32MonitorProvider? _monitorProvider;
    private Win32ClickSender? _clickSender;
    private Win32HotkeyRegistrar? _hotkeyRegistrar;
    private SelectionManager? _selectionManager;
    private ClickExecutor? _clickExecutor;
    private OverlayManager? _overlayManager;
    private TrayManager? _trayManager;
    private HotkeyManager? _hotkeyManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            FileLogger.LogError("Unhandled exception encountered", ex);
            Cleanup();
        };

        // 1. Single Instance check via Named Mutex
        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Show temporary tray notification and exit according to spec.md Section 7.2
            ShowAlreadyRunningNotification();
            Shutdown();
            return;
        }

        FileLogger.LogInfo("KeyGrid application starting.");

        // 2. Create message-only HwndSource for receiving WM_HOTKEY and WM_TRAYICON
        var parameters = new HwndSourceParameters("KeyGridMessageReceiver")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
        nint hWnd = _hwndSource.Handle;

        // 3. Initialize components
        _monitorProvider = new Win32MonitorProvider();
        _clickSender = new Win32ClickSender();
        _hotkeyRegistrar = new Win32HotkeyRegistrar();
        _selectionManager = new SelectionManager();
        _clickExecutor = new ClickExecutor(_clickSender);

        _overlayManager = new OverlayManager(_monitorProvider, _selectionManager, _clickExecutor);
        _overlayManager.ErrorOccurred += (msg) =>
        {
            FileLogger.LogError($"OverlayManager error: {msg}");
            _trayManager?.ShowBalloon("KeyGrid エラー", msg, isError: true);
        };

        _trayManager = new TrayManager(hWnd);
        _trayManager.ExitRequested += () => Shutdown();

        _hotkeyManager = new HotkeyManager(_hotkeyRegistrar, hWnd);
        _hotkeyManager.HotkeyTriggered += OnHotkeyTriggered;

        // 4. Register Hotkey
        bool hotkeyOk = _hotkeyManager.RegisterDefaultHotkey();
        _trayManager.SetHotkeyStatus(hotkeyOk);

        if (!hotkeyOk)
        {
            string warnMsg = "ホットキー (Ctrl + Shift + Space) の登録に失敗しました。他の常駐アプリと競合している可能性があります。";
            FileLogger.LogWarning(warnMsg);
            _trayManager.ShowBalloon("KeyGrid", warnMsg, isError: true);
        }
        else
        {
            FileLogger.LogInfo("Hotkey registered successfully (Ctrl + Shift + Space).");
        }
    }

    private void OnHotkeyTriggered()
    {
        // Ignore hotkey while selection is already active according to spec.md Section 7.2
        if (_overlayManager != null && _overlayManager.IsActive)
        {
            return;
        }

        FileLogger.LogInfo("Hotkey triggered: opening overlays.");
        _overlayManager?.ShowOverlays();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_HOTKEY:
                _hotkeyManager?.HandleHotkeyMessage((int)wParam);
                handled = true;
                break;

            case NativeMethods.WM_TRAYICON:
                _trayManager?.HandleTrayMessage(lParam);
                handled = true;
                break;

            case NativeMethods.WM_DISPLAYCHANGE:
            case NativeMethods.WM_DPICHANGED:
                FileLogger.LogInfo("Display or DPI topology changed.");
                _monitorProvider?.NotifyChanged();
                break;
        }

        return nint.Zero;
    }

    private void ShowAlreadyRunningNotification()
    {
        var tempWindow = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        tempWindow.Show();
        nint hWnd = new WindowInteropHelper(tempWindow).Handle;

        using var tray = new TrayManager(hWnd);
        tray.ShowBalloon("KeyGrid", "既に起動しています。", isError: false);
        Thread.Sleep(1500);
        tempWindow.Close();
    }

    private void Cleanup()
    {
        try
        {
            _overlayManager?.CloseOverlays();
            _hotkeyManager?.Dispose();
            _trayManager?.Dispose();
            _hwndSource?.Dispose();

            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Ignore if mutex is not owned by this thread
                }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }
        catch (Exception ex)
        {
            FileLogger.LogError("Error during application cleanup", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FileLogger.LogInfo("KeyGrid application shutting down.");
        Cleanup();
        base.OnExit(e);
    }
}
