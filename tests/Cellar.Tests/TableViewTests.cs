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
using Cellar.Theming;
using Cellar.Widgets;
using Xunit;

namespace Cellar.Tests;

public enum JobState { Pending, Running, Failed }

public sealed record Job(string Name, JobState State, int Retries, TimeSpan Age);

public class TableViewTests
{
    private static KeyEvent K(Key key) => new(key, default, KeyModifiers.None);
    private static KeyEvent C(char c) => new(Key.Char, new Rune(c), KeyModifiers.None);

    private static List<Job> Jobs(int n = 10) => Enumerable.Range(0, n)
        .Select(i => new Job($"job-{i:00}", (JobState)(i % 3), i, TimeSpan.FromMinutes(i * 5)))
        .ToList();

    private static Table<Job> JobTable(bool keyed = true)
    {
        var t = new Table<Job> { Sortable = true, ShowHeader = true, HasFocus = true };
        if (keyed)
        {
            t.RowKey = p => p.Name;
        }

        t.Columns.Add(new TableColumn<Job>("Name", Constraint.Length(8), p => p.Name));
        t.Columns.Add(new TableColumn<Job>("State", Constraint.Length(8), p => p.State.ToString()) { SortKey = p => p.State });
        t.Columns.Add(new TableColumn<Job>("Retries", Constraint.Length(3), p => p.Retries.ToString(CultureInfo.InvariantCulture)) { SortKey = p => p.Retries });
        t.Columns.Add(new TableColumn<Job>("Age", Constraint.Length(5), p => $"{p.Age.TotalMinutes:0}m") { SortKey = p => p.Age });
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
        var t = JobTable(keyed: false);
        t.SetRows(Jobs());
        t.Select(4);
        t.SetRows(Jobs());
        Assert.Equal(0, t.SelectedIndex);
        Assert.Equal(0, t.ScrollOffset);
    }

    [Fact]
    public void RowKey_KeepsTheSelectedRowAndItsPlaceOnScreen()
    {
        var t = JobTable();
        t.SetRows(Jobs(20));
        Draw(t, height: 6); // 5 data rows
        t.Select(7);
        Assert.Equal(3, t.ScrollOffset);

        // Two rows appear above the selection; it stays on job-07, still 4 lines from the top.
        var refreshed = Jobs(20);
        refreshed.Insert(0, new Job("new-a", JobState.Pending, 0, TimeSpan.Zero));
        refreshed.Insert(0, new Job("new-b", JobState.Pending, 0, TimeSpan.Zero));
        t.SetRows(refreshed);
        Assert.Equal("job-07", t.Selected!.Name);
        Assert.Equal(9, t.SelectedIndex);
        Assert.Equal(t.SelectedIndex - 4, t.ScrollOffset);
    }

    [Fact]
    public void RowKey_WhenTheSelectedRowGoes_SelectsTheRowNowInItsPlace()
    {
        var t = JobTable();
        t.SetRows(Jobs(5));
        t.Select(2);
        t.SetRows(Jobs(5).Where(p => p.Name != "job-02"));
        Assert.Equal("job-03", t.Selected!.Name);

        t.Select(3);
        t.SetRows(Jobs(3));
        Assert.Equal("job-02", t.Selected!.Name);
    }

    [Fact]
    public void RowKey_TracksAddedChangedAndRemovedRows()
    {
        var t = JobTable();
        t.SetRows(Jobs(3));
        Assert.Empty(t.AddedRows); // the first load isn't "new"

        var next = Jobs(3).Where(p => p.Name != "job-01").ToList();
        next[1] = next[1] with { Retries = 9 };
        next.Add(new Job("job-99", JobState.Running, 0, TimeSpan.Zero));
        t.SetRows(next);

        Assert.Equal(["job-99"], t.AddedRows.Select(p => p.Name));
        Assert.Equal(["job-02"], t.ChangedRows.Select(p => p.Name));
        Assert.Equal(["job-01"], t.RemovedRows.Select(p => p.Name));
        Assert.True(t.IsNew(next[2]));
        Assert.True(t.IsCellChanged(next[1], 2));
        Assert.False(t.IsCellChanged(next[1], 0));
        Assert.False(t.IsChanged(next[0]));
    }

    [Fact]
    public void HighlightChanges_DrawsChangedCellsBoldUntilTheNextRefresh()
    {
        var t = JobTable();
        t.HighlightChanges = true;
        t.SetRows(Jobs(2));
        var next = Jobs(2);
        next[1] = next[1] with { Retries = 7 };
        t.SetRows(next);
        var s = Draw(t, 30, 3);
        int retriesX = 18; // Name 8 + 1 + State 8 + 1
        Assert.True(s.Get(retriesX, 2).Style.Attributes.HasFlag(TextAttributes.Bold));
        Assert.False(s.Get(0, 2).Style.Attributes.HasFlag(TextAttributes.Bold));

        t.SetRows(next);
        s = Draw(t, 30, 3);
        Assert.False(s.Get(retriesX, 2).Style.Attributes.HasFlag(TextAttributes.Bold));
    }

    // ---- (b) empty text ----

    [Fact]
    public void EmptyText_IsDrawnCenteredAndMuted()
    {
        var t = JobTable();
        t.EmptyText = "no jobs";
        var s = Draw(t, 21, 3);
        Assert.Equal("       no jobs", SurfaceText.Row(s, 1).TrimEnd());
        Assert.Equal(Theming.Theme.Current.Muted, s.Get(7, 1).Style.Foreground);

        var strings = new Table { EmptyText = "nothing" };
        strings.Columns.Add(new Column("A", Constraint.Fill()));
        Assert.Contains("nothing", SurfaceText.Row(Draw(strings, 20, 3), 1), StringComparison.Ordinal);
    }

    // ---- (c) filter ----

    [Fact]
    public void Filter_HidesRowsButKeepsThemInRows()
    {
        var t = JobTable();
        t.SetRows(Jobs(6));
        t.Select(4); // job-04, Running
        t.Filter = p => p.State == JobState.Running;
        Assert.Equal(6, t.Rows.Count);
        Assert.Equal(["job-01", "job-04"], t.FilteredRows.Select(p => p.Name));
        Assert.Equal("job-04", t.Selected!.Name);
        Assert.Equal(1, t.SelectedIndex);
        Assert.Equal("job-01", SurfaceText.Row(Draw(t), 1)[..6]);

        // The selected row filtered out: back to the first match.
        t.Filter = p => p.Name.EndsWith("2", StringComparison.Ordinal) || p.Name.EndsWith("5", StringComparison.Ordinal);
        Assert.Equal("job-02", t.Selected!.Name);

        t.Filter = null;
        Assert.Equal(6, t.FilteredRows.Count);
    }

    [Fact]
    public void Filter_KeysAndCallbacksUseTheVisibleRows()
    {
        var t = JobTable();
        var selected = new List<string>();
        t.OnSelect = p => selected.Add(p.Name);
        t.SetRows(Jobs(6));
        t.Filter = p => p.State == JobState.Failed; // job-02, job-05
        t.OnEvent(K(Key.Down));
        t.OnEvent(K(Key.Down));
        Assert.Equal(["job-05"], selected);
        t.OnEvent(K(Key.End));
        Assert.Equal(1, t.SelectedIndex);
    }

    [Fact]
    public void Sorting_KeepsTheSelectedRowWhenFiltered()
    {
        var t = JobTable();
        t.SetRows(Jobs(6));
        t.Filter = p => p.Retries > 0;
        t.SelectRow(t.Rows[2]);
        t.SortBy(2, SortState.Descending);
        Assert.Equal("job-02", t.Selected!.Name);
        Assert.Equal("job-05", t.FilteredRows[0].Name);
    }

    [Fact]
    public void EditingRowsInPlace_IsPickedUpByRefreshView()
    {
        var t = JobTable();
        t.SetRows(Jobs(4));
        t.Filter = p => p.Retries >= 2;
        Assert.Equal(2, t.FilteredRows.Count);
        t.Rows.Add(new Job("late", JobState.Running, 5, TimeSpan.Zero)); // count change is noticed on its own
        Assert.Equal(3, t.FilteredRows.Count);
        t.Rows[0] = t.Rows[0] with { Retries = 8 };
        t.RefreshView();
        Assert.Equal(4, t.FilteredRows.Count);
    }

    [Fact]
    public void ContainsText_MatchesAnyColumnIgnoringCase()
    {
        var t = JobTable();
        t.SetRows(Jobs(8));
        Assert.True(t.ContainsText(t.Rows[1], "RUNNING"));
        Assert.True(t.ContainsText(t.Rows[1], "job-01"));
        Assert.True(t.ContainsText(t.Rows[1], ""));
        Assert.False(t.ContainsText(t.Rows[1], "failed"));
        t.Filter = j => t.ContainsText(j, "fail");
        Assert.Equal(["job-02", "job-05"], t.FilteredRows.Select(j => j.Name));
    }

    // ---- (e) grouping ----

    [Fact]
    public void GroupBy_ShowsHeadersWithCountsAndSummaries()
    {
        var t = JobTable();
        t.GroupSummary = g => $"{g.Rows.Sum(p => p.Retries)} retries";
        t.SetRows(Jobs(6));
        t.GroupBy("state");
        var s = Draw(t, 40, 10);
        Assert.Equal("▾ Pending  (2)  3 retries", SurfaceText.Row(s, 1).TrimEnd());
        Assert.StartsWith("job-00", SurfaceText.Row(s, 2));
        Assert.StartsWith("job-03", SurfaceText.Row(s, 3));
        Assert.StartsWith("▾ Running  (2)", SurfaceText.Row(s, 4));
        Assert.Equal(["Pending", "Running", "Failed"], t.Groups.Select(g => g.Value));

        // Grouping keeps the selected row; the header above it is line 0.
        Assert.Equal("job-00", t.Selected!.Name);
        Assert.Equal(1, t.SelectedIndex);
        t.Select(0);
        Assert.False(t.HasSelectedRow);
        Assert.Equal("Pending", t.SelectedGroup!.Value);
    }

    [Fact]
    public void GroupHeaders_OnStripedLines_KeepTheStripeUnderTheirText()
    {
        var t = JobTable();
        t.Striped = true;
        t.SetRows(Jobs(6));
        t.GroupBy("state");
        t.Select(1);

        // Lines: Pending header (0), job-00, job-03, Running header (3, striped), ...; the header shows on screen row 4.
        var s = Draw(t, 40, 10);
        Assert.StartsWith("▾ Running", SurfaceText.Row(s, 4));
        var stripe = Theme.Current.StripeBackground;
        for (int x = 0; x < 40; x++)
        {
            Assert.Equal(stripe, s.Get(x, 4).Style.Background);
        }
    }

    [Fact]
    public void GroupHeaders_CollapseAndExpandWithKeys()
    {
        var t = JobTable();
        var activated = new List<string>();
        t.OnActivate = p => activated.Add(p.Name);
        t.SetRows(Jobs(6));
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
        Assert.Equal("job-00", t.Selected!.Name);
        t.OnEvent(K(Key.Left));
        Assert.True(t.IsCollapsed("Pending"));
        Assert.Equal(0, t.SelectedIndex);

        t.OnEvent(C(' '));
        Assert.False(t.IsCollapsed("Pending"));
        t.CollapseAll();
        Assert.Equal(3, t.Groups.Count(g => g.Collapsed));
        t.ExpandAll();
        t.Ungroup();
        Assert.Equal("job-00", t.Selected?.Name ?? t.Rows[t.SelectedIndex].Name);
    }

    [Fact]
    public void Grouping_FollowsSortingAndFiltering()
    {
        var t = JobTable();
        t.SetRows(Jobs(6));
        t.GroupBy(1);
        t.SortBy(1, SortState.Descending);
        Assert.Equal(["Failed", "Running", "Pending"], t.Groups.Select(g => g.Value));
        t.Filter = p => p.Retries >= 3;
        Assert.Equal([1, 1, 1], t.Groups.Select(g => g.Rows.Count));
        t.Filter = p => p.State != JobState.Running;
        Assert.Equal(["Failed", "Pending"], t.Groups.Select(g => g.Value));
    }

    // ---- (f) marks ----

    [Fact]
    public void Marks_ToggleWithSpaceAndStar()
    {
        var t = JobTable();
        int changes = 0;
        t.Markable = true;
        t.OnMarksChanged = () => changes++;
        t.SetRows(Jobs(4));
        t.OnEvent(C(' '));
        t.OnEvent(K(Key.Down));
        t.OnEvent(K(Key.Down));
        t.OnEvent(C(' '));
        Assert.Equal(["job-00", "job-02"], t.Marked.Select(p => p.Name));

        var s = Draw(t, 30, 5);
        Assert.Equal("●", s.Get(0, 1).Grapheme);
        Assert.Equal(" ", SurfaceText.Row(s, 2)[..1]);
        Assert.StartsWith("  job-01", SurfaceText.Row(s, 2));

        t.OnEvent(C('*'));
        Assert.Equal(4, t.Marked.Count);
        t.OnEvent(C('*'));
        Assert.Empty(t.Marked);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Marks_StarMarksOnlyFilteredRows_AndSurviveRefreshByKey()
    {
        var t = JobTable();
        t.Markable = true;
        t.SetRows(Jobs(6));
        t.Filter = p => p.State == JobState.Failed;
        t.OnEvent(C('*'));
        Assert.Equal(["job-02", "job-05"], t.Marked.Select(p => p.Name));

        t.SetRows(Jobs(6).Select(p => p with { Retries = p.Retries + 1 }).Where(p => p.Name != "job-05"));
        Assert.Equal(["job-02"], t.Marked.Select(p => p.Name));
        Assert.True(t.IsMarked(t.Rows.First(p => p.Name == "job-02")));
    }

    [Fact]
    public void Marks_DoNotAffectSpaceWhenOff()
    {
        var t = JobTable();
        t.SetRows(Jobs(2));
        Assert.False(t.OnEvent(C(' ')));
        Assert.Empty(t.Marked);
    }

    [Fact]
    public void EmptyText_MutatedInPlace_IsRedrawn()
    {
        var t = JobTable();
        var empty = new StyledText("loading");
        t.EmptyText = empty;
        Assert.Contains("loading", SurfaceText.Row(Draw(t, 21, 3), 1), StringComparison.Ordinal);
        empty.Append("…done");
        Assert.Contains("loading…done", SurfaceText.Row(Draw(t, 21, 3), 1), StringComparison.Ordinal);
    }
}
