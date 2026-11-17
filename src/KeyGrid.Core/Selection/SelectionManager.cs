using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Selection;

/// <summary>
/// Manages the selection lifecycle, state machine, keystroke history,
/// and region recalculation according to spec.md Section 8 and Section 13.
/// </summary>
public class SelectionManager
{
    public const int Stage2ColumnCount = 5;
    public const int Stage2RowCount = 5;

    private readonly List<PixelRect> _initialMonitors = new();
    private readonly List<char> _history = new();
    private KeyAllocation? _keyAllocation;

    public SelectionState State { get; private set; } = SelectionState.Standby;

    public IReadOnlyList<char> History => _history;

    public IReadOnlyList<PixelRect> CurrentSelection { get; private set; } = Array.Empty<PixelRect>();

    public PixelRect CurrentBoundingBox { get; private set; } = PixelRect.Empty;

    public PixelRect VirtualScreenBoundingBox { get; private set; } = PixelRect.Empty;

    public int ColumnCount { get; private set; } = 1;

    public int RowCount { get; private set; } = 1;

    /// <summary>
    /// Disjoint column keys for the current session, in column-index order.
    /// </summary>
    public IReadOnlyList<char> ColumnKeys => _keyAllocation?.ColumnKeys ?? Array.Empty<char>();

    /// <summary>
    /// Disjoint row keys for the current session, in row-index order.
    /// </summary>
    public IReadOnlyList<char> RowKeys => _keyAllocation?.RowKeys ?? Array.Empty<char>();

    public int[] XBoundaries { get; private set; } = Array.Empty<int>();

    public int[] YBoundaries { get; private set; } = Array.Empty<int>();

    public int SelectedColumnIndex { get; private set; } = -1;

    public int SelectedRowIndex { get; private set; } = -1;

    public PixelPoint? TargetCoordinate { get; private set; }

    /// <summary>
    /// Current stage: 1 or 2 (or 0 when standby).
    /// </summary>
    public int CurrentStage => State == SelectionState.Standby ? 0 : (_history.Count < 2 ? 1 : 2);

    /// <summary>
    /// Initiates selection mode with the provided monitor display bounds.
    /// </summary>
    /// <param name="monitorRects">Active monitor bounds in physical pixels.</param>
    public bool StartSelection(IReadOnlyList<PixelRect> monitorRects)
    {
        var validRects = monitorRects.Where(r => !r.IsEmpty).ToList();
        if (validRects.Count == 0)
        {
            return false;
        }

        _initialMonitors.Clear();
        _initialMonitors.AddRange(validRects);
        _history.Clear();
        VirtualScreenBoundingBox = PixelRect.BoundingBox(_initialMonitors);
        CurrentSelection = _initialMonitors.ToList();
        CurrentBoundingBox = VirtualScreenBoundingBox;

        SetupStage1Divisions();

        SelectedColumnIndex = -1;
        SelectedRowIndex = -1;
        TargetCoordinate = null;
        State = SelectionState.Selecting;

        return true;
    }

    private void SetupStage1Divisions()
    {
        var (cols, rows) = GridCalculator.CalculateFirstStageDivisions(
            CurrentBoundingBox.Width,
            CurrentBoundingBox.Height,
            Stage2ColumnCount,
            Stage2RowCount);

        ColumnCount = cols;
        RowCount = rows;
        _keyAllocation = KeyLayout.Allocate(ColumnCount, RowCount);
        CalculateBoundaries();
    }

    /// <summary>
    /// Stage 2 uses a fixed 5x5 grid and starts on the valid cell nearest its center.
    /// </summary>
    private void SetupStage2Divisions()
    {
        ColumnCount = Stage2ColumnCount;
        RowCount = Stage2RowCount;
        CalculateBoundaries();
        SelectNearestValidStage2Cell();
    }

    private void SelectNearestValidStage2Cell()
    {
        int centerColumn = ColumnCount / 2;
        int centerRow = RowCount / 2;
        int bestDistance = int.MaxValue;
        int bestColumn = -1;
        int bestRow = -1;

        for (int row = 0; row < RowCount; row++)
        {
            for (int column = 0; column < ColumnCount; column++)
            {
                if (!GridCalculator.IsCellValid(column, row, XBoundaries, YBoundaries, CurrentSelection))
                {
                    continue;
                }

                int distance = Math.Abs(column - centerColumn) + Math.Abs(row - centerRow);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestColumn = column;
                    bestRow = row;
                }
            }
        }

        if (bestColumn < 0 || bestRow < 0)
        {
            throw new InvalidOperationException("Stage 2 grid has no valid cell.");
        }

        SelectedColumnIndex = bestColumn;
        SelectedRowIndex = bestRow;
    }

    private void CalculateBoundaries()
    {
        XBoundaries = GridCalculator.CalculateBoundaries(CurrentBoundingBox.Left, CurrentBoundingBox.Width, ColumnCount);
        YBoundaries = GridCalculator.CalculateBoundaries(CurrentBoundingBox.Top, CurrentBoundingBox.Height, RowCount);
    }

    /// <summary>
    /// Processes a Stage 1 character key according to the column or row key set.
    /// </summary>
    public bool ProcessKey(char key)
    {
        if (State != SelectionState.Selecting
            || _history.Count >= 2
            || _keyAllocation is not { } allocation)
        {
            return false;
        }

        // Even count (0 or 2): Column key
        if (_history.Count % 2 == 0)
        {
            if (!allocation.TryGetColumnIndex(key, out int index))
            {
                return false;
            }

            if (!GridCalculator.IsColumnValid(index, XBoundaries, CurrentBoundingBox, CurrentSelection))
            {
                return false;
            }

            SelectedColumnIndex = index;
            SelectedRowIndex = -1;
            _history.Add(char.ToUpperInvariant(key));
            return true;
        }
        else // Odd count (1 or 3): Row key
        {
            if (!allocation.TryGetRowIndex(key, out int index))
            {
                return false;
            }

            if (!GridCalculator.IsCellValid(SelectedColumnIndex, index, XBoundaries, YBoundaries, CurrentSelection))
            {
                return false;
            }

            SelectedRowIndex = index;
            _history.Add(char.ToUpperInvariant(key));

            // The second Stage 1 key selects the region to refine in Stage 2.
            var selectedCell = GridCalculator.GetCell(XBoundaries, YBoundaries, SelectedColumnIndex, SelectedRowIndex);
            CurrentSelection = GridCalculator.IntersectCellWithSelection(selectedCell, CurrentSelection);
            CurrentBoundingBox = PixelRect.BoundingBox(CurrentSelection);

            if (_history.Count == 2)
            {
                SetupStage2Divisions();
            }

            return true;
        }
    }

    /// <summary>
    /// Moves the Stage 2 selection to the next valid cell in the requested direction.
    /// Invalid cells are skipped so monitor gaps do not block navigation.
    /// </summary>
    public bool MoveStage2Cursor(int columnDelta, int rowDelta)
    {
        if (State != SelectionState.Selecting
            || _history.Count != 2
            || Math.Abs(columnDelta) + Math.Abs(rowDelta) != 1)
        {
            return false;
        }

        int column = SelectedColumnIndex + columnDelta;
        int row = SelectedRowIndex + rowDelta;
        while (column >= 0
            && column < ColumnCount
            && row >= 0
            && row < RowCount)
        {
            if (GridCalculator.IsCellValid(column, row, XBoundaries, YBoundaries, CurrentSelection))
            {
                SelectedColumnIndex = column;
                SelectedRowIndex = row;
                return true;
            }

            column += columnDelta;
            row += rowDelta;
        }

        return false;
    }

    /// <summary>
    /// Resolves the Stage 2 cursor cell to a click coordinate without changing the
    /// selection state. Range and scroll modes use this to act on the current target.
    /// </summary>
    public bool TryGetStage2Target(out PixelPoint target)
    {
        target = default;
        if (State != SelectionState.Selecting
            || _history.Count != 2
            || SelectedColumnIndex < 0
            || SelectedRowIndex < 0)
        {
            return false;
        }

        var selectedCell = GridCalculator.GetCell(
            XBoundaries,
            YBoundaries,
            SelectedColumnIndex,
            SelectedRowIndex);
        var selectedArea = GridCalculator.IntersectCellWithSelection(selectedCell, CurrentSelection);
        var boundingBox = PixelRect.BoundingBox(selectedArea);
        if (boundingBox.IsEmpty || selectedArea.Count == 0)
        {
            return false;
        }

        target = GridCalculator.ResolveTargetCoordinate(boundingBox, selectedArea);
        return true;
    }

    /// <summary>
    /// Confirms the Stage 2 cursor position and resolves its click coordinate.
    /// </summary>
    public bool ProcessEnter()
    {
        if (State == SelectionState.Selecting
            && _history.Count == 2
            && SelectedColumnIndex >= 0
            && SelectedRowIndex >= 0)
        {
            var selectedCell = GridCalculator.GetCell(
                XBoundaries,
                YBoundaries,
                SelectedColumnIndex,
                SelectedRowIndex);
            CurrentSelection = GridCalculator.IntersectCellWithSelection(selectedCell, CurrentSelection);
            CurrentBoundingBox = PixelRect.BoundingBox(CurrentSelection);
            TargetCoordinate = GridCalculator.ResolveTargetCoordinate(CurrentBoundingBox, CurrentSelection);
            State = SelectionState.ExecutingClick;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Processes Backspace key.
    /// Pops the last keystroke from history and recalculates the selection state from scratch.
    /// </summary>
    public bool ProcessBackspace()
    {
        if (State != SelectionState.Selecting)
        {
            return false;
        }

        if (_history.Count == 0)
        {
            return false;
        }

        // Copy history and remove last key
        var replayKeys = _history.Take(_history.Count - 1).ToList();

        // Reset to initial state
        _history.Clear();
        CurrentSelection = _initialMonitors.ToList();
        CurrentBoundingBox = VirtualScreenBoundingBox;
        SetupStage1Divisions();
        SelectedColumnIndex = -1;
        SelectedRowIndex = -1;
        TargetCoordinate = null;
        State = SelectionState.Selecting;

        // Replay history
        foreach (var key in replayKeys)
        {
            ProcessKey(key);
        }

        return true;
    }

    /// <summary>
    /// Cancels selection and returns to Standby.
    /// </summary>
    public void Cancel()
    {
        State = SelectionState.Standby;
        _history.Clear();
        _initialMonitors.Clear();
        CurrentSelection = Array.Empty<PixelRect>();
        CurrentBoundingBox = PixelRect.Empty;
        VirtualScreenBoundingBox = PixelRect.Empty;
        ColumnCount = 1;
        RowCount = 1;
        XBoundaries = Array.Empty<int>();
        YBoundaries = Array.Empty<int>();
        SelectedColumnIndex = -1;
        SelectedRowIndex = -1;
        TargetCoordinate = null;
        _keyAllocation = null;
    }

    /// <summary>
    /// Marks click execution completed and returns to Standby.
    /// </summary>
    public void CompleteClick()
    {
        Cancel();
    }
}
