using System;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// A clickable button: a labeled region that highlights on hover, depresses on mouse-down, and
/// fires <see cref="OnClick"/> on release (or on Enter/Space when focused). Its render rect is
/// its hit region, so positional mouse routing delivers events to it directly.
/// </summary>
public sealed class Button : Widget
{
    private Rect _bounds;
    private bool _hovered;
    private bool _pressed;

    public string Label { get; set; }

    /// <summary>Optional rich label; overrides <see cref="Label"/> when set (e.g. a bold key hint).
    /// Its spans keep their own foreground/attributes but inherit the button's state background.</summary>
    public StyledText? Content { get; set; }

    /// <summary>Invoked when the button is activated (click or Enter/Space).</summary>
    public Action? OnClick { get; set; }

    /// <summary>Normal style. Null uses a muted background from the theme.</summary>
    public Style? Style { get; set; }

    /// <summary>Hover style. Null uses the theme accent background.</summary>
    public Style? HoverStyle { get; set; }

    /// <summary>Pressed style. Null uses a brighter accent.</summary>
    public Style? PressedStyle { get; set; }

    public Button(string label = "", Action? onClick = null)
    {
        Label = label;
        OnClick = onClick;
    }

    public override bool IsFocusable => true;

    public override Size Measure(Size available) => new(ContentWidth() + 2, 1);

    private int ContentWidth() => Content?.Width ?? Unicode.StringWidth(Label);

    private Style EffectiveNormal =>
        Style ?? new Style(Theme.Current.Foreground, Theme.Current.Muted);
    private Style EffectiveHover =>
        HoverStyle ?? new Style(Theme.Current.SelectionForeground, Theme.Current.Accent);
    private Style EffectivePressed =>
        PressedStyle ?? new Style(Theme.Current.SelectionForeground, Theme.Current.Secondary);

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        _bounds = area;
        var style = _pressed ? EffectivePressed : _hovered ? EffectiveHover : EffectiveNormal;

        // A one-row band; a taller area centers the label vertically.
        int y = area.Y + area.Height / 2;
        surface.FillRect(area, style);
        surface.SetClip(area);
        // A rich Content label keeps its own span styles over the button background; otherwise
        // draw the plain Label in the state style.
        var label = Content is { } c ? OnBackground(c, style.Background) : new StyledText(Label, style);
        TextRenderer.DrawLine(surface, area.X, y, area.Width, label, Justify.Center);
        surface.ResetClip();
    }

    // Re-emits a rich label so each span keeps its foreground/attributes but sits on the button's
    // current background (so bold key hints read over hover/pressed fills).
    private static StyledText OnBackground(StyledText text, Color background)
    {
        var result = StyledText.Empty();
        foreach (var span in text.Spans)
        {
            result.Append(span.Text, span.Style.WithBackground(background));
        }
        return result;
    }

    public override bool OnEvent(InputEvent e)
    {
        switch (e)
        {
            case KeyEvent key when HasFocus && (key.Key == Key.Enter || (key.IsChar && key.Rune.Value == ' ')):
                OnClick?.Invoke();
                return true;

            case MouseEvent { Kind: MouseEventKind.Move } m:
            {
                bool over = _bounds.Contains(m.X, m.Y);
                if (over != _hovered)
                {
                    _hovered = over;
                    return true; // hover changed — repaint
                }
                return false;
            }

            case MouseEvent { Kind: MouseEventKind.Down, Button: MouseButton.Left } m
                when _bounds.Contains(m.X, m.Y):
                _pressed = true;
                return true;

            case MouseEvent { Kind: MouseEventKind.Up } m:
                bool fire = _pressed && _bounds.Contains(m.X, m.Y);
                _pressed = false;
                if (fire)
                {
                    OnClick?.Invoke();
                    return true;
                }
                return false;
        }
        return false;
    }
}
