using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Calculation;

/// <summary>
/// Converts physical pixel coordinates to normalized absolute coordinates (0..65535)
/// required by SendInput with MOUSEEVENTF_VIRTUALDESK according to spec.md Section 12.1.
/// </summary>
public static class CoordinateConverter
{
    public const int MaxSendInputValue = 65535;

    /// <summary>
    /// Converts a physical pixel point to normalized virtual desktop coordinates in [0, 65535].
    /// X = (x - virtualLeft) * 65535 / (virtualWidth - 1)
    /// Y = (y - virtualTop) * 65535 / (virtualHeight - 1)
    /// </summary>
    public static (int NormalizedX, int NormalizedY) ToNormalizedVirtualDesktop(
        PixelPoint point,
        PixelRect virtualScreen)
    {
        if (virtualScreen.IsEmpty)
        {
            throw new ArgumentException("Virtual screen cannot be empty.", nameof(virtualScreen));
        }

        int widthSpan = virtualScreen.Width - 1;
        int heightSpan = virtualScreen.Height - 1;

        int normX = 0;
        if (widthSpan > 0)
        {
            long relativeX = point.X - virtualScreen.Left;
            normX = (int)((relativeX * MaxSendInputValue) / widthSpan);
        }

        int normY = 0;
        if (heightSpan > 0)
        {
            long relativeY = point.Y - virtualScreen.Top;
            normY = (int)((relativeY * MaxSendInputValue) / heightSpan);
        }

        return (normX, normY);
    }
}
