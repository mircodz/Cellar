using System;
using System.Collections.Generic;
using System.Text;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Text;
using Tessera.Theming;

namespace Tessera.Widgets;

/// <summary>
/// A single-line editable text field: insertion, backspace/delete, cursor movement, a
/// placeholder, and horizontal scrolling. Edits operate on Unicode scalars. Consumes
/// keystrokes only while focused.
/// </summary>
public sealed class Input : Widget
{
    private readonly List<Rune> _runes = new();
    private int _cursor;
    private int _scroll;

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

    public override bool IsFocusable => true;

    public string Text
    {
        get
        {
            var sb = new StringBuilder(_runes.Count);
            foreach (var r in _runes)
            {
                sb.Append(r.ToString());
            }

            return sb.ToString();
        }
        set
        {
            _runes.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                foreach (var r in value.EnumerateRunes())
                {
                    _runes.Add(r);
                }
            }

            _cursor = _runes.Count;
            _scroll = 0;
        }
    }

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
        for (int i = _scroll; i < _runes.Count && col < width; i++)
        {
            string g = Mask is { } m ? m.ToString() : _runes[i].ToString();
            int w = Unicode.StringWidth(g);
            if (col + w > width)
            {
                break;
            }

            surface.DrawText(x, area.Y, g, style);
            x += w;
            col += w;
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
        if (!HasFocus || e is not KeyEvent key)
        {
            return false;
        }

        switch (key.Key)
        {
            case Key.Left: MoveCursor(-1); return true;
            case Key.Right: MoveCursor(1); return true;
            case Key.Home: _cursor = 0; return true;
            case Key.End: _cursor = _runes.Count; return true;
            case Key.Backspace: DeleteBefore(); return true;
            case Key.Delete: DeleteAt(); return true;
            case Key.Enter: OnSubmit?.Invoke(Text); return true;
            case Key.Char when key.Modifiers is KeyModifiers.None or KeyModifiers.Shift:
                Insert(key.Rune);
                return true;
        }
        return false;
    }

    private void Insert(Rune r)
    {
        _runes.Insert(_cursor, r);
        _cursor++;
        OnChange?.Invoke(Text);
    }

    private void DeleteBefore()
    {
        if (_cursor <= 0)
        {
            return;
        }

        _runes.RemoveAt(_cursor - 1);
        _cursor--;
        OnChange?.Invoke(Text);
    }

    private void DeleteAt()
    {
        if (_cursor >= _runes.Count)
        {
            return;
        }

        _runes.RemoveAt(_cursor);
        OnChange?.Invoke(Text);
    }

    private void MoveCursor(int delta) =>
        _cursor = Math.Clamp(_cursor + delta, 0, _runes.Count);

    private int VisibleColumn(int runeIndex)
    {
        int col = 0;
        for (int i = 0; i < runeIndex && i < _runes.Count; i++)
        {
            string g = Mask is { } m ? m.ToString() : _runes[i].ToString();
            col += Unicode.StringWidth(g);
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
