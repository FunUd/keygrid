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
        bool started = mgr.StartSelection(_single1080pMonitor);

        Assert.True(started);
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(1, mgr.CurrentStage);
        Assert.Equal(16, mgr.ColumnCount);
        Assert.Equal(10, mgr.RowCount);
        Assert.Empty(mgr.History);
        Assert.Equal(-1, mgr.SelectedColumnIndex);
        Assert.Equal(-1, mgr.SelectedRowIndex);
    }

    [Fact]
    public void StartSelection_RestartsFromTheBeginning()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('U'));
        Assert.True(mgr.MoveStage2Cursor(1, 0));

        bool restarted = mgr.StartSelection(_single1080pMonitor);

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

        bool started = mgr.StartSelection(Array.Empty<PixelRect>());

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
    public void Stage1UsesKeyBudgetToImproveFixedStage2Precision()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);

        Assert.Equal(16, mgr.ColumnCount);
        Assert.Equal(10, mgr.RowCount);
    }

    [Fact]
    public void Stage2ArrowNavigation_ExecutesClickOnEnter()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);

        // Stage 1 selects a cell using the two disjoint letter blocks.
        Assert.True(mgr.ProcessKey('Q'));
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(0, mgr.SelectedColumnIndex);
        Assert.Single(mgr.History);
        Assert.True(mgr.ProcessKey('U'));
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(2, mgr.CurrentStage);
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(5, mgr.ColumnCount);
        Assert.Equal(5, mgr.RowCount);

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
    public void Stage2Cursor_RejectsInvalidDirectionsAndStopsAtGridEdges()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('U'));

        int initialColumn = mgr.SelectedColumnIndex;
        int initialRow = mgr.SelectedRowIndex;
        Assert.False(mgr.MoveStage2Cursor(0, 0));
        Assert.False(mgr.MoveStage2Cursor(1, 1));
        Assert.False(mgr.MoveStage2Cursor(-1, -1));
        Assert.Equal(initialColumn, mgr.SelectedColumnIndex);
        Assert.Equal(initialRow, mgr.SelectedRowIndex);

        while (mgr.MoveStage2Cursor(-1, 0)) { }
        Assert.Equal(0, mgr.SelectedColumnIndex);
        Assert.False(mgr.MoveStage2Cursor(-1, 0));

        while (mgr.MoveStage2Cursor(0, -1)) { }
        Assert.Equal(0, mgr.SelectedRowIndex);
        Assert.False(mgr.MoveStage2Cursor(0, -1));
    }

    [Fact]
    public void CompleteClick_ReturnsToStandbyAndClearsSelection()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('U'));
        Assert.True(mgr.ProcessEnter());

        mgr.CompleteClick();

        Assert.Equal(SelectionState.Standby, mgr.State);
        Assert.Empty(mgr.History);
        Assert.Empty(mgr.XBoundaries);
        Assert.Empty(mgr.YBoundaries);
        Assert.Null(mgr.TargetCoordinate);
        Assert.Empty(mgr.ColumnKeys);
        Assert.Empty(mgr.RowKeys);
    }

    [Fact]
    public void Stage2_UsesFixedFiveByFiveGrid()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);

        Assert.Equal(16, mgr.ColumnCount);
        Assert.Equal(10, mgr.RowCount);

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('U'));

        Assert.Equal(5, mgr.ColumnCount);
        Assert.Equal(5, mgr.RowCount);
        Assert.Equal(6, mgr.XBoundaries.Length);
        Assert.Equal(6, mgr.YBoundaries.Length);
        Assert.InRange(mgr.SelectedColumnIndex, 0, 4);
        Assert.InRange(mgr.SelectedRowIndex, 0, 4);

        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.Equal(24, mgr.CurrentBoundingBox.Width);
        Assert.Equal(21, mgr.CurrentBoundingBox.Height);
    }

    [Fact]
    public void PrematureEnter_OnlyAllowedAfterStage1()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);

        // Enter is ignored until Stage 1 has selected a region.
        Assert.False(mgr.ProcessEnter());
        Assert.Equal(SelectionState.Selecting, mgr.State);

        // Step 1: Column 'Q'
        mgr.ProcessKey('Q');
        Assert.False(mgr.ProcessEnter());
        Assert.Equal(SelectionState.Selecting, mgr.State);

        // Stage 2 starts with a centered cell ready to confirm or navigate.
        mgr.ProcessKey('U');
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.NotNull(mgr.TargetCoordinate);
    }

    [Fact]
    public void Backspace_RecalculatesSelectionCorrectlyAtEachStep()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);

        // Select a Stage 1 cell, then move the Stage 2 cursor.
        mgr.ProcessKey('Q');
        mgr.ProcessKey('U');
        Assert.True(mgr.MoveStage2Cursor(1, 0));

        // Backspace returns from Stage 2 to the preceding Stage 1 key.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Single(mgr.History);
        Assert.Equal(16, mgr.ColumnCount);
        Assert.Equal(10, mgr.RowCount);
        Assert.Equal(17, mgr.XBoundaries.Length);
        Assert.Equal(11, mgr.YBoundaries.Length);
        Assert.Equal("QWERTASDFGZXCVBY", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("UIOPHJKLNM", new string(mgr.RowKeys.ToArray()));

        // Backspace removes the first Stage 1 key.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Empty(mgr.History);
        Assert.Equal(-1, mgr.SelectedColumnIndex);
        Assert.Equal(17, mgr.XBoundaries.Length);
        Assert.Equal(11, mgr.YBoundaries.Length);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), mgr.CurrentBoundingBox);

        // Backspace on empty Stage 1 history does nothing.
        Assert.False(mgr.ProcessBackspace());
    }

    [Fact]
    public void Esc_CancelsSelectionToStandby()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);
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
        mgr.StartSelection(_single1080pMonitor);

        Assert.Equal("QWERTASDFGZXCVBY", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("UIOPHJKLNM", new string(mgr.RowKeys.ToArray()));

        // Q is a column key, not a row key.
        Assert.True(mgr.ProcessKey('q'));
        Assert.False(mgr.ProcessKey('Q'));
        Assert.Single(mgr.History);

        // U is a row key, not a column key; Y is borrowed for columns.
        Assert.True(mgr.ProcessKey('U'));
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(SelectionState.Selecting, mgr.State);

        // Stage 2 ignores letters and uses arrows plus Enter.
        Assert.False(mgr.ProcessKey('E'));
        Assert.True(mgr.MoveStage2Cursor(1, 0));
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void OverflowGrid_ColumnBorrowsRightBlockKeys()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { new PixelRect(0, 0, 7680, 2160) });

        Assert.Equal(20, mgr.ColumnCount);
        Assert.Equal(6, mgr.RowCount);
        Assert.Equal("QWERTASDFGZXCVBYUIOP", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("HJKLNM", new string(mgr.RowKeys.ToArray()));

        // U is a borrowed column key; H is a row key.
        Assert.True(mgr.ProcessKey('U'));
        Assert.True(mgr.ProcessKey('H'));
        Assert.Equal(2, mgr.CurrentStage);

        // Backspace must replay the same borrowed allocation.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal("QWERTASDFGZXCVBYUIOP", new string(mgr.ColumnKeys.ToArray()));
        Assert.True(mgr.ProcessKey('H'));

        // Stage 2 uses a fixed arrow-navigated grid.
        Assert.Equal(5, mgr.ColumnCount);
        Assert.Equal(5, mgr.RowCount);
        Assert.False(mgr.ProcessKey('U'));
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void OverflowGrid_RowBorrowsUnusedLeftBlockKeys()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { new PixelRect(0, 0, 2160, 3840) });

        Assert.Equal(10, mgr.ColumnCount);
        Assert.Equal(16, mgr.RowCount);
        Assert.Equal("QWERTASDFG", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("YUIOPHJKLNMZXCVB", new string(mgr.RowKeys.ToArray()));

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('V'));
        Assert.Equal(2, mgr.CurrentStage);

        // The second-stage grid is navigated with arrows and confirmed with Enter.
        Assert.Equal(5, mgr.ColumnCount);
        Assert.Equal(5, mgr.RowCount);
        Assert.False(mgr.ProcessKey('V'));
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void OutOfBoundsKey_IsIgnored()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor);

        // U is a valid row key but not a column key.
        Assert.False(mgr.ProcessKey('U'));
        Assert.Empty(mgr.History);
    }
}
