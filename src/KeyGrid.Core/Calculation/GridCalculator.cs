using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Calculation;

/// <summary>
/// Implements pure grid calculation logic according to spec.md Section 4:
/// division counts, cell boundaries, intersections, bounding boxes, and click coordinate resolution.
/// </summary>
public static class GridCalculator
{
    public const int MaxDivisions = 26;

    /// <summary>
    /// Chooses the Stage 1 divisions that minimize the final cell size after dividing
    /// each cell by the Stage 2 focus grid, while keeping the Stage 1 key allocation
    /// within its 26-key budget. The focus dimensions, not the full context window,
    /// determine the Stage 2 cell size.
    /// </summary>
    public static (int Columns, int Rows) CalculateFirstStageDivisions(
        int width,
        int height,
        int stage2Columns,
        int stage2Rows)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (stage2Columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stage2Columns));
        }

        if (stage2Rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stage2Rows));
        }

        int bestColumns = 1;
        int bestRows = 1;
        int bestWorstFinalSize = int.MaxValue;
        long bestCellCount = long.MaxValue;
        int bestKeyCount = int.MaxValue;

        for (int columns = 1; columns < MaxDivisions; columns++)
        {
            for (int rows = 1; columns + rows <= MaxDivisions; rows++)
            {
                int finalWidth = DivideCeiling(DivideCeiling(width, columns), stage2Columns);
                int finalHeight = DivideCeiling(DivideCeiling(height, rows), stage2Rows);
                int worstFinalSize = Math.Max(finalWidth, finalHeight);
                long cellCount = (long)columns * rows;
                int keyCount = columns + rows;

                bool isBetter = worstFinalSize < bestWorstFinalSize
                    || (worstFinalSize == bestWorstFinalSize && cellCount > bestCellCount)
                    || (worstFinalSize == bestWorstFinalSize && cellCount == bestCellCount && keyCount < bestKeyCount)
                    || (worstFinalSize == bestWorstFinalSize && cellCount == bestCellCount && keyCount == bestKeyCount && columns < bestColumns);

                if (isBetter)
                {
                    bestWorstFinalSize = worstFinalSize;
                    bestCellCount = cellCount;
                    bestKeyCount = keyCount;
                    bestColumns = columns;
                    bestRows = rows;
                }
            }
        }

        return (bestColumns, bestRows);
    }

    private static int DivideCeiling(int dividend, int divisor)
    {
        return (int)(((long)dividend + divisor - 1) / divisor);
    }

    /// <summary>
    /// Calculates cell boundaries for a given length and division count using integer arithmetic:
    /// Boundary(k) = origin + (k * L + N - 1) / N for k = 1..N-1,
    /// with Boundary(0) = origin and Boundary(N) = origin + L.
    /// </summary>
    public static int[] CalculateBoundaries(int origin, int length, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Division count must be greater than zero.");
        }

        int[] boundaries = new int[count + 1];
        boundaries[0] = origin;
        boundaries[count] = origin + length;

        for (int k = 1; k < count; k++)
        {
            // Promote the multiplication to 64-bit so large lengths cannot overflow
            // before the division, even if the division-count limit is relaxed later.
            boundaries[k] = origin + (int)(((long)k * length + count - 1) / count);
        }

        return boundaries;
    }

    /// <summary>
    /// Calculates the Stage 2 context window centered on the Stage 1 focus box.
    /// The window contains <paramref name="contextColumns"/> x <paramref name="contextRows"/>
    /// cells, and its central <paramref name="focusColumns"/> x <paramref name="focusRows"/>
    /// cells exactly match the focus box. The context scale must be an odd multiple of
    /// the focus count so both sides receive the same number of context cells.
    /// </summary>
    public static PixelRect CalculateCenteredContextWindow(
        PixelRect focusBox,
        int contextColumns,
        int contextRows,
        int focusColumns,
        int focusRows)
    {
        if (focusBox.IsEmpty)
        {
            throw new ArgumentException("Focus box must not be empty.", nameof(focusBox));
        }

        if (contextColumns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextColumns));
        }

        if (contextRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextRows));
        }

        if (focusColumns <= 0 || focusColumns > contextColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(focusColumns));
        }

        if (focusRows <= 0 || focusRows > contextRows)
        {
            throw new ArgumentOutOfRangeException(nameof(focusRows));
        }

        if (contextColumns % focusColumns != 0)
        {
            throw new ArgumentException(
                "Context column count must be an integer multiple of the focus column count.",
                nameof(contextColumns));
        }

        if (contextRows % focusRows != 0)
        {
            throw new ArgumentException(
                "Context row count must be an integer multiple of the focus row count.",
                nameof(contextRows));
        }

        int scaleColumns = contextColumns / focusColumns;
        int scaleRows = contextRows / focusRows;
        if (scaleColumns % 2 == 0 || scaleRows % 2 == 0)
        {
            throw new ArgumentException(
                "Context scale must be odd so the focus cells remain exactly centered.",
                nameof(contextColumns));
        }

        int marginColumns = (contextColumns - focusColumns) / 2;
        int marginRows = (contextRows - focusRows) / 2;

        long left = (long)focusBox.Left - ((long)marginColumns * focusBox.Width / focusColumns);
        long top = (long)focusBox.Top - ((long)marginRows * focusBox.Height / focusRows);
        long width = (long)scaleColumns * focusBox.Width;
        long height = (long)scaleRows * focusBox.Height;

        return PixelRect.FromEdges(
            checked((int)left),
            checked((int)top),
            checked((int)(left + width)),
            checked((int)(top + height)));
    }

    /// <summary>
    /// Generates the rectangular cell at column index c and row index r.
    /// </summary>
    public static PixelRect GetCell(int[] xBoundaries, int[] yBoundaries, int columnIndex, int rowIndex)
    {
        if (columnIndex < 0 || columnIndex >= xBoundaries.Length - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        if (rowIndex < 0 || rowIndex >= yBoundaries.Length - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        int left = xBoundaries[columnIndex];
        int right = xBoundaries[columnIndex + 1];
        int top = yBoundaries[rowIndex];
        int bottom = yBoundaries[rowIndex + 1];

        return PixelRect.FromEdges(left, top, right, bottom);
    }

    /// <summary>
    /// Computes the intersection of a cell with the current selection area.
    /// Returns only non-empty intersecting rectangles.
    /// </summary>
    public static IReadOnlyList<PixelRect> IntersectCellWithSelection(
        PixelRect cell,
        IReadOnlyList<PixelRect> currentSelection)
    {
        var result = new List<PixelRect>();

        foreach (var rect in currentSelection)
        {
            var intersection = cell.Intersect(rect);
            if (!intersection.IsEmpty)
            {
                result.Add(intersection);
            }
        }

        return result;
    }

    /// <summary>
    /// Determines whether a column has any valid intersection with the current selection area.
    /// </summary>
    public static bool IsColumnValid(
        int columnIndex,
        int[] xBoundaries,
        PixelRect boundingBox,
        IReadOnlyList<PixelRect> currentSelection)
    {
        if (columnIndex < 0 || columnIndex >= xBoundaries.Length - 1)
        {
            return false;
        }

        int left = xBoundaries[columnIndex];
        int right = xBoundaries[columnIndex + 1];
        var columnSlice = PixelRect.FromEdges(left, boundingBox.Top, right, boundingBox.Bottom);

        foreach (var rect in currentSelection)
        {
            if (columnSlice.IntersectsWith(rect))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a specific cell [c, r] has any valid intersection with the current selection area.
    /// </summary>
    public static bool IsCellValid(
        int columnIndex,
        int rowIndex,
        int[] xBoundaries,
        int[] yBoundaries,
        IReadOnlyList<PixelRect> currentSelection)
    {
        var cell = GetCell(xBoundaries, yBoundaries, columnIndex, rowIndex);
        if (cell.IsEmpty)
        {
            return false;
        }

        foreach (var rect in currentSelection)
        {
            if (cell.IntersectsWith(rect))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Calculates the reference center point of a bounding box according to spec.md Section 4.4:
    /// Center = (Left + (Width - 1) / 2, Top + (Height - 1) / 2).
    /// </summary>
    public static PixelPoint CalculateReferencePoint(PixelRect boundingBox)
    {
        if (boundingBox.IsEmpty)
        {
            throw new ArgumentException("Cannot calculate reference point of an empty bounding box.", nameof(boundingBox));
        }

        int centerX = boundingBox.Left + (boundingBox.Width - 1) / 2;
        int centerY = boundingBox.Top + (boundingBox.Height - 1) / 2;
        return new PixelPoint(centerX, centerY);
    }

    /// <summary>
    /// Resolves the final click coordinate according to spec.md Section 4.4:
    /// 1. If reference point is inside the selection area, return it.
    /// 2. If reference point is outside (e.g. monitor gap), find the point in the selection area
    ///    with the minimal Euclidean distance to the reference point.
    /// 3. In case of ties, prioritize smaller Y, then smaller X.
    /// </summary>
    public static PixelPoint ResolveTargetCoordinate(
        PixelRect boundingBox,
        IReadOnlyList<PixelRect> selectionArea)
    {
        if (selectionArea.Count == 0)
        {
            throw new InvalidOperationException("Selection area is empty; cannot resolve target coordinate.");
        }

        var refPoint = CalculateReferencePoint(boundingBox);

        // 1. Check if reference point is directly inside any selection rectangle
        foreach (var rect in selectionArea)
        {
            if (rect.Contains(refPoint))
            {
                return refPoint;
            }
        }

        // 2. Find closest point in selectionArea with minimal Euclidean distance
        PixelPoint? bestCandidate = null;
        long bestDistSq = long.MaxValue;

        foreach (var rect in selectionArea)
        {
            if (rect.IsEmpty)
            {
                continue;
            }

            var candidate = rect.Clamp(refPoint);
            long dx = candidate.X - refPoint.X;
            long dy = candidate.Y - refPoint.Y;
            long distSq = dx * dx + dy * dy;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestCandidate = candidate;
            }
            else if (distSq == bestDistSq && bestCandidate.HasValue)
            {
                // Tie-breaking: smaller Y first, then smaller X
                if (candidate.Y < bestCandidate.Value.Y ||
                    (candidate.Y == bestCandidate.Value.Y && candidate.X < bestCandidate.Value.X))
                {
                    bestCandidate = candidate;
                }
            }
        }

        return bestCandidate ?? throw new InvalidOperationException("Failed to find a valid target coordinate.");
    }
}
