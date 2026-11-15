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
    /// Default target final-cell size in device-independent pixels. The target is
    /// converted to physical pixels with the primary monitor's effective DPI scale.
    /// </summary>
    public const double DefaultTargetDip = 20.0;

    /// <summary>
    /// Converts the default target to physical pixels for a monitor DPI scale.
    /// </summary>
    public static double CalculateTargetPixels(double dpiScale)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale), "DPI scale must be finite and greater than zero.");
        }

        return DefaultTargetDip * dpiScale;
    }

    /// <summary>
    /// Calculates the column count C and row count R for a physical width and height.
    /// Finds the minimum counts satisfying target * C^2 >= W and target * R^2 >= H.
    /// The column and row allocations must fit within the 26-key budget, so the
    /// result always satisfies C + R &lt;= 26. If the independent minimums exceed
    /// that budget, the target is relaxed by minimizing the worst-case final cell size.
    /// </summary>
    public static (int Columns, int Rows) CalculateDivisions(int width, int height, double targetPixels)
    {
        ValidateTargetPixels(targetPixels);

        // The caller passes valid monitor bounds. Keeping empty dimensions at one cell
        // preserves the previous defensive behavior for degenerate inputs.
        int effectiveWidth = Math.Max(1, width);
        int effectiveHeight = Math.Max(1, height);

        int minimumColumns = CalculateMinimumDivision(effectiveWidth, targetPixels);
        int minimumRows = CalculateMinimumDivision(effectiveHeight, targetPixels);
        if (minimumColumns + minimumRows <= MaxDivisions)
        {
            return (minimumColumns, minimumRows);
        }

        return CalculateRelaxedDivisions(effectiveWidth, effectiveHeight);
    }

    private static int CalculateMinimumDivision(int length, double targetPixels)
    {
        // Smallest integer N such that targetPixels * N^2 >= length.
        for (int n = 1; n < MaxDivisions; n++)
        {
            if (targetPixels * n * n >= length)
            {
                return n;
            }
        }

        return MaxDivisions;
    }

    /// <summary>
    /// Chooses C and R with C + R &lt;= 26 that minimize the worst-case final cell size
    /// max(ceil(ceil(W / C) / C), ceil(ceil(H / R) / R)).
    /// Because the target is common to both dimensions, minimizing this pixel size is
    /// equivalent to minimizing the worst-case ratio to the target. Ties are broken by
    /// fewer total cells, then fewer total keys, then smaller C.
    /// </summary>
    private static (int Columns, int Rows) CalculateRelaxedDivisions(int width, int height)
    {
        int bestColumns = 1;
        int bestRows = 1;
        int bestWorstFinalSize = int.MaxValue;
        long bestCellCount = long.MaxValue;
        int bestKeyCount = int.MaxValue;

        for (int columns = 1; columns < MaxDivisions; columns++)
        {
            for (int rows = 1; columns + rows <= MaxDivisions; rows++)
            {
                int finalWidth = CalculateFinalDimension(width, columns);
                int finalHeight = CalculateFinalDimension(height, rows);
                int worstFinalSize = Math.Max(finalWidth, finalHeight);
                long cellCount = (long)columns * rows;
                int keyCount = columns + rows;

                bool isBetter = worstFinalSize < bestWorstFinalSize
                    || (worstFinalSize == bestWorstFinalSize && cellCount < bestCellCount)
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

    private static int CalculateFinalDimension(int length, int count)
    {
        int stage1 = (int)(((long)length + count - 1) / count);
        return (int)(((long)stage1 + count - 1) / count);
    }

    private static void ValidateTargetPixels(double targetPixels)
    {
        if (!double.IsFinite(targetPixels) || targetPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetPixels), "Target size must be finite and greater than zero.");
        }
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
