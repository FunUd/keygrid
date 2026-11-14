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
/// selection masks, column/row labels, and target crosshairs on an overlay window.
/// </summary>
public class OverlayRenderElement : FrameworkElement
{
    private PixelRect _monitorRect;
    private SelectionManager? _selectionManager;

    private readonly Brush _dimBrush = new SolidColorBrush(Color.FromArgb(140, 10, 15, 25));
    private readonly Brush _columnHighlightBrush = new SolidColorBrush(Color.FromArgb(50, 0, 180, 255));
    private readonly Brush _cellHighlightBrush = new SolidColorBrush(Color.FromArgb(70, 0, 255, 150));
    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1.0);
    private readonly Pen _bboxPen = new(new SolidColorBrush(Color.FromArgb(200, 0, 200, 255)), 2.0);
    private readonly Pen _crosshairPen = new(new SolidColorBrush(Color.FromArgb(240, 255, 50, 50)), 2.0);
    private readonly Brush _labelBgBrush = new SolidColorBrush(Color.FromArgb(230, 20, 25, 35));
    private readonly Pen _labelBorderPen = new(new SolidColorBrush(Color.FromArgb(200, 0, 200, 255)), 1.0);
    private readonly Typeface _labelTypeface = new("Segoe UI");

    public OverlayRenderElement()
    {
        _dimBrush.Freeze();
        _columnHighlightBrush.Freeze();
        _cellHighlightBrush.Freeze();
        _gridPen.Freeze();
        _bboxPen.Freeze();
        _crosshairPen.Freeze();
        _labelBgBrush.Freeze();
        _labelBorderPen.Freeze();
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

        // 4. Draw Grid Lines
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

        // 5. Draw Column Labels (Top edge)
        DrawColumnLabels(dc, dpiX, dpiY);

        // 6. Draw Row Labels (Left edge)
        DrawRowLabels(dc, dpiX, dpiY);

        // 7. Draw Target Crosshair in AwaitingConfirmation state
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

    private void DrawColumnLabels(DrawingContext dc, double dpiX, double dpiY)
    {
        if (_selectionManager == null) return;

        for (int c = 0; c < _selectionManager.ColumnCount; c++)
        {
            if (!GridCalculator.IsColumnValid(c, _selectionManager.XBoundaries, _selectionManager.CurrentBoundingBox, _selectionManager.CurrentSelection))
            {
                continue;
            }

            int left = _selectionManager.XBoundaries[c];
            int right = _selectionManager.XBoundaries[c + 1];
            int centerX = left + (right - left) / 2;

            // Find top-most Y coordinate in this column's selection
            int topY = _selectionManager.CurrentBoundingBox.Top;
            var colSlice = PixelRect.FromEdges(left, _selectionManager.CurrentBoundingBox.Top, right, _selectionManager.CurrentBoundingBox.Bottom);
            foreach (var rect in _selectionManager.CurrentSelection)
            {
                var isect = rect.Intersect(colSlice);
                if (!isect.IsEmpty && isect.Top < topY)
                {
                    topY = isect.Top;
                }
            }

            if (_monitorRect.Contains(centerX, topY))
            {
                double xDip = (centerX - _monitorRect.Left) / dpiX;
                double yDip = (topY - _monitorRect.Top) / dpiY;
                char keyChar = KeyLayout.GetKey(c);
                DrawBadge(dc, keyChar.ToString(), new Point(xDip, yDip + 14));
            }
        }
    }

    private void DrawRowLabels(DrawingContext dc, double dpiX, double dpiY)
    {
        if (_selectionManager == null) return;

        for (int r = 0; r < _selectionManager.RowCount; r++)
        {
            // Row is valid if at least one cell in row is valid
            bool rowValid = false;
            for (int c = 0; c < _selectionManager.ColumnCount; c++)
            {
                if (GridCalculator.IsCellValid(c, r, _selectionManager.XBoundaries, _selectionManager.YBoundaries, _selectionManager.CurrentSelection))
                {
                    rowValid = true;
                    break;
                }
            }

            if (!rowValid) continue;

            int top = _selectionManager.YBoundaries[r];
            int bottom = _selectionManager.YBoundaries[r + 1];
            int centerY = top + (bottom - top) / 2;

            // Find left-most X coordinate in this row's selection
            int leftX = _selectionManager.CurrentBoundingBox.Left;
            var rowSlice = PixelRect.FromEdges(_selectionManager.CurrentBoundingBox.Left, top, _selectionManager.CurrentBoundingBox.Right, bottom);
            foreach (var rect in _selectionManager.CurrentSelection)
            {
                var isect = rect.Intersect(rowSlice);
                if (!isect.IsEmpty && isect.Left < leftX)
                {
                    leftX = isect.Left;
                }
            }

            if (_monitorRect.Contains(leftX, centerY))
            {
                double xDip = (leftX - _monitorRect.Left) / dpiX;
                double yDip = (centerY - _monitorRect.Top) / dpiY;
                char keyChar = KeyLayout.GetKey(r);
                DrawBadge(dc, keyChar.ToString(), new Point(xDip + 14, yDip));
            }
        }
    }

    private void DrawBadge(DrawingContext dc, string text, Point center)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            13,
            Brushes.White,
            1.0);

        double badgeWidth = Math.Max(20, formattedText.Width + 8);
        double badgeHeight = Math.Max(20, formattedText.Height + 4);

        var rect = new Rect(center.X - badgeWidth / 2, center.Y - badgeHeight / 2, badgeWidth, badgeHeight);
        dc.DrawRoundedRectangle(_labelBgBrush, _labelBorderPen, rect, 4, 4);

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
