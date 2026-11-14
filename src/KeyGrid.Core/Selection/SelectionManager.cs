using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;

namespace KeyGrid.Core.Selection;

/// <summary>
/// Manages the selection lifecycle, state machine, keystroke history,
/// and region recalculation according to spec.md Section 8 and Section 13.
/// </summary>
public class SelectionManager
{
    private readonly List<PixelRect> _initialMonitors = new();
    private readonly List<char> _history = new();

    public SelectionState State { get; private set; } = SelectionState.Standby;

    public IReadOnlyList<char> History => _history;

    public IReadOnlyList<PixelRect> CurrentSelection { get; private set; } = Array.Empty<PixelRect>();

    public PixelRect CurrentBoundingBox { get; private set; } = PixelRect.Empty;

    public PixelRect VirtualScreenBoundingBox { get; private set; } = PixelRect.Empty;

    public int ColumnCount { get; private set; } = 1;

    public int RowCount { get; private set; } = 1;

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
        var (cols, rows) = GridCalculator.CalculateDivisions(CurrentBoundingBox.Width, CurrentBoundingBox.Height);
        ColumnCount = cols;
        RowCount = rows;
        XBoundaries = GridCalculator.CalculateBoundaries(CurrentBoundingBox.Left, CurrentBoundingBox.Width, ColumnCount);
        YBoundaries = GridCalculator.CalculateBoundaries(CurrentBoundingBox.Top, CurrentBoundingBox.Height, RowCount);
    }

    private void SetupStage2Divisions()
    {
        var (cols, rows) = GridCalculator.CalculateDivisions(CurrentBoundingBox.Width, CurrentBoundingBox.Height);
        ColumnCount = cols;
        RowCount = rows;
        XBoundaries = GridCalculator.CalculateBoundaries(CurrentBoundingBox.Left, CurrentBoundingBox.Width, ColumnCount);
        YBoundaries = GridCalculator.CalculateBoundaries(CurrentBoundingBox.Top, CurrentBoundingBox.Height, RowCount);
    }

    /// <summary>
    /// Processes a character key (Q..M) according to the current selection state.
    /// </summary>
    public bool ProcessKey(char key)
    {
        if (State != SelectionState.Selecting)
        {
            return false;
        }

        if (!KeyLayout.TryGetIndex(key, out int index))
        {
            return false;
        }

        // Even count (0 or 2): Column key
        if (_history.Count % 2 == 0)
        {
            if (index < 0 || index >= ColumnCount)
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
            if (index < 0 || index >= RowCount)
            {
                return false;
            }

            if (!GridCalculator.IsCellValid(SelectedColumnIndex, index, XBoundaries, YBoundaries, CurrentSelection))
            {
                return false;
            }

            SelectedRowIndex = index;
            _history.Add(char.ToUpperInvariant(key));

            // Cell confirmed for this stage
            var selectedCell = GridCalculator.GetCell(XBoundaries, YBoundaries, SelectedColumnIndex, SelectedRowIndex);
            CurrentSelection = GridCalculator.IntersectCellWithSelection(selectedCell, CurrentSelection);
            CurrentBoundingBox = PixelRect.BoundingBox(CurrentSelection);

            if (_history.Count == 2)
            {
                // Stage 1 completed, initialize Stage 2
                SetupStage2Divisions();
                SelectedColumnIndex = -1;
                SelectedRowIndex = -1;
            }
            else if (_history.Count == 4)
            {
                // Stage 2 completed. Resolve the target and start the click immediately;
                // there is no separate confirmation step.
                TargetCoordinate = GridCalculator.ResolveTargetCoordinate(CurrentBoundingBox, CurrentSelection);
                State = SelectionState.ExecutingClick;
            }

            return true;
        }
    }

    /// <summary>
    /// Processes Enter key for the early click after stage 1 (_history.Count == 2).
    /// Stage 2 clicks automatically on its final keystroke, so Enter is not needed there.
    /// </summary>
    public bool ProcessEnter()
    {
        if (State == SelectionState.Selecting && _history.Count == 2)
        {
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
        SelectedColumnIndex = -1;
        SelectedRowIndex = -1;
        TargetCoordinate = null;
    }

    /// <summary>
    /// Marks click execution completed and returns to Standby.
    /// </summary>
    public void CompleteClick()
    {
        Cancel();
    }
}
