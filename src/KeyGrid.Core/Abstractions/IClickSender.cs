using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Abstractions;

public enum ClickKind
{
    Left,
    Right
}

public interface IClickSender
{
    /// <summary>
    /// Sends a mouse move and click (down + up) at the specified physical pixel target
    /// relative to the given virtual screen bounding box. Returns true if SendInput succeeded.
    /// </summary>
    /// <param name="clickKind">Mouse button to send.</param>
    bool SendClick(PixelPoint target, PixelRect virtualScreen, ClickKind clickKind);
}
