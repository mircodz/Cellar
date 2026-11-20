using System;
using System.Collections.Generic;
using System.Text;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// A pager for long text: a log, a file, a JSON document. Scrolls both ways, searches with <c>/</c> (matches are
/// highlighted while typing, <c>n</c>/<c>N</c> step through them and a counter shows where you are), shows line
/// numbers and wraps long lines on request, colors lines with a pluggable <see cref="Highlighter"/>, and copies
/// what's on screen or everything.
/// </summary>
/// <remarks>
/// <para>Keys: <c>↑</c>/<c>↓</c>/<c>j</c>/<c>k</c> scroll, <c>PgUp</c>/<c>PgDn</c>/<c>space</c>/<c>b</c> page,
/// <c>Home</c>/<c>End</c>/<c>g</c>/<c>G</c> jump, <c>←</c>/<c>→</c>/<c>h</c>/<c>l</c> scroll sideways (when not
/// wrapping), <c>/</c> searches, <c>n</c>/<c>N</c> go to the next/previous match, <c>#</c> toggles line numbers,
/// <c>w</c> toggles wrapping, <c>y</c> copies the lines on screen and <c>Y</c> the whole text. Esc clears the
/// search; with no search it isn't handled, so a container can use it to close the viewer.</para>
/// <para>Search ignores case unless the query has an upper-case letter ("smart case"). Lines are highlighted one
/// at a time when they first scroll into view, and the result is kept, so a huge text costs nothing up front.</para>
/// </remarks>
public sealed class TextViewer : Widget
{
    private const int WheelStep = 3;
    private const int SideStep = 8;

    private readonly List<string> _plain = new();
    private readonly List<int> _widths = new();
    private readonly List<StyledText?> _styled = new();
    private readonly List<Match> _matches = new();
    private readonly List<int> _rowStarts = new(); // first wrapped row of each line, plus the total at the end
    private readonly Input _prompt = new();
    private Func<string, StyledText>? _highlighter;
    private int _rowWidth = -1; // the width _rowStarts was computed for, -1 when stale
    private int _topRow;
    private int _left;
    private int _current = -1;
    private int _searchOrigin;
    private string _searchBeforePrompt = string.Empty;
    private bool _wrap;
    private string? _note;
    private Rect _bounds;
    private Rect _textRect;
    private App? _app;

    public TextViewer(string text = "")
    {
        _prompt.OnChange = query =>
        {
            Search = query;
            FindMatches(_searchOrigin);
        };
        Text = text;
    }

    /// <summary>The text, lines separated by <c>\n</c> (a <c>\r</c> before it is dropped). Tabs become spaces to
    /// the next multiple of four columns. Setting it keeps the scroll position where it can and searches again.</summary>
    public string Text
    {
        get => string.Join('\n', _plain);
        set
        {
            int top = TopLine;
            _plain.Clear();
            _widths.Clear();
            _styled.Clear();
            foreach (var line in (value ?? string.Empty).Split('\n'))
            {
                AddLine(ExpandTabs(line.EndsWith('\r') ? line[..^1] : line), null);
            }

            Reflow(top);
        }
    }

    /// <summary>Replaces the text with lines styled already; <see cref="Highlighter"/> isn't used for them.</summary>
    public void SetLines(IEnumerable<StyledText> lines)
    {
        int top = TopLine;
        _plain.Clear();
        _widths.Clear();
        _styled.Clear();
        foreach (var line in lines)
        {
            AddLine(line.PlainText, line);
        }

        Reflow(top);
    }

    /// <summary>Colors a line of <see cref="Text"/>; null draws plain text. See <see cref="TextHighlighters"/> for
    /// JSON and XML. Each line is highlighted on its own, so the highlighter can't carry state between lines.</summary>
    public Func<string, StyledText>? Highlighter
    {
        get => _highlighter;
        set
        {
            _highlighter = value;
            for (int i = 0; i < _styled.Count; i++)
            {
                _styled[i] = null;
            }
        }
    }

    /// <summary>The number of lines.</summary>
    public int LineCount => _plain.Count;

    /// <summary>The line at <paramref name="index"/>, unstyled.</summary>
    public string Line(int index) => _plain[index];

    /// <summary>Whether to draw line numbers in a gutter. Toggled with <c>#</c>.</summary>
    public bool ShowLineNumbers { get; set; }

    /// <summary>Whether long lines continue on the next row instead of running off to the right. Toggled with
    /// <c>w</c>. Switching keeps the top line where it is.</summary>
    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap == value)
            {
                return;
            }

            int top = TopLine;
            _wrap = value;
            _left = 0;
            _rowWidth = -1;
            ScrollToLine(top);
        }
    }

    /// <summary>Whether <c>/</c> opens the built-in search prompt on the bottom row. Turn it off to drive the search
    /// from elsewhere (a <see cref="CommandLine"/> "/" mode calling <see cref="Find"/>, say).</summary>
    public bool SearchPrompt { get; set; } = true;

    /// <summary>Earlier searches, browsed with ↑/↓ in the search prompt.</summary>
    public InputHistory? SearchHistory
    {
        get => _prompt.History;
        set => _prompt.History = value;
    }

    /// <summary>Copies text; by default to the system clipboard through the app the viewer is mounted in.</summary>
    public Action<string>? OnCopy { get; set; }

    /// <summary>What's being searched for, or empty.</summary>
    public string Search { get; private set; } = string.Empty;

    /// <summary>Whether the search prompt is open.</summary>
    public bool IsSearching { get; private set; }

    /// <summary>How many matches <see cref="Search"/> has.</summary>
    public int MatchCount => _matches.Count;

    /// <summary>The 1-based number of the current match, or 0 when there's none.</summary>
    public int CurrentMatch => _current + 1;

    /// <summary>The line (0-based) at the top of the view.</summary>
    public int TopLine => _wrap ? LineOfRow(_topRow) : _topRow;

    /// <summary>How far the view is scrolled to the right, in columns.</summary>
    public int LeftColumn => _left;

    /// <summary>The whole lines that are at least partly on screen, joined with <c>\n</c>.</summary>
    public string VisibleText
    {
        get
        {
            if (_plain.Count == 0)
            {
                return string.Empty;
            }

            int first = TopLine;
            int height = Math.Max(1, _textRect.Height);
            int last = _wrap ? LineOfRow(Math.Min(TotalRows - 1, _topRow + height - 1)) : Math.Min(_plain.Count - 1, _topRow + height - 1);
            var sb = new StringBuilder();
            for (int i = first; i <= last; i++)
            {
                if (i > first)
                {
                    sb.Append('\n');
                }

                sb.Append(_plain[i]);
            }

            return sb.ToString();
        }
    }

    public override bool IsFocusable => true;

    public override bool HasFocus
    {
        get => base.HasFocus;
        set
        {
            base.HasFocus = value;
            _prompt.HasFocus = value && IsSearching;
        }
    }

    /// <summary>Highlights <paramref name="query"/> and moves to its first match at or below the top line.
    /// Empty clears the search.</summary>
    public void Find(string query)
    {
        Search = query ?? string.Empty;
        FindMatches(TopLine);
    }

    /// <summary>Goes to the next match (<paramref name="direction"/> 1) or the previous one (-1), wrapping around.</summary>
    public void NextMatch(int direction = 1)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        _current = _current < 0
            ? (direction > 0 ? 0 : _matches.Count - 1)
            : ((_current + direction) % _matches.Count + _matches.Count) % _matches.Count;
        ScrollToMatch();
    }

    /// <summary>Scrolls so that <paramref name="line"/> is at the top (or as close as the end allows).</summary>
    public void ScrollToLine(int line)
    {
        if (_plain.Count == 0)
        {
            _topRow = 0;
            return;
        }

        line = Math.Clamp(line, 0, _plain.Count - 1);
        _topRow = _wrap ? FirstRowOf(line) : line;
        ClampTop();
    }

    /// <summary>Copies the lines on screen (<paramref name="all"/> false) or the whole text.</summary>
    public void Copy(bool all)
    {
        string text = all ? Text : VisibleText;
        if (OnCopy is { } copy)
        {
            copy(text);
        }
        else
        {
            _app?.CopyToClipboard(text);
        }

        int lines = all ? _plain.Count : text.Length == 0 ? 0 : CountLines(text);
        _note = $"copied {lines} line{(lines == 1 ? "" : "s")}";
    }

    protected override void OnMount(App app) => _app = app;

    protected override void OnUnmount() => _app = null;

    public override void Render(Surface surface, Rect area)
    {
        _bounds = area;
        if (area.IsEmpty)
        {
            return;
        }

        var theme = Theme.Current;
        bool footer = (IsSearching || Search.Length > 0 || _note is not null) && area.Height > 1;
        var body = new Rect(area.X, area.Y, area.Width, area.Height - (footer ? 1 : 0));
        int gutter = ShowLineNumbers ? Digits(_plain.Count) + 1 : 0;

        // A scrollbar column only when the text doesn't fit.
        int narrow = Math.Max(1, body.Width - gutter - 1);
        bool overflow = _plain.Count > body.Height || (_wrap && RowsFor(narrow) > body.Height);
        int textWidth = Math.Max(1, body.Width - gutter - (overflow && body.Width > gutter + 1 ? 1 : 0));
        _textRect = new Rect(body.X + gutter, body.Y, textWidth, body.Height);
        if (_wrap)
        {
            EnsureRows(textWidth);
        }

        ClampTop();
        DrawBody(surface, body, gutter);

        if (overflow && body.Width > gutter + 1)
        {
            Scrollbar.Default.Render(surface, new Rect(body.Right - 1, body.Y, 1, body.Height), _topRow, body.Height, TotalRows);
        }

        if (footer)
        {
            DrawFooter(surface, new Rect(area.X, area.Bottom - 1, area.Width, 1), theme);
        }
    }

    private void DrawBody(Surface surface, Rect body, int gutter)
    {
        var theme = Theme.Current;
        var clip = surface.Clip;
        var highlight = new Style(theme.Background, theme.Warning);
        var current = new Style(theme.Background, theme.Accent);
        int line = TopLine;
        int sub = _wrap ? _topRow - FirstRowOf(line) : 0;
        int m = FirstMatchAtOrAfter(line);
        Span<char> number = stackalloc char[11];
        for (int row = 0; row < body.Height && line < _plain.Count; row++)
        {
            int y = body.Y + row;
            if (gutter > 0 && sub == 0 && (line + 1).TryFormat(number, out int digits))
            {
                surface.DrawText(body.X + gutter - 1 - digits, y, number[..digits], theme.MutedStyle);
            }

            int offset = _wrap ? sub * _textRect.Width : _left;
            int x0 = _textRect.X - offset;
            surface.SetClip(new Rect(_textRect.X, y, _textRect.Width, 1).Intersect(clip));
            var styled = Styled(line);
            // Drawn from left of the clip so the line scrolls; wrapped rows draw the whole line, others end in an
            // ellipsis at the right edge when there's more.
            TextRenderer.DrawLine(surface, x0, y, _wrap ? Math.Max(_widths[line], 1) : offset + _textRect.Width, styled, Justify.Left);

            while (m < _matches.Count && _matches[m].Line == line)
            {
                var match = _matches[m];
                var style = m == _current ? current : highlight;
                for (int c = 0; c < match.Width; c++)
                {
                    int x = x0 + match.Column + c;
                    if (x >= _textRect.X && x < _textRect.Right)
                    {
                        surface.Set(x, y, surface.Get(x, y).WithStyle(style));
                    }
                }

                m++;
            }

            surface.SetClip(clip);

            if (_wrap && sub + 1 < RowsOfLine(line, _textRect.Width))
            {
                sub++;
                m = FirstMatchAtOrAfter(line);
            }
            else
            {
                line++;
                sub = 0;
            }
        }
    }

    private void DrawFooter(Surface surface, Rect row, Theme theme)
    {
        var clip = surface.Clip;
        surface.SetClip(row.Intersect(clip));
        string? counter = Search.Length == 0 ? null : _matches.Count == 0 ? "no matches" : null;
        int counterWidth;
        Span<char> buffer = stackalloc char[24];
        if (counter is not null)
        {
            counterWidth = counter.Length;
            surface.DrawText(row.Right - counterWidth, row.Y, counter, new Style(theme.Error, Color.Default));
        }
        else if (Search.Length > 0 && FormatCounter(buffer, out int length))
        {
            counterWidth = length;
            surface.DrawText(row.Right - length, row.Y, buffer[..length], theme.MutedStyle);
        }
        else
        {
            counterWidth = 0;
        }

        int room = Math.Max(0, row.Width - counterWidth - 1);
        if (IsSearching)
        {
            surface.DrawText(row.X, row.Y, "/", theme.AccentStyle.Bold);
            _prompt.Render(surface, new Rect(row.X + 1, row.Y, Math.Max(0, room - 1), 1));
        }
        else if (_note is { } note)
        {
            surface.SetClip(new Rect(row.X, row.Y, room, 1).Intersect(clip));
            surface.DrawText(row.X, row.Y, note, new Style(theme.Success, Color.Default));
        }
        else if (Search.Length > 0)
        {
            surface.SetClip(new Rect(row.X, row.Y, room, 1).Intersect(clip));
            int x = surface.DrawText(row.X, row.Y, "/", theme.MutedStyle);
            surface.DrawText(x, row.Y, Search, theme.MutedStyle);
        }

        surface.SetClip(clip);
    }

    private bool FormatCounter(Span<char> buffer, out int length)
    {
        length = 0;
        if (!(_current + 1).TryFormat(buffer, out int a))
        {
            return false;
        }

        buffer[a] = '/';
        if (!_matches.Count.TryFormat(buffer[(a + 1)..], out int b))
        {
            return false;
        }

        length = a + 1 + b;
        return true;
    }

    public override bool OnEvent(InputEvent e)
    {
        if (e is MouseEvent { Kind: MouseEventKind.Wheel } wheel && _bounds.Contains(wheel.X, wheel.Y))
        {
            ScrollBy(wheel.Button == MouseButton.WheelUp ? -WheelStep : WheelStep);
            return true;
        }

        if (!HasFocus)
        {
            return false;
        }

        if (IsSearching)
        {
            return OnPromptEvent(e);
        }

        if (e is not KeyEvent key || (key.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0)
        {
            return false;
        }

        _note = null;
        int page = Math.Max(1, _textRect.Height - 1);
        switch (key.Key)
        {
            case Key.Up: ScrollBy(-1); return true;
            case Key.Down: ScrollBy(1); return true;
            case Key.PageUp: ScrollBy(-page); return true;
            case Key.PageDown: ScrollBy(page); return true;
            case Key.Home: _topRow = 0; _left = 0; return true;
            case Key.End: _topRow = int.MaxValue; ClampTop(); return true;
            case Key.Left: return ScrollSideways(-SideStep);
            case Key.Right: return ScrollSideways(SideStep);
            case Key.Escape when Search.Length > 0:
                Find(string.Empty);
                return true;
            case Key.Char:
                return OnChar(key.Rune.Value, page);
        }

        return false;
    }

    private bool OnChar(int c, int page)
    {
        switch (c)
        {
            case 'j': ScrollBy(1); return true;
            case 'k': ScrollBy(-1); return true;
            case ' ': ScrollBy(page); return true;
            case 'b': ScrollBy(-page); return true;
            case 'g': _topRow = 0; _left = 0; return true;
            case 'G': _topRow = int.MaxValue; ClampTop(); return true;
            case 'h': return ScrollSideways(-SideStep);
            case 'l': return ScrollSideways(SideStep);
            case 'n': NextMatch(1); return true;
            case 'N': NextMatch(-1); return true;
            case '#': ShowLineNumbers = !ShowLineNumbers; return true;
            case 'w': Wrap = !Wrap; return true;
            case 'y': Copy(all: false); return true;
            case 'Y': Copy(all: true); return true;
            case '/' when SearchPrompt:
                IsSearching = true;
                _searchBeforePrompt = Search;
                _searchOrigin = TopLine;
                _prompt.Text = string.Empty;
                _prompt.HasFocus = true;
                return true;
        }

        return false;
    }

    private bool OnPromptEvent(InputEvent e)
    {
        if (e is KeyEvent key)
        {
            switch (key.Key)
            {
                case Key.Enter:
                    _prompt.History?.Add(_prompt.Text);
                    ClosePrompt();
                    return true;
                case Key.Escape:
                    ClosePrompt();
                    Search = _searchBeforePrompt;
                    FindMatches(_searchOrigin);
                    _topRow = _wrap ? FirstRowOf(Math.Min(_searchOrigin, Math.Max(0, _plain.Count - 1))) : _searchOrigin;
                    ClampTop();
                    return true;
            }
        }

        _prompt.OnEvent(e);
        return true;
    }

    private void ClosePrompt()
    {
        IsSearching = false;
        _prompt.HasFocus = false;
    }

    private bool ScrollSideways(int delta)
    {
        if (_wrap)
        {
            return false;
        }

        _left = Math.Max(0, _left + delta);
        return true;
    }

    private void ScrollBy(int delta)
    {
        _topRow = (int)Math.Clamp((long)_topRow + delta, 0, int.MaxValue);
        ClampTop();
    }

    private void ClampTop()
    {
        int height = Math.Max(1, _textRect.Height);
        _topRow = Math.Clamp(_topRow, 0, Math.Max(0, TotalRows - height));
    }

    private void FindMatches(int from, bool scroll = true)
    {
        _matches.Clear();
        _current = -1;
        if (Search.Length == 0)
        {
            return;
        }

        var comparison = HasUpper(Search) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int line = 0; line < _plain.Count; line++)
        {
            string text = _plain[line];
            for (int at = text.IndexOf(Search, comparison); at >= 0; at = text.IndexOf(Search, at + Search.Length, comparison))
            {
                int column = Unicode.StringWidth(text.AsSpan(0, at));
                _matches.Add(new Match(line, column, Math.Max(1, Unicode.StringWidth(text.AsSpan(at, Search.Length)))));
            }
        }

        if (_matches.Count == 0)
        {
            return;
        }

        _current = FirstMatchAtOrAfter(from);
        if (_current >= _matches.Count)
        {
            _current = 0;
        }

        if (scroll)
        {
            ScrollToMatch();
        }
    }

    private void ScrollToMatch()
    {
        var match = _matches[_current];
        int height = Math.Max(1, _textRect.Height);
        int width = Math.Max(1, _textRect.Width);
        int row = _wrap ? FirstRowOf(match.Line) + match.Column / width : match.Line;
        if (row < _topRow || row >= _topRow + height)
        {
            _topRow = row - height / 3;
            ClampTop();
        }

        if (!_wrap && (match.Column < _left || match.Column + match.Width > _left + width))
        {
            _left = Math.Max(0, match.Column - width / 3);
        }
    }

    private int FirstMatchAtOrAfter(int line)
    {
        int lo = 0, hi = _matches.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_matches[mid].Line < line)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private StyledText Styled(int line)
    {
        if (_styled[line] is { } styled)
        {
            return styled;
        }

        styled = _highlighter is { } highlight ? highlight(_plain[line]) : new StyledText(_plain[line], Theme.Current.TextStyle);
        _styled[line] = styled;
        return styled;
    }

    private void AddLine(string plain, StyledText? styled)
    {
        _plain.Add(plain);
        _widths.Add(Unicode.StringWidth(plain));
        _styled.Add(styled);
    }

    // After the lines changed: the old wrapped layout is stale, so the top is kept as a line until the next render
    // lays the rows out again. Matches are found again without moving the view, so a refreshing log stays put.
    private void Reflow(int topLine)
    {
        _rowWidth = -1;
        _topRow = topLine;
        ClampTop();
        FindMatches(topLine, scroll: false);
    }

    // ---- wrapped rows ----

    private int TotalRows => _wrap && _rowWidth > 0 ? _rowStarts[^1] : _plain.Count;

    private static int RowsOf(int width, int rowWidth) => Math.Max(1, (width + rowWidth - 1) / rowWidth);

    private int RowsOfLine(int line, int rowWidth) => _wrap ? RowsOf(_widths[line], rowWidth) : 1;

    // Rows the text needs at a width, without disturbing the cached layout; cheap when it fits (few lines).
    private int RowsFor(int rowWidth)
    {
        if (rowWidth == _rowWidth)
        {
            return _rowStarts[^1];
        }

        int rows = 0;
        foreach (int width in _widths)
        {
            rows += RowsOf(width, rowWidth);
        }

        return rows;
    }

    private void EnsureRows(int rowWidth)
    {
        if (rowWidth == _rowWidth)
        {
            return;
        }

        // Keep the top line in place across the re-layout.
        int top = _rowWidth > 0 ? LineOfRow(_topRow) : -1;
        _rowStarts.Clear();
        int rows = 0;
        foreach (int width in _widths)
        {
            _rowStarts.Add(rows);
            rows += RowsOf(width, rowWidth);
        }

        _rowStarts.Add(rows);
        _rowWidth = rowWidth;
        if (top >= 0)
        {
            _topRow = FirstRowOf(top);
        }
        else if (_topRow < _plain.Count)
        {
            // Coming from unwrapped, where rows were lines.
            _topRow = FirstRowOf(_topRow);
        }
    }

    private int FirstRowOf(int line) => _wrap && _rowWidth > 0 ? _rowStarts[Math.Clamp(line, 0, _plain.Count)] : line;

    private int LineOfRow(int row)
    {
        if (!_wrap || _rowWidth <= 0)
        {
            return Math.Clamp(row, 0, Math.Max(0, _plain.Count - 1));
        }

        int lo = 0, hi = _plain.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_rowStarts[mid] <= row)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return Math.Max(0, lo);
    }

    // ---- helpers ----

    private static bool HasUpper(string text)
    {
        foreach (char c in text)
        {
            if (char.IsUpper(c))
            {
                return true;
            }
        }

        return false;
    }

    private static int Digits(int n) => n < 10 ? 1 : (int)Math.Floor(Math.Log10(n)) + 1;

    private static int CountLines(string text)
    {
        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static string ExpandTabs(string line)
    {
        if (!line.Contains('\t', StringComparison.Ordinal))
        {
            return line;
        }

        var sb = new StringBuilder(line.Length + 8);
        int column = 0;
        foreach (char c in line)
        {
            if (c == '\t')
            {
                int spaces = 4 - column % 4;
                sb.Append(' ', spaces);
                column += spaces;
            }
            else
            {
                sb.Append(c);
                column++;
            }
        }

        return sb.ToString();
    }

    private readonly record struct Match(int Line, int Column, int Width);
}
