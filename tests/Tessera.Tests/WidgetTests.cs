using Tessera.Layout;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Text;
using Tessera.Widgets;

namespace Tessera.Tests;

public class WidgetTests
{
    private static string RowText(Surface s, int y)
    {
        var sb = new System.Text.StringBuilder();
        for (int x = 0; x < s.Width; x++)
        {
            var g = s.Get(x, y).Grapheme;
            sb.Append(g.Length == 0 ? "" : g);
        }
        return sb.ToString();
    }

    [Fact]
    public void Text_LeftAligned()
    {
        var s = new Surface(10, 1);
        new Label("hi").Render(s, s.Bounds);
        Assert.StartsWith("hi", RowText(s, 0));
    }

    [Fact]
    public void Text_RightAligned()
    {
        var s = new Surface(10, 1);
        Label.Plain("hi", Style.Default, Justify.Right).Render(s, s.Bounds);
        Assert.Equal("        hi", RowText(s, 0));
    }

    [Fact]
    public void Border_DrawsCornersAndTitle()
    {
        var s = new Surface(12, 3);
        new Border(null, "T").Render(s, s.Bounds);
        Assert.Equal("╭", s.Get(0, 0).Grapheme);
        Assert.Equal("╮", s.Get(11, 0).Grapheme);
        Assert.Equal("╰", s.Get(0, 2).Grapheme);
        Assert.Equal("╯", s.Get(11, 2).Grapheme);
        Assert.Contains("T", RowText(s, 0));
    }

    [Fact]
    public void Border_RendersChildInside()
    {
        var s = new Surface(12, 3);
        new Border(new Label("x")).Render(s, s.Bounds);
        Assert.Equal("x", s.Get(1, 1).Grapheme); // inside the frame
    }

    [Fact]
    public void Stack_TilesChildrenVertically()
    {
        var s = new Surface(10, 4);
        var stack = new Stack(Direction.Vertical)
            .Add(new Label("top"), Constraint.Length(1))
            .Add(new Label("bot"), Constraint.Fill());
        stack.Render(s, s.Bounds);
        Assert.StartsWith("top", RowText(s, 0));
        Assert.StartsWith("bot", RowText(s, 1));
    }

    [Fact]
    public void Table_RendersHeaderAndRows()
    {
        var s = new Surface(30, 5);
        var t = new Table();
        t.Columns.Add(new Column("Name", Constraint.Fill()));
        t.Columns.Add(new Column("Val", Constraint.Length(6), Justify.Right));
        t.Rows.Add(["alpha", "10"]);
        t.Rows.Add(["beta", "20"]);
        t.Render(s, s.Bounds);

        Assert.Contains("Name", RowText(s, 0));
        Assert.Contains("alpha", RowText(s, 1));
        Assert.Contains("beta", RowText(s, 2));
    }

    [Fact]
    public void Table_CycleSort_NoneAscDescNone()
    {
        var t = new Table { Sortable = true };
        t.Columns.Add(new Column("V", Constraint.Fill()) { SortKey = s => int.Parse(s) });
        t.Rows.Add(["1"]);
        t.Rows.Add(["3"]);
        t.Rows.Add(["2"]);

        t.CycleSort(0); // None -> Ascending
        Assert.Equal(SortState.Ascending, t.SortState);
        Assert.Equal("1", t.Rows[0][0]);

        t.CycleSort(0); // Ascending -> Descending
        Assert.Equal(SortState.Descending, t.SortState);
        Assert.Equal("3", t.Rows[0][0]);

        t.CycleSort(0); // Descending -> None (restores insertion order)
        Assert.Equal(SortState.None, t.SortState);
        Assert.Equal("1", t.Rows[0][0]);
        Assert.Equal("3", t.Rows[1][0]);
        Assert.Equal("2", t.Rows[2][0]);
    }

    [Fact]
    public void Table_StringColumn_SortsLexicographically()
    {
        var t = new Table { Sortable = true };
        t.Columns.Add(new Column("Name", Constraint.Fill())); // no SortKey => string sort
        t.Rows.Add(["charlie"]);
        t.Rows.Add(["alpha"]);
        t.Rows.Add(["bravo"]);
        t.SortBy(0, SortState.Ascending);
        Assert.Equal("alpha", t.Rows[0][0]);
        Assert.Equal("bravo", t.Rows[1][0]);
        Assert.Equal("charlie", t.Rows[2][0]);
    }

    [Fact]
    public void Table_HeaderClick_SortsColumn()
    {
        var t = new Table { Sortable = true };
        t.Columns.Add(new Column("Name", Constraint.Fill()));
        t.Columns.Add(new Column("Val", Constraint.Length(6), Justify.Right)
            { SortKey = s => int.Parse(s) });
        t.Rows.Add(["a", "30"]);
        t.Rows.Add(["b", "10"]);
        t.Rows.Add(["c", "20"]);

        var s = new Surface(30, 5);
        t.Render(s, s.Bounds); // header on row 0, records hit ranges

        // Click within the "Val" column header (right side of the 30-wide table).
        t.OnEvent(new Terminal.MouseEvent(Terminal.MouseEventKind.Down,
            Terminal.MouseButton.Left, 27, 0, Terminal.KeyModifiers.None));
        Assert.Equal(1, t.SortColumn);
        Assert.Equal("10", t.Rows[0][1]); // sorted ascending by Val
    }

    [Fact]
    public void Table_SortArrow_ShownInHeader()
    {
        var t = new Table { Sortable = true };
        t.Columns.Add(new Column("V", Constraint.Fill()) { SortKey = s => int.Parse(s) });
        t.Rows.Add(["2"]);
        t.Rows.Add(["1"]);
        t.SortBy(0, SortState.Ascending);
        var s = new Surface(20, 3);
        t.Render(s, s.Bounds);
        Assert.Contains("▲", RowText(s, 0));
    }

    [Fact]
    public void Table_TruncatesOverflowingCell()
    {
        var s = new Surface(8, 2);
        var t = new Table { ShowHeader = false };
        t.Columns.Add(new Column("C", Constraint.Length(5)));
        t.Rows.Add(["abcdefghij"]);
        t.Render(s, s.Bounds);
        Assert.Contains("…", RowText(s, 0)); // ellipsis on truncation
    }

    [Fact]
    public void Table_MoveSelection_ScrollsViewport()
    {
        var t = new Table { ShowHeader = false, SelectedIndex = 0 };
        t.Columns.Add(new Column("C", Constraint.Fill()));
        for (int i = 0; i < 20; i++)
        {
            t.Rows.Add([$"row{i}"]);
        }

        // Viewport of 5 rows; move down past the bottom.
        for (int i = 0; i < 10; i++)
        {
            t.MoveSelection(1, 5);
        }

        Assert.Equal(10, t.SelectedIndex);
        Assert.True(t.ScrollOffset > 0); // scrolled to keep selection visible
        Assert.True(t.SelectedIndex >= t.ScrollOffset);
        Assert.True(t.SelectedIndex < t.ScrollOffset + 5);
    }
}
