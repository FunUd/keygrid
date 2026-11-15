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

    [Theory]
    [InlineData(-1000, -1000, 0, 0)]
    [InlineData(5000, 5000, 65535, 65535)]
    [InlineData(-1, 1080, 0, 65535)]
    [InlineData(int.MaxValue, int.MinValue, 65535, 0)]
    public void ToNormalizedVirtualDesktop_PointOutsideVirtualScreen_ClampsToValidRange(
        int x,
        int y,
        int expectedX,
        int expectedY)
    {
        var virtualScreen = new PixelRect(0, 0, 1920, 1080);

        var normalized = CoordinateConverter.ToNormalizedVirtualDesktop(
            new PixelPoint(x, y),
            virtualScreen);

        Assert.Equal(expectedX, normalized.NormalizedX);
        Assert.Equal(expectedY, normalized.NormalizedY);
    }

    [Fact]
    public void ToNormalizedVirtualDesktop_ExtremeCoordinateWithNegativeOrigin_DoesNotOverflow()
    {
        // Subtracting the negative origin from int.MaxValue overflows 32-bit arithmetic;
        // the converter must use 64-bit math before clamping.
        var virtualScreen = new PixelRect(-1920, 0, 3840, 1080);

        var normalized = CoordinateConverter.ToNormalizedVirtualDesktop(
            new PixelPoint(int.MaxValue, int.MaxValue),
            virtualScreen);

        Assert.Equal(CoordinateConverter.MaxSendInputValue, normalized.NormalizedX);
        Assert.Equal(CoordinateConverter.MaxSendInputValue, normalized.NormalizedY);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    [InlineData(7680, 2160)]
    public void CoordinateConverter_RoundTripPrecision_NeverExceeds1Pixel(int width, int height)
    {
        var screen = new PixelRect(0, 0, width, height);

        // Test sample of coordinates including corners, centers, and sub-regions
        var samplePoints = new[]
        {
            new PixelPoint(0, 0),
            new PixelPoint(width - 1, height - 1),
            new PixelPoint(width / 2, height / 2),
            new PixelPoint(width / 4, height / 3),
            new PixelPoint(width * 3 / 4, height * 2 / 3)
        };

        foreach (var pt in samplePoints)
        {
            var (normX, normY) = CoordinateConverter.ToNormalizedVirtualDesktop(pt, screen);

            // Reconstruct screen pixel using OS formula
            int reconstructedX = (int)Math.Round((double)normX * (width - 1) / CoordinateConverter.MaxSendInputValue);
            int reconstructedY = (int)Math.Round((double)normY * (height - 1) / CoordinateConverter.MaxSendInputValue);

            int deltaX = Math.Abs(reconstructedX - pt.X);
            int deltaY = Math.Abs(reconstructedY - pt.Y);

            Assert.True(deltaX <= 1, $"Delta X for {pt} in {width}x{height} is {deltaX}");
            Assert.True(deltaY <= 1, $"Delta Y for {pt} in {width}x{height} is {deltaY}");
        }
    }
}
