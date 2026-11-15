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

        Assert.Equal(8, leftGlyphs.Length);
        Assert.Equal(4, rightGlyphs.Length);
        Assert.Equal(4, leftGlyphs.Count(selection.ColumnKeys.Contains));
        Assert.Equal(4, leftGlyphs.Count(selection.RowKeys.Contains));
        Assert.Equal(2, rightGlyphs.Count(selection.ColumnKeys.Contains));
        Assert.Equal(2, rightGlyphs.Count(selection.RowKeys.Contains));
    }

    [Fact]
    public void Stage1AfterColumnSelection_DrawsOnlyActiveColumnRowKeys()
    {
        var selection = CreateSelection();
        Assert.True(selection.ProcessKey(selection.ColumnKeys[1]));

        var leftGlyphs = RenderGlyphs(LeftMonitor, selection);
        var rightGlyphs = RenderGlyphs(RightMonitor, selection);

        Assert.Equal(selection.RowKeys.Take(2), leftGlyphs);
        Assert.Empty(rightGlyphs);
    }

    [Fact]
    public void Stage2Rulers_DrawValidKeysOnlyOnOwningMonitor()
    {
        var selection = CreateSelection();
        SelectFirstStageCell(selection);

        var leftGlyphs = RenderGlyphs(LeftMonitor, selection);
        var rightGlyphs = RenderGlyphs(RightMonitor, selection);
        var expected = selection.ColumnKeys.Take(3)
            .Concat(selection.RowKeys.Take(2))
            .SelectMany(key => new[] { key, key })
            .OrderBy(key => key);

        Assert.Equal(expected, leftGlyphs.OrderBy(key => key));
        Assert.Empty(rightGlyphs);
    }

    [Fact]
    public void Stage2AfterSubColumnSelection_SuppressesOtherSubColumnLabels()
    {
        var selection = CreateSelection();
        SelectFirstStageCell(selection);
        Assert.True(selection.ProcessKey(selection.ColumnKeys[1]));

        var glyphs = RenderGlyphs(LeftMonitor, selection);
        var expected = new[]
        {
            selection.ColumnKeys[1],
            selection.ColumnKeys[1],
            selection.RowKeys[0],
            selection.RowKeys[0],
            selection.RowKeys[1],
            selection.RowKeys[1]
        }.OrderBy(key => key);

        Assert.Equal(expected, glyphs.OrderBy(key => key));
    }

    [Fact]
    public void Stage2CellSpanningMonitors_ShowsEachSubColumnKeyOnVisibleMonitor()
    {
        var selection = CreateSelection();
        Assert.True(selection.ProcessKey(selection.ColumnKeys[1]));
        Assert.True(selection.ProcessKey(selection.RowKeys[0]));

        var leftGlyphs = RenderGlyphs(LeftMonitor, selection);
        var rightGlyphs = RenderGlyphs(RightMonitor, selection);

        Assert.Equal(2, leftGlyphs.Count(selection.ColumnKeys[0]));
        Assert.Equal(0, leftGlyphs.Count(selection.ColumnKeys[1]));
        Assert.Equal(0, leftGlyphs.Count(selection.ColumnKeys[2]));
        Assert.Equal(0, rightGlyphs.Count(selection.ColumnKeys[0]));
        Assert.Equal(2, rightGlyphs.Count(selection.ColumnKeys[1]));
        Assert.Equal(2, rightGlyphs.Count(selection.ColumnKeys[2]));
        Assert.Equal(2, leftGlyphs.Count(selection.RowKeys[0]));
        Assert.Equal(2, leftGlyphs.Count(selection.RowKeys[1]));
        Assert.Equal(0, rightGlyphs.Count(selection.RowKeys[0]));
        Assert.Equal(0, rightGlyphs.Count(selection.RowKeys[1]));
    }

    private static SelectionManager CreateSelection()
    {
        var selection = new SelectionManager();
        Assert.True(selection.StartSelection(Monitors, targetPixels: 100));
        Assert.Equal(3, selection.ColumnCount);
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
        char[]? glyphs = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var element = new TestOverlayRenderElement();
                element.UpdateState(monitor, Monitors, selection);
                var drawing = new DrawingGroup();
                using (var context = drawing.Open())
                {
                    element.Render(context);
                }

                glyphs = GetGlyphs(drawing).ToArray();
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

        return glyphs ?? Array.Empty<char>();
    }

    private static IEnumerable<char> GetGlyphs(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyphRunDrawing)
        {
            foreach (char character in glyphRunDrawing.GlyphRun.Characters)
            {
                yield return character;
            }
        }
        else if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (char character in GetGlyphs(child))
                {
                    yield return character;
                }
            }
        }
    }

    private sealed class TestOverlayRenderElement : OverlayRenderElement
    {
        public void Render(DrawingContext context) => base.OnRender(context);
    }
}
