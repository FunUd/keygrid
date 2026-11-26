using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;

namespace KeyGrid.UI;

/// <summary>
/// Handles high-performance hardware-accelerated drawing of grid lines,
/// selection masks, stage-1 in-cell labels, and the stage-2 focus-plus-context grid.
/// </summary>
public class OverlayRenderElement : FrameworkElement
{
    private PixelRect _monitorRect;
    private IReadOnlyList<PixelRect> _monitorRects = Array.Empty<PixelRect>();
    private SelectionManager? _selectionManager;
    private double _pixelsPerDip = 1.0;
    private bool _isPrimary;
    private bool _isContinuousMode;
    private PixelPoint? _rangeStartPoint;
    private PixelPoint? _dragStartPoint;
    private bool _isScrollMode;
    private bool _isFineMode;
    private PixelPoint? _fineTargetPoint;
    private bool _isRangeDragWorkflow;
    private string? _monitorBadgeLabel;

    private readonly Brush _dimBrush = new SolidColorBrush(Color.FromArgb(125, 10, 12, 20));
    private readonly Brush _columnHighlightBrush = new SolidColorBrush(Color.FromArgb(30, 140, 180, 225));
    private readonly Brush _cellHighlightBrush = new SolidColorBrush(Color.FromArgb(40, 160, 200, 235));
    private readonly Brush _stage2CursorBrush = new SolidColorBrush(Color.FromArgb(100, 255, 40, 40));
    // Grid lines are drawn as two one-physical-pixel lines straddling each boundary:
    // black on the left/top and white on the right/bottom. On white content only the
    // black line is visible; on black content only the white line is visible, so the
    // grid remains legible without reading the screen or changing colors dynamically.
    private readonly Brush _gridDarkBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
    private readonly Brush _gridLightBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
    private readonly Pen _bboxPen = new(new SolidColorBrush(Color.FromArgb(200, 0, 200, 255)), 2.0);
    // Stage 0/1 frame: a quiet neutral outline so the badges stay the focus.
    private readonly Pen _stage1FramePen = new(new SolidColorBrush(Color.FromArgb(190, 170, 185, 205)), 1.25);
    // The outer Stage 2 window is context; the central focus keeps the stronger pen.
    private readonly Pen _stage2WindowPen = new(new SolidColorBrush(Color.FromArgb(120, 0, 200, 255)), 1.0);

    // Stage-1 badges use a dark translucent fill so the dense label grid stays
    // legible over arbitrary content. The alpha is high enough that the pill
    // still reads as a dark surface over white backgrounds (where a weaker
    // fill would composite to mid-gray and wash out the pastel key colors),
    // while staying slightly translucent so it does not become a solid slab.
    private readonly Brush _badgeBgBrush = new SolidColorBrush(Color.FromArgb(212, 12, 14, 20));
    private readonly Pen _badgeBorderPen = new(new SolidColorBrush(Color.FromArgb(130, 180, 195, 215)), 1.0);
    private readonly Brush _activeBadgeBgBrush = new SolidColorBrush(Color.FromArgb(228, 24, 28, 38));
    private readonly Pen _activeBadgeBorderPen = new(new SolidColorBrush(Color.FromArgb(235, 220, 235, 255)), 1.0);

    private readonly Brush _columnKeyBrush = new SolidColorBrush(Color.FromRgb(140, 190, 220));
    private readonly Brush _rowKeyBrush = new SolidColorBrush(Color.FromRgb(225, 190, 130));

    // Simple status badges. They are drawn only on the primary overlay.
    private readonly Brush _statusTextBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private readonly Brush _statusBackgroundBrush = new SolidColorBrush(Color.FromArgb(225, 12, 16, 24));
    private readonly Pen _statusBorderPen = new(new SolidColorBrush(Color.FromRgb(0, 229, 255)), 1.0);

    // Target markers (range/drag anchors and the Fine Mode point) use the same
    // double-stroke trick as the grid lines: a dark silhouette is drawn under a
    // thin bright accent so the marker stays legible over arbitrary content
    // without a heavy halo. Green marks range/drag anchors; warm amber marks
    // the fine point so it stays distinct from the anchors and the red cursor.
    private readonly Pen _markerSilhouettePen = new(new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)), 3.0);
    private readonly Pen _rangeAccentPen = new(new SolidColorBrush(Color.FromRgb(80, 255, 160)), 1.25);
    private readonly Brush _rangeAccentFillBrush = new SolidColorBrush(Color.FromRgb(80, 255, 160));
    private readonly Pen _fineAccentPen = new(new SolidColorBrush(Color.FromRgb(255, 200, 80)), 1.25);
    private readonly Brush _fineAccentFillBrush = new SolidColorBrush(Color.FromRgb(255, 200, 80));

    private readonly Typeface _labelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private readonly Typeface _cellTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // Reuse glyph layouts across labels and renders. The key includes DPI because
    // FormattedText stores a pixelsPerDip value that changes with per-monitor DPI.
    private readonly Dictionary<GlyphKey, FormattedText> _glyphCache = new();
    private readonly Dictionary<GlyphKey, FormattedText> _cellGlyphCache = new();

    public OverlayRenderElement()
    {
        _dimBrush.Freeze();
        _columnHighlightBrush.Freeze();
        _cellHighlightBrush.Freeze();
        _stage2CursorBrush.Freeze();
        _gridDarkBrush.Freeze();
        _gridLightBrush.Freeze();
        _bboxPen.Freeze();
        _stage1FramePen.Freeze();
        _stage2WindowPen.Freeze();
        _badgeBgBrush.Freeze();
        _badgeBorderPen.Freeze();
        _activeBadgeBgBrush.Freeze();
        _activeBadgeBorderPen.Freeze();
        _columnKeyBrush.Freeze();
        _rowKeyBrush.Freeze();
        _statusTextBrush.Freeze();
        _statusBackgroundBrush.Freeze();
        _statusBorderPen.Freeze();
        _markerSilhouettePen.Freeze();
        _rangeAccentPen.Freeze();
        _rangeAccentFillBrush.Freeze();
        _fineAccentPen.Freeze();
        _fineAccentFillBrush.Freeze();
    }

    public void UpdateState(
        PixelRect monitorRect,
        IReadOnlyList<PixelRect> monitorRects,
        SelectionManager selectionManager,
        bool isPrimary = false,
        bool isContinuousMode = false,
        PixelPoint? rangeStartPoint = null,
        bool isScrollMode = false,
        bool isRangeDragWorkflow = false,
        PixelPoint? dragStartPoint = null,
        string? monitorBadgeLabel = null,
        bool isFineMode = false,
        PixelPoint? fineTargetPoint = null)
    {
        _monitorRect = monitorRect;
        _monitorRects = monitorRects;
        _selectionManager = selectionManager;
        _isPrimary = isPrimary;
        _isContinuousMode = isContinuousMode;
        _rangeStartPoint = rangeStartPoint;
        _dragStartPoint = dragStartPoint;
        _isScrollMode = isScrollMode;
        _isFineMode = isFineMode;
        _fineTargetPoint = fineTargetPoint;
        _isRangeDragWorkflow = isRangeDragWorkflow;
        _monitorBadgeLabel = monitorBadgeLabel;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (_selectionManager == null || _monitorRect.IsEmpty || _selectionManager.State == SelectionState.Standby)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        double dpiX = dpi.DpiScaleX;
        double dpiY = dpi.DpiScaleY;
        _pixelsPerDip = Math.Max(1.0, dpi.PixelsPerDip);

        double monitorWidthDip = _monitorRect.Width / dpiX;
        double monitorHeightDip = _monitorRect.Height / dpiY;
        var fullMonitorDipRect = new Rect(0, 0, monitorWidthDip, monitorHeightDip);

        // 1. Draw Dimmed Mask outside selection. In Stage 2 the Stage 1 cell stays
        // bright as the focus while the surrounding context margin is dimmed.
        // Scroll Mode skips the mask so the window being scrolled stays fully
        // visible; the dual-tone grid lines remain legible without dimming.
        if (!_isScrollMode)
        {
            var focusSelection = _selectionManager.CurrentStage >= 2
                ? _selectionManager.FocusSelection
                : _selectionManager.CurrentSelection;
            var selectionGeometries = new GeometryGroup();
            foreach (var rect in focusSelection)
            {
                var intersection = rect.Intersect(_monitorRect);
                if (!intersection.IsEmpty)
                {
                    var dipRect = ToDipRect(intersection, dpiX, dpiY);
                    selectionGeometries.Children.Add(new RectangleGeometry(dipRect));
                }
            }

            var maskGeometry = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(fullMonitorDipRect),
                selectionGeometries);
            dc.DrawGeometry(_dimBrush, null, maskGeometry);
        }

        // 2. Draw Column Highlight (if 1st keystroke of stage completed)
        if (_selectionManager.CurrentStage == 1
            && _selectionManager.SelectedColumnIndex >= 0
            && _selectionManager.SelectedColumnIndex < _selectionManager.ColumnCount)
        {
            int colIdx = _selectionManager.SelectedColumnIndex;
            int colLeft = _selectionManager.XBoundaries[colIdx];
            int colRight = _selectionManager.XBoundaries[colIdx + 1];
            var colSlice = PixelRect.FromEdges(colLeft, _selectionManager.CurrentBoundingBox.Top, colRight, _selectionManager.CurrentBoundingBox.Bottom);
            var colIntersection = colSlice.Intersect(_monitorRect);
            if (!colIntersection.IsEmpty)
            {
                dc.DrawRectangle(_columnHighlightBrush, null, ToDipRect(colIntersection, dpiX, dpiY));
            }
        }

        // 3. Draw Cell Highlight (if 2nd keystroke completed, or during Stage 2)
        if (_selectionManager.SelectedColumnIndex >= 0 && _selectionManager.SelectedRowIndex >= 0)
        {
            var cell = GridCalculator.GetCell(
                _selectionManager.XBoundaries,
                _selectionManager.YBoundaries,
                _selectionManager.SelectedColumnIndex,
                _selectionManager.SelectedRowIndex);
            var cellIntersection = cell.Intersect(_monitorRect);
            if (!cellIntersection.IsEmpty)
            {
                var brush = _selectionManager.CurrentStage >= 2
                    ? _stage2CursorBrush
                    : _cellHighlightBrush;
                dc.DrawRectangle(brush, null, ToDipRect(cellIntersection, dpiX, dpiY));
            }
        }
        else if (_selectionManager.CurrentStage >= 2)
        {
            var cellIntersection = _selectionManager.CurrentBoundingBox.Intersect(_monitorRect);
            if (!cellIntersection.IsEmpty)
            {
                dc.DrawRectangle(_cellHighlightBrush, null, ToDipRect(cellIntersection, dpiX, dpiY));
            }
        }

        // 4. Draw Grid Lines & Bounding Box. Scroll Mode hides the whole grid
        // and keeps only the red focus cell, since scrolling targets the window
        // under the cursor rather than a grid position.
        var gridBox = _selectionManager.GridBoundingBox;
        var bboxIntersection = gridBox.Intersect(_monitorRect);
        if (!_isScrollMode && !bboxIntersection.IsEmpty)
        {
            // Bounding box border. The Stage 2 context window uses a lighter pen
            // so the central focus remains the visual anchor; Stage 0/1 get a
            // quieter neutral frame.
            var bboxPen = _selectionManager.CurrentStage >= 2 ? _stage2WindowPen : _stage1FramePen;
            dc.DrawRectangle(null, bboxPen, ToDipRect(bboxIntersection, dpiX, dpiY));

            // Vertical grid lines
            for (int c = 1; c < _selectionManager.ColumnCount; c++)
            {
                int x = _selectionManager.XBoundaries[c];
                if (x >= _monitorRect.Left && x < _monitorRect.Right)
                {
                    int startY = Math.Max(gridBox.Top, _monitorRect.Top);
                    int endY = Math.Min(gridBox.Bottom, _monitorRect.Bottom);
                    if (startY < endY)
                    {
                        DrawVerticalGridLine(dc, x, startY, endY, dpiX, dpiY);
                    }
                }
            }

            // Horizontal grid lines
            for (int r = 1; r < _selectionManager.RowCount; r++)
            {
                int y = _selectionManager.YBoundaries[r];
                if (y >= _monitorRect.Top && y < _monitorRect.Bottom)
                {
                    int startX = Math.Max(gridBox.Left, _monitorRect.Left);
                    int endX = Math.Min(gridBox.Right, _monitorRect.Right);
                    if (startX < endX)
                    {
                        DrawHorizontalGridLine(dc, y, startX, endX, dpiX, dpiY);
                    }
                }
            }

            // Outline the selected Stage 1 cell in Stage 2
            if (_selectionManager.CurrentStage >= 2)
            {
                foreach (var focusRect in _selectionManager.FocusSelection)
                {
                    var focusIntersection = focusRect.Intersect(_monitorRect);
                    if (!focusIntersection.IsEmpty)
                    {
                        dc.DrawRectangle(null, _bboxPen, ToDipRect(focusIntersection, dpiX, dpiY));
                    }
                }
            }
        }

        // 5. Draw in-cell labels for the current selection stage
        if (_selectionManager.State == SelectionState.Selecting)
        {
            DrawCellSectionLabels(dc, dpiX, dpiY);
        }

        // 6. Draw the range anchor at its exact screen coordinate.
        if (_rangeStartPoint is { } rangeStart && _selectionManager.State == SelectionState.Selecting)
        {
            DrawRangeStartMarker(dc, rangeStart, dpiX, dpiY);
        }

        if (_dragStartPoint is { } dragStart && _selectionManager.State == SelectionState.Selecting)
        {
            DrawRangeStartMarker(dc, dragStart, dpiX, dpiY);
        }

        // 6b. Draw the Fine Mode adjustment point at its exact screen coordinate.
        if (_isFineMode && _fineTargetPoint is { } fineTarget
            && _selectionManager.State == SelectionState.Selecting)
        {
            DrawFinePointMarker(dc, fineTarget, dpiX, dpiY);
        }

        // 7. Draw status badges on the primary overlay only.
        if (_isPrimary && _selectionManager.State == SelectionState.Selecting)
        {
            DrawStatusBadges(dc);
        }

        // 8. Every monitor shows its digit-key number so the user can see which
        // key moves the Stage 1 grid there; the grid monitor is marked "Grid".
        if (_monitorBadgeLabel != null
            && _selectionManager.State == SelectionState.Selecting)
        {
            DrawMonitorBadge(dc);
        }
    }

    /// <summary>
    /// Draws a small badge in the top-right corner identifying this monitor's
    /// position in the digit-key ordering (for example "2/3"). The monitor
    /// hosting the Stage 1 grid is prefixed with "Grid".
    /// </summary>
    private void DrawMonitorBadge(DrawingContext dc)
    {
        const double margin = 12.0;
        const double paddingX = 10.0;
        const double paddingY = 6.0;
        const double fontSize = 14.0;

        bool isGridMonitor = _selectionManager?.ActiveMonitorRect == _monitorRect;
        string textValue = isGridMonitor ? $"Grid {_monitorBadgeLabel}" : _monitorBadgeLabel!;
        var text = new FormattedText(
            textValue,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            fontSize,
            _statusTextBrush,
            _pixelsPerDip);

        double width = text.Width + (paddingX * 2);
        double height = text.Height + (paddingY * 2);
        double badgeLeft = ActualWidth - margin - width;
        if (badgeLeft < margin)
        {
            badgeLeft = margin;
        }

        var badgeRect = new Rect(badgeLeft, margin, width, height);
        dc.DrawRoundedRectangle(_statusBackgroundBrush, _statusBorderPen, badgeRect, 4, 4);
        dc.DrawText(text, new Point(badgeRect.Left + paddingX, badgeRect.Top + paddingY));
    }

    /// <summary>
    /// Draws the active status badges stacked from the top-left corner. The order
    /// keeps the mode that changes the next confirmation closest to the grid.
    /// </summary>
    private void DrawStatusBadges(DrawingContext dc)
    {
        double top = 12.0;
        if (_rangeStartPoint.HasValue)
        {
            // The composite workflow reuses the range anchor but advertises the
            // two-phase flow so the user knows a drag follows the selection.
            string rangeBadge = _isRangeDragWorkflow
                ? "Range-to-Drag: Enter to select"
                : "Range: Enter to select";
            top = DrawStatusBadge(dc, rangeBadge, top) + 4.0;
        }

        if (_dragStartPoint.HasValue)
        {
            top = DrawStatusBadge(dc, "Drag: Enter to drop", top) + 4.0;
        }

        if (_isScrollMode)
        {
            top = DrawStatusBadge(dc, "Scroll Mode", top) + 4.0;
        }

        if (_isFineMode)
        {
            top = DrawStatusBadge(dc, "Fine Mode", top) + 4.0;
        }

        if (_isContinuousMode)
        {
            DrawStatusBadge(dc, "Continuous Mode", top);
        }
    }

    /// <summary>
    /// Draws a small status badge so the user can tell whether a confirmation
    /// will keep the grid open. The badge is intentionally simple and uses the
    /// same dark pill treatment as the key labels.
    /// </summary>
    private double DrawStatusBadge(DrawingContext dc, string textValue, double top)
    {
        const double margin = 12.0;
        const double paddingX = 10.0;
        const double paddingY = 6.0;
        const double fontSize = 14.0;

        var text = new FormattedText(
            textValue,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            fontSize,
            _statusTextBrush,
            _pixelsPerDip);

        double width = text.Width + (paddingX * 2);
        double height = text.Height + (paddingY * 2);
        var badgeRect = new Rect(margin, top, width, height);
        dc.DrawRoundedRectangle(_statusBackgroundBrush, _statusBorderPen, badgeRect, 4, 4);
        dc.DrawText(text, new Point(badgeRect.Left + paddingX, badgeRect.Top + paddingY));
        return badgeRect.Bottom;
    }

    /// <summary>
    /// Draws a high-contrast anchor for the start of a two-point range. It is
    /// rendered on the monitor that owns the point rather than only on the primary.
    /// </summary>
    private void DrawRangeStartMarker(
        DrawingContext dc,
        PixelPoint point,
        double dpiX,
        double dpiY)
    {
        DrawTargetMarker(dc, point, dpiX, dpiY, _rangeAccentPen, _rangeAccentFillBrush);
    }

    /// <summary>
    /// Draws a minimal reticle for the Fine Mode adjustment point: a thin ring
    /// and crosshair in warm amber with a dark silhouette for legibility.
    /// </summary>
    private void DrawFinePointMarker(
        DrawingContext dc,
        PixelPoint point,
        double dpiX,
        double dpiY)
    {
        DrawTargetMarker(dc, point, dpiX, dpiY, _fineAccentPen, _fineAccentFillBrush);
    }

    /// <summary>
    /// Draws the shared target reticle: a small ring, four short crosshair
    /// arms, and a center dot. Every element is stroked twice — a wide dark
    /// silhouette under a thin accent — matching the dual-tone grid lines so
    /// the marker reads on both light and dark content.
    /// </summary>
    private void DrawTargetMarker(
        DrawingContext dc,
        PixelPoint point,
        double dpiX,
        double dpiY,
        Pen accentPen,
        Brush accentFill)
    {
        if (!_monitorRect.Contains(point))
        {
            return;
        }

        double x = (point.X - _monitorRect.Left) / dpiX;
        double y = (point.Y - _monitorRect.Top) / dpiY;
        var center = new Point(x, y);
        const double ringRadius = 5.0;
        const double crosshairInner = 6.5;
        const double crosshairOuter = 14.0;
        const double dotRadius = 1.6;

        // Silhouette pass.
        dc.DrawEllipse(null, _markerSilhouettePen, center, ringRadius, ringRadius);
        dc.DrawLine(_markerSilhouettePen, new Point(x - crosshairOuter, y), new Point(x - crosshairInner, y));
        dc.DrawLine(_markerSilhouettePen, new Point(x + crosshairInner, y), new Point(x + crosshairOuter, y));
        dc.DrawLine(_markerSilhouettePen, new Point(x, y - crosshairOuter), new Point(x, y - crosshairInner));
        dc.DrawLine(_markerSilhouettePen, new Point(x, y + crosshairInner), new Point(x, y + crosshairOuter));

        // Accent pass.
        dc.DrawEllipse(null, accentPen, center, ringRadius, ringRadius);
        dc.DrawLine(accentPen, new Point(x - crosshairOuter, y), new Point(x - crosshairInner, y));
        dc.DrawLine(accentPen, new Point(x + crosshairInner, y), new Point(x + crosshairOuter, y));
        dc.DrawLine(accentPen, new Point(x, y - crosshairOuter), new Point(x, y - crosshairInner));
        dc.DrawLine(accentPen, new Point(x, y + crosshairInner), new Point(x, y + crosshairOuter));
        dc.DrawEllipse(accentFill, null, center, dotRadius, dotRadius);
    }

    /// <summary>
    /// Draws Stage 1 labels inside valid cells. The active column shows only row keys.
    /// </summary>
    private void DrawCellSectionLabels(DrawingContext dc, double dpiX, double dpiY)
    {
        if (_selectionManager == null) return;

        if (_selectionManager.CurrentStage >= 2)
        {
            return;
        }

        int selectedCol = _selectionManager.SelectedColumnIndex;
        bool rowOnly = selectedCol >= 0;

        for (int c = 0; c < _selectionManager.ColumnCount; c++)
        {
            // If a column is already selected, only display labels for that active column
            if (rowOnly && selectedCol != c)
            {
                continue;
            }

            char colChar = _selectionManager.ColumnKeys[c];

            for (int r = 0; r < _selectionManager.RowCount; r++)
            {
                if (!GridCalculator.IsCellValid(c, r, _selectionManager.XBoundaries, _selectionManager.YBoundaries, _selectionManager.CurrentSelection))
                {
                    continue;
                }

                var cell = GridCalculator.GetCell(_selectionManager.XBoundaries, _selectionManager.YBoundaries, c, r);
                if (!IsOwnerMonitor(cell))
                {
                    continue;
                }

                var visibleCell = cell.Intersect(_monitorRect);
                if (visibleCell.IsEmpty)
                {
                    continue;
                }

                var cellRect = ToDipRect(visibleCell, dpiX, dpiY);
                if (cellRect.Width < 14 || cellRect.Height < 12)
                {
                    continue;
                }

                char rowChar = _selectionManager.RowKeys[r];
                DrawSectionBadge(
                    dc,
                    rowOnly ? null : colChar,
                    rowChar,
                    cellRect,
                    rowOnly,
                    _pixelsPerDip);
            }
        }
    }

    /// <summary>
    /// Draws a two-character column+row badge, or a single row badge after
    /// the column is selected.
    /// </summary>
    private void DrawSectionBadge(
        DrawingContext dc,
        char? columnKey,
        char rowKey,
        Rect cellRect,
        bool isActiveColumn,
        double pixelsPerDip)
    {
        double maxWidth = Math.Max(1.0, cellRect.Width - 2);
        double maxHeight = Math.Max(1.0, cellRect.Height - 2);

        double targetFontSize = columnKey.HasValue ? 15.0 : 18.0;
        double widthFactor = columnKey.HasValue ? 1.4 : 0.7;
        double estimatedByWidth = maxWidth / widthFactor;
        double estimatedByHeight = maxHeight / 1.2;
        double fontSize = Math.Clamp(
            Math.Min(targetFontSize, Math.Min(estimatedByWidth, estimatedByHeight)),
            7.0,
            targetFontSize);

        FormattedText? columnGlyph = null;
        FormattedText rowGlyph;
        double textWidth;
        double textHeight;
        double badgeWidth;
        double badgeHeight;
        const double glyphGap = 0.5;
        const double paddingX = 2.0;
        const double paddingY = 1.0;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            columnGlyph = columnKey.HasValue
                ? GetCellGlyph(columnKey.Value, _columnKeyBrush, fontSize, pixelsPerDip)
                : null;
            rowGlyph = GetCellGlyph(rowKey, _rowKeyBrush, fontSize, pixelsPerDip);
            textWidth = (columnGlyph?.Width ?? 0) + (columnGlyph != null ? glyphGap : 0) + rowGlyph.Width;
            textHeight = Math.Max(columnGlyph?.Height ?? 0, rowGlyph.Height);
            badgeWidth = textWidth + (paddingX * 2);
            badgeHeight = textHeight + (paddingY * 2);

            double overflow = Math.Max(badgeWidth / maxWidth, badgeHeight / maxHeight);
            if (overflow <= 1.0)
            {
                break;
            }

            double nextSize = Math.Max(7.0, fontSize / overflow);
            if (Math.Abs(nextSize - fontSize) < 0.05)
            {
                break;
            }

            fontSize = nextSize;
        }

        columnGlyph = columnKey.HasValue
            ? GetCellGlyph(columnKey.Value, _columnKeyBrush, fontSize, pixelsPerDip)
            : null;
        rowGlyph = GetCellGlyph(rowKey, _rowKeyBrush, fontSize, pixelsPerDip);
        textWidth = (columnGlyph?.Width ?? 0) + (columnGlyph != null ? glyphGap : 0) + rowGlyph.Width;
        textHeight = Math.Max(columnGlyph?.Height ?? 0, rowGlyph.Height);
        badgeWidth = textWidth + (paddingX * 2);
        badgeHeight = textHeight + (paddingY * 2);

        if (badgeWidth > maxWidth + 0.5 || badgeHeight > maxHeight + 0.5)
        {
            return;
        }

        double cellCenterX = cellRect.Left + (cellRect.Width / 2.0);
        double badgeCenterY = cellRect.Top + (cellRect.Height / 2.0);
        double badgeCenterX = cellCenterX;
        double minCenterX = cellRect.Left + 1 + (badgeWidth / 2.0);
        double maxCenterX = cellRect.Right - 1 - (badgeWidth / 2.0);
        if (minCenterX <= maxCenterX)
        {
            badgeCenterX = Math.Clamp(badgeCenterX, minCenterX, maxCenterX);
        }

        var badgeRect = new Rect(
            badgeCenterX - (badgeWidth / 2.0),
            badgeCenterY - (badgeHeight / 2.0),
            badgeWidth,
            badgeHeight);

        var bgBrush = isActiveColumn ? _activeBadgeBgBrush : _badgeBgBrush;
        var borderPen = isActiveColumn ? _activeBadgeBorderPen : _badgeBorderPen;

        dc.DrawRoundedRectangle(bgBrush, borderPen, badgeRect, 2, 2);

        double textLeft = badgeCenterX - (textWidth / 2.0);
        if (columnGlyph != null)
        {
            double columnY = badgeCenterY - (columnGlyph.Height / 2.0);
            dc.DrawText(columnGlyph, new Point(textLeft, columnY));
            textLeft += columnGlyph.Width + glyphGap;
        }

        double rowY = badgeCenterY - (rowGlyph.Height / 2.0);
        dc.DrawText(rowGlyph, new Point(textLeft, rowY));
    }

    /// <summary>
    /// A cell spanning monitors gets exactly one owner for its Stage 1 label.
    /// </summary>
    private bool IsOwnerMonitor(PixelRect cell)
    {
        if (_monitorRects.Count == 0)
        {
            return true;
        }

        int ownerIndex = LabelOwnership.FindOwnerIndex(cell, _monitorRects);
        return ownerIndex >= 0 && _monitorRects[ownerIndex] == _monitorRect;
    }

    /// <summary>
    /// Draws a vertical grid line as two one-physical-pixel lines centered on the
    /// boundary: black on the left pixel and white on the right pixel. Using physical
    /// pixel sizes keeps the pair crisp and prevents it from growing at higher DPI.
    /// </summary>
    private void DrawVerticalGridLine(
        DrawingContext dc,
        int x,
        int startY,
        int endY,
        double dpiX,
        double dpiY)
    {
        double top = (startY - _monitorRect.Top) / dpiY;
        double height = (endY - startY) / dpiY;
        double width = 1.0 / dpiX;

        // The pair straddles the boundary, so the dark pixel is outside the monitor
        // only when the line lies exactly on the left edge.
        if (x > _monitorRect.Left)
        {
            double darkLeft = (x - 1 - _monitorRect.Left) / dpiX;
            dc.DrawRectangle(_gridDarkBrush, null, new Rect(darkLeft, top, width, height));
        }

        double lightLeft = (x - _monitorRect.Left) / dpiX;
        dc.DrawRectangle(_gridLightBrush, null, new Rect(lightLeft, top, width, height));
    }

    /// <summary>
    /// Draws a horizontal grid line as two one-physical-pixel lines centered on the
    /// boundary: black on the upper pixel and white on the lower pixel.
    /// </summary>
    private void DrawHorizontalGridLine(
        DrawingContext dc,
        int y,
        int startX,
        int endX,
        double dpiX,
        double dpiY)
    {
        double left = (startX - _monitorRect.Left) / dpiX;
        double width = (endX - startX) / dpiX;
        double height = 1.0 / dpiY;

        if (y > _monitorRect.Top)
        {
            double darkTop = (y - 1 - _monitorRect.Top) / dpiY;
            dc.DrawRectangle(_gridDarkBrush, null, new Rect(left, darkTop, width, height));
        }

        double lightTop = (y - _monitorRect.Top) / dpiY;
        dc.DrawRectangle(_gridLightBrush, null, new Rect(left, lightTop, width, height));
    }

    private FormattedText GetCellGlyph(char character, Brush brush, double fontSize, double pixelsPerDip)
    {
        int fontSizeKey = Math.Max(1, (int)Math.Round(fontSize * 100));
        int pixelsPerDipKey = Math.Max(1, (int)Math.Round(pixelsPerDip * 1000));
        var key = new GlyphKey(character, brush, fontSizeKey, pixelsPerDipKey);
        if (_cellGlyphCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var glyph = new FormattedText(
            character.ToString(),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _cellTypeface,
            fontSizeKey / 100.0,
            brush,
            pixelsPerDipKey / 1000.0);
        _cellGlyphCache[key] = glyph;
        return glyph;
    }

    private FormattedText GetGlyph(char character, Brush brush, double fontSize, double pixelsPerDip)
    {
        int fontSizeKey = Math.Max(1, (int)Math.Round(fontSize * 100));
        int pixelsPerDipKey = Math.Max(1, (int)Math.Round(pixelsPerDip * 1000));
        var key = new GlyphKey(character, brush, fontSizeKey, pixelsPerDipKey);
        if (_glyphCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var glyph = new FormattedText(
            character.ToString(),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            fontSizeKey / 100.0,
            brush,
            pixelsPerDipKey / 1000.0);
        _glyphCache[key] = glyph;
        return glyph;
    }

    private Rect ToDipRect(PixelRect rect, double dpiX, double dpiY)
    {
        double left = (rect.Left - _monitorRect.Left) / dpiX;
        double top = (rect.Top - _monitorRect.Top) / dpiY;
        double width = rect.Width / dpiX;
        double height = rect.Height / dpiY;
        return new Rect(left, top, width, height);
    }

    private readonly record struct GlyphKey(
        char Character,
        Brush Brush,
        int FontSizeKey,
        int PixelsPerDipKey);
}
