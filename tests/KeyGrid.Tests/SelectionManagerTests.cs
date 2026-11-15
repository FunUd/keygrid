using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using Xunit;

namespace KeyGrid.Tests;

public class SelectionManagerTests
{
    private const double Target20Pixels = 20.0;
    private readonly PixelRect[] _single1080pMonitor = new[] { new PixelRect(0, 0, 1920, 1080) };

    [Fact]
    public void StartSelection_InitializesStateAndDivisions()
    {
        var mgr = new SelectionManager();
        bool started = mgr.StartSelection(_single1080pMonitor, Target20Pixels);

        Assert.True(started);
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(1, mgr.CurrentStage);
        Assert.Equal(10, mgr.ColumnCount);
        Assert.Equal(8, mgr.RowCount);
        Assert.Equal(Target20Pixels, mgr.TargetPixels);
        Assert.Empty(mgr.History);
        Assert.Equal(-1, mgr.SelectedColumnIndex);
        Assert.Equal(-1, mgr.SelectedRowIndex);
    }

    [Fact]
    public void StartSelection_RestartsFromTheBeginning()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, Target20Pixels);
        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('Y'));
        Assert.True(mgr.ProcessKey('E'));

        bool restarted = mgr.StartSelection(_single1080pMonitor, Target20Pixels);

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

        bool started = mgr.StartSelection(Array.Empty<PixelRect>(), Target20Pixels);

        Assert.False(started);
        Assert.Equal(SelectionState.Standby, mgr.State);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void StartSelection_InvalidTarget_Throws(double targetPixels)
    {
        var mgr = new SelectionManager();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => mgr.StartSelection(_single1080pMonitor, targetPixels));
    }

    [Fact]
    public void TargetPixels_DriveDivisionCounts()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, 40.0);

        Assert.Equal(7, mgr.ColumnCount);
        Assert.Equal(6, mgr.RowCount);
    }

    [Fact]
    public void TwoStageKeystrokes_ExecutesClickImmediatelyAfterFourthKey()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, Target20Pixels);

        // Stage 1: Column 'Q' (index 0)
        Assert.True(mgr.ProcessKey('Q'));
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(0, mgr.SelectedColumnIndex);
        Assert.Single(mgr.History);

        // Stage 1: Row 'U' (index 1)
        Assert.True(mgr.ProcessKey('U'));
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(2, mgr.CurrentStage);
        Assert.Equal(2, mgr.History.Count);

        // Stage 2: Column 'E' (index 2)
        Assert.True(mgr.ProcessKey('E'));
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(2, mgr.SelectedColumnIndex);
        Assert.Equal(3, mgr.History.Count);

        // Stage 2: Row 'O' (index 3) resolves the target and starts the click immediately
        Assert.True(mgr.ProcessKey('O'));
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.Equal(4, mgr.History.Count);
        Assert.NotNull(mgr.TargetCoordinate);

        // There is no confirmation state: Enter and Backspace are no longer accepted
        Assert.False(mgr.ProcessEnter());
        Assert.False(mgr.ProcessBackspace());
        Assert.False(mgr.ProcessKey('Q'));
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void Stage2_ReusesStage1CountsAndFinalCellMeetsTarget()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, Target20Pixels);

        Assert.Equal(10, mgr.ColumnCount);
        Assert.Equal(8, mgr.RowCount);

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('Y'));

        // Entering Stage 2 must not choose new counts.
        Assert.Equal(10, mgr.ColumnCount);
        Assert.Equal(8, mgr.RowCount);
        Assert.Equal(11, mgr.XBoundaries.Length);
        Assert.Equal(9, mgr.YBoundaries.Length);

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('Y'));

        Assert.Equal(10, mgr.ColumnCount);
        Assert.Equal(8, mgr.RowCount);
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);

        // The first 1920x1080 cell is 192x135. With same-count Stage 2,
        // the final cell is ceil(192/10) x ceil(135/8) = 20x17.
        Assert.Equal(20, mgr.CurrentBoundingBox.Width);
        Assert.Equal(17, mgr.CurrentBoundingBox.Height);
        Assert.True(mgr.CurrentBoundingBox.Width <= Target20Pixels);
        Assert.True(mgr.CurrentBoundingBox.Height <= Target20Pixels);
    }

    [Fact]
    public void PrematureEnter_OnlyAllowedAfterStage1()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, Target20Pixels);

        // Step 0: Enter ignored
        Assert.False(mgr.ProcessEnter());
        Assert.Equal(SelectionState.Selecting, mgr.State);

        // Step 1: Column 'Q'
        mgr.ProcessKey('Q');
        Assert.False(mgr.ProcessEnter());
        Assert.Equal(SelectionState.Selecting, mgr.State);

        // Step 2: Row 'Y' (Stage 1 complete)
        mgr.ProcessKey('Y');
        Assert.True(mgr.ProcessEnter());
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
        Assert.NotNull(mgr.TargetCoordinate);
    }

    [Fact]
    public void Backspace_RecalculatesSelectionCorrectlyAtEachStep()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, Target20Pixels);

        // Type 3 keys: Q, Y, E (stage 2 column selected, click not yet executed)
        mgr.ProcessKey('Q');
        mgr.ProcessKey('Y');
        mgr.ProcessKey('E');
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(2, mgr.SelectedColumnIndex);

        // Backspace from 3 -> returns to history 2: Q, W (Stage 1 completed)
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(-1, mgr.SelectedColumnIndex);
        Assert.Equal(10, mgr.ColumnCount);
        Assert.Equal(8, mgr.RowCount);
        Assert.Equal(11, mgr.XBoundaries.Length);
        Assert.Equal(9, mgr.YBoundaries.Length);
        Assert.Equal(192, mgr.CurrentBoundingBox.Width);
        Assert.Equal(135, mgr.CurrentBoundingBox.Height);
        Assert.Equal("QWERTASDFG", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("YUIOPHJK", new string(mgr.RowKeys.ToArray()));

        // Backspace from 2 -> returns to history 1: Q
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Single(mgr.History);
        Assert.Equal(0, mgr.SelectedColumnIndex);
        Assert.Equal(11, mgr.XBoundaries.Length);
        Assert.Equal(9, mgr.YBoundaries.Length);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), mgr.CurrentBoundingBox);

        // Backspace from 1 -> returns to history 0
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal(SelectionState.Selecting, mgr.State);
        Assert.Empty(mgr.History);
        Assert.Equal(-1, mgr.SelectedColumnIndex);
        Assert.Equal(11, mgr.XBoundaries.Length);
        Assert.Equal(9, mgr.YBoundaries.Length);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), mgr.CurrentBoundingBox);

        // Backspace on empty history -> does nothing
        Assert.False(mgr.ProcessBackspace());
    }

    [Fact]
    public void Esc_CancelsSelectionToStandby()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, Target20Pixels);
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
        mgr.StartSelection(_single1080pMonitor, Target20Pixels);

        Assert.Equal("QWERTASDFG", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("YUIOPHJK", new string(mgr.RowKeys.ToArray()));

        // Q is a column key, not a row key.
        Assert.True(mgr.ProcessKey('q'));
        Assert.False(mgr.ProcessKey('Q'));
        Assert.Single(mgr.History);

        // Y is a row key, not a column key.
        Assert.True(mgr.ProcessKey('Y'));
        Assert.Equal(2, mgr.History.Count);
        Assert.Equal(SelectionState.Selecting, mgr.State);

        // Stage 2 uses the same sets.
        Assert.False(mgr.ProcessKey('Y'));
        Assert.True(mgr.ProcessKey('E'));
        Assert.False(mgr.ProcessKey('E'));
        Assert.True(mgr.ProcessKey('O'));
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void OverflowGrid_ColumnBorrowsRightBlockKeys()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { new PixelRect(0, 0, 7680, 2160) }, Target20Pixels);

        Assert.Equal(17, mgr.ColumnCount);
        Assert.Equal(9, mgr.RowCount);
        Assert.Equal("QWERTASDFGZXCVBYU", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("IOPHJKLNM", new string(mgr.RowKeys.ToArray()));

        // U is a borrowed column key; I is a row key.
        Assert.True(mgr.ProcessKey('U'));
        Assert.True(mgr.ProcessKey('I'));
        Assert.Equal(2, mgr.CurrentStage);

        // Backspace must replay the same borrowed allocation.
        Assert.True(mgr.ProcessBackspace());
        Assert.Equal("QWERTASDFGZXCVBYU", new string(mgr.ColumnKeys.ToArray()));
        Assert.True(mgr.ProcessKey('I'));

        // Stage 2 reuses both sets; U is not a row key.
        Assert.False(mgr.ProcessKey('I'));
        Assert.True(mgr.ProcessKey('U'));
        Assert.False(mgr.ProcessKey('U'));
        Assert.True(mgr.ProcessKey('M'));
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void OverflowGrid_RowBorrowsUnusedLeftBlockKeys()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(new[] { new PixelRect(0, 0, 2160, 3840) }, Target20Pixels);

        Assert.Equal(11, mgr.ColumnCount);
        Assert.Equal(14, mgr.RowCount);
        Assert.Equal("QWERTASDFGZ", new string(mgr.ColumnKeys.ToArray()));
        Assert.Equal("YUIOPHJKLNMXCV", new string(mgr.RowKeys.ToArray()));

        Assert.True(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('V'));
        Assert.Equal(2, mgr.CurrentStage);

        // V is a borrowed row key, not a column key.
        Assert.False(mgr.ProcessKey('V'));
        Assert.True(mgr.ProcessKey('Q'));
        Assert.False(mgr.ProcessKey('Q'));
        Assert.True(mgr.ProcessKey('X'));
        Assert.Equal(SelectionState.ExecutingClick, mgr.State);
    }

    [Fact]
    public void OutOfBoundsKey_IsIgnored()
    {
        var mgr = new SelectionManager();
        mgr.StartSelection(_single1080pMonitor, Target20Pixels);

        // Y is a valid row key but not a column key.
        Assert.False(mgr.ProcessKey('Y'));
        Assert.Empty(mgr.History);
    }
}
