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
}
