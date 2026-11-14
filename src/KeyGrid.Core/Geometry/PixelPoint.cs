namespace KeyGrid.Core.Geometry;

/// <summary>
/// Represents an integer point in physical pixel coordinates.
/// </summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public override string ToString() => $"({X}, {Y})";
}
