using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Calculation;

/// <summary>
/// Implements pure grid calculation logic according to spec.md Section 4:
/// division counts, cell boundaries, intersections, bounding boxes, and click coordinate resolution.
/// </summary>
public static class GridCalculator
{
    public const int MaxDivisions = 26;
    public const double Stage1CellTargetDip = 128.0;
    public const double Stage2CellDip = 24.0;
    public const int Stage2ColumnCount = 25;
    public const int Stage2RowCount = 25;
    public const double Stage2WindowDip = Stage2ColumnCount * Stage2CellDip; // 600.0 DIP

    /// <summary>
    /// Chooses the Stage 1 divisions that keep the physical cell size close to
    /// <paramref name="targetCellPx"/> while keeping the total key count (Columns + Rows)
    /// within the 26-key budget.
    /// </summary>
    public static (int Columns, int Rows) CalculateFirstStageDivisions(
        int widthPx,
        int heightPx,
        int targetCellPx)
    {
        if (widthPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPx));
        }

        if (heightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightPx));
        }

        if (targetCellPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCellPx));
        }

        int bestColumns = 1;
        int bestRows = 1;
        int bestMaxDeviation = int.MaxValue;
        int bestTotalDeviation = int.MaxValue;
        int bestKeyCount = int.MaxValue;
        long bestCellCount = -1;

        for (int columns = 1; columns < MaxDivisions; columns++)
        {
            for (int rows = 1; columns + rows <= MaxDivisions; rows++)
            {
                int cellWidthPx = DivideCeiling(widthPx, columns);
                int cellHeightPx = DivideCeiling(heightPx, rows);

                int devWidth = Math.Abs(cellWidthPx - targetCellPx);
                int devHeight = Math.Abs(cellHeightPx - targetCellPx);

                int maxDeviation = Math.Max(devWidth, devHeight);
                int totalDeviation = devWidth + devHeight;
                int keyCount = columns + rows;
                long cellCount = (long)columns * rows;

                bool isBetter = maxDeviation < bestMaxDeviation
                    || (maxDeviation == bestMaxDeviation && totalDeviation < bestTotalDeviation)
                    || (maxDeviation == bestMaxDeviation && totalDeviation == bestTotalDeviation && keyCount < bestKeyCount)
                    || (maxDeviation == bestMaxDeviation && totalDeviation == bestTotalDeviation && keyCount == bestKeyCount && cellCount > bestCellCount)
                    || (maxDeviation == bestMaxDeviation && totalDeviation == bestTotalDeviation && keyCount == bestKeyCount && cellCount == bestCellCount && columns < bestColumns);

                if (isBetter)
                {
                    bestMaxDeviation = maxDeviation;
                    bestTotalDeviation = totalDeviation;
                    bestKeyCount = keyCount;
                    bestCellCount = cellCount;
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
    /// Calculates the Stage 2 window centered on the Stage 1 focus box reference point.
    /// The window is <paramref name="columnCount"/> x <paramref name="rowCount"/> cells,
    /// each of exact size <paramref name="cellSizePx"/> physical pixels.
    /// </summary>
    public static PixelRect CalculateCenteredStage2Window(
        PixelRect focusBox,
        int cellSizePx,
        int columnCount,
        int rowCount)
    {
        if (focusBox.IsEmpty)
        {
            throw new ArgumentException("Focus box must not be empty.", nameof(focusBox));
        }

        if (cellSizePx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSizePx), "Cell size must be positive.");
        }

        if (columnCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columnCount), "Column count must be positive.");
        }

        if (rowCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount), "Row count must be positive.");
        }

        var refPoint = CalculateReferencePoint(focusBox);
        long windowWidthPx = (long)columnCount * cellSizePx;
        long windowHeightPx = (long)rowCount * cellSizePx;

        long left = (long)refPoint.X - windowWidthPx / 2;
        long top = (long)refPoint.Y - windowHeightPx / 2;

        return PixelRect.FromEdges(
            checked((int)left),
            checked((int)top),
            checked((int)(left + windowWidthPx)),
            checked((int)(top + windowHeightPx)));
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
