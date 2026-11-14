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
/// selection masks, 2-letter section labels inside cells, and target crosshairs.
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
    private readonly Pen _crosshairPen = new(new SolidColorBrush(Color.FromArgb(240, 255, 50, 50)), 2.0);

    // Normal cell badge
    private readonly Brush _badgeBgBrush = new SolidColorBrush(Color.FromArgb(220, 18, 22, 32));
    private readonly Pen _badgeBorderPen = new(new SolidColorBrush(Color.FromArgb(180, 0, 210, 255)), 1.2);

    // Highlighted column cell badge
    private readonly Brush _activeBadgeBgBrush = new SolidColorBrush(Color.FromArgb(240, 0, 120, 215));
    private readonly Pen _activeBadgeBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 1.5);

    private readonly Typeface _labelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public OverlayRenderElement()
    {
        _dimBrush.Freeze();
        _columnHighlightBrush.Freeze();
        _cellHighlightBrush.Freeze();
        _gridPen.Freeze();
        _bboxPen.Freeze();
        _crosshairPen.Freeze();
        _badgeBgBrush.Freeze();
        _badgeBorderPen.Freeze();
        _activeBadgeBgBrush.Freeze();
        _activeBadgeBorderPen.Freeze();
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

        // 6. Draw Target Crosshair in AwaitingConfirmation state
        if (_selectionManager.State == SelectionState.AwaitingConfirmation && _selectionManager.TargetCoordinate.HasValue)
        {
            var target = _selectionManager.TargetCoordinate.Value;
            if (_monitorRect.Contains(target))
            {
                double tx = (target.X - _monitorRect.Left) / dpiX;
                double ty = (target.Y - _monitorRect.Top) / dpiY;
                DrawCrosshair(dc, tx, ty);
            }
        }
    }

    /// <summary>
    /// Displays 2-letter labels (Column key + Row key) directly inside each valid grid section,
    /// eliminating the need to look at screen edges.
    /// </summary>
    private void DrawCellSectionLabels(DrawingContext dc, double dpiX, double dpiY)
    {
        if (_selectionManager == null) return;

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

    private void DrawCrosshair(DrawingContext dc, double x, double y)
    {
        double radius = 8;
        double arm = 14;

        dc.DrawEllipse(null, _crosshairPen, new Point(x, y), radius, radius);
        dc.DrawLine(_crosshairPen, new Point(x - arm, y), new Point(x - radius, y));
        dc.DrawLine(_crosshairPen, new Point(x + radius, y), new Point(x + arm, y));
        dc.DrawLine(_crosshairPen, new Point(x, y - arm), new Point(x, y - radius));
        dc.DrawLine(_crosshairPen, new Point(x, y + radius), new Point(x, y + arm));
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
