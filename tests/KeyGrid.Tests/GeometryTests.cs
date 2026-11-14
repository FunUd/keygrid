using KeyGrid.Core.Geometry;
using Xunit;

namespace KeyGrid.Tests;

public class GeometryTests
{
    [Fact]
    public void PixelPoint_PropertiesAndToString_WorkCorrectly()
    {
        var pt = new PixelPoint(100, 200);
        Assert.Equal(100, pt.X);
        Assert.Equal(200, pt.Y);
        Assert.Equal("(100, 200)", pt.ToString());
    }

    [Fact]
    public void PixelRect_HalfOpenIntervalProperties_WorkAsExpected()
    {
        var rect = new PixelRect(10, 20, 100, 50);

        Assert.Equal(10, rect.Left);
        Assert.Equal(20, rect.Top);
        Assert.Equal(100, rect.Width);
        Assert.Equal(50, rect.Height);
        Assert.Equal(110, rect.Right);
        Assert.Equal(70, rect.Bottom);
        Assert.False(rect.IsEmpty);

        // Half-open interval containment: [10, 110) x [20, 70)
        Assert.True(rect.Contains(10, 20));
        Assert.True(rect.Contains(109, 69));
        Assert.False(rect.Contains(110, 20));
        Assert.False(rect.Contains(10, 70));
        Assert.False(rect.Contains(9, 20));
        Assert.False(rect.Contains(10, 19));
    }

    [Fact]
    public void PixelRect_IntersectsAndIntersect_CalculateCorrectly()
    {
        var r1 = new PixelRect(0, 0, 100, 100);
        var r2 = new PixelRect(50, 50, 100, 100);
        var disjoint = new PixelRect(200, 200, 50, 50);

        Assert.True(r1.IntersectsWith(r2));
        Assert.False(r1.IntersectsWith(disjoint));

        var intersection = r1.Intersect(r2);
        Assert.Equal(new PixelRect(50, 50, 50, 50), intersection);

        var emptyIntersect = r1.Intersect(disjoint);
        Assert.True(emptyIntersect.IsEmpty);
    }

    [Fact]
    public void PixelRect_BoundingBox_ComputesMinimalEnclosingRectangle()
    {
        var rects = new[]
        {
            new PixelRect(100, 200, 50, 50),   // [100, 150) x [200, 250)
            new PixelRect(-100, 50, 80, 100),  // [-100, -20) x [50, 150)
            new PixelRect(300, 100, 10, 10)    // [300, 310) x [100, 110)
        };

        var bbox = PixelRect.BoundingBox(rects);

        Assert.Equal(-100, bbox.Left);
        Assert.Equal(50, bbox.Top);
        Assert.Equal(310 - (-100), bbox.Width);
        Assert.Equal(250 - 50, bbox.Height);
    }

    [Fact]
    public void PixelRect_Clamp_ClampsToValidPointsWithinHalfOpenInterval()
    {
        var rect = new PixelRect(10, 20, 100, 50); // X: [10, 109], Y: [20, 69]

        var pInside = rect.Clamp(new PixelPoint(50, 30));
        Assert.Equal(new PixelPoint(50, 30), pInside);

        var pOutsideLeft = rect.Clamp(new PixelPoint(0, 30));
        Assert.Equal(new PixelPoint(10, 30), pOutsideLeft);

        var pOutsideRight = rect.Clamp(new PixelPoint(200, 30));
        Assert.Equal(new PixelPoint(109, 30), pOutsideRight);

        var pOutsideBottom = rect.Clamp(new PixelPoint(50, 100));
        Assert.Equal(new PixelPoint(50, 69), pOutsideBottom);
    }
}
