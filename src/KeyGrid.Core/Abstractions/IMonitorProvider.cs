namespace KeyGrid.Core.Abstractions;

public interface IMonitorProvider
{
    /// <summary>
    /// Returns all active monitors with their physical bounds, effective DPI scale,
    /// and primary-monitor flag.
    /// </summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    event Action? Changed;
}
