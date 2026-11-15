using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using Xunit;

namespace KeyGrid.Tests;

public class LabelOwnershipTests
{
    [Fact]
    public void ReferencePointInsideMonitor_OwnsCellEvenWhenIntersectionTies()
    {
        var monitors = new[]
        {
            new PixelRect(0, 0, 100, 100),
            new PixelRect(100, 0, 100, 100)
        };
        var cell = new PixelRect(50, 0, 100, 100);

        Assert.Equal(0, LabelOwnership.FindOwnerIndex(cell, monitors));
    }

    [Fact]
    public void ReferencePointInsideOverlappingMonitors_TieBreaksByTopThenLeft()
    {
        var cell = new PixelRect(50, 0, 100, 100);
        var monitors = new[]
        {
            new PixelRect(50, 0, 100, 100),
            new PixelRect(0, 0, 150, 100)
        };

        Assert.Equal(1, LabelOwnership.FindOwnerIndex(cell, monitors));
    }

    [Fact]
    public void ReferencePointInsideSameOriginMonitors_TieBreaksBySmallerHeightThenWidth()
    {
        var heightCell = new PixelRect(0, 0, 100, 50);
        var heightMonitors = new[]
        {
            new PixelRect(0, 0, 100, 100),
            new PixelRect(0, 0, 100, 50)
        };
        Assert.Equal(1, LabelOwnership.FindOwnerIndex(heightCell, heightMonitors));

        var widthCell = new PixelRect(0, 0, 50, 100);
        var widthMonitors = new[]
        {
            new PixelRect(0, 0, 100, 100),
            new PixelRect(0, 0, 50, 100)
        };
        Assert.Equal(1, LabelOwnership.FindOwnerIndex(widthCell, widthMonitors));
    }

    [Fact]
    public void ReferencePointInGap_UsesLargestIntersection()
    {
        var monitors = new[]
        {
            new PixelRect(0, 0, 100, 100),
            new PixelRect(200, 0, 100, 100)
        };
        var cell = new PixelRect(50, 0, 220, 100);

        Assert.Equal(1, LabelOwnership.FindOwnerIndex(cell, monitors));
    }

    [Fact]
    public void EqualIntersectionArea_TieBreaksByTopThenLeft()
    {
        var monitors = new[]
        {
            new PixelRect(0, 100, 100, 100),
            new PixelRect(200, 0, 100, 100)
        };
        var cell = new PixelRect(50, 0, 200, 200);

        Assert.Equal(1, LabelOwnership.FindOwnerIndex(cell, monitors));
    }

    [Fact]
    public void EqualIntersectionAreaOnSameRow_TieBreaksByLeftRegardlessOfOrder()
    {
        var left = new PixelRect(0, 0, 100, 100);
        var right = new PixelRect(199, 0, 100, 100);

        Assert.Equal(1, LabelOwnership.FindOwnerIndex(
            new PixelRect(0, 0, 299, 100),
            new[] { right, left }));
    }

    [Fact]
    public void CellWithoutMonitorIntersection_HasNoOwner()
    {
        var monitors = new[]
        {
            new PixelRect(0, 0, 100, 100),
            new PixelRect(200, 0, 100, 100)
        };

        Assert.Equal(-1, LabelOwnership.FindOwnerIndex(new PixelRect(1000, 1000, 10, 10), monitors));
    }

    [Fact]
    public void EmptyCellOrMonitorList_HasNoOwner()
    {
        var monitors = new[] { new PixelRect(0, 0, 100, 100) };

        Assert.Equal(-1, LabelOwnership.FindOwnerIndex(PixelRect.Empty, monitors));
        Assert.Equal(-1, LabelOwnership.FindOwnerIndex(new PixelRect(0, 0, 10, 10), Array.Empty<PixelRect>()));
    }

    [Fact]
    public void NullMonitorList_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => LabelOwnership.FindOwnerIndex(new PixelRect(0, 0, 10, 10), null!));
    }
}
