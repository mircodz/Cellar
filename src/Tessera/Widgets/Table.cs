using System;
using System.Collections.Generic;
using System.Text;
using Tessera.Layout;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Text;

namespace Tessera.Widgets;

/// <summary>The sort direction of a <see cref="Table"/> column.</summary>
public enum SortState { None, Ascending, Descending }

/// <summary>A table column: a header label, a width constraint, and cell alignment.</summary>
public sealed class Column
{
    public string Header { get; set; }
    public Constraint Width { get; set; }
    public Justify Justify { get; set; }

    /// <summary>Maps a cell string to a comparable sort key so numeric columns sort correctly
    /// (42 before 100). Null uses ordinal string comparison. E.g. <c>SortKey = double.Parse</c>.</summary>
    public Func<string, IComparable>? SortKey { get; set; }

    public Column(string header, Constraint width, Justify justify = Justify.Left)
    {
        Header = header;
        Width = width;
        Justify = justify;
    }
}

/// <summary>
/// A scrollable, column-constrained table with an optional header and a highlighted selected
/// row. Column widths are resolved by the <see cref="LayoutSolver"/> to tile the width exactly.
/// </summary>
public sealed class Table : Widget
{
    public List<Column> Columns { get; } = new();
    public List<string[]> Rows { get; } = new();

    public bool ShowHeader { get; set; } = true;
    public int SelectedIndex { get; set; } = -1;
    public int ScrollOffset { get; set; }

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

    // The row order as first sorted, so a None state can restore it. Rows are reference types,
    // so we keep the array of references (cheap) — not a deep copy.
    private string[][]? _originalOrder;

    private string[] _headerCells = [];
    private int _headerSortColumn = -2;
    private bool _headerSortDescending;

    private int[] _widths = [];
    private Constraint[] _constraints = [];

    /// <summary>Raised when the selected row changes, with the new selected index.</summary>
    public Action<int>? OnSelect { get; set; }

    /// <summary>Raised when a row is activated (double-click or Enter), with its index.</summary>
    public Action<int>? OnActivate { get; set; }

    public override bool IsFocusable => true;

    /// <summary>Cycles the sort on <paramref name="column"/>: None → Asc → Desc → None (None
    /// restores insertion order). Uses <see cref="Column.SortKey"/> when set.</summary>
    public void CycleSort(int column)
    {
        if (column < 0 || column >= Columns.Count)
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
        if (column < 0 || column >= Columns.Count)
        {
            return;
        }

        ApplySort(column, state);
    }

    private void ApplySort(int column, SortState state)
    {
        SortColumn = column;
        SortState = state;

        if (state == SortState.None)
        {
            RestoreOriginalOrder();
            SortColumn = -1;
            return;
        }

        CaptureOriginalOrder();

        var keySelector = Columns[column].SortKey;
        int n = Rows.Count;

        // Schwartzian transform: compute each row's comparison key exactly once.
        var keyed = new (IComparable? key, string text, string[] row)[n];
        for (int i = 0; i < n; i++)
        {
            var row = Rows[i];
            string cell = column < row.Length ? row[column] : string.Empty;
            IComparable? key = null;
            if (keySelector is not null)
            {
                try { key = keySelector(cell); } catch { key = null; }
            }
            keyed[i] = (key, cell, row);
        }

        int dir = state == SortState.Descending ? -1 : 1;
        Array.Sort(keyed, (a, b) =>
        {
            int cmp;
            if (a.key is not null && b.key is not null)
            {
                cmp = a.key.CompareTo(b.key);
            }
            else
            {
                cmp = string.CompareOrdinal(a.text, b.text);
            }

            return cmp * dir;
        });

        Rows.Clear();
        foreach (var k in keyed)
        {
            Rows.Add(k.row);
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

        // Only restore rows that still exist (data may have changed since capture).
        var present = new HashSet<string[]>(Rows, ReferenceEqualityComparer.Instance);
        Rows.Clear();
        foreach (var r in _originalOrder)
        {
            if (present.Remove(r))
            {
                Rows.Add(r);
            }
        }

        foreach (var leftover in present)
        {
            Rows.Add(leftover);
        }

        _originalOrder = null;
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty || Columns.Count == 0)
        {
            return;
        }

        _bounds = area;

        int dataRowsVisible = area.Height - (ShowHeader ? 1 : 0);
        bool overflowing = Rows.Count > dataRowsVisible;
        bool drawBar = ShowScrollbar && overflowing && area.Width > 1;
        int barWidth = drawBar ? 1 : 0;

        var contentArea = new Rect(area.X, area.Y, area.Width - barWidth, area.Height);
        int[] widths = ResolveColumnWidths(contentArea.Width);

        int y = contentArea.Top;
        if (ShowHeader)
        {
            DrawRow(surface, contentArea, widths, y, HeaderCells(), EffectiveHeader, fill: true);
            RecordHeaderHits(contentArea, widths, y);
            y++;
        }

        int firstRow = Math.Max(0, ScrollOffset);
        int visibleRows = contentArea.Bottom - y;
        _dataTop = y;
        _dataViewportRows = visibleRows;
        _dataArea = new Rect(contentArea.X, y, contentArea.Width, Math.Max(0, visibleRows));
        for (int i = 0; i < visibleRows; i++)
        {
            int rowIndex = firstRow + i;
            if (rowIndex >= Rows.Count)
            {
                break;
            }

            bool selected = rowIndex == SelectedIndex;
            bool striped = Striped && (rowIndex & 1) == 1;
            var style = selected ? EffectiveSelected : striped ? EffectiveStripe : EffectiveRow;
            DrawRow(surface, contentArea, widths, y + i, Rows[rowIndex], style, fill: selected || striped);
        }

        if (drawBar)
        {
            int barTop = area.Top + (ShowHeader ? 1 : 0);
            var barRect = new Rect(area.Right - 1, barTop, 1, area.Height - (ShowHeader ? 1 : 0));
            Scrollbar.Render(surface, barRect, ScrollOffset, dataRowsVisible, Rows.Count);
        }
    }

    /// <summary>Selects row <paramref name="index"/> programmatically: fires <see cref="OnSelect"/>
    /// (if it changed) and scrolls it into view. The same path a click/arrow uses.</summary>
    public void Select(int index) => SetSelection(index);

    /// <summary>Moves the selection by <paramref name="delta"/>, keeping it visible.</summary>
    public void MoveSelection(int delta, int viewportRows)
    {        if (Rows.Count == 0)
        {
            return;
        }

        int previous = SelectedIndex;
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Rows.Count - 1);
        if (SelectedIndex != previous)
        {
            OnSelect?.Invoke(SelectedIndex);
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

    private string[] HeaderCells()
    {
        int n = Columns.Count;
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
                    ? Columns[i].Header + (SortDescending ? " ▼" : " ▲")
                    : Columns[i].Header;
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
        for (int c = 0; c < Columns.Count; c++)
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
            if (rowIndex >= 0 && rowIndex < Rows.Count)
            {
                bool reclick = rowIndex == SelectedIndex;
                SetSelection(rowIndex);
                if (reclick)
                {
                    OnActivate?.Invoke(rowIndex);
                }

                return true;
            }
        }
        return false;
    }

    private bool HandleKey(Terminal.KeyEvent key)
    {
        int viewport = _dataViewportRows + (ShowHeader ? 1 : 0);
        switch (key.Key)
        {
            case Terminal.Key.Up: MoveSelection(-1, viewport); return true;
            case Terminal.Key.Down: MoveSelection(1, viewport); return true;
            case Terminal.Key.PageUp: MoveSelection(-Math.Max(1, _dataViewportRows - 1), viewport); return true;
            case Terminal.Key.PageDown: MoveSelection(Math.Max(1, _dataViewportRows - 1), viewport); return true;
            case Terminal.Key.Home: SetSelection(0); return true;
            case Terminal.Key.End: SetSelection(Rows.Count - 1); return true;
            case Terminal.Key.Enter:
                if (SelectedIndex >= 0)
                {
                    OnActivate?.Invoke(SelectedIndex);
                }

                return true;
        }
        return false;
    }

    private void SetSelection(int index)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        int clamped = Math.Clamp(index, 0, Rows.Count - 1);
        if (clamped != SelectedIndex)
        {
            SelectedIndex = clamped;
            OnSelect?.Invoke(SelectedIndex);
        }
        int dataRows = _dataViewportRows > 0 ? _dataViewportRows : Rows.Count;
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
        int n = Columns.Count;
        int spacingTotal = ColumnSpacing * Math.Max(0, n - 1);
        int usable = Math.Max(0, totalWidth - spacingTotal);

        if (_widths.Length != n)
        {
            _widths = new int[n];
            _constraints = new Constraint[n];
        }
        for (int i = 0; i < n; i++)
        {
            _constraints[i] = Columns[i].Width;
        }

        LayoutSolver.SolveInto(usable, _constraints, _widths);
        return _widths;
    }

    private void DrawRow(Surface surface, Rect area, int[] widths, int y, string[] cells, Style style, bool fill)
    {
        if (y < area.Top || y >= area.Bottom)
        {
            return;
        }

        if (fill)
        {
            surface.FillRect(new Rect(area.X, y, area.Width, 1), style);
        }

        int x = area.X;
        for (int c = 0; c < Columns.Count; c++)
        {
            int w = widths[c];
            if (w > 0 && c < cells.Length)
            {
                string text = cells[c] ?? string.Empty;
                DrawClippedAligned(surface, new Rect(x, y, w, 1), text, style, Columns[c].Justify);
            }
            x += w + ColumnSpacing;
        }
    }

    private static void DrawClippedAligned(Surface surface, Rect cell, string text, Style style, Justify align)
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
            char c = span[i];
            int len = char.IsHighSurrogate(c) && i + 1 < span.Length && char.IsLowSurrogate(span[i + 1]) ? 2 : 1;
            var rune = len == 2 ? new Rune(c, span[i + 1]) : new Rune(c);
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
