using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KeyGrid.Core;
using KeyGrid.Core.Calculation;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;

namespace KeyGrid.UI;

/// <summary>
/// Handles high-performance hardware-accelerated drawing of grid lines,
/// selection masks, stage-1 in-cell labels, and stage-2 local key rulers.
/// </summary>
public class OverlayRenderElement : FrameworkElement
{
    private PixelRect _monitorRect;
    private SelectionManager? _selectionManager;

    private readonly Brush _dimBrush = new SolidColorBrush(Color.FromArgb(145, 10, 15, 25));
    private readonly Brush _columnHighlightBrush = new SolidColorBrush(Color.FromArgb(50, 0, 180, 255));
    private readonly Brush _cellHighlightBrush = new SolidColorBrush(Color.FromArgb(70, 0, 255, 150));
    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1.0);
    private readonly Pen _bboxPen = new(new SolidColorBrush(Color.FromArgb(200, 0, 200, 255)), 2.0);

    // Normal cell badge
    private readonly Brush _badgeBgBrush = new SolidColorBrush(Color.FromArgb(220, 18, 22, 32));
    private readonly Pen _badgeBorderPen = new(new SolidColorBrush(Color.FromArgb(180, 0, 210, 255)), 1.2);

    // Highlighted column cell badge
    private readonly Brush _activeBadgeBgBrush = new SolidColorBrush(Color.FromArgb(240, 0, 120, 215));
    private readonly Pen _activeBadgeBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 1.5);

    // Stage-2 key rulers. They sit outside the selected cell so the sub-cell
    // centers (the click targets) stay unobstructed.
    private readonly Brush _rulerBgBrush = new SolidColorBrush(Color.FromArgb(190, 18, 22, 32));
    private readonly Pen _rulerBorderPen = new(new SolidColorBrush(Color.FromArgb(120, 0, 210, 255)), 1.0);
    private readonly Brush _activeRulerBgBrush = new SolidColorBrush(Color.FromArgb(230, 0, 120, 215));
    private readonly Brush _labelShadowBrush = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));

    private readonly Typeface _labelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public OverlayRenderElement()
    {
        _dimBrush.Freeze();
        _columnHighlightBrush.Freeze();
        _cellHighlightBrush.Freeze();
        _gridPen.Freeze();
        _bboxPen.Freeze();
        _badgeBgBrush.Freeze();
        _badgeBorderPen.Freeze();
        _activeBadgeBgBrush.Freeze();
        _activeBadgeBorderPen.Freeze();
        _rulerBgBrush.Freeze();
        _rulerBorderPen.Freeze();
        _activeRulerBgBrush.Freeze();
        _labelShadowBrush.Freeze();
    }

    public void UpdateState(PixelRect monitorRect, SelectionManager selectionManager)
    {
        _monitorRect = monitorRect;
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

        // 3. Draw Cell Highlight (if 2nd keystroke completed)
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
                    double xDip = (x - _monitorRect.Left) / dpiX;
                    int startY = Math.Max(_selectionManager.CurrentBoundingBox.Top, _monitorRect.Top);
                    int endY = Math.Min(_selectionManager.CurrentBoundingBox.Bottom, _monitorRect.Bottom);
                    if (startY < endY)
                    {
                        dc.DrawLine(_gridPen,
                            new Point(xDip, (startY - _monitorRect.Top) / dpiY),
                            new Point(xDip, (endY - _monitorRect.Top) / dpiY));
                    }
                }
            }

            // Horizontal grid lines
            for (int r = 1; r < _selectionManager.RowCount; r++)
            {
                int y = _selectionManager.YBoundaries[r];
                if (y >= _monitorRect.Top && y < _monitorRect.Bottom)
                {
                    double yDip = (y - _monitorRect.Top) / dpiY;
                    int startX = Math.Max(_selectionManager.CurrentBoundingBox.Left, _monitorRect.Left);
                    int endX = Math.Min(_selectionManager.CurrentBoundingBox.Right, _monitorRect.Right);
                    if (startX < endX)
                    {
                        dc.DrawLine(_gridPen,
                            new Point((startX - _monitorRect.Left) / dpiX, yDip),
                            new Point((endX - _monitorRect.Left) / dpiX, yDip));
                    }
                }
            }
        }

        // 5. Draw 2-Letter Section Labels inside each valid cell
        if (_selectionManager.State == SelectionState.Selecting)
        {
            DrawCellSectionLabels(dc, dpiX, dpiY);
        }
    }

    /// <summary>
    /// Draws stage-1 labels inside each valid grid section. Stage 2 uses local
    /// rulers around the selected cell because sub-cells are too small for the
    /// 2-letter badge without covering the click target.
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

        for (int c = 0; c < _selectionManager.ColumnCount; c++)
        {
            // If a column is already selected, only display labels for that active column
            if (selectedCol >= 0 && selectedCol != c)
            {
                continue;
            }

            char colChar = KeyLayout.GetKey(c);

            for (int r = 0; r < _selectionManager.RowCount; r++)
            {
                if (!GridCalculator.IsCellValid(c, r, _selectionManager.XBoundaries, _selectionManager.YBoundaries, _selectionManager.CurrentSelection))
                {
                    continue;
                }

                char rowChar = KeyLayout.GetKey(r);

                var cell = GridCalculator.GetCell(_selectionManager.XBoundaries, _selectionManager.YBoundaries, c, r);
                var visibleCell = cell.Intersect(_monitorRect);
                if (visibleCell.IsEmpty)
                {
                    continue;
                }

                double cellWidthDip = visibleCell.Width / dpiX;
                double cellHeightDip = visibleCell.Height / dpiY;

                // Only render if cell is large enough to display text
                if (cellWidthDip < 14 || cellHeightDip < 12)
                {
                    continue;
                }

                double centerXDip = (visibleCell.Left + visibleCell.Width / 2.0 - _monitorRect.Left) / dpiX;
                double centerYDip = (visibleCell.Top + visibleCell.Height / 2.0 - _monitorRect.Top) / dpiY;

                string labelText = $"{colChar}{rowChar}";
                bool isActiveColumn = (selectedCol == c);

                DrawSectionBadge(dc, labelText, new Point(centerXDip, centerYDip), cellWidthDip, cellHeightDip, isActiveColumn);
            }
        }
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
        // Keep the ruler sized in DIPs so it remains readable at any DPI scale.
        double dpiScale = Math.Min(dpiX, dpiY);
        int stripThickness = Math.Max(10, (int)Math.Round(16 * dpiScale));
        int gap = Math.Max(2, (int)Math.Round(3 * dpiScale));

        var layout = Stage2LabelLayout.Calculate(
            selectionManager.CurrentBoundingBox,
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
                dc.DrawRoundedRectangle(_activeRulerBgBrush, null, clipped, 2, 2);
                dc.Pop();
            }
        }

        dc.DrawRoundedRectangle(null, _rulerBorderPen, horizontalStrip, 3, 3);
        dc.DrawRoundedRectangle(null, _rulerBorderPen, verticalStrip, 3, 3);

        double horizontalCenterY = horizontalStrip.Top + horizontalStrip.Height / 2.0;
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

            double centerX = (layout.ColumnCenters[c] - _monitorRect.Left) / dpiX;
            if (centerX < horizontalStrip.Left || centerX > horizontalStrip.Right)
            {
                continue;
            }

            DrawRulerChar(dc, KeyLayout.GetKey(c), new Point(centerX, horizontalCenterY));
        }

        double verticalCenterX = verticalStrip.Left + verticalStrip.Width / 2.0;
        for (int r = 0; r < selectionManager.RowCount; r++)
        {
            if (!IsRowValidForStage2(selectionManager, r, selectedColumn))
            {
                continue;
            }

            double centerY = (layout.RowCenters[r] - _monitorRect.Top) / dpiY;
            if (centerY < verticalStrip.Top || centerY > verticalStrip.Bottom)
            {
                continue;
            }

            DrawRulerChar(dc, KeyLayout.GetKey(r), new Point(verticalCenterX, centerY));
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

    private void DrawRulerChar(DrawingContext dc, char key, Point center)
    {
        const double fontSize = 11.0;
        string text = key.ToString();

        var label = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            fontSize,
            Brushes.White,
            1.0);
        var shadow = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            fontSize,
            _labelShadowBrush,
            1.0);

        double x = center.X - label.Width / 2.0;
        double y = center.Y - label.Height / 2.0;
        dc.DrawText(shadow, new Point(x + 1, y + 1));
        dc.DrawText(label, new Point(x, y));
    }

    private void DrawSectionBadge(DrawingContext dc, string text, Point center, double cellW, double cellH, bool isActive)
    {
        // Dynamically scale font size according to cell dimensions
        double minDim = Math.Min(cellW, cellH);
        double fontSize = Math.Clamp(minDim * 0.35, 10.0, 14.0);

        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            fontSize,
            Brushes.White,
            1.0);

        double badgeWidth = Math.Min(cellW - 2, formattedText.Width + 10);
        double badgeHeight = Math.Min(cellH - 2, formattedText.Height + 4);

        if (badgeWidth <= 0 || badgeHeight <= 0) return;

        var rect = new Rect(center.X - badgeWidth / 2, center.Y - badgeHeight / 2, badgeWidth, badgeHeight);

        var bgBrush = isActive ? _activeBadgeBgBrush : _badgeBgBrush;
        var borderPen = isActive ? _activeBadgeBorderPen : _badgeBorderPen;

        dc.DrawRoundedRectangle(bgBrush, borderPen, rect, 4, 4);

        var textOrigin = new Point(center.X - formattedText.Width / 2, center.Y - formattedText.Height / 2);
        dc.DrawText(formattedText, textOrigin);
    }

    private Rect ToDipRect(PixelRect rect, double dpiX, double dpiY)
    {
        double left = (rect.Left - _monitorRect.Left) / dpiX;
        double top = (rect.Top - _monitorRect.Top) / dpiY;
        double width = rect.Width / dpiX;
        double height = rect.Height / dpiY;
        return new Rect(left, top, width, height);
    }
}
