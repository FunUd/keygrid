using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Calculation;

/// <summary>
/// Chooses the single monitor that owns a grid cell's label and stage-2 rulers.
/// The owner is the monitor containing the cell reference point. When that point
/// falls in a gap, the monitor with the largest intersection wins; ties use the
/// monitor's top-left position so the result does not depend on enumeration order.
/// </summary>
public static class LabelOwnership
{
    public static int FindOwnerIndex(PixelRect cell, IReadOnlyList<PixelRect> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (cell.IsEmpty || monitors.Count == 0)
        {
            return -1;
        }

        var reference = GridCalculator.CalculateReferencePoint(cell);
        int containingIndex = -1;
        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            if (monitor.IsEmpty || !monitor.Contains(reference))
            {
                continue;
            }

            if (containingIndex < 0 || CompareMonitors(monitor, monitors[containingIndex]) < 0)
            {
                containingIndex = i;
            }
        }

        if (containingIndex >= 0)
        {
            return containingIndex;
        }

        int ownerIndex = -1;
        long largestArea = -1;
        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            if (monitor.IsEmpty)
            {
                continue;
            }

            var intersection = cell.Intersect(monitor);
            if (intersection.IsEmpty)
            {
                continue;
            }

            long area = (long)intersection.Width * intersection.Height;
            if (area > largestArea ||
                (area == largestArea && CompareMonitors(monitor, monitors[ownerIndex]) < 0))
            {
                largestArea = area;
                ownerIndex = i;
            }
        }

        return ownerIndex;
    }

    private static int CompareMonitors(PixelRect left, PixelRect right)
    {
        int comparison = left.Top.CompareTo(right.Top);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Left.CompareTo(right.Left);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Height.CompareTo(right.Height);
        return comparison != 0 ? comparison : left.Width.CompareTo(right.Width);
    }
}
