using System;
using System.Collections.Generic;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>A group of a <see cref="Table{T}"/> grouped with <see cref="Table{T}.GroupBy(int)"/>: the shared cell text,
/// the rows in it that pass the filter, and whether it's collapsed.</summary>
public sealed record TableGroup<T>(string Value, IReadOnlyList<T> Rows, bool Collapsed);

/// <summary>
/// A table whose rows are objects: each <see cref="TableColumn{T}"/> draws a row's cell as styled text
/// and sorts by a value of the row, and the callbacks receive the row itself, wherever sorting has moved it.
/// </summary>
/// <remarks>
/// <para>On top of <see cref="TableBase{TRow}.Rows"/>, which always holds every row (in sort order), the table can
/// show a subset: <see cref="Filter"/> hides rows, and <see cref="GroupBy(int)"/> puts rows
/// under collapsible header lines. <see cref="TableBase{TRow}.SelectedIndex"/> then counts lines on screen; use
/// <see cref="Selected"/> for the row. The view is rebuilt when any of those change, on <see cref="TableBase{TRow}.SetRows"/>
/// and sorting, and when the number of rows changes; after editing <see cref="TableBase{TRow}.Rows"/> in place, call
/// <see cref="RefreshView"/>.</para>
/// <para>With a <see cref="RowKey"/>, <see cref="TableBase{TRow}.SetRows"/> becomes a refresh: the selection stays on
/// the same logical row at the same place on screen, marks carry over, and the table knows which rows are new,
/// changed or gone (<see cref="AddedRows"/>, <see cref="ChangedRows"/>, <see cref="RemovedRows"/>).</para>
/// </remarks>
public sealed class Table<T> : TableBase<T>
{
    private const ulong NewRowFlag = 1UL << 63;
    private static readonly object NullIdentity = new();

    // Lines on screen while a filter or grouping is set: a row index into Rows, or ~group for a group header.
    private readonly List<int> _lines = new();
    private readonly List<GroupInfo> _groups = new();
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private bool _viewDirty = true;
    private int _viewRowCount = -1;
    private Func<T, bool>? _filter;
    private int _groupColumn = -1;

    // Change tracking (with a RowKey): each row's cell texts from the last SetRows, and per-row flags for drawing.
    private Dictionary<object, (T Row, string[] Cells)>? _snapshot;
    private readonly Dictionary<RowRef, ulong> _flags = new();
    private readonly List<T> _added = new();
    private readonly List<T> _changed = new();
    private readonly List<T> _removed = new();

    private readonly HashSet<object> _markedIds = new();
    private readonly HashSet<RowRef> _markedRows = new();

    private SelectionState? _pendingSelection;

    public List<TableColumn<T>> Columns { get; } = new();

    /// <summary>Raised when the selected row changes, with the newly selected row. Not raised for group headers.</summary>
    public Action<T>? OnSelect { get; set; }

    /// <summary>Raised when a row is activated (double-click or Enter). Activating a group header toggles it.</summary>
    public Action<T>? OnActivate { get; set; }

    /// <summary>What makes two rows the same logical row across <see cref="TableBase{TRow}.SetRows"/> calls, e.g. a
    /// name or id. With one, a refresh keeps the selection and marks on the same rows and tracks what changed.
    /// Null (the default) keeps <see cref="TableBase{TRow}.SetRows"/>'s reset-to-the-top behavior.</summary>
    public Func<T, object?>? RowKey { get; set; }

    /// <summary>When true (and with a <see cref="RowKey"/>), rows that are new and cells whose text changed in the
    /// last <see cref="TableBase{TRow}.SetRows"/> are drawn bold until the next one.</summary>
    public bool HighlightChanges { get; set; }

    /// <summary>Hides rows it returns false for. <see cref="TableBase{TRow}.Rows"/> still holds them.</summary>
    public Func<T, bool>? Filter
    {
        get => _filter;
        set => ChangeView(() => _filter = value);
    }

    /// <summary>Whether any column's text for <paramref name="row"/> contains <paramref name="text"/>, ignoring
    /// case: what a plain "/" filter wants, <c>table.Filter = row => table.ContainsText(row, typed)</c>. Matches
    /// what's drawn, so it finds "2 days" in an age column but not the date behind it.</summary>
    public bool ContainsText(T row, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        foreach (var column in Columns)
        {
            if (column.Render(row).PlainText.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The column rows are grouped by, or -1.</summary>
    public int GroupColumn => _groupColumn;

    /// <summary>Text after a group's value and count in its header line, e.g. "3 overdue". Called when the view is
    /// rebuilt, not per frame.</summary>
    public Func<TableGroup<T>, StyledText>? GroupSummary { get; set; }

    /// <summary>Shows a mark gutter and lets <c>space</c> mark the selected row and <c>*</c> mark (or, when they're
    /// all marked, unmark) every row that passes the filter, for actions on several rows at once.</summary>
    public bool Markable { get; set; }

    /// <summary>Drawn in the gutter of a marked row.</summary>
    public string MarkGlyph { get; set; } = "●";

    /// <summary>Raised when marks change through keys or the mark methods.</summary>
    public Action? OnMarksChanged { get; set; }

    private bool ViewActive => _filter is not null || (_groupColumn >= 0 && _groupColumn < Columns.Count);

    /// <summary>The selected row, or default when nothing (or a group header) is selected.</summary>
    public T? Selected => RowIndexOf(SelectedIndex) is int r and >= 0 ? Rows[r] : default;

    /// <summary>Whether a row (not a group header) is selected.</summary>
    public bool HasSelectedRow => RowIndexOf(SelectedIndex) >= 0;

    /// <summary>The selected group header's group, or null.</summary>
    public TableGroup<T>? SelectedGroup
    {
        get
        {
            EnsureView();
            return ViewActive && SelectedIndex >= 0 && SelectedIndex < _lines.Count && _lines[SelectedIndex] < 0
                ? _groups[~_lines[SelectedIndex]].Public
                : null;
        }
    }

    /// <summary>The rows that pass <see cref="Filter"/>, in display order, including those in
    /// collapsed groups.</summary>
    public IReadOnlyList<T> FilteredRows
    {
        get
        {
            EnsureView();
            if (!ViewActive)
            {
                return Rows.ToArray();
            }

            var result = new List<T>();
            if (_groups.Count == 0)
            {
                foreach (int line in _lines)
                {
                    result.Add(Rows[line]);
                }
            }
            else
            {
                foreach (var group in _groups)
                {
                    foreach (int r in group.RowIndices)
                    {
                        result.Add(Rows[r]);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>The groups, in display order; empty when not grouped.</summary>
    public IReadOnlyList<TableGroup<T>> Groups
    {
        get
        {
            EnsureView();
            var result = new List<TableGroup<T>>(_groups.Count);
            foreach (var group in _groups)
            {
                result.Add(group.Public);
            }

            return result;
        }
    }

    /// <summary>Rows whose key wasn't there before the last <see cref="TableBase{TRow}.SetRows"/> (needs a
    /// <see cref="RowKey"/>; empty after the first load).</summary>
    public IReadOnlyList<T> AddedRows => _added;

    /// <summary>Rows whose rendered text changed in the last <see cref="TableBase{TRow}.SetRows"/>.</summary>
    public IReadOnlyList<T> ChangedRows => _changed;

    /// <summary>The previous rows whose key is gone after the last <see cref="TableBase{TRow}.SetRows"/>.</summary>
    public IReadOnlyList<T> RemovedRows => _removed;

    public bool IsNew(T row) => _flags.TryGetValue(new RowRef(row), out var f) && (f & NewRowFlag) != 0;

    public bool IsChanged(T row) => _flags.TryGetValue(new RowRef(row), out var f) && (f & ~NewRowFlag) != 0;

    /// <summary>Whether <paramref name="column"/>'s text changed for <paramref name="row"/> in the last refresh.</summary>
    public bool IsCellChanged(T row, int column) =>
        _flags.TryGetValue(new RowRef(row), out var f) && (f & ColumnBit(column)) != 0;

    /// <summary>The marked rows, in <see cref="TableBase{TRow}.Rows"/> order.</summary>
    public IReadOnlyList<T> Marked
    {
        get
        {
            var result = new List<T>(_markedRows.Count);
            foreach (var row in Rows)
            {
                if (_markedRows.Contains(new RowRef(row)))
                {
                    result.Add(row);
                }
            }

            return result;
        }
    }

    public bool IsMarked(T row) => _markedRows.Contains(new RowRef(row));

    public void SetMarked(T row, bool marked)
    {
        if (SetMarkedQuietly(row, marked))
        {
            OnMarksChanged?.Invoke();
        }
    }

    public void ToggleMark(T row) => SetMarked(row, !IsMarked(row));

    /// <summary>Marks every row that passes the filter.</summary>
    public void MarkAll()
    {
        bool changed = false;
        foreach (var row in FilteredRows)
        {
            changed |= SetMarkedQuietly(row, true);
        }

        if (changed)
        {
            OnMarksChanged?.Invoke();
        }
    }

    public void ClearMarks()
    {
        if (_markedIds.Count == 0 && _markedRows.Count == 0)
        {
            return;
        }

        _markedIds.Clear();
        _markedRows.Clear();
        OnMarksChanged?.Invoke();
    }

    private bool SetMarkedQuietly(T row, bool marked)
    {
        var key = new RowRef(row);
        if (marked)
        {
            _markedIds.Add(Identity(row));
            return _markedRows.Add(key);
        }

        _markedIds.Remove(Identity(row));
        return _markedRows.Remove(key);
    }

    /// <summary>Groups rows by the text of <paramref name="column"/> under collapsible header lines showing the
    /// value and count. Groups appear in the order their first row does, so sorting orders them too.</summary>
    public void GroupBy(int column) => ChangeView(() => _groupColumn = column);

    /// <summary>Groups by the column with this header (ignoring case); false if there's none.</summary>
    public bool GroupBy(string header)
    {
        int column = Columns.FindIndex(c => string.Equals(c.Header, header, StringComparison.OrdinalIgnoreCase));
        if (column < 0)
        {
            return false;
        }

        GroupBy(column);
        return true;
    }

    public void Ungroup() => ChangeView(() => _groupColumn = -1);

    public bool IsCollapsed(string value) => _collapsed.Contains(value);

    /// <summary>Collapses or expands the group with this value.</summary>
    public void SetCollapsed(string value, bool collapsed) => ChangeView(() =>
    {
        if (collapsed)
        {
            _collapsed.Add(value);
        }
        else
        {
            _collapsed.Remove(value);
        }
    }, toGroup: collapsed ? value : null);

    public void ToggleGroup(string value) => SetCollapsed(value, !IsCollapsed(value));

    public void CollapseAll() => ChangeView(() =>
    {
        foreach (var group in _groups)
        {
            _collapsed.Add(group.Value);
        }
    }, toGroup: SelectedGroupValue());

    public void ExpandAll() => ChangeView(() => _collapsed.Clear());

    /// <summary>Selects <paramref name="row"/> (raising <see cref="OnSelect"/> if that changes the selection);
    /// false if it isn't on screen (filtered out or collapsed).</summary>
    public bool SelectRow(T row)
    {
        int line = FindLine(Identity(row));
        if (line < 0)
        {
            return false;
        }

        Select(line);
        return true;
    }

    /// <summary>Rebuilds the filtered/grouped view after editing <see cref="TableBase{TRow}.Rows"/> in place,
    /// keeping the selection on the same row where it can.</summary>
    public void RefreshView() => ChangeView(static () => { });

    private protected override int ColumnCount => Columns.Count;
    private protected override string ColumnHeader(int column) => Columns[column].Header;
    private protected override Constraint ColumnWidth(int column) => Columns[column].Width;
    private protected override Justify ColumnJustify(int column) => Columns[column].Justify;

    private protected override int LineCount
    {
        get
        {
            EnsureView();
            return ViewActive ? _lines.Count : Rows.Count;
        }
    }

    private protected override int RowAt(int line) => ViewActive ? (_lines[line] >= 0 ? _lines[line] : -1) : line;

    private protected override int GutterWidth => Markable ? 2 : 0;

    private protected override (IComparable? Key, string Text) SortValue(T row, int column)
    {
        var col = Columns[column];
        IComparable? key = col.SortKey?.Invoke(row);
        return (key, key is null ? col.Render(row).PlainText : string.Empty);
    }

    private protected override void DrawCell(Surface surface, Rect cell, T row, int column, Style style, bool selected)
    {
        var col = Columns[column];
        if (HighlightChanges && _flags.Count > 0 && _flags.TryGetValue(new RowRef(row), out var flags)
            && (flags & (NewRowFlag | ColumnBit(column))) != 0)
        {
            style = style.Bold;
        }

        var content = col.Render(row);
        TextRenderer.DrawLine(surface, cell.X, cell.Y, cell.Width, OnRow(content, style, selected), col.Justify);
    }

    /// <summary>The selection's colors win; elsewhere a span keeps its own colors and takes the row's (a stripe,
    /// say) where it has none, so text never punches a hole in the row's background.</summary>
    private static StyledText OnRow(StyledText content, Style style, bool selected)
    {
        var drawn = StyledText.Empty();
        foreach (var span in content.Spans)
        {
            var own = span.Style;
            drawn.Append(span.WithStyle(selected ? style : new Style(
                own.Foreground.IsDefault ? style.Foreground : own.Foreground,
                own.Background.IsDefault ? style.Background : own.Background,
                own.Attributes | style.Attributes)));
        }
        return drawn;
    }

    private protected override void DrawGutter(Surface surface, Rect gutter, T row, Style style, bool selected)
    {
        if (_markedRows.Count > 0 && _markedRows.Contains(new RowRef(row)))
        {
            surface.DrawText(gutter.X, gutter.Y, MarkGlyph, selected ? style.Bold : style.WithForeground(Theme.Current.Accent).Bold);
        }
    }

    private protected override void DrawLine(Surface surface, Rect line, int index, Style style, bool selected)
    {
        var group = _groups[~_lines[index]];
        TextRenderer.DrawLine(surface, line.X, line.Y, line.Width, selected ? group.SelectedHeader : OnRow(group.Header, style, selected: false), Justify.Left);
    }

    private protected override bool HandleExtraKey(Terminal.KeyEvent key)
    {
        int row = RowIndexOf(SelectedIndex);
        bool onHeader = SelectedIndex >= 0 && SelectedIndex < LineCount && row < 0;
        string? groupValue = onHeader ? _groups[~_lines[SelectedIndex]].Value : null;
        switch (key)
        {
            case { Key: Terminal.Key.Right, Modifiers: Terminal.KeyModifiers.None } when groupValue is not null && IsCollapsed(groupValue):
                SetCollapsed(groupValue, false);
                return true;
            case { Key: Terminal.Key.Left, Modifiers: Terminal.KeyModifiers.None } when groupValue is not null && !IsCollapsed(groupValue):
                SetCollapsed(groupValue, true);
                return true;
            case { Key: Terminal.Key.Left, Modifiers: Terminal.KeyModifiers.None } when row >= 0 && ViewActive && _groups.Count > 0:
                SetCollapsed(GroupOfLine(SelectedIndex), true);
                return true;
            case { Key: Terminal.Key.Char, Modifiers: Terminal.KeyModifiers.None } when key.Rune.Value == ' ' && groupValue is not null:
                ToggleGroup(groupValue);
                return true;
            case { Key: Terminal.Key.Char, Modifiers: Terminal.KeyModifiers.None } when key.Rune.Value == ' ' && Markable && row >= 0:
                ToggleMark(Rows[row]);
                return true;
            case { Key: Terminal.Key.Char } when key.Rune.Value == '*' && Markable:
                var rows = FilteredRows;
                bool all = rows.Count > 0;
                foreach (var r in rows)
                {
                    all &= IsMarked(r);
                }

                if (all)
                {
                    bool changed = false;
                    foreach (var r in rows)
                    {
                        changed |= SetMarkedQuietly(r, false);
                    }

                    if (changed)
                    {
                        OnMarksChanged?.Invoke();
                    }
                }
                else
                {
                    MarkAll();
                }

                return true;
        }

        return false;
    }

    private protected override void RaiseSelect(int index)
    {
        if (RowIndexOf(index) is int r and >= 0)
        {
            OnSelect?.Invoke(Rows[r]);
        }
    }

    private protected override void RaiseActivate(int index)
    {
        int r = RowIndexOf(index);
        if (r >= 0)
        {
            OnActivate?.Invoke(Rows[r]);
        }
        else if (index >= 0 && index < LineCount)
        {
            ToggleGroup(_groups[~_lines[index]].Value);
        }
    }

    private protected override void RowsReplacing()
    {
        _pendingSelection = RowKey is not null || ViewActive ? CaptureSelection() : null;
    }

    private protected override void RowsReplaced()
    {
        _viewDirty = true;
        TrackChanges();
        RebindMarks();
        var pending = _pendingSelection;
        _pendingSelection = null;
        if (RowKey is not null && pending is { } state)
        {
            RestoreSelection(state, fallbackToTop: false);
        }
        else
        {
            base.RowsReplaced();
        }
    }

    private protected override void RowsReordering()
    {
        _pendingSelection = RowKey is not null || ViewActive ? CaptureSelection() : null;
    }

    private protected override void RowsReordered()
    {
        _viewDirty = true;
        var pending = _pendingSelection;
        _pendingSelection = null;
        if (pending is { } state)
        {
            RestoreSelection(state, fallbackToTop: false);
        }
    }

    // ---- view ----

    private void ChangeView(Action change, string? toGroup = null)
    {
        var state = CaptureSelection();
        change();
        _viewDirty = true;
        if (toGroup is not null && state.Identity is { } id and not GroupRef && FindLine(id) < 0)
        {
            // The selected row was folded away: select its group's header instead.
            state = state with { Identity = new GroupRef(toGroup) };
        }

        RestoreSelection(state, fallbackToTop: true);
    }

    private void EnsureView()
    {
        if (!ViewActive)
        {
            if (_lines.Count > 0 || _groups.Count > 0)
            {
                _lines.Clear();
                _groups.Clear();
            }

            return;
        }

        if (_viewDirty || _viewRowCount != Rows.Count)
        {
            BuildView();
        }
    }

    private void BuildView()
    {
        _viewDirty = false;
        _viewRowCount = Rows.Count;
        _lines.Clear();
        _groups.Clear();
        bool grouped = _groupColumn >= 0 && _groupColumn < Columns.Count;
        Dictionary<string, int>? byValue = grouped ? new(StringComparer.Ordinal) : null;
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (_filter is not null && !_filter(row))
            {
                continue;
            }

            if (!grouped)
            {
                _lines.Add(i);
                continue;
            }

            string value = Columns[_groupColumn].Render(row).PlainText;
            if (!byValue!.TryGetValue(value, out int g))
            {
                g = _groups.Count;
                byValue[value] = g;
                _groups.Add(new GroupInfo(value));
            }

            _groups[g].RowIndices.Add(i);
        }

        if (!grouped)
        {
            return;
        }

        var theme = Theme.Current;
        for (int g = 0; g < _groups.Count; g++)
        {
            var group = _groups[g];
            bool collapsed = _collapsed.Contains(group.Value);
            var rows = new T[group.RowIndices.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = Rows[group.RowIndices[i]];
            }

            group.Public = new TableGroup<T>(group.Value, rows, collapsed);
            string marker = collapsed ? "▸ " : "▾ ";
            string count = $"  ({rows.Length})";
            var summary = GroupSummary?.Invoke(group.Public);
            group.Header = new StyledText(marker, new Style(theme.Muted, Color.Default))
                .Append(group.Value.Length == 0 ? "(none)" : group.Value, new Style(theme.Accent, Color.Default).Bold)
                .Append(count, new Style(theme.Muted, Color.Default));
            var selection = theme.SelectionStyle;
            group.SelectedHeader = new StyledText(marker + (group.Value.Length == 0 ? "(none)" : group.Value), selection.Bold)
                .Append(count, selection);
            if (summary is not null && summary.Width > 0)
            {
                group.Header.Append("  ").Append(summary);
                group.SelectedHeader.Append("  " + summary.PlainText, selection);
            }

            _lines.Add(~g);
            if (!collapsed)
            {
                _lines.AddRange(group.RowIndices);
            }
        }
    }

    private int RowIndexOf(int line)
    {
        int count = LineCount;
        return line >= 0 && line < count ? RowAt(line) : -1;
    }

    private string GroupOfLine(int line)
    {
        for (int i = line; i >= 0; i--)
        {
            if (_lines[i] < 0)
            {
                return _groups[~_lines[i]].Value;
            }
        }

        return string.Empty;
    }

    private string? SelectedGroupValue()
    {
        int count = LineCount;
        if (!ViewActive || _groups.Count == 0 || SelectedIndex < 0 || SelectedIndex >= count)
        {
            return null;
        }

        return GroupOfLine(SelectedIndex);
    }

    // ---- selection across view changes ----

    private sealed record GroupRef(string Value);

    private readonly record struct SelectionState(object? Identity, int Line, int ScreenOffset);

    private SelectionState CaptureSelection()
    {
        int count = LineCount;
        if (SelectedIndex < 0 || SelectedIndex >= count)
        {
            return new SelectionState(null, SelectedIndex, 0);
        }

        int row = RowAt(SelectedIndex);
        object identity = row >= 0 ? Identity(Rows[row]) : new GroupRef(_groups[~_lines[SelectedIndex]].Value);
        return new SelectionState(identity, SelectedIndex, SelectedIndex - ScrollOffset);
    }

    private void RestoreSelection(SelectionState state, bool fallbackToTop)
    {
        int count = LineCount;
        int line = state.Identity is null ? -1 : FindLine(state.Identity);
        if (line >= 0)
        {
            SelectedIndex = line;
            ScrollToSelection(state.ScreenOffset);
            return;
        }

        SelectedIndex = count == 0 ? -1 : fallbackToTop || state.Line < 0 ? 0 : Math.Min(state.Line, count - 1);
        ScrollToSelection(fallbackToTop ? null : state.ScreenOffset);
    }

    private int FindLine(object identity)
    {
        int count = LineCount;
        for (int line = 0; line < count; line++)
        {
            int row = RowAt(line);
            if (row >= 0 ? identity is not GroupRef && Equals(Identity(Rows[row]), identity)
                : identity is GroupRef g && g.Value == _groups[~_lines[line]].Value)
            {
                return line;
            }
        }

        return -1;
    }

    private object Identity(T row) => (RowKey is { } key ? key(row) : row) ?? NullIdentity;

    private static ulong ColumnBit(int column) => 1UL << Math.Clamp(column, 0, 62);

    // ---- refresh bookkeeping ----

    private void TrackChanges()
    {
        _flags.Clear();
        _added.Clear();
        _changed.Clear();
        _removed.Clear();
        if (RowKey is null)
        {
            _snapshot = null;
            return;
        }

        var previous = _snapshot;
        var next = new Dictionary<object, (T Row, string[] Cells)>(Rows.Count);
        foreach (var row in Rows)
        {
            var cells = new string[Columns.Count];
            for (int c = 0; c < cells.Length; c++)
            {
                cells[c] = Columns[c].Render(row).PlainText;
            }

            var id = Identity(row);
            next[id] = (row, cells);
            if (previous is null)
            {
                continue;
            }

            if (!previous.TryGetValue(id, out var before))
            {
                _added.Add(row);
                _flags[new RowRef(row)] = NewRowFlag;
                continue;
            }

            ulong mask = 0;
            for (int c = 0; c < cells.Length; c++)
            {
                if (c >= before.Cells.Length || !string.Equals(cells[c], before.Cells[c], StringComparison.Ordinal))
                {
                    mask |= ColumnBit(c);
                }
            }

            if (mask != 0)
            {
                _changed.Add(row);
                _flags[new RowRef(row)] = mask;
            }
        }

        if (previous is not null)
        {
            foreach (var (id, before) in previous)
            {
                if (!next.ContainsKey(id))
                {
                    _removed.Add(before.Row);
                }
            }
        }

        _snapshot = next;
    }

    private void RebindMarks()
    {
        if (_markedIds.Count == 0)
        {
            _markedRows.Clear();
            return;
        }

        int before = _markedIds.Count;
        _markedRows.Clear();
        var kept = new HashSet<object>();
        foreach (var row in Rows)
        {
            var id = Identity(row);
            if (_markedIds.Contains(id))
            {
                _markedRows.Add(new RowRef(row));
                kept.Add(id);
            }
        }

        _markedIds.Clear();
        _markedIds.UnionWith(kept);
        if (_markedIds.Count != before)
        {
            OnMarksChanged?.Invoke();
        }
    }

    private sealed class GroupInfo(string value)
    {
        public string Value { get; } = value;
        public List<int> RowIndices { get; } = new();
        public TableGroup<T> Public { get; set; } = null!;
        public StyledText Header { get; set; } = StyledText.Empty();
        public StyledText SelectedHeader { get; set; } = StyledText.Empty();
    }
}
