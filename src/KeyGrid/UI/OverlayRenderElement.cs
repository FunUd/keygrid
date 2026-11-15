using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;

namespace KeyGrid.UI;

/// <summary>
/// Handles high-performance hardware-accelerated drawing of grid lines,
/// selection masks, stage-1 in-cell labels, the early-Enter marker, and
/// stage-2 local key rulers.
/// </summary>
public class OverlayRenderElement : FrameworkElement
{
    private PixelRect _monitorRect;
    private IReadOnlyList<PixelRect> _monitorRects = Array.Empty<PixelRect>();
    private SelectionManager? _selectionManager;
    private double _pixelsPerDip = 1.0;

    private readonly Brush _dimBrush = new SolidColorBrush(Color.FromArgb(145, 10, 15, 25));
    private readonly Brush _columnHighlightBrush = new SolidColorBrush(Color.FromArgb(55, 0, 229, 255));
    private readonly Brush _cellHighlightBrush = new SolidColorBrush(Color.FromArgb(65, 180, 220, 255));
    // Grid lines are drawn as two one-physical-pixel lines straddling each boundary:
    // black on the left/top and white on the right/bottom. On white content only the
    // black line is visible; on black content only the white line is visible, so the
    // grid remains legible without reading the screen or changing colors dynamically.
    private readonly Brush _gridDarkBrush = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
    private readonly Brush _gridLightBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
    private readonly Pen _bboxPen = new(new SolidColorBrush(Color.FromArgb(200, 0, 200, 255)), 2.0);

    // Stage-1 badges. The dark pill keeps both key colors legible over arbitrary
    // screen content without sampling the background.
    private readonly Brush _badgeBgBrush = new SolidColorBrush(Color.FromArgb(225, 12, 16, 24));
    private readonly Pen _badgeBorderPen = new(new SolidColorBrush(Color.FromArgb(150, 0, 229, 255)), 1.0);
    private readonly Brush _activeBadgeBgBrush = new SolidColorBrush(Color.FromArgb(240, 12, 16, 24));
    private readonly Pen _activeBadgeBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 1.4);

    // Stage-2 key rulers. They sit outside the selected cell so the sub-cell
    // centers (the click targets) stay unobstructed.
    private readonly Brush _rulerBgBrush = new SolidColorBrush(Color.FromArgb(205, 12, 16, 24));
    private readonly Brush _activeSubColumnBrush = new SolidColorBrush(Color.FromArgb(85, 0, 229, 255));
    private readonly Brush _labelShadowBrush = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0));
    private readonly Pen _activeHorizontalRulerBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 0, 229, 255)), 1.8);
    private readonly Pen _activeVerticalRulerBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 255, 200, 87)), 1.8);
    private readonly Pen _inactiveRulerBorderPen = new(new SolidColorBrush(Color.FromArgb(60, 180, 200, 220)), 0.8);

    private readonly Brush _columnKeyBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255));
    private readonly Brush _rowKeyBrush = new SolidColorBrush(Color.FromRgb(255, 200, 87));

    // The early-Enter crosshair marks the optional intermediate click point.
    private readonly Pen _crosshairPen = new(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 1.4);
    private readonly Pen _crosshairShadowPen = new(new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)), 3.6);

    private readonly Typeface _labelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    // Reuse glyph layouts across labels and renders. The key includes DPI because
    // FormattedText stores a pixelsPerDip value that changes with per-monitor DPI.
    private readonly Dictionary<GlyphKey, FormattedText> _glyphCache = new();

    public OverlayRenderElement()
    {
        _dimBrush.Freeze();
        _columnHighlightBrush.Freeze();
        _cellHighlightBrush.Freeze();
        _gridDarkBrush.Freeze();
        _gridLightBrush.Freeze();
        _bboxPen.Freeze();
        _badgeBgBrush.Freeze();
        _badgeBorderPen.Freeze();
        _activeBadgeBgBrush.Freeze();
        _activeBadgeBorderPen.Freeze();
        _rulerBgBrush.Freeze();
        _activeSubColumnBrush.Freeze();
        _labelShadowBrush.Freeze();
        _activeHorizontalRulerBorderPen.Freeze();
        _activeVerticalRulerBorderPen.Freeze();
        _inactiveRulerBorderPen.Freeze();
        _columnKeyBrush.Freeze();
        _rowKeyBrush.Freeze();
        _crosshairPen.Freeze();
        _crosshairShadowPen.Freeze();
    }

    public void UpdateState(
        PixelRect monitorRect,
        IReadOnlyList<PixelRect> monitorRects,
        SelectionManager selectionManager)
    {
        _monitorRect = monitorRect;
        _monitorRects = monitorRects;
        _selectionManager = selectionManager;
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

        // 1. Draw Dimmed Mask outside selection
        var selectionGeometries = new GeometryGroup();
        foreach (var rect in _selectionManager.CurrentSelection)
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

        // 2. Draw Column Highlight (if 1st keystroke of stage completed)
        if (_selectionManager.SelectedColumnIndex >= 0 && _selectionManager.SelectedColumnIndex < _selectionManager.ColumnCount)
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
                dc.DrawRectangle(_cellHighlightBrush, null, ToDipRect(cellIntersection, dpiX, dpiY));
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

        // 4. Draw Grid Lines & Bounding Box
        var bboxIntersection = _selectionManager.CurrentBoundingBox.Intersect(_monitorRect);
        if (!bboxIntersection.IsEmpty)
        {
            // Bounding box border
            dc.DrawRectangle(null, _bboxPen, ToDipRect(bboxIntersection, dpiX, dpiY));

            // Vertical grid lines
            for (int c = 1; c < _selectionManager.ColumnCount; c++)
            {
                int x = _selectionManager.XBoundaries[c];
                if (x >= _monitorRect.Left && x < _monitorRect.Right)
                {
                    int startY = Math.Max(_selectionManager.CurrentBoundingBox.Top, _monitorRect.Top);
                    int endY = Math.Min(_selectionManager.CurrentBoundingBox.Bottom, _monitorRect.Bottom);
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
                    int startX = Math.Max(_selectionManager.CurrentBoundingBox.Left, _monitorRect.Left);
                    int endX = Math.Min(_selectionManager.CurrentBoundingBox.Right, _monitorRect.Right);
                    if (startX < endX)
                    {
                        DrawHorizontalGridLine(dc, y, startX, endX, dpiX, dpiY);
                    }
                }
            }
        }

        // 5. Draw Stage 1 labels or Stage 2 rulers
        if (_selectionManager.State == SelectionState.Selecting)
        {
            DrawCellSectionLabels(dc, dpiX, dpiY);
            DrawEarlyClickMarker(dc, dpiX, dpiY);
        }
    }

    /// <summary>
    /// Draws stage-1 labels inside each valid grid section. After the column key,
    /// the active column shows only the row key in amber. Stage 2 uses local rulers
    /// around the selected cell because sub-cells are too small for the badge.
    /// </summary>
    private void DrawCellSectionLabels(DrawingContext dc, double dpiX, double dpiY)
    {
        if (_selectionManager == null) return;

        if (_selectionManager.CurrentStage >= 2)
        {
            DrawStage2RulerLabels(dc, dpiX, dpiY, _selectionManager);
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
    /// Draws a two-character column+row badge, or a single amber row badge after
    /// the column is selected. The badge is lifted above the cell center when the
    /// cell is tall enough, keeping the click target clear.
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

        double targetFontSize = columnKey.HasValue ? 18.0 : 22.0;
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
                ? GetGlyph(columnKey.Value, _columnKeyBrush, fontSize, pixelsPerDip)
                : null;
            rowGlyph = GetGlyph(rowKey, _rowKeyBrush, fontSize, pixelsPerDip);
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
            ? GetGlyph(columnKey.Value, _columnKeyBrush, fontSize, pixelsPerDip)
            : null;
        rowGlyph = GetGlyph(rowKey, _rowKeyBrush, fontSize, pixelsPerDip);
        textWidth = (columnGlyph?.Width ?? 0) + (columnGlyph != null ? glyphGap : 0) + rowGlyph.Width;
        textHeight = Math.Max(columnGlyph?.Height ?? 0, rowGlyph.Height);
        badgeWidth = textWidth + (paddingX * 2);
        badgeHeight = textHeight + (paddingY * 2);

        if (badgeWidth > maxWidth + 0.5 || badgeHeight > maxHeight + 0.5)
        {
            return;
        }

        double cellCenterX = cellRect.Left + (cellRect.Width / 2.0);
        double cellCenterY = cellRect.Top + (cellRect.Height / 2.0);

        double badgeCenterX = cellCenterX;
        double minCenterX = cellRect.Left + 1 + (badgeWidth / 2.0);
        double maxCenterX = cellRect.Right - 1 - (badgeWidth / 2.0);
        if (minCenterX <= maxCenterX)
        {
            badgeCenterX = Math.Clamp(badgeCenterX, minCenterX, maxCenterX);
        }

        double minCenterY = cellRect.Top + 1 + (badgeHeight / 2.0);
        double maxCenterY = cellRect.Bottom - 1 - (badgeHeight / 2.0);
        double badgeCenterY = cellCenterY;
        if (minCenterY <= maxCenterY)
        {
            double desiredCenterY = cellCenterY - (badgeHeight / 2.0) - 6.0;
            badgeCenterY = Math.Clamp(desiredCenterY, minCenterY, maxCenterY);

            // Compact cells cannot keep the badge above the center; center it
            // instead of letting it cover the click point from one side.
            if (badgeCenterY + (badgeHeight / 2.0) > cellCenterY - 2.0)
            {
                badgeCenterY = cellCenterY;
            }
        }

        var badgeRect = new Rect(
            badgeCenterX - (badgeWidth / 2.0),
            badgeCenterY - (badgeHeight / 2.0),
            badgeWidth,
            badgeHeight);

        var bgBrush = isActiveColumn ? _activeBadgeBgBrush : _badgeBgBrush;
        var borderPen = isActiveColumn ? _activeBadgeBorderPen : _badgeBorderPen;
        dc.DrawRoundedRectangle(bgBrush, borderPen, badgeRect, 3, 3);

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
    /// In stage 2 the sub-cell centers are too small for the in-cell 2-letter badge,
    /// so the keys are moved to rulers just outside the selected cell. The rulers
    /// flip to the opposite side when the preferred side would leave the monitor.
    /// </summary>
    private void DrawStage2RulerLabels(
        DrawingContext dc,
        double dpiX,
        double dpiY,
        SelectionManager selectionManager)
    {
        var selectedCell = selectionManager.CurrentBoundingBox;
        if (selectedCell.IsEmpty || selectedCell.Intersect(_monitorRect).IsEmpty)
        {
            return;
        }

        // Keep the ruler sized in DIPs so it remains readable at any DPI scale.
        double dpiScale = Math.Min(dpiX, dpiY);
        int stripThickness = Math.Max(10, (int)Math.Round(16 * dpiScale));
        int gap = Math.Max(2, (int)Math.Round(3 * dpiScale));

        var layout = Stage2LabelLayout.Calculate(
            selectedCell,
            _monitorRect,
            selectionManager.XBoundaries,
            selectionManager.YBoundaries,
            selectionManager.ColumnCount,
            selectionManager.RowCount,
            stripThickness,
            gap);

        if (layout == null)
        {
            return;
        }

        var horizontalStrip = ToDipRect(layout.HorizontalStrip, dpiX, dpiY);
        var verticalStrip = ToDipRect(layout.VerticalStrip, dpiX, dpiY);
        if (horizontalStrip.Width <= 0 || horizontalStrip.Height <= 0 ||
            verticalStrip.Width <= 0 || verticalStrip.Height <= 0)
        {
            return;
        }

        var visibleCell = selectedCell.Intersect(_monitorRect);
        if (visibleCell.IsEmpty)
        {
            return;
        }

        double visibleCellWidthDip = visibleCell.Width / dpiX;
        double visibleCellHeightDip = visibleCell.Height / dpiY;
        double columnSpacingDip = visibleCellWidthDip / selectionManager.ColumnCount;
        double rowSpacingDip = visibleCellHeightDip / selectionManager.RowCount;

        double columnFontSize = Math.Clamp(
            Math.Min(13.0, Math.Min(columnSpacingDip * 0.75, horizontalStrip.Height * 0.82)),
            8.0,
            13.0);
        double rowFontSize = Math.Clamp(
            Math.Min(13.0, Math.Min(rowSpacingDip * 0.80, verticalStrip.Width * 0.82)),
            8.0,
            13.0);

        dc.DrawRoundedRectangle(_rulerBgBrush, null, horizontalStrip, 3, 3);
        dc.DrawRoundedRectangle(_rulerBgBrush, null, verticalStrip, 3, 3);

        int selectedColumn = selectionManager.SelectedColumnIndex;

        // Once the sub-column is chosen, highlight its segment in the top ruler.
        if (selectedColumn >= 0 && selectedColumn < selectionManager.ColumnCount)
        {
            var activeSegment = PixelRect.FromEdges(
                selectionManager.XBoundaries[selectedColumn],
                layout.HorizontalStrip.Top,
                selectionManager.XBoundaries[selectedColumn + 1],
                layout.HorizontalStrip.Bottom);
            var activeSegmentDip = ToDipRect(activeSegment, dpiX, dpiY);
            var clipped = Rect.Intersect(activeSegmentDip, horizontalStrip);
            if (!clipped.IsEmpty)
            {
                // Keep the active segment inside the ruler's rounded corners.
                dc.PushClip(new RectangleGeometry(horizontalStrip, 3, 3));
                dc.DrawRoundedRectangle(_activeSubColumnBrush, null, clipped, 2, 2);
                dc.Pop();
            }
        }

        // Determine which ruler is awaiting input next and highlight it.
        bool columnIsNext = selectedColumn < 0; // no sub-column chosen yet => horizontal is next
        var hRulerBorderPen = columnIsNext ? _activeHorizontalRulerBorderPen : _inactiveRulerBorderPen;
        var vRulerBorderPen = columnIsNext ? _inactiveRulerBorderPen : _activeVerticalRulerBorderPen;

        dc.DrawRoundedRectangle(null, hRulerBorderPen, horizontalStrip, 3, 3);
        dc.DrawRoundedRectangle(null, vRulerBorderPen, verticalStrip, 3, 3);

        double horizontalCenterY = horizontalStrip.Top + (horizontalStrip.Height / 2.0);
        for (int c = 0; c < selectionManager.ColumnCount; c++)
        {
            // After the third keystroke only the chosen sub-column remains relevant.
            if (selectedColumn >= 0 && c != selectedColumn)
            {
                continue;
            }

            if (!GridCalculator.IsColumnValid(
                    c,
                    selectionManager.XBoundaries,
                    selectionManager.CurrentBoundingBox,
                    selectionManager.CurrentSelection))
            {
                continue;
            }

            var columnRegion = PixelRect.FromEdges(
                selectionManager.XBoundaries[c],
                selectedCell.Top,
                selectionManager.XBoundaries[c + 1],
                selectedCell.Bottom);
            if (!IsOwnerMonitor(columnRegion))
            {
                continue;
            }

            double centerX = (layout.ColumnCenters[c] - _monitorRect.Left) / dpiX;
            DrawRulerChar(
                dc,
                selectionManager.ColumnKeys[c],
                new Point(centerX, horizontalCenterY),
                _columnKeyBrush,
                columnFontSize,
                _pixelsPerDip,
                horizontalStrip,
                clampX: true);
        }

        double verticalCenterX = verticalStrip.Left + (verticalStrip.Width / 2.0);
        for (int r = 0; r < selectionManager.RowCount; r++)
        {
            if (!IsRowValidForStage2(selectionManager, r, selectedColumn))
            {
                continue;
            }

            var rowRegion = PixelRect.FromEdges(
                selectedCell.Left,
                selectionManager.YBoundaries[r],
                selectedCell.Right,
                selectionManager.YBoundaries[r + 1]);
            if (!IsOwnerMonitor(rowRegion))
            {
                continue;
            }

            double centerY = (layout.RowCenters[r] - _monitorRect.Top) / dpiY;
            DrawRulerChar(
                dc,
                selectionManager.RowKeys[r],
                new Point(verticalCenterX, centerY),
                _rowKeyBrush,
                rowFontSize,
                _pixelsPerDip,
                verticalStrip,
                clampX: false);
        }
    }

    private static bool IsRowValidForStage2(
        SelectionManager selectionManager,
        int rowIndex,
        int selectedColumn)
    {
        if (selectedColumn >= 0)
        {
            return GridCalculator.IsCellValid(
                selectedColumn,
                rowIndex,
                selectionManager.XBoundaries,
                selectionManager.YBoundaries,
                selectionManager.CurrentSelection);
        }

        for (int c = 0; c < selectionManager.ColumnCount; c++)
        {
            if (GridCalculator.IsCellValid(
                    c,
                    rowIndex,
                    selectionManager.XBoundaries,
                    selectionManager.YBoundaries,
                    selectionManager.CurrentSelection))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Marks the current Stage 1 click point while Enter can confirm it. This is
    /// shown only between the second and third keystrokes to avoid persistent
    /// clutter in every cell.
    /// </summary>
    private void DrawEarlyClickMarker(DrawingContext dc, double dpiX, double dpiY)
    {
        if (_selectionManager == null ||
            _selectionManager.CurrentStage != 2 ||
            _selectionManager.History.Count != 2 ||
            _selectionManager.CurrentSelection.Count == 0)
        {
            return;
        }

        var target = GridCalculator.ResolveTargetCoordinate(
            _selectionManager.CurrentBoundingBox,
            _selectionManager.CurrentSelection);
        if (!_monitorRect.Contains(target))
        {
            return;
        }

        double x = (target.X - _monitorRect.Left) / dpiX;
        double y = (target.Y - _monitorRect.Top) / dpiY;
        const double innerGap = 1.5;
        const double armLength = 6.0;

        DrawCrosshair(dc, _crosshairShadowPen, x, y, innerGap, armLength);
        DrawCrosshair(dc, _crosshairPen, x, y, innerGap, armLength);
    }

    private static void DrawCrosshair(
        DrawingContext dc,
        Pen pen,
        double x,
        double y,
        double innerGap,
        double armLength)
    {
        double outer = innerGap + armLength;
        dc.DrawLine(pen, new Point(x - outer, y), new Point(x - innerGap, y));
        dc.DrawLine(pen, new Point(x + innerGap, y), new Point(x + outer, y));
        dc.DrawLine(pen, new Point(x, y - outer), new Point(x, y - innerGap));
        dc.DrawLine(pen, new Point(x, y + innerGap), new Point(x, y + outer));
    }

    /// <summary>
    /// A cell spanning monitors gets exactly one owner. Its labels and rulers are
    /// drawn only by that monitor's overlay.
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

    private void DrawRulerChar(
        DrawingContext dc,
        char key,
        Point center,
        Brush brush,
        double fontSize,
        double pixelsPerDip,
        Rect strip,
        bool clampX)
    {
        var label = GetGlyph(key, brush, fontSize, pixelsPerDip);
        var shadow = GetGlyph(key, _labelShadowBrush, fontSize, pixelsPerDip);

        if (clampX)
        {
            double minCenter = strip.Left + (label.Width / 2.0);
            double maxCenter = strip.Right - (label.Width / 2.0);
            center.X = minCenter <= maxCenter
                ? Math.Clamp(center.X, minCenter, maxCenter)
                : strip.Left + (strip.Width / 2.0);
        }
        else
        {
            double minCenter = strip.Top + (label.Height / 2.0);
            double maxCenter = strip.Bottom - (label.Height / 2.0);
            center.Y = minCenter <= maxCenter
                ? Math.Clamp(center.Y, minCenter, maxCenter)
                : strip.Top + (strip.Height / 2.0);
        }

        double x = center.X - (label.Width / 2.0);
        double y = center.Y - (label.Height / 2.0);
        dc.PushClip(new RectangleGeometry(strip));
        dc.DrawText(shadow, new Point(x + 0.75, y + 0.75));
        dc.DrawText(label, new Point(x, y));
        dc.Pop();
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
