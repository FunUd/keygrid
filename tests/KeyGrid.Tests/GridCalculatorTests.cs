using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using Xunit;

namespace KeyGrid.Tests;

public class GridCalculatorTests
{
    [Theory]
    [InlineData(1920, 1080, 128, 15, 8)]
    [InlineData(1920, 1080, 160, 12, 7)]
    [InlineData(1920, 1080, 192, 10, 6)]
    [InlineData(1920, 1080, 224, 9, 5)]
    [InlineData(1920, 1080, 256, 8, 4)]
    [InlineData(2560, 1440, 128, 16, 10)]
    [InlineData(2560, 1440, 192, 13, 8)]
    [InlineData(3840, 2160, 128, 16, 10)]
    [InlineData(3840, 2160, 192, 16, 10)]
    [InlineData(3840, 2160, 256, 15, 8)]
    [InlineData(5120, 1440, 128, 20, 6)]
    [InlineData(5120, 2880, 128, 16, 10)]
    [InlineData(7680, 2160, 128, 20, 6)]
    [InlineData(7680, 4320, 128, 16, 10)]
    [InlineData(1080, 1920, 128, 8, 15)]
    [InlineData(2160, 3840, 128, 10, 16)]
    public void CalculateFirstStageDivisions_RepresentativeResolutions_MatchPlanTable(
        int width,
        int height,
        int targetCellPx,
        int expectedColumns,
        int expectedRows)
    {
        var (columns, rows) = GridCalculator.CalculateFirstStageDivisions(
            width,
            height,
            targetCellPx);

        Assert.Equal(expectedColumns, columns);
        Assert.Equal(expectedRows, rows);
        Assert.True(columns + rows <= GridCalculator.MaxDivisions);
    }

    [Theory]
    [InlineData(0, 1080, 256)]
    [InlineData(1920, 0, 256)]
    [InlineData(1920, 1080, 0)]
    [InlineData(-1, 1080, 256)]
    [InlineData(1920, -1, 256)]
    [InlineData(1920, 1080, -1)]
    public void CalculateFirstStageDivisions_InvalidDimensions_Throws(
        int width,
        int height,
        int targetCellPx)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GridCalculator.CalculateFirstStageDivisions(width, height, targetCellPx));
    }

    [Fact]
    public void CalculateFirstStageDivisions_BeatsSimpleCeilingFormulaOn1080p()
    {
        // Simple ceiling formula: ceil(1920/128)=15, ceil(1080/128)=9 => cells 128x120, max deviation 8px.
        // Target search chooses 15x8 => cells 128x135, max deviation 7px.
        var (cols, rows) = GridCalculator.CalculateFirstStageDivisions(1920, 1080, 128);
        Assert.Equal(15, cols);
        Assert.Equal(8, rows);
    }

    [Theory]
    [InlineData(1920, 1080, 256)]
    [InlineData(1080, 1920, 256)]
    [InlineData(7680, 2160, 256)]
    [InlineData(1, 1, 256)]
    [InlineData(int.MaxValue, 16384, 256)]
    public void CalculateFirstStageDivisions_MatchesBestCandidateFromKeyBudget(
        int width,
        int height,
        int targetCellPx)
    {
        var actual = GridCalculator.CalculateFirstStageDivisions(
            width,
            height,
            targetCellPx);
        var expected = FindBestCandidate(width, height, targetCellPx);

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
    [InlineData(0, 0, 240, 270, 24, 25, 25)]
    [InlineData(100, 200, 256, 256, 24, 25, 25)]
    [InlineData(-500, -300, 320, 288, 30, 25, 25)]
    public void CalculateCenteredStage2Window_DimensionsAndCenteringAreExact(
        int left,
        int top,
        int width,
        int height,
        int cellSizePx,
        int columnCount,
        int rowCount)
    {
        var focusBox = new PixelRect(left, top, width, height);
        var window = GridCalculator.CalculateCenteredStage2Window(
            focusBox,
            cellSizePx,
            columnCount,
            rowCount);

        Assert.Equal(columnCount * cellSizePx, window.Width);
        Assert.Equal(rowCount * cellSizePx, window.Height);

        // Center cell contains the reference point of focusBox
        var refPoint = GridCalculator.CalculateReferencePoint(focusBox);
        int centerCol = columnCount / 2;
        int centerRow = rowCount / 2;

        int centerCellLeft = window.Left + centerCol * cellSizePx;
        int centerCellRight = centerCellLeft + cellSizePx;
        int centerCellTop = window.Top + centerRow * cellSizePx;
        int centerCellBottom = centerCellTop + cellSizePx;

        Assert.InRange(refPoint.X, centerCellLeft, centerCellRight - 1);
        Assert.InRange(refPoint.Y, centerCellTop, centerCellBottom - 1);
    }

    [Fact]
    public void CalculateCenteredStage2Window_InvalidConfiguration_Throws()
    {
        var focusBox = new PixelRect(0, 0, 10, 10);

        Assert.Throws<ArgumentException>(
            () => GridCalculator.CalculateCenteredStage2Window(PixelRect.Empty, 24, 25, 25));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GridCalculator.CalculateCenteredStage2Window(focusBox, 0, 25, 25));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GridCalculator.CalculateCenteredStage2Window(focusBox, 24, 0, 25));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GridCalculator.CalculateCenteredStage2Window(focusBox, 24, 25, 0));
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

    [Theory]
    [InlineData(1920, 1080, 112, -1, -1)]   // 17x9 fits the 26-key budget
    [InlineData(1080, 1920, 112, -1, -1)]
    [InlineData(2560, 1440, 112, 2, 1)]
    [InlineData(3840, 2160, 112, 5, 1)]
    [InlineData(5120, 1440, 112, 5, 1)]
    [InlineData(5120, 2880, 112, 5, 2)]
    [InlineData(7680, 4320, 112, 7, 3)]
    [InlineData(2160, 3840, 112, 1, 5)]
    public void CalculateStage0Divisions_SelectsCoarseGridOnlyWhenNeeded(
        int width,
        int height,
        int targetCellPx,
        int expectedColumns,
        int expectedRows)
    {
        var result = GridCalculator.CalculateStage0Divisions(width, height, targetCellPx);

        if (expectedColumns < 0)
        {
            Assert.Null(result);
            return;
        }

        Assert.NotNull(result);
        Assert.Equal(expectedColumns, result.Value.Columns);
        Assert.Equal(expectedRows, result.Value.Rows);
        Assert.True(result.Value.Columns + result.Value.Rows <= KeyGrid.Core.KeyLayout.MaxKeys);
    }

    [Theory]
    [InlineData(0, 1080, 112)]
    [InlineData(1920, 0, 112)]
    [InlineData(1920, 1080, 0)]
    public void CalculateStage0Divisions_InvalidDimensions_Throws(
        int width,
        int height,
        int targetCellPx)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GridCalculator.CalculateStage0Divisions(width, height, targetCellPx));
    }

    private static (int Columns, int Rows) FindBestCandidate(
        int width,
        int height,
        int targetCellPx)
    {
        var candidates =
            from columns in Enumerable.Range(1, GridCalculator.MaxDivisions - 1)
            from rows in Enumerable.Range(1, GridCalculator.MaxDivisions - columns)
            let cellWidth = DivideCeiling(width, columns)
            let cellHeight = DivideCeiling(height, rows)
            let devWidth = Math.Abs(cellWidth - targetCellPx)
            let devHeight = Math.Abs(cellHeight - targetCellPx)
            select new
            {
                Columns = columns,
                Rows = rows,
                MaxDeviation = Math.Max(devWidth, devHeight),
                TotalDeviation = devWidth + devHeight,
                KeyCount = columns + rows,
                CellCount = (long)columns * rows
            };

        var best = candidates
            .OrderBy(candidate => candidate.MaxDeviation)
            .ThenBy(candidate => candidate.TotalDeviation)
            .ThenBy(candidate => candidate.KeyCount)
            .ThenByDescending(candidate => candidate.CellCount)
            .ThenBy(candidate => candidate.Columns)
            .First();

        return (best.Columns, best.Rows);
    }

    private static int DivideCeiling(int dividend, int divisor)
    {
        return (int)(((long)dividend + divisor - 1) / divisor);
    }
}
