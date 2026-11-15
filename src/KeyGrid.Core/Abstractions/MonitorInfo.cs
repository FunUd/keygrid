using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Abstractions;

/// <summary>
/// Describes a display in physical pixels.
/// </summary>
/// <param name="Bounds">Monitor display bounds in the physical virtual-screen coordinate space.</param>
/// <param name="DpiScale">Effective DPI scale (for example, 1.5 for 150%).</param>
/// <param name="IsPrimary">Whether Windows marks this monitor as the primary display.</param>
public readonly record struct MonitorInfo(PixelRect Bounds, double DpiScale, bool IsPrimary);
