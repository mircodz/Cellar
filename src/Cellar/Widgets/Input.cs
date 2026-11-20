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
/// A single-line editable text field: insertion, backspace/delete, cursor movement, a
/// placeholder, and horizontal scrolling. Edits operate on Unicode scalars. Consumes
/// keystrokes only while focused.
/// </summary>
/// <remarks>
/// Understands the readline keys people type without thinking in a shell, so every text box in an app behaves
/// the same: <c>ctrl+a</c>/<c>ctrl+e</c> (start/end), <c>ctrl+w</c> (delete the previous whitespace-separated
/// word), <c>alt+backspace</c> (delete the previous word), <c>alt+d</c> (delete the next word), <c>ctrl+u</c>
/// (delete to start), <c>ctrl+k</c> (delete to end), <c>ctrl+y</c> (paste back what those deleted),
/// <c>alt+b</c>/<c>alt+f</c> and <c>ctrl+←</c>/<c>ctrl+→</c> (word left/right). As in readline, a "word" for the
/// alt keys is letters, digits and underscores, while <c>ctrl+w</c> deletes back to whitespace, so it removes a
/// whole path or flag. With a <see cref="History"/>, <c>↑</c>/<c>↓</c> browse earlier entries.
/// </remarks>
public sealed class Input : Widget
{
    private readonly List<Rune> _runes = new();
    private int _cursor;
    private int _scroll;
    private string? _text; // cached Text; null after an edit
    private string _killed = string.Empty;

    // History browsing: -1 while editing the draft, else the index into History.Entries being shown.
    private int _historyIndex = -1;
    private string _draft = string.Empty;

    /// <summary>Text shown dimmed when the field is empty.</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>Raised whenever the text changes, with the new value.</summary>
    public Action<string>? OnChange { get; set; }

    /// <summary>Raised when Enter is pressed, with the current value.</summary>
    public Action<string>? OnSubmit { get; set; }

    /// <summary>Masking character for password fields; null shows the real text.</summary>
    public char? Mask { get; set; }

    public Style? TextStyle { get; set; }
    public Style? PlaceholderStyle { get; set; }

    /// <summary>Earlier entries that <c>↑</c>/<c>↓</c> browse, or null (the default) to leave those keys to the
    /// container. What was being typed is kept as a draft and comes back after the newest entry. Enter records
    /// the submitted text. Share one instance between fields that should share a history.</summary>
    public InputHistory? History { get; set; }

    /// <summary>Text drawn dimmed after the text while the caret is at the end, such as the rest of a completion
    /// (<see cref="CommandLine"/> uses it for the best match). It's only a hint: it isn't part of
    /// <see cref="Text"/> until the owner accepts it.</summary>
    public string? Suggestion { get; set; }

    /// <summary>Style of <see cref="Suggestion"/>. Null uses the theme's muted style.</summary>
    public Style? SuggestionStyle { get; set; }

    public override bool IsFocusable => true;

    public string Text
    {
        get
        {
            if (_text is null)
            {
                var sb = new StringBuilder(_runes.Count);
                foreach (var r in _runes)
                {
                    sb.Append(r.ToString());
                }

                _text = sb.ToString();
            }

            return _text;
        }
        set
        {
            SetRunes(value);
            _cursor = _runes.Count;
            _scroll = 0;
            _historyIndex = -1;
        }
    }

    /// <summary>The caret position, in Unicode scalars from the start of <see cref="Text"/>. Clamped to the text.</summary>
    public int CursorPosition
    {
        get => _cursor;
        set => _cursor = Math.Clamp(value, 0, _runes.Count);
    }

    // The length of Text in Unicode scalars, the unit of CursorPosition.
    internal int RuneCount => _runes.Count;

    public override Size Measure(Size available) => new(available.Width, 1);

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        var theme = Theme.Current;
        int width = area.Width;

        if (_runes.Count == 0 && !HasFocus && !string.IsNullOrEmpty(Placeholder))
        {
            var ph = new StyledText(Placeholder, PlaceholderStyle ?? theme.MutedStyle);
            TextRenderer.DrawLine(surface, area.X, area.Y, width, ph, Justify.Left);
            return;
        }

        EnsureCursorVisible(width);

        var style = TextStyle ?? theme.TextStyle;
        int x = area.X;
        int col = 0;
        Span<char> utf16 = stackalloc char[2];
        for (int i = _scroll; i < _runes.Count && col < width; i++)
        {
            var rune = Mask is { } m ? new Rune(m) : _runes[i];
            int w = Unicode.RuneWidth(rune);
            if (col + w > width)
            {
                break;
            }

            int len = rune.EncodeToUtf16(utf16);
            surface.DrawText(x, area.Y, utf16[..len], style);
            x += w;
            col += w;
        }

        if (HasFocus && _cursor == _runes.Count && !string.IsNullOrEmpty(Suggestion) && col < width)
        {
            var clip = surface.Clip;
            surface.SetClip(new Rect(x, area.Y, width - col, 1).Intersect(clip));
            surface.DrawText(x, area.Y, Suggestion, SuggestionStyle ?? new Style(theme.Muted, style.Background));
            surface.SetClip(clip);
        }

        if (HasFocus)
        {
            int caretCol = VisibleColumn(_cursor) - VisibleColumn(_scroll);
            if (caretCol >= 0 && caretCol < width)
            {
                int cx = area.X + caretCol;
                var under = surface.Get(cx, area.Y);
                string g = under.Grapheme.Length == 0 ? " " : under.Grapheme;
                surface.Set(cx, area.Y, Cell.FromGrapheme(g, style.Reverse));
            }
        }
    }

    public override bool OnEvent(InputEvent e)
    {
        if (!HasFocus)
        {
            return false;
        }

        if (e is PasteEvent paste)
        {
            InsertText(paste.Text);
            return true;
        }

        if (e is not KeyEvent key)
        {
            return false;
        }

        bool ctrl = (key.Modifiers & KeyModifiers.Control) != 0;
        bool alt = (key.Modifiers & KeyModifiers.Alt) != 0;

        switch (key.Key)
        {
            case Key.Left when ctrl || alt: _cursor = WordStartBefore(_cursor); return true;
            case Key.Right when ctrl || alt: _cursor = WordEndAfter(_cursor); return true;
            case Key.Left: MoveCursor(-1); return true;
            case Key.Right: MoveCursor(1); return true;
            case Key.Home: _cursor = 0; return true;
            case Key.End: _cursor = _runes.Count; return true;
            case Key.Backspace when ctrl || alt: Kill(WordStartBefore(_cursor), _cursor); return true;
            case Key.Backspace: DeleteBefore(); return true;
            case Key.Delete when ctrl || alt: Kill(_cursor, WordEndAfter(_cursor)); return true;
            case Key.Delete: DeleteAt(); return true;
            case Key.Up: return BrowseHistory(-1);
            case Key.Down: return BrowseHistory(1);
            case Key.Enter:
                History?.Add(Text);
                _historyIndex = -1;
                OnSubmit?.Invoke(Text);
                return true;
            case Key.Char when key.Modifiers is KeyModifiers.None or KeyModifiers.Shift:
                Insert(key.Rune);
                return true;
            case Key.Char when ctrl && !alt:
                return OnControlChar(key.Rune.Value);
            case Key.Char when alt && !ctrl:
                return OnAltChar(key.Rune.Value);
        }
        return false;
    }

    private bool OnControlChar(int c)
    {
        switch (c)
        {
            case 'a': _cursor = 0; return true;
            case 'e': _cursor = _runes.Count; return true;
            case 'w': Kill(WhitespaceWordStartBefore(_cursor), _cursor); return true;
            case 'u': Kill(0, _cursor); return true;
            case 'k': Kill(_cursor, _runes.Count); return true;
            case 'y': InsertText(_killed); return true;
        }
        return false;
    }

    private bool OnAltChar(int c)
    {
        switch (c)
        {
            case 'b': _cursor = WordStartBefore(_cursor); return true;
            case 'f': _cursor = WordEndAfter(_cursor); return true;
            case 'd': Kill(_cursor, WordEndAfter(_cursor)); return true;
        }
        return false;
    }

    private void Insert(Rune r)
    {
        _runes.Insert(_cursor, r);
        _cursor++;
        Changed();
    }

    // Inserts pasted or yanked text at the caret. A single-line field turns line breaks and tabs into spaces.
    private void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        bool lastWasBreak = false;
        foreach (var r in text.EnumerateRunes())
        {
            bool isBreak = r.Value is '\r' or '\n';
            if (isBreak && lastWasBreak)
            {
                continue; // \r\n is one break
            }

            lastWasBreak = isBreak;
            var insert = isBreak || r.Value == '\t' ? new Rune(' ') : r;
            if (Rune.IsControl(insert))
            {
                continue;
            }

            _runes.Insert(_cursor++, insert);
        }
        Changed();
    }

    private void DeleteBefore()
    {
        if (_cursor <= 0)
        {
            return;
        }

        _runes.RemoveAt(_cursor - 1);
        _cursor--;
        Changed();
    }

    private void DeleteAt()
    {
        if (_cursor >= _runes.Count)
        {
            return;
        }

        _runes.RemoveAt(_cursor);
        Changed();
    }

    // Deletes [from, to) and keeps it for ctrl+y, as readline's kill commands do.
    private void Kill(int from, int to)
    {
        if (to <= from)
        {
            return;
        }

        if (Mask is not null)
        {
            // A secret must not survive in the yank buffer, where another field could paste it in the clear.
            _killed = string.Empty;
        }
        else
        {
            var sb = new StringBuilder(to - from);
            for (int i = from; i < to; i++)
            {
                sb.Append(_runes[i].ToString());
            }

            _killed = sb.ToString();
        }

        _runes.RemoveRange(from, to - from);
        _cursor = from;
        Changed();
    }

    private void Changed()
    {
        _text = null;
        _historyIndex = -1; // an edited history entry becomes the new draft
        OnChange?.Invoke(Text);
    }

    private void SetRunes(string? value)
    {
        _runes.Clear();
        _text = null;
        if (!string.IsNullOrEmpty(value))
        {
            foreach (var r in value.EnumerateRunes())
            {
                _runes.Add(r);
            }
        }
    }

    private bool BrowseHistory(int direction)
    {
        if (History is not { Count: > 0 } history)
        {
            return false;
        }

        int index = _historyIndex < 0 ? history.Count : _historyIndex;
        int next = Math.Clamp(index + direction, 0, history.Count);
        if (next == index)
        {
            return true;
        }

        if (_historyIndex < 0)
        {
            _draft = Text;
        }

        SetRunes(next == history.Count ? _draft : history.Entries[next]);
        _historyIndex = next == history.Count ? -1 : next;
        _cursor = _runes.Count;
        _scroll = 0;
        OnChange?.Invoke(Text);
        return true;
    }

    private void MoveCursor(int delta) =>
        _cursor = Math.Clamp(_cursor + delta, 0, _runes.Count);

    // A masked field has no visible words, so word motions jump straight to the start or end of it.
    private bool IsWordRune(int index) =>
        Mask is null && (Rune.IsLetterOrDigit(_runes[index]) || _runes[index].Value == '_');

    private int WordStartBefore(int from)
    {
        if (Mask is not null)
        {
            return 0;
        }

        int i = from;
        while (i > 0 && !IsWordRune(i - 1))
        {
            i--;
        }

        while (i > 0 && IsWordRune(i - 1))
        {
            i--;
        }

        return i;
    }

    private int WordEndAfter(int from)
    {
        if (Mask is not null)
        {
            return _runes.Count;
        }

        int i = from;
        while (i < _runes.Count && !IsWordRune(i))
        {
            i++;
        }

        while (i < _runes.Count && IsWordRune(i))
        {
            i++;
        }

        return i;
    }

    private int WhitespaceWordStartBefore(int from)
    {
        int i = from;
        while (i > 0 && Rune.IsWhiteSpace(_runes[i - 1]))
        {
            i--;
        }

        while (i > 0 && !Rune.IsWhiteSpace(_runes[i - 1]))
        {
            i--;
        }

        return i;
    }

    private int VisibleColumn(int runeIndex)
    {
        int col = 0;
        for (int i = 0; i < runeIndex && i < _runes.Count; i++)
        {
            col += Unicode.RuneWidth(Mask is { } m ? new Rune(m) : _runes[i]);
        }
        return col;
    }

    private void EnsureCursorVisible(int width)
    {
        if (_cursor < _scroll) { _scroll = _cursor; return; }

        while (VisibleColumn(_cursor) - VisibleColumn(_scroll) >= width && _scroll < _cursor)
        {
            _scroll++;
        }
    }
}
