using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Abstractions;

public interface IMonitorProvider
{
    IReadOnlyList<PixelRect> GetMonitorRects();
    event Action? Changed;
}
