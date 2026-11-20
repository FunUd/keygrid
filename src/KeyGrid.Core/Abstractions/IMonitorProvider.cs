namespace KeyGrid.Core.Abstractions;

public interface IMonitorProvider
{
    /// <summary>
    /// Returns all active monitors with their physical bounds, effective DPI scale,
    /// and primary-monitor flag.
    /// </summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>
    /// Returns the monitor containing the specified window handle, or null if the window
    /// cannot be resolved to a monitor.
    /// </summary>
    MonitorInfo? GetMonitorForWindow(nint windowHandle);

    event Action? Changed;
}
