using System.Runtime.ExceptionServices;
using System.Windows.Media;
using KeyGrid.Core.Geometry;
using KeyGrid.Core.Selection;
using KeyGrid.UI;

namespace KeyGrid.UI.Tests;

public class OverlayRenderElementTests
{
    private static readonly PixelRect LeftMonitor = new(0, 0, 300, 200);
    private static readonly PixelRect RightMonitor = new(300, 0, 300, 200);
    private static readonly PixelRect[] Monitors = { LeftMonitor, RightMonitor };

    [Fact]
    public void Stage1Labels_AreDrawnOnlyOnMonitorOwningEachCell()
    {
        var selection = CreateSelection();

        var leftGlyphs = RenderGlyphs(LeftMonitor, selection);
        var rightGlyphs = RenderGlyphs(RightMonitor, selection);

        Assert.NotEmpty(leftGlyphs);
        Assert.NotEmpty(rightGlyphs);
    }

    [Fact]
    public void Stage1AfterColumnSelection_DrawsOnlyActiveColumnRowKeys()
    {
        var selection = CreateSelection();
        Assert.True(selection.ProcessKey(selection.ColumnKeys[0]));

        var leftGlyphs = RenderGlyphs(LeftMonitor, selection);
        var rightGlyphs = RenderGlyphs(RightMonitor, selection);

        Assert.Equal(selection.RowKeys, leftGlyphs);
        Assert.Empty(rightGlyphs);
    }

    [Fact]
    public void Stage2Cells_HideLetterLabelsAndHighlightTheCursorInRed()
    {
        var selection = CreateSelection();
        SelectFirstStageCell(selection);

        var drawings = RenderDrawings(LeftMonitor, selection);

        Assert.Empty(RenderGlyphs(LeftMonitor, selection));
        Assert.Contains(
            GetGeometryDrawings(drawings),
            drawing => drawing.Brush is SolidColorBrush brush
                && brush.Color == Color.FromArgb(100, 255, 40, 40));
    }

    [Fact]
    public void Stage2ArrowMovement_ChangesTheHighlightedCell()
    {
        var selection = CreateSelection();
        SelectFirstStageCell(selection);
        int initialColumn = selection.SelectedColumnIndex;
        int initialRow = selection.SelectedRowIndex;

        Assert.True(selection.MoveStage2Cursor(1, 0));
        Assert.Equal(initialColumn + 1, selection.SelectedColumnIndex);
        Assert.Equal(initialRow, selection.SelectedRowIndex);

        Assert.Empty(RenderGlyphs(LeftMonitor, selection));
        Assert.Contains(
            GetGeometryDrawings(RenderDrawings(LeftMonitor, selection)),
            drawing => drawing.Brush is SolidColorBrush brush
                && brush.Color == Color.FromArgb(100, 255, 40, 40));

    }

    [Fact]
    public void Stage2Context_DrawsOuterWindowAndCentralFocusBorders()
    {
        var selection = CreateSelection();
        SelectFirstStageCell(selection);

        var drawings = GetGeometryDrawings(RenderDrawings(LeftMonitor, selection)).ToList();

        Assert.Contains(
            drawings,
            drawing => drawing.Pen?.Brush is SolidColorBrush brush
                && brush.Color == Color.FromArgb(120, 0, 200, 255));
        Assert.Contains(
            drawings,
            drawing => drawing.Pen?.Brush is SolidColorBrush brush
                && brush.Color == Color.FromArgb(200, 0, 200, 255));
    }

    [Fact]
    public void RangeStartMarker_IsDrawnOnlyOnTheMonitorContainingTheAnchor()
    {
        var selection = CreateSelection();
        var anchor = new PixelPoint(150, 100);

        var leftDrawings = RenderDrawings(LeftMonitor, selection, rangeStartPoint: anchor);
        var rightDrawings = RenderDrawings(RightMonitor, selection, rangeStartPoint: anchor);

        Assert.Contains(
            GetGeometryDrawings(leftDrawings),
            drawing => drawing.Brush is SolidColorBrush brush
                && brush.Color == Color.FromArgb(90, 80, 255, 160));
        Assert.DoesNotContain(
            GetGeometryDrawings(rightDrawings),
            drawing => drawing.Brush is SolidColorBrush brush
                && brush.Color == Color.FromArgb(90, 80, 255, 160));
    }

    [Fact]
    public void RangeIndicator_IsDrawnOnlyOnThePrimaryOverlayWhenActive()
    {
        var selection = CreateSelection();
        var anchor = new PixelPoint(150, 100);

        string primaryText = new(
            RenderGlyphDetails(LeftMonitor, selection, isPrimary: true, rangeStartPoint: anchor)
                .Select(glyph => glyph.Character)
                .ToArray());
        string secondaryText = new(
            RenderGlyphDetails(RightMonitor, selection, isPrimary: false, rangeStartPoint: anchor)
                .Select(glyph => glyph.Character)
                .ToArray());

        Assert.Contains("Range: Enter to select", primaryText);
        Assert.DoesNotContain("Range: Enter to select", secondaryText);
    }

    [Fact]
    public void ContinuousModeIndicator_IsDrawnOnlyOnThePrimaryOverlayWhenEnabled()
    {
        var selection = CreateSelection();

        string primaryText = new(
            RenderGlyphDetails(LeftMonitor, selection, isPrimary: true, isContinuousMode: true)
                .Select(glyph => glyph.Character)
                .ToArray());
        string secondaryText = new(
            RenderGlyphDetails(RightMonitor, selection, isPrimary: false, isContinuousMode: true)
                .Select(glyph => glyph.Character)
                .ToArray());
        string normalText = new(
            RenderGlyphDetails(LeftMonitor, selection, isPrimary: true, isContinuousMode: false)
                .Select(glyph => glyph.Character)
                .ToArray());

        Assert.Contains("Continuous Mode", primaryText);
        Assert.DoesNotContain("Continuous Mode", secondaryText);
        Assert.DoesNotContain("Continuous Mode", normalText);
    }

    [Fact]
    public void ScrollModeIndicator_IsDrawnOnlyOnThePrimaryOverlayWhenActive()
    {
        var selection = CreateSelection();
        SelectFirstStageCell(selection);

        string primaryText = new(
            RenderGlyphDetails(LeftMonitor, selection, isPrimary: true, isScrollMode: true)
                .Select(glyph => glyph.Character)
                .ToArray());
        string secondaryText = new(
            RenderGlyphDetails(RightMonitor, selection, isPrimary: false, isScrollMode: true)
                .Select(glyph => glyph.Character)
                .ToArray());
        string normalText = new(
            RenderGlyphDetails(LeftMonitor, selection, isPrimary: true, isScrollMode: false)
                .Select(glyph => glyph.Character)
                .ToArray());

        Assert.Contains("Scroll Mode", primaryText);
        Assert.DoesNotContain("Scroll Mode", secondaryText);
        Assert.DoesNotContain("Scroll Mode", normalText);
    }

    private static SelectionManager CreateSelection()
    {
        var selection = new SelectionManager();
        Assert.True(selection.StartSelection(Monitors, 1.0));
        Assert.Equal(5, selection.ColumnCount);
        Assert.Equal(2, selection.RowCount);
        return selection;
    }

    private static void SelectFirstStageCell(SelectionManager selection)
    {
        Assert.True(selection.ProcessKey(selection.ColumnKeys[0]));
        Assert.True(selection.ProcessKey(selection.RowKeys[0]));
        Assert.Equal(2, selection.CurrentStage);
    }

    private static char[] RenderGlyphs(PixelRect monitor, SelectionManager selection)
    {
        return RenderGlyphDetails(monitor, selection)
            .Select(glyph => glyph.Character)
            .ToArray();
    }

    private static RenderedGlyph[] RenderGlyphDetails(
        PixelRect monitor,
        SelectionManager selection,
        bool isPrimary = false,
        bool isContinuousMode = false,
        PixelPoint? rangeStartPoint = null,
        bool isScrollMode = false)
    {
        DrawingGroup drawing = RenderDrawings(
            monitor,
            selection,
            isPrimary,
            isContinuousMode,
            rangeStartPoint,
            isScrollMode);
        return GetGlyphs(drawing).ToArray();
    }

    private static DrawingGroup RenderDrawings(
        PixelRect monitor,
        SelectionManager selection,
        bool isPrimary = false,
        bool isContinuousMode = false,
        PixelPoint? rangeStartPoint = null,
        bool isScrollMode = false)
    {
        DrawingGroup? drawing = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var element = new TestOverlayRenderElement();
                element.UpdateState(
                    monitor,
                    Monitors,
                    selection,
                    isPrimary,
                    isContinuousMode,
                    rangeStartPoint,
                    isScrollMode);
                var result = new DrawingGroup();
                using (var context = result.Open())
                {
                    element.Render(context);
                }

                result.Freeze();
                drawing = result;
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return drawing ?? new DrawingGroup();
    }

    private static IEnumerable<GeometryDrawing> GetGeometryDrawings(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometryDrawing)
        {
            yield return geometryDrawing;
        }
        else if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (var geometry in GetGeometryDrawings(child))
                {
                    yield return geometry;
                }
            }
        }
    }

    private static IEnumerable<RenderedGlyph> GetGlyphs(Drawing drawing, double parentOpacity = 1.0)
    {
        if (drawing is GlyphRunDrawing glyphRunDrawing)
        {
            foreach (char character in glyphRunDrawing.GlyphRun.Characters)
            {
                yield return new RenderedGlyph(
                    character,
                    glyphRunDrawing.GlyphRun.FontRenderingEmSize,
                    parentOpacity);
            }
        }
        else if (drawing is DrawingGroup group)
        {
            double opacity = parentOpacity * group.Opacity;
            foreach (var child in group.Children)
            {
                foreach (var glyph in GetGlyphs(child, opacity))
                {
                    yield return glyph;
                }
            }
        }
    }

    private readonly record struct RenderedGlyph(char Character, double FontSize, double Opacity);

    private sealed class TestOverlayRenderElement : OverlayRenderElement
    {
        public void Render(DrawingContext context) => base.OnRender(context);
    }
}
