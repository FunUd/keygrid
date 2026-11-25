using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using Xunit;

namespace KeyGrid.Tests;

public class SelectionManagerTests
{
    private readonly PixelRect[] _single1080pMonitor = new[] { new PixelRect(0, 0, 1920, 1080) };

    [Fact]
    public void StartSelection_InitializesStateAndDivisions()
    {
        var mgr = new SelectionManager();
        bool started = mgr.StartSelection(_single1080pMonitor, 1.0);

        Assert.True(started);
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(1, mgr.CurrentStage);
        Assert.Equal(17, mgr.ColumnCount);
        Assert.Equal(9, mgr.RowCount);
        Assert.Empty(mgr.History);
        Assert.Equal(-1, mgr.SelectedColumnIndex);
        Assert.Equal(-1, mgr.SelectedRowIndex);
    }

    [Fact]
    public void StartSelection_RestartsFromTheBeginning()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('I'));
        Assert.True(mgr.MoveStage2Cursor(1, 0));

        bool restarted = mgr.StartSelection(_single1080pMonitor, 1.0);

        Assert.True(restarted);
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Empty(mgr.History);
        Assert.Equal(1, mgr.CurrentStage);
        Assert.Equal(-1, mgr.SelectedColumnIndex);
        Assert.Equal(-1, mgr.SelectedRowIndex);
        Assert.Null(mgr.TargetCoordinate);
        Assert.Equal(_single1080pMonitor[0], mgr.CurrentBoundingBox);
    }

    [Fact]
    public void StartSelection_EmptyMonitorList_ReturnsFalse()
    {
        var mgr = new SelectionManager();

        bool started = mgr.StartSelection(Array.Empty<PixelRect>(), 1.0);

        Assert.False(started);
        Assert.Equal(SelectionState.Standby, mgr.State);
    }

    [Fact]
    public void Standby_IgnoresSelectionInput()
    {
        var mgr = new SelectionManager();

        Assert.False(mgr.ProcessKey('Q'));
        Assert.False(mgr.ProcessEnter());
        Assert.False(mgr.ProcessBackspace());
        Assert.False(mgr.MoveStage2Cursor(1, 0));
        Assert.Equal(SelectionState.Standby, mgr.State);
    }

    [Fact]
    public void Stage1UsesTargetToCalculateDivisions()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);

        Assert.Equal(17, mgr.ColumnCount);
        Assert.Equal(9, mgr.RowCount);
    }

    [Fact]
    public void Stage2ArrowNavigation_ExecutesClickOnEnter()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);

        // Stage 1 selects a cell using the two disjoint letter blocks.
        Assert.True(mgr.ProcessKey('Q'));
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(0, mgr.SelectedColumnIndex);
        Assert.Single(mgr.History);
        Assert.True(mgr.ProcessKey('I'));
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(2, mgr.CurrentStage);
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(SelectionManager.Stage2ColumnCount, mgr.ColumnCount);
        Assert.Equal(SelectionManager.Stage2RowCount, mgr.RowCount);

        int startingColumn = mgr.SelectedColumnIndex;
        int startingRow = mgr.SelectedRowIndex;
        Assert.True(mgr.MoveStage2Cursor(1, 0));
        Assert.True(mgr.MoveStage2Cursor(0, 1));
        Assert.Equal(startingColumn + 1, mgr.SelectedColumnIndex);
        Assert.Equal(startingRow + 1, mgr.SelectedRowIndex);

        Assert.False(mgr.ProcessKey('E'));
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.Equal(2, mgr.History.Count);
        Assert.NotNull(mgr.TargetCoordinate);

        // The confirmation completes selection; further input is ignored.
        Assert.False(mgr.ProcessEnter());
        Assert.False(mgr.ProcessBackspace());
        Assert.False(mgr.ProcessKey('Q'));
        Assert.False(mgr.MoveStage2Cursor(1, 0));
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void TryGetStage2Target_ResolvesWithoutChangingSelectionState()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);

        Assert.False(mgr.TryGetStage2Target(out _));
        Assert.True(mgr.ProcessKey('Q'));
        Assert.False(mgr.TryGetStage2Target(out _));

        Assert.True(mgr.ProcessKey('I'));
        Assert.True(mgr.MoveStage2Cursor(1, 0));
        Assert.True(mgr.TryGetStage2Target(out var previewTarget));
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Null(mgr.TargetCoordinate);

        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.Equal(previewTarget, mgr.TargetCoordinate);
    }

    [Fact]
    public void Stage2Cursor_RejectsInvalidDirectionsAndStopsAtGridEdges()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('I'));

        int initialColumn = mgr.SelectedColumnIndex;
        int initialRow = mgr.SelectedRowIndex;
        Assert.False(mgr.MoveStage2Cursor(0, 0));
        Assert.False(mgr.MoveStage2Cursor(1, 1));
        Assert.False(mgr.MoveStage2Cursor(-1, -1));
        Assert.Equal(initialColumn, mgr.SelectedColumnIndex);
        Assert.Equal(initialRow, mgr.SelectedRowIndex);

        // The context window extends past the monitor, so movement stops at the
        // edge of the monitor area.
        while (mgr.MoveStage2Cursor(-1, 0)) { }
        Assert.Equal(9, mgr.SelectedColumnIndex);
        Assert.False(mgr.MoveStage2Cursor(-1, 0));

        while (mgr.MoveStage2Cursor(0, -1)) { }
        Assert.Equal(9, mgr.SelectedRowIndex);
        Assert.False(mgr.MoveStage2Cursor(0, -1));
    }

    [Fact]
    public void Stage2Context_AllowsTargetsOutsideTheStage1Cell()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('I'));

        var focusBox = PixelRect.BoundingBox(mgr.FocusSelection);
        Assert.Equal(new PixelRect(0, 0, 113, 120), focusBox);

        Assert.True(mgr.TryGetStage2Target(out var focusTarget));
        Assert.True(focusBox.Contains(focusTarget));

        // Move right until target coordinate is outside focusBox
        while (mgr.TryGetStage2Target(out var target) && focusBox.Contains(target))
        {
            Assert.True(mgr.MoveStage2Cursor(1, 0));
        }

        Assert.True(mgr.TryGetStage2Target(out var contextTarget));
        Assert.True(contextTarget.X >= focusBox.Right);
        Assert.False(focusBox.Contains(contextTarget));

        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.NotNull(mgr.TargetCoordinate);
        Assert.True(mgr.TargetCoordinate.Value.X >= focusBox.Right);
    }

    [Fact]
    public void CompleteClick_ReturnsToStandbyAndClearsSelection()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('I'));
        Assert.True(mgr.ProcessEnter());

        mgr.CompleteClick();

        Assert.Equal(SelectionState.Standby, mgr.State);
        Assert.Empty(mgr.History);
        Assert.Empty(mgr.XBoundaries);
        Assert.Empty(mgr.YBoundaries);
        Assert.Empty(mgr.FocusSelection);
        Assert.True(mgr.Stage2Window.IsEmpty);
        Assert.Null(mgr.TargetCoordinate);
        Assert.Empty(mgr.ColumnKeys);
        Assert.Empty(mgr.RowKeys);
    }

    [Fact]
    public void Stage2_UsesCenteredFixed20DipGrid()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);

        Assert.Equal(17, mgr.ColumnCount);
        Assert.Equal(9, mgr.RowCount);

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('I'));

        Assert.Equal(SelectionManager.Stage2ColumnCount, mgr.ColumnCount);
        Assert.Equal(SelectionManager.Stage2RowCount, mgr.RowCount);
        Assert.Equal(26, mgr.XBoundaries.Length);
        Assert.Equal(26, mgr.YBoundaries.Length);
        Assert.Equal(new PixelRect(-194, -191, 500, 500), mgr.Stage2Window);
        Assert.Equal(12, mgr.SelectedColumnIndex);
        Assert.Equal(12, mgr.SelectedRowIndex);

        // Every cell in Stage 2 has exact width 20 and height 20
        for (int i = 0; i < 25; i++)
        {
            Assert.Equal(20, mgr.XBoundaries[i + 1] - mgr.XBoundaries[i]);
            Assert.Equal(20, mgr.YBoundaries[i + 1] - mgr.YBoundaries[i]);
        }

        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.Equal(20, mgr.CurrentBoundingBox.Width);
        Assert.Equal(20, mgr.CurrentBoundingBox.Height);
    }

    [Fact]
    public void PrematureEnter_OnlyAllowedAfterStage1()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);

        // Enter is ignored until Stage 1 has selected a region.
        Assert.False(mgr.ProcessEnter());
        Assert.Equal(SelectionState.Selecting, mgr.State);

        // Step 1: Column 'Q'
        mgr.ProcessKey('Q');
        Assert.False(mgr.ProcessEnter());
        Assert.Equal(SelectionState.Selecting, mgr.State);

        // Stage 2 starts with a centered cell ready to confirm or navigate.
        mgr.ProcessKey('I');
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.NotNull(mgr.TargetCoordinate);
    }

    [Fact]
    public void Backspace_RecalculatesSelectionCorrectlyAtEachStep()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);

        // Select a Stage 1 cell, then move the Stage 2 cursor.
        mgr.ProcessKey('Q');
        mgr.ProcessKey('I');
        Assert.True(mgr.MoveStage2Cursor(1, 0));

        // Backspace returns from Stage 2 to the preceding Stage 1 key.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Single(mgr.History);
        Assert.Equal(17, mgr.ColumnCount);
        Assert.Equal(9, mgr.RowCount);
        Assert.Equal(18, mgr.XBoundaries.Length);
        Assert.Equal(10, mgr.YBoundaries.Length);
        Assert.Equal("QWERTASDFGZXCVBYU", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("IOPHJKLNM", new string(mgr.RowKeys.ToArray()));
        Assert.Empty(mgr.FocusSelection);
        Assert.True(mgr.Stage2Window.IsEmpty);

        // Backspace removes the first Stage 1 key.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Empty(mgr.History);
        Assert.Equal(-1, mgr.SelectedColumnIndex);
        Assert.Equal(18, mgr.XBoundaries.Length);
        Assert.Equal(10, mgr.YBoundaries.Length);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), mgr.CurrentBoundingBox);

        // Backspace on empty Stage 1 history does nothing.
        Assert.False(mgr.ProcessBackspace());
    }

    [Fact]
    public void Esc_CancelsSelectionToStandby()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);
        mgr.ProcessKey('Q');

        mgr.Cancel();
        Assert.Equal(SelectionState.Standby, mgr.State);
        Assert.Empty(mgr.History);
        Assert.Empty(mgr.ColumnKeys);
        Assert.Empty(mgr.RowKeys);
        Assert.Empty(mgr.XBoundaries);
        Assert.Empty(mgr.YBoundaries);
    }

    [Fact]
    public void KeySets_AreDisjointAndRejectCrossSetKeys()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);

        Assert.Equal("QWERTASDFGZXCVBYU", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("IOPHJKLNM", new string(mgr.RowKeys.ToArray()));

        // Q is a column key, not a row key.
        Assert.True(mgr.ProcessKey('q'));
        Assert.False(mgr.ProcessKey('Q'));
        Assert.Single(mgr.History);

        // I is a row key, not a column key.
        Assert.True(mgr.ProcessKey('I'));
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(SelectionState.Selecting, mgr.State);

        // Stage 2 ignores letters and uses arrows plus Enter.
        Assert.False(mgr.ProcessKey('E'));
        Assert.True(mgr.MoveStage2Cursor(1, 0));
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void OverflowGrid_Stage0ThenColumnBorrowsRightBlockKeys()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { new PixelRect(0, 0, 7680, 2160) }, 1.0);

        // 7680x2160 needs a Stage 0 coarse grid (4x4) before the fine grid.
        Assert.True(mgr.Stage0Enabled);
        Assert.Equal(4, mgr.ColumnCount);
        Assert.Equal(4, mgr.RowCount);
        Assert.Equal("QWER", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("YUIO", new string(mgr.RowKeys.ToArray()));

        // Stage 0 column + row select the top-left 1920x540 region.
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('Y'));
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(1, mgr.CurrentStage);

        // Inside the region the fine Stage 1 grid is 17x5 and borrows right-block keys.
        Assert.Equal(17, mgr.ColumnCount);
        Assert.Equal(5, mgr.RowCount);
        Assert.Equal("QWERTASDFGZXCVBYU", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("IOPHJ", new string(mgr.RowKeys.ToArray()));

        // U is a borrowed column key; J is a row key.
        Assert.True(mgr.ProcessKey('U'));
        Assert.True(mgr.ProcessKey('J'));
        Assert.Equal(4, mgr.History.Count);
        Assert.Equal(2, mgr.CurrentStage);

        // Backspace must replay through Stage 0 and restore the fine allocation.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(3, mgr.History.Count);
        Assert.Equal("QWERTASDFGZXCVBYU", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("IOPHJ", new string(mgr.RowKeys.ToArray()));

        // Re-entering the fine row key reaches Stage 2 again.
        Assert.True(mgr.ProcessKey('J'));
        Assert.Equal(4, mgr.History.Count);
        Assert.Equal(2, mgr.CurrentStage);

        // Stage 2 uses a fixed arrow-navigated grid.
        Assert.Equal(SelectionManager.Stage2ColumnCount, mgr.ColumnCount);
        Assert.Equal(SelectionManager.Stage2RowCount, mgr.RowCount);
        Assert.False(mgr.ProcessKey('U'));
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void OverflowGrid_Stage0ThenRowBorrowsUnusedLeftBlockKeys()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { new PixelRect(0, 0, 2160, 3840) }, 1.0);

        // 2160x3840 needs a Stage 0 coarse grid (1x5) before the fine grid.
        Assert.True(mgr.Stage0Enabled);
        Assert.Equal(1, mgr.ColumnCount);
        Assert.Equal(5, mgr.RowCount);
        Assert.Equal("Q", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("YUIOP", new string(mgr.RowKeys.ToArray()));

        // Stage 0 column + row select the top 2160x768 region.
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('Y'));
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(1, mgr.CurrentStage);

        // Inside the region the fine Stage 1 grid is 19x7 and borrows left-block keys.
        Assert.Equal(19, mgr.ColumnCount);
        Assert.Equal(7, mgr.RowCount);
        Assert.Equal("QWERTASDFGZXCVBYUIO", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("PHJKLNM", new string(mgr.RowKeys.ToArray()));

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('P'));
        Assert.Equal(4, mgr.History.Count);
        Assert.Equal(2, mgr.CurrentStage);

        // The second-stage grid is navigated with arrows and confirmed with Enter.
        Assert.Equal(SelectionManager.Stage2ColumnCount, mgr.ColumnCount);
        Assert.Equal(SelectionManager.Stage2RowCount, mgr.RowCount);
        Assert.False(mgr.ProcessKey('P'));
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void Stage0_LargeMonitor_RequiresFourKeystrokes()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { new PixelRect(0, 0, 2560, 1440) }, 1.0);

        Assert.True(mgr.Stage0Enabled);
        Assert.Equal(2, mgr.ColumnCount);
        Assert.Equal(1, mgr.RowCount);
        Assert.Equal("QW", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("Y", new string(mgr.RowKeys.ToArray()));

        // Stage 0 column + row narrow to the left half (1280x1440).
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('Y'));
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(1, mgr.CurrentStage);
        Assert.Equal(11, mgr.ColumnCount);
        Assert.Equal(13, mgr.RowCount);

        // Fine Stage 1 column + row reach Stage 2 on the fourth keystroke.
        Assert.True(mgr.ProcessKey(mgr.ColumnKeys[0]));
        Assert.Equal(3, mgr.History.Count);
        Assert.Equal(1, mgr.CurrentStage);
        Assert.True(mgr.ProcessKey(mgr.RowKeys[0]));
        Assert.Equal(4, mgr.History.Count);
        Assert.Equal(2, mgr.CurrentStage);
        Assert.Equal(SelectionManager.Stage2ColumnCount, mgr.ColumnCount);

        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.NotNull(mgr.TargetCoordinate);
    }

    [Fact]
    public void Stage0_BackspaceReplaysThroughBothKeyEntryGrids()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { new PixelRect(0, 0, 2560, 1440) }, 1.0);

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('Y'));
        Assert.True(mgr.ProcessKey(mgr.ColumnKeys[0]));
        Assert.True(mgr.ProcessKey(mgr.RowKeys[0]));
        Assert.Equal(2, mgr.CurrentStage);

        // Backspace returns to the fine Stage 1 grid with three keys replayed.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(3, mgr.History.Count);
        Assert.Equal(1, mgr.CurrentStage);
        Assert.Equal(11, mgr.ColumnCount);
        Assert.Equal(13, mgr.RowCount);

        // Backspace to the Stage 0 boundary replays the coarse pair, which
        // lands on the fine Stage 1 grid with an empty fine history.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(1, mgr.CurrentStage);
        Assert.Equal(11, mgr.ColumnCount);
        Assert.Equal(13, mgr.RowCount);

        Assert.True(mgr.ProcessBackspace());
        Assert.Single(mgr.History);
        Assert.Equal("QW", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("Y", new string(mgr.RowKeys.ToArray()));

        Assert.True(mgr.ProcessBackspace());
        Assert.Empty(mgr.History);
        Assert.True(mgr.Stage0Enabled);
    }

    [Fact]
    public void Stage0_IsDecidedPerMonitor()
    {
        var small = new PixelRect(0, 0, 1920, 1080);
        var large = new PixelRect(1920, 0, 2560, 1440);
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { small, large }, small, 1.0);

        Assert.False(mgr.Stage0Enabled);

        // Switching to the large monitor enables Stage 0 for that grid only.
        Assert.True(mgr.SwitchActiveMonitor(large, 1.0));
        Assert.True(mgr.Stage0Enabled);
        Assert.Empty(mgr.History);

        Assert.True(mgr.SwitchActiveMonitor(small, 1.0));
        Assert.False(mgr.Stage0Enabled);
        Assert.Equal(17, mgr.ColumnCount);
        Assert.Equal(9, mgr.RowCount);
    }

    [Fact]
    public void OutOfBoundsKey_IsIgnored()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 1.0);

        // I is a row key but not a column key.
        Assert.False(mgr.ProcessKey('I'));
        Assert.Empty(mgr.History);
    }
}
