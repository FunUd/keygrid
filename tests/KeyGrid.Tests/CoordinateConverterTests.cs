using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using Xunit;

namespace KeyGrid.Tests;

public class CoordinateConverterTests
{
    [Fact]
    public void ToNormalizedVirtualDesktop_PrimaryScreen_NormalizesCornersCorrectly()
    {
        var virtualScreen = new PixelRect(0, 0, 1920, 1080);

        // Top-Left corner: (0, 0) -> (0, 0)
        var topLeft = CoordinateConverter.ToNormalizedVirtualDesktop(new PixelPoint(0, 0), virtualScreen);
        Assert.Equal(0, topLeft.NormalizedX);
        Assert.Equal(0, topLeft.NormalizedY);

        // Bottom-Right pixel: (1919, 1079) -> (65535, 65535)
        var bottomRight = CoordinateConverter.ToNormalizedVirtualDesktop(new PixelPoint(1919, 1079), virtualScreen);
        Assert.Equal(65535, bottomRight.NormalizedX);
        Assert.Equal(65535, bottomRight.NormalizedY);

        // Center point: (959, 539) -> approximately 32767
        var center = CoordinateConverter.ToNormalizedVirtualDesktop(new PixelPoint(959, 539), virtualScreen);
        Assert.InRange(center.NormalizedX, 32700, 32800);
        Assert.InRange(center.NormalizedY, 32700, 32800);
    }

    [Fact]
    public void ToNormalizedVirtualDesktop_NegativeOriginVirtualScreen_CalculatesCorrectRelativeOffsets()
    {
        // Monitor setup with left monitor starting at negative coordinate:
        // Left monitor: [-1920, 0, 1920, 1080]
        // Primary monitor: [0, 0, 1920, 1080]
        // Total virtual screen: [-1920, 0, 3840, 1080]
        var virtualScreen = new PixelRect(-1920, 0, 3840, 1080);

        var farLeft = CoordinateConverter.ToNormalizedVirtualDesktop(new PixelPoint(-1920, 0), virtualScreen);
        Assert.Equal(0, farLeft.NormalizedX);
        Assert.Equal(0, farLeft.NormalizedY);

        var farRight = CoordinateConverter.ToNormalizedVirtualDesktop(new PixelPoint(1919, 1079), virtualScreen);
        Assert.Equal(65535, farRight.NormalizedX);
        Assert.Equal(65535, farRight.NormalizedY);

        var primaryOrigin = CoordinateConverter.ToNormalizedVirtualDesktop(new PixelPoint(0, 0), virtualScreen);
        Assert.InRange(primaryOrigin.NormalizedX, 32700, 32800);
    }
}
