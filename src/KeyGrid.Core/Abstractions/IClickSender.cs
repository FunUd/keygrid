using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Abstractions;

public interface IClickSender
{
    /// <summary>
    /// Sends a mouse move and left click (down + up) at the specified physical pixel target
    /// relative to the given virtual screen bounding box. Returns true if SendInput succeeded.
    /// </summary>
    bool SendLeftClick(PixelPoint target, PixelRect virtualScreen);
}
