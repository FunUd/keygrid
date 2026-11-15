using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using Xunit;

namespace KeyGrid.Tests;

public class GridCalculatorTests
{
    [Theory]
    [InlineData(1920, 1080, 20.0, 10, 8)]
    [InlineData(2560, 1440, 20.0, 12, 9)]
    [InlineData(3840, 2160, 20.0, 14, 11)]
    [InlineData(1920, 1080, 25.0, 9, 7)]
    [InlineData(1920, 1080, 30.0, 8, 6)]
    [InlineData(1920, 1080, 35.0, 8, 6)]
    [InlineData(1920, 1080, 40.0, 7, 6)]
    [InlineData(2560, 1440, 30.0, 10, 7)]
    [InlineData(2560, 1440, 40.0, 8, 6)]
    [InlineData(3840, 2160, 25.0, 13, 10)]
    [InlineData(3840, 2160, 30.0, 12, 9)]
    [InlineData(3840, 2160, 35.0, 11, 8)]
    [InlineData(3840, 2160, 40.0, 10, 8)]
    public void CalculateDivisions_MatchesTargetTable(
        int width,
        int height,
        double targetPixels,
        int expectedCols,
        int expectedRows)
    {
        var (cols, rows) = GridCalculator.CalculateDivisions(width, height, targetPixels);

        Assert.Equal(expectedCols, cols);
        Assert.Equal(expectedRows, rows);
        Assert.True(cols + rows <= GridCalculator.MaxDivisions);
    }

    [Fact]
    public void CalculateDivisions_LargeDisplay_RelaxesWithinKeyBudget()
    {
        var (cols, rows) = GridCalculator.CalculateDivisions(100_000, 100_000, 20.0);

        Assert.Equal(13, cols);
        Assert.Equal(13, rows);
        Assert.True(cols + rows <= GridCalculator.MaxDivisions);
    }

    [Fact]
    public void CalculateDivisions_RelaxationTieBreak_PrefersFewerCells()
    {
        // 2500x4096 at 20px needs 12x15 independently (27 keys), so it relaxes.
        // 11x14, 11x15, and 12x14 all have a 21px worst final dimension. The
        // specification prefers 11x14 because it has the fewest cells (154).
        var (cols, rows) = GridCalculator.CalculateDivisions(2500, 4096, 20.0);

        Assert.Equal(11, cols);
        Assert.Equal(14, rows);
        Assert.Equal(154, cols * rows);
    }

    [Theory]
    [InlineData(7680, 2160, 17, 9, 27, 27)]
    [InlineData(7680, 4320, 15, 11, 35, 36)]
    public void CalculateDivisions_UltraWideDisplay_RelaxesToMinimaxPair(
        int width,
        int height,
        int expectedCols,
        int expectedRows,
        int expectedFinalWidth,
        int expectedFinalHeight)
    {
        var (cols, rows) = GridCalculator.CalculateDivisions(width, height, 20.0);

        Assert.Equal(expectedCols, cols);
        Assert.Equal(expectedRows, rows);
        Assert.Equal(expectedFinalWidth, FinalDimension(width, cols));
        Assert.Equal(expectedFinalHeight, FinalDimension(height, rows));
    }

    [Fact]
    public void CalculateDivisions_AlwaysRespectsKeyBudget()
    {
        int[] lengths = { 1, 100, 1000, 1920, 2560, 3840, 7680, 100_000 };
        double[] targets = { 20.0, 25.0, 30.0, 35.0, 40.0 };

        foreach (int width in lengths)
        {
            foreach (int height in lengths)
            {
                foreach (double target in targets)
                {
                    var (cols, rows) = GridCalculator.CalculateDivisions(width, height, target);

                    Assert.InRange(cols, 1, GridCalculator.MaxDivisions);
                    Assert.InRange(rows, 1, GridCalculator.MaxDivisions);
                    Assert.True(
                        cols + rows <= GridCalculator.MaxDivisions,
                        $"{width}x{height} at {target}px chose {cols}x{rows}.");
                }
            }
        }
    }

    [Theory]
    [InlineData(1.0, 20.0)]
    [InlineData(1.25, 25.0)]
    [InlineData(1.5, 30.0)]
    [InlineData(1.75, 35.0)]
    [InlineData(2.0, 40.0)]
    public void CalculateTargetPixels_Uses20DipAndDpiScale(double dpiScale, double expectedPixels)
    {
        double actual = GridCalculator.CalculateTargetPixels(dpiScale);

        Assert.Equal(expectedPixels, actual, 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CalculateTargetPixels_InvalidScale_Throws(double dpiScale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCalculator.CalculateTargetPixels(dpiScale));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CalculateDivisions_InvalidTarget_Throws(double targetPixels)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GridCalculator.CalculateDivisions(1920, 1080, targetPixels));
    }

    [Theory]
    [InlineData(10, 3, new[] { 0, 4, 7, 10 })] // Example from spec.md: 4px, 3px, 3px
    [InlineData(100, 4, new[] { 0, 25, 50, 75, 100 })]
    [InlineData(1920, 10, null)]
    public void CalculateBoundaries_PartitionsWithoutGapsOrOverlaps(int length, int count, int[]? expected)
    {
        var boundaries = GridCalculator.CalculateBoundaries(0, length, count);

        Assert.Equal(count + 1, boundaries.Length);
        Assert.Equal(0, boundaries[0]);
        Assert.Equal(length, boundaries[count]);

        if (expected != null)
        {
            Assert.Equal(expected, boundaries);
        }

        int floorSize = length / count;
        int ceilSize = (length + count - 1) / count;

        for (int k = 0; k < count; k++)
        {
            int cellWidth = boundaries[k + 1] - boundaries[k];
            Assert.True(cellWidth == floorSize || cellWidth == ceilSize,
                $"Cell {k} has size {cellWidth}, expected {floorSize} or {ceilSize}");
        }
    }

    [Fact]
    public void CalculateBoundaries_LargeLength_DoesNotOverflow()
    {
        const int origin = -50_000_000;
        const int length = 200_000_000;
        const int count = 26;

        var boundaries = GridCalculator.CalculateBoundaries(origin, length, count);

        Assert.Equal(count + 1, boundaries.Length);
        Assert.Equal(origin, boundaries[0]);
        Assert.Equal(origin + length, boundaries[count]);

        long floorSize = length / count;
        long ceilSize = (length + (long)count - 1) / count;

        for (int k = 0; k < count; k++)
        {
            long cellWidth = (long)boundaries[k + 1] - boundaries[k];
            Assert.InRange(cellWidth, floorSize, ceilSize);
        }

        // k * length exceeds int.MaxValue for k >= 11; the boundary must still be exact.
        long expectedLastBoundary = origin + ((long)25 * length + count - 1) / count;
        Assert.Equal((int)expectedLastBoundary, boundaries[25]);
    }

    [Theory]
    [InlineData(1920, 1080, 20.0, 10, 8, 20, 17)]
    [InlineData(2560, 1440, 20.0, 12, 9, 18, 18)]
    [InlineData(3840, 2160, 20.0, 14, 11, 20, 18)]
    [InlineData(1920, 1080, 25.0, 9, 7, 24, 23)]
    [InlineData(1920, 1080, 30.0, 8, 6, 30, 30)]
    [InlineData(1920, 1080, 40.0, 7, 6, 40, 30)]
    public void TwoStageDivision_ReusingCounts_MatchesFinalCellFormula(
        int width,
        int height,
        double targetPixels,
        int expectedCols,
        int expectedRows,
        int expectedFinalWidth,
        int expectedFinalHeight)
    {
        var (cols, rows) = GridCalculator.CalculateDivisions(width, height, targetPixels);

        Assert.Equal(expectedCols, cols);
        Assert.Equal(expectedRows, rows);
        Assert.Equal(expectedFinalWidth, FinalDimension(width, cols));
        Assert.Equal(expectedFinalHeight, FinalDimension(height, rows));
        Assert.True(FinalDimension(width, cols) <= targetPixels);
        Assert.True(FinalDimension(height, rows) <= targetPixels);
    }

    [Theory]
    [InlineData(0, 10, 4)]    // 0 + (10 - 1)/2 = 4 (integer division)
    [InlineData(0, 11, 5)]    // 0 + (11 - 1)/2 = 5
    [InlineData(100, 1, 100)] // 100 + (1 - 1)/2 = 100
    [InlineData(-50, 10, -46)]// -50 + 9/2 = -46
    public void CalculateReferencePoint_CalculatesCenterCorrectly(int left, int width, int expectedX)
    {
        var rect = new PixelRect(left, 0, width, 10);
        var refPt = GridCalculator.CalculateReferencePoint(rect);
        Assert.Equal(expectedX, refPt.X);
    }

    [Fact]
    public void ResolveTargetCoordinate_WhenReferencePointInsideSelection_ReturnsReferencePoint()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var bbox = new PixelRect(100, 100, 50, 50); // Center: (124, 124)

        var target = GridCalculator.ResolveTargetCoordinate(bbox, new[] { monitor });
        Assert.Equal(new PixelPoint(124, 124), target);
    }

    [Fact]
    public void ResolveTargetCoordinate_WhenReferencePointInMonitorGap_FindsNearestPointWithTieBreaking()
    {
        // Two monitors with a 200px gap:
        // Left: [0, 1000) x [0, 1000)
        // Right: [1200, 2200) x [0, 1000)
        var leftMonitor = new PixelRect(0, 0, 1000, 1000);
        var rightMonitor = new PixelRect(1200, 0, 1000, 1000);
        var selectionArea = new[] { leftMonitor, rightMonitor };

        // Bounding box spans [0, 2200) x [400, 600)
        // Center is at X = 0 + (2200 - 1) / 2 = 1099, Y = 400 + (200 - 1) / 2 = 499
        // 1099 is in the gap [1000, 1200).
        // Closest point in left monitor: (999, 499), dist = 100px
        // Closest point in right monitor: (1200, 499), dist = 101px
        var bbox = new PixelRect(0, 400, 2200, 200);

        var target = GridCalculator.ResolveTargetCoordinate(bbox, selectionArea);
        Assert.Equal(new PixelPoint(999, 499), target);
    }

    [Fact]
    public void ResolveTargetCoordinate_TieBreaking_PrioritizesSmallerYThenSmallerX()
    {
        // Two disjoint rectangles equally distant from center:
        // Top rect: [100, 100, 50, 50] (Right: 150, Bottom: 150)
        // Bottom rect: [100, 250, 50, 50] (Top: 250)
        // RefPoint Y = 200
        // Top rect closest point: (124, 149) -> dy = 51
        // Bottom rect closest point: (124, 250) -> dy = 50 -> bottom rect is closer
        var r1 = new PixelRect(100, 100, 50, 50);
        var r2 = new PixelRect(100, 250, 50, 50);

        var bbox = PixelRect.BoundingBox(new[] { r1, r2 });
        var target = GridCalculator.ResolveTargetCoordinate(bbox, new[] { r1, r2 });

        // Ensure target is on one of the rectangles
        Assert.True(r1.Contains(target) || r2.Contains(target));
    }

    private static int FinalDimension(int length, int count)
    {
        int stage1 = (length + count - 1) / count;
        return (stage1 + count - 1) / count;
    }
}
