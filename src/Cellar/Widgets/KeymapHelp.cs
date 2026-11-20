using System;
using System.Collections.Generic;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// The help screen generated from a <see cref="Keymap"/>: each group's heading, then a line per action with its keys
/// and command on the left and its title on the right. Scrolls with the arrows, <c>j</c>/<c>k</c>, PgUp/PgDn,
/// Home/End and the wheel; <c>q</c> or <c>?</c> raise <see cref="OnClose"/>. It's a snapshot: build a new one (or
/// call <see cref="Refresh"/>) after the available actions change.
/// </summary>
public sealed class KeymapHelp : Widget
{
    private readonly Keymap _keymap;
    private readonly List<(string? Heading, string Keys, string Title)> _lines = new();
    private int _keyWidth;
    private int _top;
    private int _height;

    public KeymapHelp(Keymap keymap)
    {
        _keymap = keymap ?? throw new ArgumentNullException(nameof(keymap));
        Refresh();
    }

    /// <summary>Raised on <c>q</c> or <c>?</c>, to close whatever shows the help.</summary>
    public Action? OnClose { get; set; }

    public override bool IsFocusable => true;

    /// <summary>Lines of content, headings and blank separators included.</summary>
    public int LineCount => _lines.Count;

    /// <summary>The first line shown.</summary>
    public int ScrollOffset => _top;

    /// <summary>Rebuilds the content from the keymap's available actions.</summary>
    public void Refresh()
    {
        _lines.Clear();
        _keyWidth = 0;
        var groups = new List<string>();
        var byGroup = new Dictionary<string, List<AppAction>>();
        foreach (var action in _keymap.Actions)
        {
            if (!action.ShowInHelp)
            {
                continue;
            }

            if (!byGroup.TryGetValue(action.Group, out var list))
            {
                byGroup[action.Group] = list = new List<AppAction>();
                groups.Add(action.Group);
            }

            list.Add(action);
        }

        foreach (var group in groups)
        {
            if (_lines.Count > 0)
            {
                _lines.Add((null, string.Empty, string.Empty));
            }

            _lines.Add((group, string.Empty, string.Empty));
            foreach (var action in byGroup[group])
            {
                string hint = _keymap.Hint(action);
                _keyWidth = Math.Max(_keyWidth, Unicode.StringWidth(hint));
                _lines.Add((null, hint, action.Title));
            }
        }

        _top = Math.Clamp(_top, 0, Math.Max(0, _lines.Count - 1));
    }

    /// <summary>This help in a rounded panel with <paramref name="title"/>, on the theme background, as the help
    /// overlay shows it.</summary>
    public Panel InPanel(string title = " keys · esc to close ") => new(this, title)
    {
        BorderStyle = BorderStyle.Rounded,
        Background = Theme.Current.Background,
    };

    public override Size Measure(Size available) =>
        new(available.Width, Math.Min(available.Height, _lines.Count));

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        _height = area.Height;
        _top = Math.Clamp(_top, 0, MaxTop);
        var theme = Theme.Current;
        var headingStyle = new Style(theme.Accent, Color.Default).Bold;
        var keyStyle = new Style(theme.Warning, Color.Default);
        var titleStyle = theme.TextStyle;

        bool overflowing = _lines.Count > area.Height && area.Width > 1;
        int width = overflowing ? area.Width - 1 : area.Width;
        int keyColumn = Math.Min(_keyWidth + 2, Math.Max(1, width / 2));
        var clip = surface.Clip;

        for (int row = 0; row < area.Height && _top + row < _lines.Count; row++)
        {
            var (heading, keys, title) = _lines[_top + row];
            int y = area.Y + row;
            if (heading is not null)
            {
                surface.SetClip(new Rect(area.X, y, width, 1).Intersect(clip));
                surface.DrawText(area.X, y, heading, headingStyle);
                continue;
            }

            surface.SetClip(new Rect(area.X + 2, y, Math.Max(0, keyColumn - 2), 1).Intersect(clip));
            surface.DrawText(area.X + 2, y, keys, keyStyle);
            surface.SetClip(new Rect(area.X + 2 + keyColumn, y, Math.Max(0, width - keyColumn - 2), 1).Intersect(clip));
            surface.DrawText(area.X + 2 + keyColumn, y, title, titleStyle);
        }

        surface.SetClip(clip);
        if (overflowing)
        {
            Scrollbar.Default.Render(surface, new Rect(area.Right - 1, area.Y, 1, area.Height), _top, area.Height, _lines.Count);
        }
    }

    private int MaxTop => Math.Max(0, _lines.Count - Math.Max(1, _height));

    public override bool OnEvent(InputEvent e)
    {
        switch (e)
        {
            case MouseEvent { Kind: MouseEventKind.Wheel } wheel:
                Scroll(wheel.Button == MouseButton.WheelUp ? -3 : 3);
                return true;
            case KeyEvent key when HasFocus:
                return OnKey(key);
        }

        return false;
    }

    private bool OnKey(KeyEvent key)
    {
        int page = Math.Max(1, _height - 1);
        switch (key.Key)
        {
            case Key.Up: Scroll(-1); return true;
            case Key.Down: Scroll(1); return true;
            case Key.PageUp: Scroll(-page); return true;
            case Key.PageDown: Scroll(page); return true;
            case Key.Home: _top = 0; return true;
            case Key.End: _top = MaxTop; return true;
            case Key.Char when key.Modifiers == KeyModifiers.None:
                switch (key.Rune.Value)
                {
                    case 'j': Scroll(1); return true;
                    case 'k': Scroll(-1); return true;
                    case 'q' or '?': OnClose?.Invoke(); return true;
                }

                break;
        }

        return false;
    }

    private void Scroll(int delta) => _top = Math.Clamp(_top + delta, 0, MaxTop);
}
