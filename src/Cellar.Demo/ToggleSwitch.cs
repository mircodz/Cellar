using System;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Theming;
using Cellar.Widgets;

namespace Cellar.Demo;

/// <summary>An on/off toggle switch, styled like a pill. Interactive: Space/Enter or a click flips
/// it. A bool-state focusable widget.</summary>
public sealed class ToggleSwitch : Widget
{
    private bool _on;

    public bool On
    {
        get => _on;
        set => _on = value;
    }

    public string OnLabel { get; init; } = "ON";
    public string OffLabel { get; init; } = "OFF";

    /// <summary>Raised with the new state whenever the switch is toggled by input.</summary>
    public Action<bool>? OnToggle { get; set; }

    public override bool IsFocusable => true;
    public override bool HasFocus { get; set; }

    private Rect _lastArea;

    public override void Render(Surface surface, Rect area)
    {
        _lastArea = area;
        var theme = Theme.Current;
        var activeOn = new Style(theme.SelectionForeground, theme.Success);
        var activeOff = new Style(theme.SelectionForeground, theme.Error);
        var idle = new Style(theme.Muted, theme.Border);
        if (HasFocus)
        {
            // Bold the active segment as a focus cue.
            activeOn = activeOn.Bold;
            activeOff = activeOff.Bold;
        }

        // A two-segment pill showing BOTH labels always; the active side is filled, the other muted:
        //   " ON │ off "  (on)   /   " on │ OFF "  (off)
        int x = area.X;
        x = surface.DrawText(x, area.Y, $" {OnLabel} ", _on ? activeOn : idle);
        x = surface.DrawText(x, area.Y, "│", idle);
        surface.DrawText(x, area.Y, $" {OffLabel} ", _on ? idle : activeOff);
    }

    public override bool OnEvent(InputEvent e)
    {
        switch (e)
        {
            case KeyEvent { Key: Key.Enter } when HasFocus:
                return Toggle();
            case KeyEvent key when HasFocus && key.IsChar && key.Rune.Value == ' ':
                return Toggle();
            case MouseEvent { Kind: MouseEventKind.Down, Button: MouseButton.Left } m
                when _lastArea.Contains(m.X, m.Y):
                return Toggle();
        }
        return false;
    }

    private bool Toggle()
    {
        _on = !_on;
        OnToggle?.Invoke(_on);
        Repaint.Request();
        return true;
    }
}
