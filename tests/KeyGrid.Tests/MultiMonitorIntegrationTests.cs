using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using Xunit;

namespace KeyGrid.Tests;

public class MultiMonitorIntegrationTests
{
    [Fact]
    public void MultiMonitor_Stage1GridCoversOnlyTheActiveMonitor()
    {
        // Dual 1080p monitors side by side: [0, 0, 1920, 1080] and [1920, 0, 1920, 1080]
        var monitor1 = new PixelRect(0, 0, 1920, 1080);
        var monitor2 = new PixelRect(1920, 0, 1920, 1080);
        var monitors = new[] { monitor1, monitor2 };

        var mgr = new SelectionManager();
        Assert.True(mgr.StartSelection(monitors, monitor1, 1.0));

        // The virtual desktop still spans both monitors, but the Stage 1 grid
        // and its divisions are scoped to the active monitor only.
        Assert.Equal(new PixelRect(0, 0, 3840, 1080), mgr.VirtualScreenBoundingBox);
        Assert.Equal(monitor1, mgr.ActiveMonitorRect);
        Assert.Equal(monitor1, mgr.CurrentBoundingBox);
        Assert.Equal(15, mgr.ColumnCount);
        Assert.Equal(8, mgr.RowCount);
    }

    [Fact]
    public void MultiMonitor_ActiveMonitorSecondMonitor_UsesItsOwnSizeAndDpi()
    {
        var monitor1 = new PixelRect(0, 0, 1920, 1080);
        var monitor2 = new PixelRect(1920, 0, 2560, 1440);
        var monitors = new[] { monitor1, monitor2 };

        var mgr = new SelectionManager();
        Assert.True(mgr.StartSelection(monitors, monitor2, 1.5));

        Assert.Equal(monitor2, mgr.ActiveMonitorRect);
        Assert.Equal(monitor2, mgr.CurrentBoundingBox);
        Assert.Equal(1.5, mgr.DpiReferenceScale);

        // 2560x1440 physical pixels at 150% DPI: target cell is 192 px, which
        // resolves to the 13x8 division documented for this configuration.
        Assert.Equal(13, mgr.ColumnCount);
        Assert.Equal(8, mgr.RowCount);
    }

    [Fact]
    public void SwitchActiveMonitor_MovesGridToAnotherMonitorAndResetsHistory()
    {
        var monitor1 = new PixelRect(0, 0, 1920, 1080);
        var monitor2 = new PixelRect(1920, 0, 2560, 1440);
        var monitors = new[] { monitor1, monitor2 };

        var mgr = new SelectionManager();
        Assert.True(mgr.StartSelection(monitors, monitor1, 1.0));
        Assert.True(mgr.ProcessKey(mgr.ColumnKeys[0]));
        Assert.Single(mgr.History);

        Assert.True(mgr.SwitchActiveMonitor(monitor2, 1.5));

        Assert.Equal(monitor2, mgr.ActiveMonitorRect);
        Assert.Equal(monitor2, mgr.CurrentBoundingBox);
        Assert.Equal(1.5, mgr.DpiReferenceScale);
        Assert.Empty(mgr.History);
        Assert.Equal(1, mgr.CurrentStage);
        Assert.Equal(-1, mgr.SelectedColumnIndex);
        Assert.Equal(-1, mgr.SelectedRowIndex);
        Assert.Equal(13, mgr.ColumnCount);
        Assert.Equal(8, mgr.RowCount);

        // The new grid accepts input immediately.
        Assert.True(mgr.ProcessKey(mgr.ColumnKeys[0]));
        Assert.Single(mgr.History);
    }

    [Fact]
    public void SwitchActiveMonitor_RejectsUnknownRectAndStandby()
    {
        var monitor1 = new PixelRect(0, 0, 1920, 1080);
        var monitors = new[] { monitor1 };
        var mgr = new SelectionManager();

        // Standby: switching is not allowed.
        Assert.False(mgr.SwitchActiveMonitor(monitor1, 1.0));

        Assert.True(mgr.StartSelection(monitors, monitor1, 1.0));

        // A rect that is not one of the session monitors is rejected.
        Assert.False(mgr.SwitchActiveMonitor(new PixelRect(5000, 0, 100, 100), 1.0));
        Assert.Equal(monitor1, mgr.ActiveMonitorRect);
    }

    [Fact]
    public void MultiMonitor_BackspaceCannotEscapeTheActiveMonitor()
    {
        var monitor1 = new PixelRect(0, 0, 1920, 1080);
        var monitor2 = new PixelRect(1920, 0, 1920, 1080);
        var monitors = new[] { monitor1, monitor2 };

        var mgr = new SelectionManager();
        Assert.True(mgr.StartSelection(monitors, monitor2, 1.0));
        Assert.True(mgr.ProcessKey(mgr.ColumnKeys[0]));
        Assert.True(mgr.ProcessBackspace());

        // Returning to an empty Stage 1 history restores the active monitor's
        // grid, not the whole virtual desktop.
        Assert.Empty(mgr.History);
        Assert.Equal(monitor2, mgr.CurrentBoundingBox);
        Assert.Equal(monitor2, mgr.ActiveMonitorRect);
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
        Assert.True(mgr.StartSelection(monitors, d1, 1.0));

        // Select a valid Stage 1 cell and confirm the default Stage 2 cursor.
        Assert.True(mgr.ProcessKey(mgr.ColumnKeys[0]));
        Assert.True(mgr.ProcessKey(mgr.RowKeys[0]));
        Assert.True(mgr.ProcessEnter());

        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.NotNull(mgr.TargetCoordinate);

        var target = mgr.TargetCoordinate.Value;
        // Verify target is inside either d1 or d2
        bool inside = d1.Contains(target) || d2.Contains(target);
        Assert.True(inside, $"Target {target} should be strictly within display 1 or 2, not in the gap.");
    }

    [Fact]
    public void MultiMonitor_NegativeOrigin_TargetResolvesOnActiveMonitor()
    {
        // Left monitor at negative coordinate: [-1920, 0, 1920, 1080]
        // Primary monitor at origin: [0, 0, 1920, 1080]
        var left = new PixelRect(-1920, 0, 1920, 1080);
        var primary = new PixelRect(0, 0, 1920, 1080);
        var monitors = new[] { left, primary };

        var mgr = new SelectionManager();
        Assert.True(mgr.StartSelection(monitors, left, 1.0));

        Assert.Equal(-1920, mgr.VirtualScreenBoundingBox.Left);
        Assert.Equal(3840, mgr.VirtualScreenBoundingBox.Width);
        Assert.Equal(left, mgr.ActiveMonitorRect);

        // First key 'Q' selects the first column of the left monitor's grid.
        Assert.True(mgr.ProcessKey('Q'));
        Assert.False(mgr.ProcessKey('Q')); // Q is not a row key.
        Assert.True(mgr.ProcessKey(mgr.RowKeys[0]));

        // Backspace replays the same allocation before confirming the selection.
        Assert.True(mgr.ProcessBackspace());
        Assert.Single(mgr.History);
        Assert.Equal("QWERTASDFGZXCVB", new string(mgr.ColumnKeys.ToArray()));
        Assert.False(mgr.ProcessKey('Q')); // After Backspace, the next key is a row key.
        Assert.True(mgr.ProcessKey(mgr.RowKeys[0]));

        Assert.True(mgr.ProcessEnter());
        Assert.NotNull(mgr.TargetCoordinate);
        var target = mgr.TargetCoordinate.Value;

        Assert.True(left.Contains(target), $"Target {target} should be on the left monitor with negative coordinates.");
    }

    [Fact]
    public void MultiMonitor_Stage1SelectionStaysWithinTheActiveMonitor()
    {
        // Two small monitors with a large gap between them:
        // M1: [0, 0, 1000, 1000]
        // M2: [1900, 0, 1000, 1000]
        var m1 = new PixelRect(0, 0, 1000, 1000);
        var m2 = new PixelRect(1900, 0, 1000, 1000);

        var mgr = new SelectionManager();
        Assert.True(mgr.StartSelection(new[] { m1, m2 }, m1, 1.0));

        // Every Stage 1 cell lives inside the active monitor, so the last
        // column and row keys are always valid.
        Assert.True(mgr.ProcessKey(mgr.ColumnKeys[^1]));
        Assert.True(mgr.ProcessKey(mgr.RowKeys[^1]));

        // The Stage 1 focus cell cannot leak into the gap or the other monitor.
        Assert.All(mgr.FocusSelection, rect => Assert.Equal(rect, rect.Intersect(m1)));
    }

    [Fact]
    public void Stage2ArrowNavigation_SkipsRowsInsideMonitorGap()
    {
        var top = new PixelRect(0, 0, 1920, 480);
        var bottom = new PixelRect(0, 540, 1920, 540);
        var mgr = new SelectionManager();
        Assert.True(mgr.StartSelection(new[] { top, bottom }, top, 1.0));

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('U')); // Stage 1 row 1 spans the monitor gap.
        Assert.Equal(2, mgr.CurrentStage);
        Assert.Equal(SelectionManager.Stage2RowCount / 2, mgr.SelectedRowIndex); // Row 12

        // Move to row 16 (last valid row in top monitor)
        Assert.True(mgr.MoveStage2Cursor(0, 1)); // 13
        Assert.True(mgr.MoveStage2Cursor(0, 1)); // 14
        Assert.True(mgr.MoveStage2Cursor(0, 1)); // 15
        Assert.True(mgr.MoveStage2Cursor(0, 1)); // 16
        Assert.Equal(16, mgr.SelectedRowIndex);

        // The Stage 2 rows in the monitor gap are skipped.
        Assert.True(mgr.MoveStage2Cursor(0, 1));
        Assert.Equal(17, mgr.SelectedRowIndex);
    }
}
