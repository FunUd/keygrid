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
        mgr.StartSelection(monitors);

        Assert.Equal(new PixelRect(0, 0, 3840, 1080), mgr.VirtualScreenBoundingBox);
        Assert.Equal(20, mgr.ColumnCount);
        Assert.Equal(6, mgr.RowCount);
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
        mgr.StartSelection(monitors);

        // Select a valid Stage 1 cell and confirm the default Stage 2 cursor.
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('H'));
        Assert.True(mgr.ProcessEnter());

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
        mgr.StartSelection(monitors);

        Assert.Equal(-1920, mgr.VirtualScreenBoundingBox.Left);
        Assert.Equal(3840, mgr.VirtualScreenBoundingBox.Width);

        // First key 'Q' selects column 0 (which is on the left monitor at [-1920...]).
        Assert.True(mgr.ProcessKey('Q'));
        Assert.False(mgr.ProcessKey('Q')); // Q is not a row key.
        Assert.True(mgr.ProcessKey('H'));

        // Backspace replays the same allocation before confirming the selection.
        Assert.True(mgr.ProcessBackspace());
        Assert.Single(mgr.History);
        Assert.Equal("QWERTASDFGZXCVBYUIOP", new string(mgr.ColumnKeys.ToArray()));
        Assert.False(mgr.ProcessKey('Q')); // After Backspace, the next key is a row key.
        Assert.True(mgr.ProcessKey('H'));

        Assert.True(mgr.ProcessEnter());
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
        mgr.StartSelection(new[] { m1, m2 });

        // Columns covering the empty gap [100, 1900) should be invalid and rejected
        // Middle columns should be invalid
        int midCol = mgr.ColumnCount / 2;
        char midKey = mgr.ColumnKeys[midCol];

        bool processed = mgr.ProcessKey(midKey);
        Assert.False(processed, $"Key '{midKey}' for empty middle column should be rejected.");
    }

    [Fact]
    public void Stage2ArrowNavigation_SkipsRowsInsideMonitorGap()
    {
        var top = new PixelRect(0, 0, 1920, 490);
        var bottom = new PixelRect(0, 535, 1920, 545);
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { top, bottom });

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('H')); // Stage 1 row 4 spans the 45px monitor gap.
        Assert.Equal(2, mgr.CurrentStage);
        Assert.Equal(SelectionManager.Stage2RowCount / 2, mgr.SelectedRowIndex);

        Assert.True(mgr.MoveStage2Cursor(0, 1));

        // Stage 2 row 13 is entirely in the gap; movement skips it to row 14.
        Assert.Equal(14, mgr.SelectedRowIndex);
    }
}
