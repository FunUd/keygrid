using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Abstractions;

public enum ClickKind
{
    Left,
    Right,
    Middle
}

public interface IClickSender
{
    /// <summary>
    /// Sends a mouse move and click (down + up) at the specified physical pixel target
    /// relative to the given virtual screen bounding box. Returns true if SendInput succeeded.
    /// </summary>
    /// <param name="clickKind">Mouse button to send.</param>
    bool SendClick(PixelPoint target, PixelRect virtualScreen, ClickKind clickKind);

    /// <summary>
    /// Sends a two-point text selection: a normal left click at the start point,
    /// followed by a Shift+left click at the end point. Returns true only when
    /// the complete SendInput sequence was accepted.
    /// </summary>
    bool SendRangeClick(PixelPoint start, PixelPoint end, PixelRect virtualScreen);
}
