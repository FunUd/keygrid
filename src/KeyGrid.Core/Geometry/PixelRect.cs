namespace KeyGrid.Core.Geometry;

/// <summary>
/// Represents a rectangle in physical pixel coordinates using half-open intervals:
/// [Left, Right) on the horizontal axis and [Top, Bottom) on the vertical axis.
/// </summary>
public readonly record struct PixelRect
{
    public int Left { get; }
    public int Top { get; }
    public int Width { get; }
    public int Height { get; }

    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect Empty => new(0, 0, 0, 0);

    public PixelRect(int left, int top, int width, int height)
    {
        Left = left;
        Top = top;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
    }

    public static PixelRect FromEdges(int left, int top, int right, int bottom)
    {
        int w = Math.Max(0, right - left);
        int h = Math.Max(0, bottom - top);
        return new PixelRect(left, top, w, h);
    }

    public bool Contains(PixelPoint point) => Contains(point.X, point.Y);

    public bool Contains(int x, int y)
    {
        return !IsEmpty && x >= Left && x < Right && y >= Top && y < Bottom;
    }

    public bool IntersectsWith(PixelRect other)
    {
        if (IsEmpty || other.IsEmpty)
        {
            return false;
        }

        return Left < other.Right && Right > other.Left &&
               Top < other.Bottom && Bottom > other.Top;
    }

    public PixelRect Intersect(PixelRect other)
    {
        if (!IntersectsWith(other))
        {
            return Empty;
        }

        int newLeft = Math.Max(Left, other.Left);
        int newTop = Math.Max(Top, other.Top);
        int newRight = Math.Min(Right, other.Right);
        int newBottom = Math.Min(Bottom, other.Bottom);

        return FromEdges(newLeft, newTop, newRight, newBottom);
    }

    /// <summary>
    /// Computes the minimal bounding box enclosing all non-empty rectangles in the collection.
    /// </summary>
    public static PixelRect BoundingBox(IEnumerable<PixelRect> rects)
    {
        int minLeft = int.MaxValue;
        int minTop = int.MaxValue;
        int maxRight = int.MinValue;
        int maxBottom = int.MinValue;
        bool any = false;

        foreach (var r in rects)
        {
            if (r.IsEmpty)
            {
                continue;
            }

            any = true;
            if (r.Left < minLeft) minLeft = r.Left;
            if (r.Top < minTop) minTop = r.Top;
            if (r.Right > maxRight) maxRight = r.Right;
            if (r.Bottom > maxBottom) maxBottom = r.Bottom;
        }

        return any ? FromEdges(minLeft, minTop, maxRight, maxBottom) : Empty;
    }

    /// <summary>
    /// Finds the closest integer point within this rectangle to the given target point.
    /// Clamps coordinates to [Left, Right - 1] and [Top, Bottom - 1].
    /// </summary>
    public PixelPoint Clamp(PixelPoint target)
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException("Cannot clamp to an empty rectangle.");
        }

        int clampedX = Math.Clamp(target.X, Left, Right - 1);
        int clampedY = Math.Clamp(target.Y, Top, Bottom - 1);
        return new PixelPoint(clampedX, clampedY);
    }

    public override string ToString() => $"[{Left}, {Top}, {Width}x{Height}]";
}
