using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Calculation;

/// <summary>
/// Side of the selected cell on which the stage-2 column-key ruler is placed.
/// </summary>
public enum HorizontalLabelStripPlacement
{
    Above,
    Below,
    InsideTop
}

/// <summary>
/// Side of the selected cell on which the stage-2 row-key ruler is placed.
/// </summary>
public enum VerticalLabelStripPlacement
{
    Left,
    Right,
    InsideLeft
}

/// <summary>
/// Calculates the placement of the stage-2 key rulers around the selected cell.
///
/// The rulers are kept outside the selected cell whenever the monitor has room,
/// so the sub-cell centers (the actual click targets) remain unobstructed. When
/// the preferred side does not fit, the ruler flips to the opposite side, and
/// finally falls back to the inside edge.
/// </summary>
public sealed class Stage2LabelLayout
{
    public PixelRect HorizontalStrip { get; }
    public PixelRect VerticalStrip { get; }
    public HorizontalLabelStripPlacement HorizontalPlacement { get; }
    public VerticalLabelStripPlacement VerticalPlacement { get; }

    /// <summary>Physical X coordinate of each sub-column center.</summary>
    public IReadOnlyList<double> ColumnCenters { get; }

    /// <summary>Physical Y coordinate of each sub-row center.</summary>
    public IReadOnlyList<double> RowCenters { get; }

    private Stage2LabelLayout(
        PixelRect horizontalStrip,
        PixelRect verticalStrip,
        HorizontalLabelStripPlacement horizontalPlacement,
        VerticalLabelStripPlacement verticalPlacement,
        IReadOnlyList<double> columnCenters,
        IReadOnlyList<double> rowCenters)
    {
        HorizontalStrip = horizontalStrip;
        VerticalStrip = verticalStrip;
        HorizontalPlacement = horizontalPlacement;
        VerticalPlacement = verticalPlacement;
        ColumnCenters = columnCenters;
        RowCenters = rowCenters;
    }

    public static Stage2LabelLayout? Calculate(
        PixelRect selectedCell,
        PixelRect monitorRect,
        int[] xBoundaries,
        int[] yBoundaries,
        int columnCount,
        int rowCount,
        int stripThickness,
        int gap)
    {
        ArgumentNullException.ThrowIfNull(xBoundaries);
        ArgumentNullException.ThrowIfNull(yBoundaries);

        if (columnCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columnCount));
        }
        if (rowCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        }
        if (xBoundaries.Length != columnCount + 1)
        {
            throw new ArgumentException("Boundary count must be column count plus one.", nameof(xBoundaries));
        }
        if (yBoundaries.Length != rowCount + 1)
        {
            throw new ArgumentException("Boundary count must be row count plus one.", nameof(yBoundaries));
        }
        if (stripThickness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stripThickness));
        }
        if (gap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gap));
        }

        var visibleCell = selectedCell.Intersect(monitorRect);
        if (visibleCell.IsEmpty)
        {
            return null;
        }

        PixelRect horizontalStrip;
        HorizontalLabelStripPlacement horizontalPlacement;
        int aboveTop = visibleCell.Top - stripThickness - gap;
        int belowTop = visibleCell.Bottom + gap;
        if (aboveTop >= monitorRect.Top)
        {
            horizontalStrip = new PixelRect(visibleCell.Left, aboveTop, visibleCell.Width, stripThickness);
            horizontalPlacement = HorizontalLabelStripPlacement.Above;
        }
        else if (belowTop + stripThickness <= monitorRect.Bottom)
        {
            horizontalStrip = new PixelRect(visibleCell.Left, belowTop, visibleCell.Width, stripThickness);
            horizontalPlacement = HorizontalLabelStripPlacement.Below;
        }
        else
        {
            int insideHeight = Math.Min(stripThickness, visibleCell.Height - gap);
            if (insideHeight <= 0)
            {
                return null;
            }

            horizontalStrip = new PixelRect(visibleCell.Left, visibleCell.Top + gap, visibleCell.Width, insideHeight);
            horizontalPlacement = HorizontalLabelStripPlacement.InsideTop;
        }

        PixelRect verticalStrip;
        VerticalLabelStripPlacement verticalPlacement;
        int leftLeft = visibleCell.Left - stripThickness - gap;
        int rightLeft = visibleCell.Right + gap;
        if (leftLeft >= monitorRect.Left)
        {
            verticalStrip = new PixelRect(leftLeft, visibleCell.Top, stripThickness, visibleCell.Height);
            verticalPlacement = VerticalLabelStripPlacement.Left;
        }
        else if (rightLeft + stripThickness <= monitorRect.Right)
        {
            verticalStrip = new PixelRect(rightLeft, visibleCell.Top, stripThickness, visibleCell.Height);
            verticalPlacement = VerticalLabelStripPlacement.Right;
        }
        else
        {
            int insideWidth = Math.Min(stripThickness, visibleCell.Width - gap);
            if (insideWidth <= 0)
            {
                return null;
            }

            verticalStrip = new PixelRect(visibleCell.Left + gap, visibleCell.Top, insideWidth, visibleCell.Height);
            verticalPlacement = VerticalLabelStripPlacement.InsideLeft;
        }

        var columnCenters = new double[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            columnCenters[c] = (xBoundaries[c] + xBoundaries[c + 1]) / 2.0;
        }

        var rowCenters = new double[rowCount];
        for (int r = 0; r < rowCount; r++)
        {
            rowCenters[r] = (yBoundaries[r] + yBoundaries[r + 1]) / 2.0;
        }

        return new Stage2LabelLayout(
            horizontalStrip,
            verticalStrip,
            horizontalPlacement,
            verticalPlacement,
            columnCenters,
            rowCenters);
    }
}
