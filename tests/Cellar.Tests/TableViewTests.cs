using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Widgets;
using Xunit;

namespace Cellar.Tests;

public enum Phase { Pending, Running, Failed }

public sealed record Pod(string Name, Phase Phase, int Restarts, TimeSpan Age);

public class TableViewTests
{
    private static KeyEvent K(Key key) => new(key, default, KeyModifiers.None);
    private static KeyEvent C(char c) => new(Key.Char, new Rune(c), KeyModifiers.None);

    private static List<Pod> Pods(int n = 10) => Enumerable.Range(0, n)
        .Select(i => new Pod($"pod-{i:00}", (Phase)(i % 3), i, TimeSpan.FromMinutes(i * 5)))
        .ToList();

    private static Table<Pod> PodTable(bool keyed = true)
    {
        var t = new Table<Pod> { Sortable = true, ShowHeader = true, HasFocus = true };
        if (keyed)
        {
            t.RowKey = p => p.Name;
        }

        t.Columns.Add(new TableColumn<Pod>("Name", Constraint.Length(8), p => p.Name));
        t.Columns.Add(new TableColumn<Pod>("Phase", Constraint.Length(8), p => p.Phase.ToString()) { SortKey = p => p.Phase, Aliases = ["st", "status"] });
        t.Columns.Add(new TableColumn<Pod>("Restarts", Constraint.Length(3), p => p.Restarts.ToString(CultureInfo.InvariantCulture)) { SortKey = p => p.Restarts });
        t.Columns.Add(new TableColumn<Pod>("Age", Constraint.Length(5), p => $"{p.Age.TotalMinutes:0}m") { SortKey = p => p.Age });
        return t;
    }

    private static Surface Draw(Widget w, int width = 30, int height = 6)
    {
        var s = new Surface(width, height);
        w.Render(s, s.Bounds);
        return s;
    }

    // ---- (a) row keys ----

    [Fact]
    public void WithoutRowKey_SetRowsStillResetsToTheTop()
    {
        var t = PodTable(keyed: false);
        t.SetRows(Pods());
        t.Select(4);
        t.SetRows(Pods());
        Assert.Equal(0, t.SelectedIndex);
        Assert.Equal(0, t.ScrollOffset);
    }

    [Fact]
    public void RowKey_KeepsTheSelectedRowAndItsPlaceOnScreen()
    {
        var t = PodTable();
        t.SetRows(Pods(20));
        Draw(t, height: 6); // 5 data rows
        t.Select(7);
        Assert.Equal(3, t.ScrollOffset);

        // Two rows appear above the selection; it stays on pod-07, still 4 lines from the top.
        var refreshed = Pods(20);
        refreshed.Insert(0, new Pod("new-a", Phase.Pending, 0, TimeSpan.Zero));
        refreshed.Insert(0, new Pod("new-b", Phase.Pending, 0, TimeSpan.Zero));
        t.SetRows(refreshed);
        Assert.Equal("pod-07", t.Selected!.Name);
        Assert.Equal(9, t.SelectedIndex);
        Assert.Equal(t.SelectedIndex - 4, t.ScrollOffset);
    }

    [Fact]
    public void RowKey_WhenTheSelectedRowGoes_SelectsTheRowNowInItsPlace()
    {
        var t = PodTable();
        t.SetRows(Pods(5));
        t.Select(2);
        t.SetRows(Pods(5).Where(p => p.Name != "pod-02"));
        Assert.Equal("pod-03", t.Selected!.Name);

        t.Select(3);
        t.SetRows(Pods(3));
        Assert.Equal("pod-02", t.Selected!.Name);
    }

    [Fact]
    public void RowKey_TracksAddedChangedAndRemovedRows()
    {
        var t = PodTable();
        t.SetRows(Pods(3));
        Assert.Empty(t.AddedRows); // the first load isn't "new"

        var next = Pods(3).Where(p => p.Name != "pod-01").ToList();
        next[1] = next[1] with { Restarts = 9 };
        next.Add(new Pod("pod-99", Phase.Running, 0, TimeSpan.Zero));
        t.SetRows(next);

        Assert.Equal(["pod-99"], t.AddedRows.Select(p => p.Name));
        Assert.Equal(["pod-02"], t.ChangedRows.Select(p => p.Name));
        Assert.Equal(["pod-01"], t.RemovedRows.Select(p => p.Name));
        Assert.True(t.IsNew(next[2]));
        Assert.True(t.IsCellChanged(next[1], 2));
        Assert.False(t.IsCellChanged(next[1], 0));
        Assert.False(t.IsChanged(next[0]));
    }

    [Fact]
    public void HighlightChanges_DrawsChangedCellsBoldUntilTheNextRefresh()
    {
        var t = PodTable();
        t.HighlightChanges = true;
        t.SetRows(Pods(2));
        var next = Pods(2);
        next[1] = next[1] with { Restarts = 7 };
        t.SetRows(next);
        var s = Draw(t, 30, 3);
        int restartsX = 18; // Name 8 + 1 + Phase 8 + 1
        Assert.True(s.Get(restartsX, 2).Style.Attributes.HasFlag(TextAttributes.Bold));
        Assert.False(s.Get(0, 2).Style.Attributes.HasFlag(TextAttributes.Bold));

        t.SetRows(next);
        s = Draw(t, 30, 3);
        Assert.False(s.Get(restartsX, 2).Style.Attributes.HasFlag(TextAttributes.Bold));
    }

    // ---- (b) empty text ----

    [Fact]
    public void EmptyText_IsDrawnCenteredAndMuted()
    {
        var t = PodTable();
        t.EmptyText = "no pods";
        var s = Draw(t, 21, 3);
        Assert.Equal("       no pods", SurfaceText.Row(s, 1).TrimEnd());
        Assert.Equal(Theming.Theme.Current.Muted, s.Get(7, 1).Style.Foreground);

        var strings = new Table { EmptyText = "nothing" };
        strings.Columns.Add(new Column("A", Constraint.Fill()));
        Assert.Contains("nothing", SurfaceText.Row(Draw(strings, 20, 3), 1), StringComparison.Ordinal);
    }

    // ---- (c) filter ----

    [Fact]
    public void Filter_HidesRowsButKeepsThemInRows()
    {
        var t = PodTable();
        t.SetRows(Pods(6));
        t.Select(4); // pod-04, Running
        t.Filter = p => p.Phase == Phase.Running;
        Assert.Equal(6, t.Rows.Count);
        Assert.Equal(["pod-01", "pod-04"], t.FilteredRows.Select(p => p.Name));
        Assert.Equal("pod-04", t.Selected!.Name);
        Assert.Equal(1, t.SelectedIndex);
        Assert.Equal("pod-01", SurfaceText.Row(Draw(t), 1)[..6]);

        // The selected row filtered out: back to the first match.
        t.Filter = p => p.Name.EndsWith("2", StringComparison.Ordinal) || p.Name.EndsWith("5", StringComparison.Ordinal);
        Assert.Equal("pod-02", t.Selected!.Name);

        t.Filter = null;
        Assert.Equal(6, t.FilteredRows.Count);
    }

    [Fact]
    public void Filter_KeysAndCallbacksUseTheVisibleRows()
    {
        var t = PodTable();
        var selected = new List<string>();
        t.OnSelect = p => selected.Add(p.Name);
        t.SetRows(Pods(6));
        t.Filter = p => p.Phase == Phase.Failed; // pod-02, pod-05
        t.OnEvent(K(Key.Down));
        t.OnEvent(K(Key.Down));
        Assert.Equal(["pod-05"], selected);
        t.OnEvent(K(Key.End));
        Assert.Equal(1, t.SelectedIndex);
    }

    [Fact]
    public void Sorting_KeepsTheSelectedRowWhenFiltered()
    {
        var t = PodTable();
        t.SetRows(Pods(6));
        t.Filter = p => p.Restarts > 0;
        t.SelectRow(t.Rows[2]);
        t.SortBy(2, SortState.Descending);
        Assert.Equal("pod-02", t.Selected!.Name);
        Assert.Equal("pod-05", t.FilteredRows[0].Name);
    }

    [Fact]
    public void EditingRowsInPlace_IsPickedUpByRefreshView()
    {
        var t = PodTable();
        t.SetRows(Pods(4));
        t.Filter = p => p.Restarts >= 2;
        Assert.Equal(2, t.FilteredRows.Count);
        t.Rows.Add(new Pod("late", Phase.Running, 5, TimeSpan.Zero)); // count change is noticed on its own
        Assert.Equal(3, t.FilteredRows.Count);
        t.Rows[0] = t.Rows[0] with { Restarts = 8 };
        t.RefreshView();
        Assert.Equal(4, t.FilteredRows.Count);
    }

    // ---- (d) query ----

    [Theory]
    [InlineData("phase=running", new[] { "pod-01", "pod-04", "pod-07" })]
    [InlineData("status=RUNNING restarts>3", new[] { "pod-04", "pod-07" })]
    [InlineData("phase!=running restarts<=2", new[] { "pod-00", "pod-02" })]
    [InlineData("name~0", new[] { "pod-00", "pod-01", "pod-02", "pod-03", "pod-04", "pod-05", "pod-06", "pod-07" })]
    [InlineData("name!~pod-0", new string[0])]
    [InlineData("age>=30m", new[] { "pod-06", "pod-07" })]
    [InlineData("age<1h st=failed", new[] { "pod-02", "pod-05" })]
    [InlineData("failed", new[] { "pod-02", "pod-05" })]
    [InlineData("!pod-0", new string[0])]
    [InlineData("pod 7", new[] { "pod-07" })]
    [InlineData("phase>pending phase<failed", new[] { "pod-01", "pod-04", "pod-07" })]
    [InlineData("restarts=4", new[] { "pod-04" })]
    [InlineData("restarts>2.5 restarts<4", new[] { "pod-03" })]
    public void Query_FiltersByColumnsAndWords(string query, string[] expected)
    {
        var t = PodTable();
        t.SetRows(Pods(8));
        t.Query = TableQuery.Parse(query);
        Assert.Equal(expected, t.FilteredRows.Select(p => p.Name));
    }

    [Fact]
    public void Query_UnknownColumnIsSearchedAsText()
    {
        var t = PodTable();
        t.SetRows(new[] { new Pod("a=b", Phase.Running, 0, TimeSpan.Zero), new Pod("c", Phase.Running, 0, TimeSpan.Zero) });
        t.Query = TableQuery.Parse("a=b");
        Assert.Equal(["a=b"], t.FilteredRows.Select(p => p.Name));
    }

    [Fact]
    public void Query_ParsesQuotesAndOperators()
    {
        var q = TableQuery.Parse("node=\"node 1\" !x \"two words\" n>=3");
        Assert.Equal(
            [
                new TableQueryTerm("node", TableQueryOperator.Equal, "node 1"),
                new TableQueryTerm(null, TableQueryOperator.NotContains, "x"),
                new TableQueryTerm(null, TableQueryOperator.Contains, "two words"),
                new TableQueryTerm("n", TableQueryOperator.GreaterOrEqual, "3"),
            ],
            q.Terms);
        Assert.True(TableQuery.Parse("  ").IsEmpty);
    }

    [Theory]
    [InlineData("90s", 90)]
    [InlineData("5m", 300)]
    [InlineData("1h30m", 5400)]
    [InlineData("2d", 172800)]
    [InlineData("00:01:00", 60)]
    public void Query_ParsesDurations(string text, int seconds)
    {
        Assert.True(TableQuery.TryDuration(text, out var d));
        Assert.Equal(TimeSpan.FromSeconds(seconds), d);
    }

    [Fact]
    public void Query_ComparesUntypedColumnsAsNumbersWhenTheyAre()
    {
        var t = new Table<string[]>();
        t.Columns.Add(new TableColumn<string[]>("Size", Constraint.Fill(), r => r[0]));
        t.SetRows(new[] { new[] { "9" }, new[] { "10" }, new[] { "100" } });
        t.Query = TableQuery.Parse("size>9");
        Assert.Equal(["10", "100"], t.FilteredRows.Select(r => r[0]));
    }

    // ---- (e) grouping ----

    [Fact]
    public void GroupBy_ShowsHeadersWithCountsAndSummaries()
    {
        var t = PodTable();
        t.GroupSummary = g => $"{g.Rows.Sum(p => p.Restarts)} restarts";
        t.SetRows(Pods(6));
        t.GroupBy("phase");
        var s = Draw(t, 40, 10);
        Assert.Equal("▾ Pending  (2)  3 restarts", SurfaceText.Row(s, 1).TrimEnd());
        Assert.StartsWith("pod-00", SurfaceText.Row(s, 2));
        Assert.StartsWith("pod-03", SurfaceText.Row(s, 3));
        Assert.StartsWith("▾ Running  (2)", SurfaceText.Row(s, 4));
        Assert.Equal(["Pending", "Running", "Failed"], t.Groups.Select(g => g.Value));

        // Grouping keeps the selected row; the header above it is line 0.
        Assert.Equal("pod-00", t.Selected!.Name);
        Assert.Equal(1, t.SelectedIndex);
        t.Select(0);
        Assert.False(t.HasSelectedRow);
        Assert.Equal("Pending", t.SelectedGroup!.Value);
    }

    [Fact]
    public void GroupHeaders_CollapseAndExpandWithKeys()
    {
        var t = PodTable();
        var activated = new List<string>();
        t.OnActivate = p => activated.Add(p.Name);
        t.SetRows(Pods(6));
        t.GroupBy(1);
        t.Select(0);

        t.OnEvent(K(Key.Enter)); // on the Pending header
        Assert.True(t.IsCollapsed("Pending"));
        Assert.StartsWith("▸ Pending", SurfaceText.Row(Draw(t, 40, 10), 1));
        Assert.StartsWith("▾ Running", SurfaceText.Row(Draw(t, 40, 10), 2));
        Assert.Empty(activated);

        t.OnEvent(K(Key.Right));
        Assert.False(t.IsCollapsed("Pending"));

        // ← on a row folds its group and selects the header.
        t.OnEvent(K(Key.Down));
        Assert.Equal("pod-00", t.Selected!.Name);
        t.OnEvent(K(Key.Left));
        Assert.True(t.IsCollapsed("Pending"));
        Assert.Equal(0, t.SelectedIndex);

        t.OnEvent(C(' '));
        Assert.False(t.IsCollapsed("Pending"));
        t.CollapseAll();
        Assert.Equal(3, t.Groups.Count(g => g.Collapsed));
        t.ExpandAll();
        t.Ungroup();
        Assert.Equal("pod-00", t.Selected?.Name ?? t.Rows[t.SelectedIndex].Name);
    }

    [Fact]
    public void Grouping_FollowsSortingAndFiltering()
    {
        var t = PodTable();
        t.SetRows(Pods(6));
        t.GroupBy(1);
        t.SortBy(1, SortState.Descending);
        Assert.Equal(["Failed", "Running", "Pending"], t.Groups.Select(g => g.Value));
        t.Filter = p => p.Restarts >= 3;
        Assert.Equal([1, 1, 1], t.Groups.Select(g => g.Rows.Count));
        t.Filter = p => p.Phase != Phase.Running;
        Assert.Equal(["Failed", "Pending"], t.Groups.Select(g => g.Value));
    }

    // ---- (f) marks ----

    [Fact]
    public void Marks_ToggleWithSpaceAndStar()
    {
        var t = PodTable();
        int changes = 0;
        t.Markable = true;
        t.OnMarksChanged = () => changes++;
        t.SetRows(Pods(4));
        t.OnEvent(C(' '));
        t.OnEvent(K(Key.Down));
        t.OnEvent(K(Key.Down));
        t.OnEvent(C(' '));
        Assert.Equal(["pod-00", "pod-02"], t.Marked.Select(p => p.Name));

        var s = Draw(t, 30, 5);
        Assert.Equal("●", s.Get(0, 1).Grapheme);
        Assert.Equal(" ", SurfaceText.Row(s, 2)[..1]);
        Assert.StartsWith("  pod-01", SurfaceText.Row(s, 2));

        t.OnEvent(C('*'));
        Assert.Equal(4, t.Marked.Count);
        t.OnEvent(C('*'));
        Assert.Empty(t.Marked);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Marks_StarMarksOnlyFilteredRows_AndSurviveRefreshByKey()
    {
        var t = PodTable();
        t.Markable = true;
        t.SetRows(Pods(6));
        t.Filter = p => p.Phase == Phase.Failed;
        t.OnEvent(C('*'));
        Assert.Equal(["pod-02", "pod-05"], t.Marked.Select(p => p.Name));

        t.SetRows(Pods(6).Select(p => p with { Restarts = p.Restarts + 1 }).Where(p => p.Name != "pod-05"));
        Assert.Equal(["pod-02"], t.Marked.Select(p => p.Name));
        Assert.True(t.IsMarked(t.Rows.First(p => p.Name == "pod-02")));
    }

    [Fact]
    public void Marks_DoNotAffectSpaceWhenOff()
    {
        var t = PodTable();
        t.SetRows(Pods(2));
        Assert.False(t.OnEvent(C(' ')));
        Assert.Empty(t.Marked);
    }
}
