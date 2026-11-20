using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Text;

namespace Cellar.Widgets;

/// <summary>The sort direction of a table column.</summary>
public enum SortState { None, Ascending, Descending }

/// <summary>A <see cref="Table"/> column: a header label, a width constraint, and cell alignment.</summary>
public sealed class Column
{
    public string Header { get; set; }
    public Constraint Width { get; set; }
    public Justify Justify { get; set; }

    /// <summary>Maps a cell string to a comparable sort key so numeric columns sort correctly
    /// (42 before 100). Null uses ordinal string comparison. E.g. <c>SortKey = double.Parse</c>.
    /// Cells it throws on sort last.</summary>
    public Func<string, IComparable>? SortKey { get; set; }

    public Column(string header, Constraint width, Justify justify = Justify.Left)
    {
        Header = header;
        Width = width;
        Justify = justify;
    }
}

/// <summary>A <see cref="Table{T}"/> column: a header label, a width constraint, cell alignment, and how to draw
/// and sort a row's cell.</summary>
public sealed class TableColumn<T>
{
    public string Header { get; set; }
    public Constraint Width { get; set; }
    public Justify Justify { get; set; }

    /// <summary>Draws a row's cell. Spans keep their own colors on plain and striped rows, taking the row's where
    /// they have none; the selected row is drawn in the selection style.</summary>
    public Func<T, StyledText> Render { get; set; }

    /// <summary>What the column sorts by, so that a size column sorts by bytes and not by its text ("1.2 MB").
    /// Null sorts by the cell's text. Rows whose key is null sort last.</summary>
    public Func<T, IComparable?>? SortKey { get; set; }

    public TableColumn(string header, Constraint width, Func<T, StyledText> render, Justify justify = Justify.Left)
    {
        Header = header;
        Width = width;
        Render = render;
        Justify = justify;
    }
}

/// <summary>
/// A scrollable, column-constrained table with an optional header and a highlighted selected
/// row. Column widths are resolved by the <see cref="LayoutSolver"/> to tile the width exactly.
/// This holds what <see cref="Table"/> (rows of strings) and <see cref="Table{T}"/> (rows of
/// objects, with styled cells) share.
/// </summary>
public abstract class TableBase<TRow> : Widget
{
    private protected TableBase() { }

    /// <summary>The rows, in display order: sorting reorders them.</summary>
    public List<TRow> Rows { get; } = new();

    public bool ShowHeader { get; set; } = true;

    /// <summary>The selected line, or -1. On a <see cref="Table{T}"/> with a filter or grouping this counts the lines
    /// on screen (group headers included), not <see cref="Rows"/>; without them the two are the same.</summary>
    public int SelectedIndex { get; set; } = -1;
    public int ScrollOffset { get; set; }

    /// <summary>Drawn in the body when there's nothing to show, e.g. "no files" or "no rows match /draft". Spans without
    /// a color are drawn muted. Null leaves the body blank.</summary>
    public StyledText? EmptyText { get; set; }

    /// <summary>Header row style. Null follows the ambient theme's header style.</summary>
    public Style? HeaderStyle { get; set; }
    /// <summary>Body row style. Null follows the ambient theme's text style.</summary>
    public Style? RowStyle { get; set; }
    /// <summary>Selected row style. Null follows the ambient theme's selection style.</summary>
    public Style? SelectedStyle { get; set; }

    private Style EffectiveHeader => HeaderStyle ?? Theming.Theme.Current.HeaderStyle;
    private Style EffectiveRow => RowStyle ?? Theming.Theme.Current.TextStyle;
    private Style EffectiveSelected => SelectedStyle ?? Theming.Theme.Current.SelectionStyle;

    /// <summary>When true, odd data rows get a subtly different background (zebra striping).</summary>
    public bool Striped { get; set; }

    /// <summary>Style for striped (odd) rows. Null derives a subtle stripe from the theme.</summary>
    public Style? StripeStyle { get; set; }

    private Style EffectiveStripe =>
        StripeStyle ?? new Style(EffectiveRow.Foreground, Theming.Theme.Current.StripeBackground);

    /// <summary>Column gap in cells.</summary>
    public int ColumnSpacing { get; set; } = 1;

    /// <summary>When true, a scrollbar is drawn on the right when the rows overflow.</summary>
    public bool ShowScrollbar { get; set; }

    /// <summary>Scrollbar style used when <see cref="ShowScrollbar"/> is on.</summary>
    public Scrollbar Scrollbar { get; set; } = Scrollbar.Default;

    /// <summary>When true, clicking a header cell cycles the sort on that column.</summary>
    public bool Sortable { get; set; }

    /// <summary>The column currently sorted by, or -1 if unsorted.</summary>
    public int SortColumn { get; private set; } = -1;

    /// <summary>The current sort direction of <see cref="SortColumn"/>.</summary>
    public SortState SortState { get; private set; } = SortState.None;

    /// <summary>Whether the current sort is descending (convenience over <see cref="SortState"/>).</summary>
    public bool SortDescending => SortState == SortState.Descending;

    // Records the header row's screen position + each header cell's x-range for click sorting.
    private int _headerRow = -1;
    private readonly List<(int start, int end, int col)> _headerHits = new();

    // Data-area geometry captured on render, for row click hit-testing.
    private Rect _dataArea;
    private Rect _bounds; // the full render area, for gating wheel/mouse events by position
    private int _dataTop = -1;
    private int _dataViewportRows;

    // The row order before the first sort, so a None state can restore it.
    private TRow[]? _originalOrder;

    private string[] _headerCells = [];
    private int _headerSortColumn = -2;
    private bool _headerSortDescending;

    private int[] _widths = [];
    private Constraint[] _constraints = [];

    private StyledText? _emptySource;
    private StyledText? _emptyDrawn;
    private int _emptyVersion;
    private Color _emptyColor;
    private bool _replacing;

    public override bool IsFocusable => true;

    private protected abstract int ColumnCount { get; }
    private protected abstract string ColumnHeader(int column);
    private protected abstract Constraint ColumnWidth(int column);
    private protected abstract Justify ColumnJustify(int column);

    /// <summary>The value a row sorts by in <paramref name="column"/>, and the text it falls back to without one.</summary>
    private protected abstract (IComparable? Key, string Text) SortValue(TRow row, int column);

    /// <summary>Draws a row's cell into <paramref name="cell"/> (the clip is set to it) in the row's style.</summary>
    private protected abstract void DrawCell(Surface surface, Rect cell, TRow row, int column, Style style, bool selected);

    private protected abstract void RaiseSelect(int index);
    private protected abstract void RaiseActivate(int index);

    /// <summary>How many lines the body has: the rows, or on a <see cref="Table{T}"/> the rows that pass its filter
    /// plus group headers.</summary>
    private protected virtual int LineCount => Rows.Count;

    /// <summary>The index into <see cref="Rows"/> of body line <paramref name="line"/>, or -1 for a line that isn't
    /// a row (a group header).</summary>
    private protected virtual int RowAt(int line) => line;

    /// <summary>Cells to the left of the first column, e.g. for row marks.</summary>
    private protected virtual int GutterWidth => 0;

    /// <summary>Draws the gutter of a row line.</summary>
    private protected virtual void DrawGutter(Surface surface, Rect gutter, TRow row, Style style, bool selected) { }

    /// <summary>Draws a body line that isn't a row (a group header) across <paramref name="line"/>.</summary>
    private protected virtual void DrawLine(Surface surface, Rect line, int index, Style style, bool selected) { }

    /// <summary>Gives a subclass the first look at a key while the table has focus.</summary>
    private protected virtual bool HandleExtraKey(Terminal.KeyEvent key) => false;

    /// <summary>Runs before <see cref="SetRows"/> replaces the rows.</summary>
    private protected virtual void RowsReplacing() { }

    /// <summary>Runs after <see cref="SetRows"/> replaced (and sorted) the rows: by default selects the first line,
    /// scrolled to the top.</summary>
    private protected virtual void RowsReplaced()
    {
        SelectedIndex = LineCount > 0 ? 0 : -1;
        ScrollOffset = 0;
    }

    /// <summary>Runs before a sort reorders <see cref="Rows"/> (not during <see cref="SetRows"/>).</summary>
    private protected virtual void RowsReordering() { }

    /// <summary>Runs after a sort reordered <see cref="Rows"/> (not during <see cref="SetRows"/>).</summary>
    private protected virtual void RowsReordered() { }

    /// <summary>Replaces the rows, keeping the current sort, and selects the first row (scrolled to the top). A
    /// <see cref="Table{T}"/> with a <see cref="Table{T}.RowKey"/> keeps the selection on the same row instead.</summary>
    public void SetRows(IEnumerable<TRow> rows)
    {
        var replacement = new List<TRow>(rows); // before clearing: the rows may be a query over Rows
        RowsReplacing();
        Rows.Clear();
        Rows.AddRange(replacement);
        _originalOrder = null;
        if (SortColumn >= 0)
        {
            _replacing = true;
            try
            {
                ApplySort(SortColumn, SortState);
            }
            finally
            {
                _replacing = false;
            }
        }

        RowsReplaced();
    }

    /// <summary>Cycles the sort on <paramref name="column"/>: None → Asc → Desc → None (None
    /// restores insertion order). Uses the column's sort key when it has one.</summary>
    public void CycleSort(int column)
    {
        if (column < 0 || column >= ColumnCount)
        {
            return;
        }

        var next = column == SortColumn
            ? SortState switch
            {
                SortState.None => SortState.Ascending,
                SortState.Ascending => SortState.Descending,
                _ => SortState.None,
            }
            : SortState.Ascending;
        ApplySort(column, next);
    }

    /// <summary>Sorts by a column in an explicit direction (or clears the sort with None).</summary>
    public void SortBy(int column, SortState state)
    {
        if (column < 0 || column >= ColumnCount)
        {
            return;
        }

        ApplySort(column, state);
    }

    private void ApplySort(int column, SortState state)
    {
        if (!_replacing)
        {
            RowsReordering();
        }

        SortColumn = column;
        SortState = state;

        if (state == SortState.None)
        {
            RestoreOriginalOrder();
            SortColumn = -1;
            if (!_replacing)
            {
                RowsReordered();
            }

            return;
        }

        CaptureOriginalOrder();

        // Schwartzian transform: compute each row's comparison key exactly once.
        int n = Rows.Count;
        var keyed = new (IComparable? Key, string Text, int Index, TRow Row)[n];
        for (int i = 0; i < n; i++)
        {
            var (key, text) = SortValue(Rows[i], column);
            keyed[i] = (key, text, i, Rows[i]);
        }

        // Rows without a key go last in either direction. Equal rows keep their order, so the sort is stable.
        int dir = state == SortState.Descending ? -1 : 1;
        Array.Sort(keyed, (a, b) =>
        {
            int cmp;
            if (a.Key is not null && b.Key is not null)
            {
                cmp = dir * a.Key.CompareTo(b.Key);
            }
            else if (a.Key is not null || b.Key is not null)
            {
                return a.Key is null ? 1 : -1;
            }
            else
            {
                cmp = dir * string.CompareOrdinal(a.Text, b.Text);
            }

            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        Rows.Clear();
        foreach (var k in keyed)
        {
            Rows.Add(k.Row);
        }

        if (!_replacing)
        {
            RowsReordered();
        }
    }

    private void CaptureOriginalOrder()
    {
        if (_originalOrder is not null && _originalOrder.Length == Rows.Count)
        {
            return;
        }

        _originalOrder = Rows.ToArray();
    }

    private void RestoreOriginalOrder()
    {
        if (_originalOrder is null)
        {
            return;
        }

        // Only restore rows that still exist (data may have changed since capture): each comes back once per
        // occurrence, in the order before the sort, then any rows added since.
        var remaining = new Dictionary<RowRef, int>();
        foreach (var row in Rows)
        {
            remaining[new RowRef(row)] = remaining.GetValueOrDefault(new RowRef(row)) + 1;
        }
        var restored = new List<TRow>(Rows.Count);
        foreach (var row in _originalOrder.Concat(Rows))
        {
            var key = new RowRef(row);
            if (remaining.TryGetValue(key, out int count) && count > 0)
            {
                remaining[key] = count - 1;
                restored.Add(row);
            }
        }
        Rows.Clear();
        Rows.AddRange(restored);

        _originalOrder = null;
    }

    // A row as a dictionary key. Rows that are objects match by reference, so equal records stay distinct rows.
    private protected readonly struct RowRef(TRow row) : IEquatable<RowRef>
    {
        private readonly TRow _row = row;

        public bool Equals(RowRef other) => typeof(TRow).IsValueType
            ? EqualityComparer<TRow>.Default.Equals(_row, other._row)
            : ReferenceEquals(_row, other._row);

        public override bool Equals(object? obj) => obj is RowRef other && Equals(other);

        public override int GetHashCode() => _row is null ? 0
            : typeof(TRow).IsValueType ? EqualityComparer<TRow>.Default.GetHashCode(_row)
            : RuntimeHelpers.GetHashCode(_row);
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty || ColumnCount == 0)
        {
            return;
        }

        _bounds = area;

        int lineCount = LineCount;
        int dataRowsVisible = area.Height - (ShowHeader ? 1 : 0);
        bool overflowing = lineCount > dataRowsVisible;
        bool drawBar = ShowScrollbar && overflowing && area.Width > 1;
        int barWidth = drawBar ? 1 : 0;

        var lineArea = new Rect(area.X, area.Y, area.Width - barWidth, area.Height);
        int gutter = Math.Min(GutterWidth, lineArea.Width);
        var contentArea = new Rect(lineArea.X + gutter, area.Y, lineArea.Width - gutter, area.Height);
        int[] widths = ResolveColumnWidths(contentArea.Width);

        int y = contentArea.Top;
        if (ShowHeader)
        {
            if (gutter > 0)
            {
                surface.FillRect(new Rect(lineArea.X, y, gutter, 1), EffectiveHeader);
            }

            DrawHeader(surface, contentArea, widths, y);
            RecordHeaderHits(contentArea, widths, y);
            y++;
        }

        int firstRow = Math.Max(0, ScrollOffset);
        int visibleRows = contentArea.Bottom - y;
        _dataTop = y;
        _dataViewportRows = visibleRows;
        _dataArea = new Rect(lineArea.X, y, lineArea.Width, Math.Max(0, visibleRows));
        if (lineCount == 0 && EmptyText is { } empty && visibleRows > 0)
        {
            DrawEmpty(surface, new Rect(contentArea.X, y, contentArea.Width, 1), empty);
        }

        for (int i = 0; i < visibleRows; i++)
        {
            int line = firstRow + i;
            if (line >= lineCount)
            {
                break;
            }

            bool selected = line == SelectedIndex;
            bool striped = Striped && (line & 1) == 1;
            var style = selected ? EffectiveSelected : striped ? EffectiveStripe : EffectiveRow;
            if (selected || striped)
            {
                surface.FillRect(new Rect(lineArea.X, y + i, lineArea.Width, 1), style);
            }

            int rowIndex = RowAt(line);
            if (rowIndex < 0)
            {
                var full = new Rect(lineArea.X, y + i, lineArea.Width, 1);
                surface.SetClip(full);
                DrawLine(surface, full, line, style, selected);
                surface.ResetClip();
                continue;
            }

            if (gutter > 0)
            {
                var cell = new Rect(lineArea.X, y + i, gutter, 1);
                surface.SetClip(cell);
                DrawGutter(surface, cell, Rows[rowIndex], style, selected);
                surface.ResetClip();
            }

            int x = contentArea.X;
            for (int c = 0; c < ColumnCount; c++)
            {
                int w = widths[c];
                if (w > 0)
                {
                    var cell = new Rect(x, y + i, w, 1);
                    surface.SetClip(cell);
                    DrawCell(surface, cell, Rows[rowIndex], c, style, selected);
                    surface.ResetClip();
                }
                x += w + ColumnSpacing;
            }
        }

        if (drawBar)
        {
            int barTop = area.Top + (ShowHeader ? 1 : 0);
            var barRect = new Rect(area.Right - 1, barTop, 1, area.Height - (ShowHeader ? 1 : 0));
            Scrollbar.Render(surface, barRect, ScrollOffset, dataRowsVisible, lineCount);
        }
    }

    // The empty-state text, centered, with uncolored spans in the muted color (rebuilt only when it changes).
    private void DrawEmpty(Surface surface, Rect line, StyledText empty)
    {
        var muted = Theming.Theme.Current.Muted;
        if (!ReferenceEquals(empty, _emptySource) || empty.Version != _emptyVersion || !_emptyColor.Equals(muted))
        {
            _emptySource = empty;
            _emptyVersion = empty.Version;
            _emptyColor = muted;
            _emptyDrawn = StyledText.Empty();
            foreach (var span in empty.Spans)
            {
                _emptyDrawn.Append(span.Style.Foreground.IsDefault ? span.WithStyle(span.Style.WithForeground(muted)) : span);
            }
        }

        surface.SetClip(line);
        TextRenderer.DrawLine(surface, line.X, line.Y, line.Width, _emptyDrawn!, Justify.Center);
        surface.ResetClip();
    }

    /// <summary>Scrolls so the selected line is in view; with <paramref name="screenOffset"/>, puts it that many lines
    /// below the top (as far as the rows allow), so a refresh doesn't move it on screen.</summary>
    private protected void ScrollToSelection(int? screenOffset = null)
    {
        int count = LineCount;
        if (SelectedIndex < 0 || count == 0)
        {
            ScrollOffset = 0;
            return;
        }

        int dataRows = _dataViewportRows > 0 ? _dataViewportRows : count;
        if (screenOffset is { } offset)
        {
            ScrollOffset = SelectedIndex - Math.Clamp(offset, 0, dataRows - 1);
        }

        ScrollOffset = Math.Clamp(ScrollOffset, 0, Math.Max(0, count - dataRows));
        if (SelectedIndex < ScrollOffset)
        {
            ScrollOffset = SelectedIndex;
        }
        else if (SelectedIndex >= ScrollOffset + dataRows)
        {
            ScrollOffset = SelectedIndex - dataRows + 1;
        }
    }

    /// <summary>Selects row <paramref name="index"/> programmatically: raises the select callback
    /// (if it changed) and scrolls it into view. The same path a click/arrow uses.</summary>
    public void Select(int index) => SetSelection(index);

    /// <summary>Moves the selection by <paramref name="delta"/>, keeping it visible.</summary>
    public void MoveSelection(int delta, int viewportRows)
    {
        int count = LineCount;
        if (count == 0)
        {
            return;
        }

        int previous = SelectedIndex;
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, count - 1);
        if (SelectedIndex != previous)
        {
            RaiseSelect(SelectedIndex);
        }

        int dataRows = ShowHeader ? viewportRows - 1 : viewportRows;
        if (dataRows <= 0)
        {
            return;
        }

        if (SelectedIndex < ScrollOffset)
        {
            ScrollOffset = SelectedIndex;
        }
        else if (SelectedIndex >= ScrollOffset + dataRows)
        {
            ScrollOffset = SelectedIndex - dataRows + 1;
        }
    }

    private void DrawHeader(Surface surface, Rect area, int[] widths, int y)
    {
        var style = EffectiveHeader;
        surface.FillRect(new Rect(area.X, y, area.Width, 1), style);
        string[] cells = HeaderCells();
        int x = area.X;
        for (int c = 0; c < cells.Length; c++)
        {
            int w = widths[c];
            if (w > 0)
            {
                DrawClippedAligned(surface, new Rect(x, y, w, 1), cells[c], style, ColumnJustify(c));
            }
            x += w + ColumnSpacing;
        }
    }

    private string[] HeaderCells()
    {
        int n = ColumnCount;
        if (_headerCells.Length != n)
        {
            _headerCells = new string[n];
            _headerSortColumn = -2;
        }

        // The sort arrow is the only per-frame variation; rebuild the decorated strings only
        // when the sort column or direction actually changes, not every render.
        if (_headerSortColumn != SortColumn || _headerSortDescending != SortDescending)
        {
            for (int i = 0; i < n; i++)
            {
                _headerCells[i] = i == SortColumn
                    ? ColumnHeader(i) + (SortDescending ? " ▼" : " ▲")
                    : ColumnHeader(i);
            }
            _headerSortColumn = SortColumn;
            _headerSortDescending = SortDescending;
        }
        return _headerCells;
    }

    // Captures each header cell's screen x-range and the header row, for click hit-testing.
    private void RecordHeaderHits(Rect area, int[] widths, int y)
    {
        _headerRow = y;
        _headerHits.Clear();
        int x = area.X;
        for (int c = 0; c < ColumnCount; c++)
        {
            _headerHits.Add((x, x + widths[c], c));
            x += widths[c] + ColumnSpacing;
        }
    }

    public override bool OnEvent(Terminal.InputEvent e)
    {
        switch (e)
        {
            case Terminal.MouseEvent { Kind: Terminal.MouseEventKind.Down } m:
                return HandleClick(m);
            case Terminal.MouseEvent { Kind: Terminal.MouseEventKind.Wheel } w when _bounds.Contains(w.X, w.Y):
                MoveSelection(w.Button == Terminal.MouseButton.WheelUp ? -1 : 1, _dataViewportRows + (ShowHeader ? 1 : 0));
                return true;
            case Terminal.KeyEvent key when HasFocus:
                return HandleKey(key);
        }
        return false;
    }

    private bool HandleClick(Terminal.MouseEvent m)
    {
        if (Sortable && m.Y == _headerRow)
        {
            foreach (var (start, end, col) in _headerHits)
            {
                if (m.X >= start && m.X < end) { CycleSort(col); return true; }
            }
            return false;
        }

        if (_dataArea.Contains(m.X, m.Y))
        {
            int rowIndex = ScrollOffset + (m.Y - _dataTop);
            if (rowIndex >= 0 && rowIndex < LineCount)
            {
                bool reclick = rowIndex == SelectedIndex;
                SetSelection(rowIndex);
                if (reclick)
                {
                    RaiseActivate(rowIndex);
                }

                return true;
            }
        }
        return false;
    }

    private bool HandleKey(Terminal.KeyEvent key)
    {
        if (HandleExtraKey(key))
        {
            return true;
        }

        int viewport = _dataViewportRows + (ShowHeader ? 1 : 0);
        switch (key.Key)
        {
            case Terminal.Key.Up: MoveSelection(-1, viewport); return true;
            case Terminal.Key.Down: MoveSelection(1, viewport); return true;
            case Terminal.Key.PageUp: MoveSelection(-Math.Max(1, _dataViewportRows - 1), viewport); return true;
            case Terminal.Key.PageDown: MoveSelection(Math.Max(1, _dataViewportRows - 1), viewport); return true;
            case Terminal.Key.Home: SetSelection(0); return true;
            case Terminal.Key.End: SetSelection(LineCount - 1); return true;
            case Terminal.Key.Enter:
                if (SelectedIndex >= 0 && SelectedIndex < LineCount)
                {
                    RaiseActivate(SelectedIndex);
                }

                return true;
        }
        return false;
    }

    private void SetSelection(int index)
    {
        int count = LineCount;
        if (count == 0)
        {
            return;
        }

        int clamped = Math.Clamp(index, 0, count - 1);
        if (clamped != SelectedIndex)
        {
            SelectedIndex = clamped;
            RaiseSelect(SelectedIndex);
        }
        int dataRows = _dataViewportRows > 0 ? _dataViewportRows : count;
        if (SelectedIndex < ScrollOffset)
        {
            ScrollOffset = SelectedIndex;
        }
        else if (SelectedIndex >= ScrollOffset + dataRows)
        {
            ScrollOffset = SelectedIndex - dataRows + 1;
        }
    }

    private int[] ResolveColumnWidths(int totalWidth)
    {
        int n = ColumnCount;
        int spacingTotal = ColumnSpacing * Math.Max(0, n - 1);
        int usable = Math.Max(0, totalWidth - spacingTotal);

        if (_widths.Length != n)
        {
            _widths = new int[n];
            _constraints = new Constraint[n];
        }
        for (int i = 0; i < n; i++)
        {
            _constraints[i] = ColumnWidth(i);
        }

        LayoutSolver.SolveInto(usable, _constraints, _widths);
        return _widths;
    }

    private protected static void DrawClippedAligned(Surface surface, Rect cell, string text, Style style, Justify align)
    {
        text = Truncate(text, cell.Width);
        int textWidth = Unicode.StringWidth(text);
        int x = align switch
        {
            Justify.Right => cell.X + Math.Max(0, cell.Width - textWidth),
            Justify.Center => cell.X + Math.Max(0, (cell.Width - textWidth) / 2),
            _ => cell.X,
        };
        surface.SetClip(cell);
        surface.DrawText(x, cell.Y, text, style);
        surface.ResetClip();
    }

    /// <summary>Truncates a string to a display width, appending an ellipsis when it overflows.
    /// Allocation-free when the text already fits (the common case).</summary>
    private static string Truncate(string text, int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return string.Empty;
        }

        if (Unicode.StringWidth(text) <= maxWidth) // allocation-free width check
        {
            return text;
        }

        if (maxWidth == 1)
        {
            return "…";
        }

        // Overflow (rare): copy runes up to maxWidth-1 cells, then an ellipsis. Walk the
        // UTF-16 directly rather than allocating a grapheme enumerator + per-cluster strings.
        var sb = new StringBuilder(maxWidth + 1);
        int w = 0;
        int i = 0;
        var span = text.AsSpan();
        while (i < span.Length)
        {
            Rune.DecodeFromUtf16(span[i..], out var rune, out int len); // U+FFFD for an unpaired surrogate
            int gw = Unicode.RuneWidth(rune);
            if (w + gw > maxWidth - 1)
            {
                break;
            }

            sb.Append(span.Slice(i, len));
            w += gw;
            i += len;
        }
        sb.Append('…');
        return sb.ToString();
    }
}

/// <summary>
/// A table of strings: each row is one string per column. Sorting compares the strings, or the
/// <see cref="Column.SortKey"/> parsed from them. For rows that are objects, with styled cells, use
/// <see cref="Table{T}"/>.
/// </summary>
public sealed class Table : TableBase<string[]>
{
    public List<Column> Columns { get; } = new();

    /// <summary>Raised when the selected row changes, with the new selected index.</summary>
    public Action<int>? OnSelect { get; set; }

    /// <summary>Raised when a row is activated (double-click or Enter), with its index.</summary>
    public Action<int>? OnActivate { get; set; }

    private protected override int ColumnCount => Columns.Count;
    private protected override string ColumnHeader(int column) => Columns[column].Header;
    private protected override Constraint ColumnWidth(int column) => Columns[column].Width;
    private protected override Justify ColumnJustify(int column) => Columns[column].Justify;

    private protected override (IComparable? Key, string Text) SortValue(string[] row, int column)
    {
        string cell = column < row.Length ? row[column] ?? string.Empty : string.Empty;
        IComparable? key = null;
        if (Columns[column].SortKey is { } sortKey)
        {
            try { key = sortKey(cell); } catch { key = null; } // text that doesn't parse sorts as having no key
        }
        return (key, cell);
    }

    private protected override void DrawCell(Surface surface, Rect cell, string[] row, int column, Style style, bool selected)
    {
        if (column < row.Length)
        {
            DrawClippedAligned(surface, cell, row[column] ?? string.Empty, style, Columns[column].Justify);
        }
    }

    private protected override void RaiseSelect(int index) => OnSelect?.Invoke(index);
    private protected override void RaiseActivate(int index) => OnActivate?.Invoke(index);
}

