using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using Xunit;

namespace KeyGrid.Tests;

public class MultiMonitorIntegrationTests
{
    [Fact]
    public void MultiMonitor_DualMonitors_CorrectlyCalculatesInitialBoundsAndLimits()
    {
        // Dual 1080p monitors side by side: [0, 0, 1920, 1080] and [1920, 0, 1920, 1080]
        var monitor1 = new PixelRect(0, 0, 1920, 1080);
        var monitor2 = new PixelRect(1920, 0, 1920, 1080);
        var monitors = new[] { monitor1, monitor2 };

        var mgr = new SelectionManager();
        mgr.StartSelection(monitors, 20.0);

        Assert.Equal(new PixelRect(0, 0, 3840, 1080), mgr.VirtualScreenBoundingBox);
        Assert.Equal(14, mgr.ColumnCount);
        Assert.Equal(8, mgr.RowCount);
    }

    [Fact]
    public void MultiMonitor_DisjointMonitorsWithGap_TargetNeverFallsInGap()
    {
        // Two displays separated by a 500px gap:
        // Display 1: [0, 0, 1000, 1000]
        // Display 2: [1500, 0, 1000, 1000]
        var d1 = new PixelRect(0, 0, 1000, 1000);
        var d2 = new PixelRect(1500, 0, 1000, 1000);
        var monitors = new[] { d1, d2 };

        var mgr = new SelectionManager();
        mgr.StartSelection(monitors, 20.0);

        // Perform 4 valid keystrokes: column Q, row Y, then the same sub-cell.
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('Y'));
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('Y'));

        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.NotNull(mgr.TargetCoordinate);

        var target = mgr.TargetCoordinate.Value;
        // Verify target is inside either d1 or d2
        bool inside = d1.Contains(target) || d2.Contains(target);
        Assert.True(inside, $"Target {target} should be strictly within display 1 or 2, not in the gap.");
    }

    [Fact]
    public void MultiMonitor_NegativeOrigin_TargetResolvesCorrectly()
    {
        // Left monitor at negative coordinate: [-1920, 0, 1920, 1080]
        // Primary monitor at origin: [0, 0, 1920, 1080]
        var left = new PixelRect(-1920, 0, 1920, 1080);
        var primary = new PixelRect(0, 0, 1920, 1080);
        var monitors = new[] { left, primary };

        var mgr = new SelectionManager();
        mgr.StartSelection(monitors, 20.0);

        Assert.Equal(-1920, mgr.VirtualScreenBoundingBox.Left);
        Assert.Equal(3840, mgr.VirtualScreenBoundingBox.Width);

        // First key 'Q' selects column 0 (which is on the left monitor at [-1920...]).
        Assert.True(mgr.ProcessKey('Q'));
        Assert.False(mgr.ProcessKey('Q')); // Q is not a row key.
        Assert.True(mgr.ProcessKey('Y'));
        Assert.True(mgr.ProcessKey('Q'));

        // Backspace replays the same allocation before resolving the target.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal("QWERTASDFGZXCV", new string(mgr.ColumnKeys.ToArray()));
        Assert.True(mgr.ProcessKey('Q'));
        Assert.False(mgr.ProcessKey('Q')); // Q is still not a row key.
        Assert.True(mgr.ProcessKey('Y'));

        Assert.NotNull(mgr.TargetCoordinate);
        var target = mgr.TargetCoordinate.Value;

        Assert.True(left.Contains(target), $"Target {target} should be on the left monitor with negative coordinates.");
    }

    [Fact]
    public void MultiMonitor_InvalidColumnInGap_IsRejected()
    {
        // Two small 100x100 monitors with a large gap between them:
        // M1: [0, 0, 100, 100]
        // M2: [1900, 0, 100, 100]
        // Virtual Screen: [0, 0, 2000, 100]
        var m1 = new PixelRect(0, 0, 100, 100);
        var m2 = new PixelRect(1900, 0, 100, 100);

        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { m1, m2 }, 20.0);

        // Columns covering the empty gap [100, 1900) should be invalid and rejected
        // Middle columns should be invalid
        int midCol = mgr.ColumnCount / 2;
        char midKey = mgr.ColumnKeys[midCol];

        bool processed = mgr.ProcessKey(midKey);
        Assert.False(processed, $"Key '{midKey}' for empty middle column should be rejected.");
    }
}
