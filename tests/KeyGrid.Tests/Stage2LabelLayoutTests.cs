using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using Xunit;

namespace KeyGrid.Tests;

public class Stage2LabelLayoutTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1000, 800);
    private static readonly int[] XBoundaries = { 100, 130, 160, 190, 220 };
    private static readonly int[] YBoundaries = { 100, 130, 160, 190 };
    private const int ColumnCount = 4;
    private const int RowCount = 3;
    private const int StripThickness = 16;
    private const int Gap = 4;

    private static Stage2LabelLayout Calculate(PixelRect cell)
    {
        var layout = Stage2LabelLayout.Calculate(
            cell,
            Monitor,
            XBoundaries,
            YBoundaries,
            ColumnCount,
            RowCount,
            StripThickness,
            Gap);

        Assert.NotNull(layout);
        return layout;
    }

    [Fact]
    public void MiddleCell_PlacesRulersAboveAndLeft()
    {
        var cell = new PixelRect(100, 100, 120, 90);

        var layout = Calculate(cell);

        Assert.Equal(HorizontalLabelStripPlacement.Above, layout.HorizontalPlacement);
        Assert.Equal(new PixelRect(100, 80, 120, 16), layout.HorizontalStrip);
        Assert.Equal(VerticalLabelStripPlacement.Left, layout.VerticalPlacement);
        Assert.Equal(new PixelRect(80, 100, 16, 90), layout.VerticalStrip);
        Assert.Equal(new[] { 115.0, 145.0, 175.0, 205.0 }, layout.ColumnCenters);
        Assert.Equal(new[] { 115.0, 145.0, 175.0 }, layout.RowCenters);
    }

    [Fact]
    public void CellAtTop_PlacesHorizontalRulerBelow()
    {
        var cell = new PixelRect(100, 0, 120, 90);

        var layout = Calculate(cell);

        Assert.Equal(HorizontalLabelStripPlacement.Below, layout.HorizontalPlacement);
        Assert.Equal(new PixelRect(100, 94, 120, 16), layout.HorizontalStrip);
        Assert.True(layout.HorizontalStrip.Top >= cell.Bottom);
        Assert.Equal(VerticalLabelStripPlacement.Left, layout.VerticalPlacement);
    }

    [Fact]
    public void CellAtLeft_PlacesVerticalRulerRight()
    {
        var cell = new PixelRect(0, 100, 120, 90);

        var layout = Calculate(cell);

        Assert.Equal(VerticalLabelStripPlacement.Right, layout.VerticalPlacement);
        Assert.Equal(new PixelRect(124, 100, 16, 90), layout.VerticalStrip);
        Assert.True(layout.VerticalStrip.Left >= cell.Right);
        Assert.Equal(HorizontalLabelStripPlacement.Above, layout.HorizontalPlacement);
    }

    [Fact]
    public void CellAtTopLeft_PlacesRulersBelowAndRight()
    {
        var cell = new PixelRect(0, 0, 120, 90);

        var layout = Calculate(cell);

        Assert.Equal(HorizontalLabelStripPlacement.Below, layout.HorizontalPlacement);
        Assert.Equal(VerticalLabelStripPlacement.Right, layout.VerticalPlacement);
        Assert.True(layout.HorizontalStrip.Top >= cell.Bottom);
        Assert.True(layout.VerticalStrip.Left >= cell.Right);
    }

    [Fact]
    public void CellAtBottomRight_PlacesRulersAboveAndLeft()
    {
        var cell = new PixelRect(880, 710, 120, 90);

        var layout = Calculate(cell);

        Assert.Equal(HorizontalLabelStripPlacement.Above, layout.HorizontalPlacement);
        Assert.Equal(VerticalLabelStripPlacement.Left, layout.VerticalPlacement);
        Assert.True(layout.HorizontalStrip.Bottom <= cell.Top);
        Assert.True(layout.VerticalStrip.Right <= cell.Left);
    }

    [Fact]
    public void CellWithoutOutsideRoom_FallsBackInside()
    {
        var cell = new PixelRect(0, 0, Monitor.Width, Monitor.Height);

        var layout = Calculate(cell);

        Assert.Equal(HorizontalLabelStripPlacement.InsideTop, layout.HorizontalPlacement);
        Assert.Equal(VerticalLabelStripPlacement.InsideLeft, layout.VerticalPlacement);
        Assert.Equal(new PixelRect(0, Gap, Monitor.Width, StripThickness), layout.HorizontalStrip);
        Assert.Equal(new PixelRect(Gap, 0, StripThickness, Monitor.Height), layout.VerticalStrip);
    }

    [Fact]
    public void CellCentersFollowNonUniformBoundaries()
    {
        int[] xBoundaries = { 0, 10, 30, 60 };
        int[] yBoundaries = { 0, 5, 20 };
        var cell = new PixelRect(0, 0, 60, 20);

        var layout = Stage2LabelLayout.Calculate(
            cell,
            new PixelRect(0, 0, 200, 200),
            xBoundaries,
            yBoundaries,
            columnCount: 3,
            rowCount: 2,
            StripThickness,
            Gap);

        Assert.NotNull(layout);
        Assert.Equal(new[] { 5.0, 20.0, 45.0 }, layout!.ColumnCenters);
        Assert.Equal(new[] { 2.5, 12.5 }, layout.RowCenters);
    }

    [Fact]
    public void CellOutsideMonitor_ReturnsNull()
    {
        var cell = new PixelRect(1100, 100, 120, 90);

        var layout = Stage2LabelLayout.Calculate(
            cell,
            Monitor,
            XBoundaries,
            YBoundaries,
            ColumnCount,
            RowCount,
            StripThickness,
            Gap);

        Assert.Null(layout);
    }

    [Fact]
    public void BoundaryCountMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => Stage2LabelLayout.Calculate(
            new PixelRect(100, 100, 120, 90),
            Monitor,
            new[] { 100, 130 },
            YBoundaries,
            ColumnCount,
            RowCount,
            StripThickness,
            Gap));
    }
}
