using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using Xunit;

namespace KeyGrid.Tests;

public class GridCalculatorTests
{
    [Theory]
    [InlineData(1920, 1080, 5, 5, 16, 10)]
    [InlineData(3840, 2160, 5, 5, 16, 10)]
    [InlineData(7680, 2160, 5, 5, 20, 6)]
    [InlineData(7680, 4320, 5, 5, 16, 10)]
    public void CalculateFirstStageDivisions_MinimizesFinalCellWithinKeyBudget(
        int width,
        int height,
        int stage2Columns,
        int stage2Rows,
        int expectedColumns,
        int expectedRows)
    {
        var (columns, rows) = GridCalculator.CalculateFirstStageDivisions(
            width,
            height,
            stage2Columns,
            stage2Rows);

        Assert.Equal(expectedColumns, columns);
        Assert.Equal(expectedRows, rows);
        Assert.True(columns + rows <= GridCalculator.MaxDivisions);
    }

    [Theory]
    [InlineData(0, 1080, 5, 5)]
    [InlineData(1920, 0, 5, 5)]
    [InlineData(1920, 1080, 0, 5)]
    [InlineData(1920, 1080, 5, 0)]
    public void CalculateFirstStageDivisions_InvalidDimensions_Throws(
        int width,
        int height,
        int stage2Columns,
        int stage2Rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GridCalculator.CalculateFirstStageDivisions(width, height, stage2Columns, stage2Rows));
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    [InlineData(7680, 2160)]
    [InlineData(1, 1)]
    [InlineData(int.MaxValue, 16384)]
    public void CalculateFirstStageDivisions_MatchesBestCandidateFromKeyBudget(
        int width,
        int height)
    {
        const int stage2Columns = 5;
        const int stage2Rows = 5;

        var actual = GridCalculator.CalculateFirstStageDivisions(
            width,
            height,
            stage2Columns,
            stage2Rows);
        var expected = FindBestCandidate(width, height, stage2Columns, stage2Rows);

        Assert.Equal(expected, actual);
        Assert.True(actual.Columns + actual.Rows <= GridCalculator.MaxDivisions);
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
    public void CalculateBoundaries_AllSmallLengthsAndSupportedCounts_PartitionExactly()
    {
        const int origin = -137;

        for (int length = 1; length <= 512; length++)
        {
            for (int count = 1; count <= GridCalculator.MaxDivisions; count++)
            {
                var boundaries = GridCalculator.CalculateBoundaries(origin, length, count);

                Assert.Equal(count + 1, boundaries.Length);
                Assert.Equal(origin, boundaries[0]);
                Assert.Equal(origin + length, boundaries[^1]);

                long totalWidth = 0;
                int floorSize = length / count;
                int ceilSize = (length + count - 1) / count;
                for (int i = 0; i < count; i++)
                {
                    int cellWidth = boundaries[i + 1] - boundaries[i];
                    Assert.InRange(cellWidth, floorSize, ceilSize);
                    totalWidth += cellWidth;
                }

                Assert.Equal(length, totalWidth);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CalculateBoundaries_NonPositiveCount_Throws(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GridCalculator.CalculateBoundaries(0, 100, count));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 2)]
    public void GetCell_InvalidIndices_Throws(int columnIndex, int rowIndex)
    {
        var xBoundaries = new[] { 0, 10, 20 };
        var yBoundaries = new[] { 0, 10, 20 };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => GridCalculator.GetCell(xBoundaries, yBoundaries, columnIndex, rowIndex));
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
    public void ResolveTargetCoordinate_EqualDistance_PrioritizesSmallerY()
    {
        var upper = new PixelRect(9, 5, 1, 1);
        var lower = new PixelRect(9, 13, 1, 1);

        var target = GridCalculator.ResolveTargetCoordinate(
            new PixelRect(0, 0, 20, 20),
            new[] { lower, upper });

        Assert.Equal(new PixelPoint(9, 5), target);
    }

    [Fact]
    public void ResolveTargetCoordinate_EqualDistanceAndY_PrioritizesSmallerX()
    {
        var left = new PixelRect(5, 9, 1, 1);
        var right = new PixelRect(13, 9, 1, 1);

        var target = GridCalculator.ResolveTargetCoordinate(
            new PixelRect(0, 0, 20, 20),
            new[] { right, left });

        Assert.Equal(new PixelPoint(5, 9), target);
    }

    [Fact]
    public void ResolveTargetCoordinate_EmptySelection_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => GridCalculator.ResolveTargetCoordinate(
                new PixelRect(0, 0, 10, 10),
                Array.Empty<PixelRect>()));
    }

    private static (int Columns, int Rows) FindBestCandidate(
        int width,
        int height,
        int stage2Columns,
        int stage2Rows)
    {
        var candidates =
            from columns in Enumerable.Range(1, GridCalculator.MaxDivisions - 1)
            from rows in Enumerable.Range(1, GridCalculator.MaxDivisions - columns)
            let finalWidth = DivideCeiling(DivideCeiling(width, columns), stage2Columns)
            let finalHeight = DivideCeiling(DivideCeiling(height, rows), stage2Rows)
            select new
            {
                Columns = columns,
                Rows = rows,
                WorstFinalSize = Math.Max(finalWidth, finalHeight),
                CellCount = (long)columns * rows,
                KeyCount = columns + rows
            };

        var best = candidates
            .OrderBy(candidate => candidate.WorstFinalSize)
            .ThenByDescending(candidate => candidate.CellCount)
            .ThenBy(candidate => candidate.KeyCount)
            .ThenBy(candidate => candidate.Columns)
            .First();

        return (best.Columns, best.Rows);
    }

    private static int DivideCeiling(int dividend, int divisor)
    {
        return (int)(((long)dividend + divisor - 1) / divisor);
    }
}
