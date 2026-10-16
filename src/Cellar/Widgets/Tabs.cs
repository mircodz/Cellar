using System;
using System.Collections.Generic;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>One tab: a title and the widget shown when it is active. Content can be lazy — built by
/// a factory on first access — so tabs whose content is expensive to construct don't pay that cost
/// until the tab is actually shown.</summary>
public sealed class Tab
{
    public string Title { get; set; }

    private Widget? _content;
    private readonly Func<Widget>? _factory;

    public Tab(string title, Widget content)
    {
        Title = title;
        _content = content;
    }

    public Tab(string title, Func<Widget> factory)
    {
        Title = title;
        _factory = factory;
    }

    /// <summary>The tab's content, materialized on first access when created from a factory.</summary>
    public Widget Content
    {
        get => _content ??= _factory!();
        set => _content = value;
    }

    /// <summary>Whether the content exists yet — false for a lazy tab that hasn't been shown.</summary>
    public bool IsMaterialized => _content is not null;

    /// <summary>Whether this tab's content has been through <see cref="Widget.Mount"/> — so the
    /// owning <see cref="Tabs"/> mounts each tab's content exactly once, when it first materializes.</summary>
    internal bool Mounted { get; set; }
}

/// <summary>
/// A tabbed container: a one-line tab bar above the active tab's content. Tab/Shift+Tab (and Left/Right, unless
/// <see cref="ArrowKeysSwitch"/> is off) cycle, number keys 1-9 jump, a click selects. The bar is drawn by
/// <see cref="RenderBar"/>, kept separate from selection logic so it could move behind an
/// <c>ITabsRenderer</c> later.
/// </summary>
public sealed class Tabs : Widget
{
    private int _active;

    /// <summary>The live app, captured at mount, so tabs materialized later (on first show) can be
    /// mounted too. Null until mounted / after unmount.</summary>
    private App? _app;

    public List<Tab> Items { get; } = new();

    /// <summary>Whether Left/Right switch tabs when the content doesn't use them. On by default. Turn it off when
    /// some views use the arrows (trees, flame graphs) and others don't (tables): otherwise the arrows switch tabs
    /// in some views only. Tab/Shift+Tab and 1-9 always switch.</summary>
    public bool ArrowKeysSwitch { get; set; } = true;

    /// <summary>Records each tab header's x-range on the last render, for click hit-testing.</summary>
    private readonly List<(int start, int end, int index)> _hitRects = new();

    /// <summary>The row the tab bar was drawn on last render, for click hit-testing.</summary>
    private int _barRow = -1;

    public int ActiveIndex
    {
        get => _active;
        set
        {
            _active = Items.Count == 0 ? 0 : Math.Clamp(value, 0, Items.Count - 1);
            UpdateContentFocus();
        }
    }

    // Keeps only the active tab's content focused, so a ScrollView/Input inside it receives
    // keyboard input while inactive tabs' widgets stay dormant. Does NOT materialize lazy tabs that
    // aren't active — the whole point of a lazy tab is to defer building its content until shown.
    private void UpdateContentFocus()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (i == _active)
            {
                Items[i].Content.HasFocus = true; // materializes the active tab (it's about to render)
            }
            else if (Items[i].IsMaterialized)
            {
                Items[i].Content.HasFocus = false;
            }
        }
    }

    public Tab? Active => Items.Count == 0 ? null : Items[_active];

    public override bool IsFocusable => true;

    protected override void OnMount(App app) => _app = app;

    protected override void OnUnmount() => _app = null;

    protected override void VisitChildren(Action<Widget> visit)
    {
        // EnsureMounted is the sole mounter; only those tabs participate in teardown.
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Mounted)
            {
                visit(Items[i].Content);
            }
        }
    }

    private void EnsureMounted(Tab tab)
    {
        if (_app is { } app && !tab.Mounted)
        {
            tab.Mounted = true;
            tab.Content.Mount(app);
        }
    }

    public Tabs Add(string title, Widget content)
    {
        Items.Add(new Tab(title, content));
        UpdateContentFocus(); // keep the active tab's content focused as items are added
        return this;
    }

    /// <summary>Adds a tab whose content is built lazily by <paramref name="factory"/> the first time
    /// the tab is shown — so expensive content is not constructed until the user selects it.</summary>
    public Tabs Add(string title, Func<Widget> factory)
    {
        Items.Add(new Tab(title, factory));
        UpdateContentFocus();
        return this;
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty || Items.Count == 0)
        {
            return;
        }

        var rows = LayoutSolver.Split(area, Direction.Vertical,
            Constraint.Length(1), Constraint.Fill());
        var barArea = rows[0];
        var contentArea = rows[1];

        RenderBar(surface, barArea);

        var content = Items[_active].Content;
        EnsureMounted(Items[_active]);
        if (!contentArea.IsEmpty)
        {
            surface.SetClip(contentArea);
            content.Render(surface, contentArea);
            surface.ResetClip();
        }
    }

    private void RenderBar(Surface surface, Rect area)
    {
        _hitRects.Clear();
        _barRow = area.Y;
        var theme = Theme.Current;
        var inactive = theme.MutedStyle;
        var active = new Style(theme.SelectionForeground, theme.Accent).Bold;

        int x = area.X;
        for (int i = 0; i < Items.Count && x < area.Right; i++)
        {
            bool isActive = i == _active;
            string label = $" {i + 1}:{Items[i].Title} ";
            var style = isActive ? active : inactive;

            int start = x;
            int drawn = surface.DrawText(x, area.Y, label, style);
            _hitRects.Add((start, drawn, i));
            x = drawn;

            if (i < Items.Count - 1 && x < area.Right)
            {
                surface.Set(x, area.Y, Cell.FromGrapheme("│", theme.BorderStyle));
                x++;
            }
        }

        for (int fx = x; fx < area.Right; fx++)
        {
            surface.Set(fx, area.Y, Cell.Blank(theme.TextStyle));
        }
    }

    public override bool OnEvent(InputEvent e)
    {
        switch (e)
        {
            case MouseEvent { Kind: MouseEventKind.Down } click:
                return HandleClick(click);

            case MouseEvent mouse:
                return Active?.Content.OnEvent(mouse) ?? false;

            case KeyEvent key:
                // The active content gets first refusal so a focused Input or ScrollView can
                // claim arrows, digits, etc. Only keys it ignores fall through to tab nav.
                if (Active?.Content.OnEvent(key) == true)
                {
                    return true;
                }

                return HandleNavKey(key);
        }
        return false;
    }

    private bool HandleNavKey(KeyEvent key)
    {
        switch (key.Key)
        {
            case Key.Tab when (key.Modifiers & KeyModifiers.Shift) != 0:
                ActiveIndex = (_active - 1 + Items.Count) % Math.Max(1, Items.Count);
                return true;
            case Key.Tab:
                ActiveIndex = (_active + 1) % Math.Max(1, Items.Count);
                return true;
            case Key.Right when ArrowKeysSwitch:
                ActiveIndex = (_active + 1) % Math.Max(1, Items.Count);
                return true;
            case Key.Left when ArrowKeysSwitch:
                ActiveIndex = (_active - 1 + Items.Count) % Math.Max(1, Items.Count);
                return true;
        }
        if (key.IsChar && key.Rune.Value is >= '1' and <= '9')
        {
            int idx = key.Rune.Value - '1';
            if (idx < Items.Count) { ActiveIndex = idx; return true; }
        }
        return false;
    }

    private bool HandleClick(MouseEvent m)
    {
        // Only clicks on the tab bar's row select a tab; clicks in the content area below
        // must fall through to the active tab's content.
        if (m.Y != _barRow)
        {
            return Active?.Content.OnEvent(m) ?? false;
        }

        foreach (var (start, end, index) in _hitRects)
        {
            if (m.X >= start && m.X < end)
            {
                ActiveIndex = index;
                return true;
            }
        }
        return false;
    }
}
