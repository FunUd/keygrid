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
    /// Calculates the required column count C and row count R for a given virtual screen width and height.
    /// Finds the minimum integers satisfying 10 * C^2 >= W and 10 * R^2 >= H, clamped to [1, 26].
    /// </summary>
    public static (int Columns, int Rows) CalculateDivisions(int width, int height)
    {
        int columns = CalculateSingleDimensionDivision(width);
        int rows = CalculateSingleDimensionDivision(height);
        return (columns, rows);
    }

    private static int CalculateSingleDimensionDivision(int length)
    {
        if (length <= 0)
        {
            return 1;
        }

        // Smallest integer N such that 10 * N^2 >= length
        int n = 1;
        while (10 * n * n < length && n < MaxDivisions)
        {
            n++;
        }

        return Math.Clamp(n, 1, MaxDivisions);
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
            boundaries[k] = origin + (k * length + count - 1) / count;
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
