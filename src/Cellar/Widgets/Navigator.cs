using System;
using System.Collections.Generic;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>A page in a <see cref="Navigator"/>: a title (for the breadcrumb) and content.</summary>
public sealed class Page
{
    public string Title { get; set; }
    public Widget Content { get; set; }

    public Page(string title, Widget content)
    {
        Title = title;
        Content = content;
    }
}

/// <summary>
/// A navigation stack — a full-screen router for drill-down flows (list → detail → back).
/// The top page renders full-screen beneath an optional breadcrumb; the back key pops. Only
/// the top page receives input and focus.
/// </summary>
public sealed class Navigator : Widget
{
    private readonly List<Page> _stack = new();

    // Tracks mounted pages so child lifecycles stop on pop or teardown.
    private App? _app;
    private readonly HashSet<Page> _mounted = new();

    // The breadcrumb bar's row and each segment's clickable x-range (captured on render).
    private int _breadcrumbRow = -1;
    private readonly List<(int start, int end, int index)> _crumbHits = new();

    /// <summary>Whether to draw the breadcrumb bar at the top.</summary>
    public bool ShowBreadcrumb { get; set; } = true;

    /// <summary>The key that pops the current page. Defaults to Escape.</summary>
    public Key BackKey { get; set; } = Key.Escape;

    /// <summary>Raised after the stack changes (push or pop), with the new top page.</summary>
    public Action<Page?>? OnNavigate { get; set; }

    public override bool IsFocusable => true;

    public Navigator(Page? root = null)
    {
        if (root is not null)
        {
            _stack.Add(root);
        }
    }

    protected override void OnMount(App app) => _app = app;

    protected override void OnUnmount() => _app = null;

    protected override void VisitChildren(Action<Widget> visit)
    {
        // MountPage is the sole mounter; only those pages participate in teardown.
        foreach (var page in _stack)
        {
            if (_mounted.Contains(page))
            {
                visit(page.Content);
            }
        }
    }

    private void MountPage(Page page)
    {
        if (_app is { } app && _mounted.Add(page))
        {
            page.Content.Mount(app);
        }
    }

    private void UnmountPage(Page page)
    {
        if (_mounted.Remove(page))
        {
            page.Content.Unmount();
        }
    }

    /// <summary>The page currently shown (top of the stack), or null when empty.</summary>
    public Page? Current => _stack.Count > 0 ? _stack[^1] : null;

    /// <summary>Number of pages on the stack.</summary>
    public int Depth => _stack.Count;

    /// <summary>The page titles from root to top, for a breadcrumb.</summary>
    public IReadOnlyList<Page> Stack => _stack;

    /// <summary>Pushes a new page and gives it focus.</summary>
    public void Push(Page page)
    {
        if (Current is not null)
        {
            Current.Content.HasFocus = false;
        }

        _stack.Add(page);
        page.Content.HasFocus = true;
        MountPage(page);
        OnNavigate?.Invoke(page);
    }

    /// <summary>Pushes a page built from a title and content widget.</summary>
    public void Push(string title, Widget content) => Push(new Page(title, content));

    /// <summary>Pops the top page (unless it is the root). Returns the popped page or null.</summary>
    public Page? Pop()
    {
        if (_stack.Count <= 1)
        {
            return null;
        }

        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        top.Content.HasFocus = false;
        UnmountPage(top);
        if (Current is not null)
        {
            Current.Content.HasFocus = true;
        }

        OnNavigate?.Invoke(Current);
        return top;
    }

    /// <summary>Pops back to the root page.</summary>
    public void PopToRoot()
    {
        while (_stack.Count > 1)
        {
            UnmountPage(_stack[^1]);
            _stack.RemoveAt(_stack.Count - 1);
        }

        if (Current is not null)
        {
            Current.Content.HasFocus = true;
        }

        OnNavigate?.Invoke(Current);
    }

    /// <summary>Pops back to the page at <paramref name="index"/> (0 = root), discarding those
    /// above. No-op if already on top. Used by breadcrumb clicks.</summary>
    public void PopTo(int index)
    {
        if (index < 0 || index >= _stack.Count - 1)
        {
            return;
        }

        if (Current is not null)
        {
            Current.Content.HasFocus = false;
        }

        while (_stack.Count > index + 1)
        {
            UnmountPage(_stack[^1]);
            _stack.RemoveAt(_stack.Count - 1);
        }

        if (Current is not null)
        {
            Current.Content.HasFocus = true;
        }

        OnNavigate?.Invoke(Current);
    }

    /// <summary>Replaces the entire stack with a single root page.</summary>
    public void Reset(Page root)
    {
        foreach (var page in _stack)
        {
            UnmountPage(page);
        }

        _stack.Clear();
        _stack.Add(root);
        root.Content.HasFocus = true;
        MountPage(root);
        OnNavigate?.Invoke(root);
    }

    public override void Render(Surface surface, Rect area)
    {
        var page = Current;
        if (page is null || area.IsEmpty)
        {
            return;
        }

        MountPage(page);

        Rect contentArea = area;
        if (ShowBreadcrumb && _stack.Count > 0 && area.Height > 1)
        {
            var rows = LayoutSolver.Split(area, Direction.Vertical, Constraint.Length(1), Constraint.Fill());
            RenderBreadcrumb(surface, rows[0]);
            contentArea = rows[1];
        }

        // Clear the content region first so a shorter new page doesn't show stale cells from
        // the page beneath it (each page owns its whole area).
        surface.FillRect(contentArea, new Style(Theme.Current.Foreground, Theme.Current.Background));
        surface.SetClip(contentArea);
        page.Content.Render(surface, contentArea);
        surface.ResetClip();
    }

    private void RenderBreadcrumb(Surface surface, Rect area)
    {
        var theme = Theme.Current;
        surface.FillRect(area, new Style(theme.Foreground, theme.Background));

        _breadcrumbRow = area.Y;
        _crumbHits.Clear();

        surface.SetClip(area);
        int x = area.X + 1;
        var sepStyle = new Style(theme.Muted, theme.Background);
        for (int i = 0; i < _stack.Count && x < area.Right; i++)
        {
            if (i > 0)
            {
                x = surface.DrawText(x, area.Y, " › ", sepStyle);
            }

            bool last = i == _stack.Count - 1;
            var style = new Style(last ? theme.Accent : theme.Muted, theme.Background);
            if (last)
            {
                style = style.Bold;
            }

            int start = x;
            x = surface.DrawText(x, area.Y, _stack[i].Title, style);
            _crumbHits.Add((start, x, i));
        }
        surface.ResetClip();
    }

    public override bool OnEvent(InputEvent e)
    {
        if (ShowBreadcrumb && e is MouseEvent { Kind: MouseEventKind.Down } m && m.Y == _breadcrumbRow)
        {
            foreach (var (start, end, index) in _crumbHits)
            {
                if (m.X >= start && m.X < end)
                {
                    PopTo(index);
                    return true;
                }
            }
        }

        if (e is KeyEvent key && key.Key == BackKey && _stack.Count > 1)
        {
            Pop();
            return true;
        }

        return Current?.Content.OnEvent(e) ?? false;
    }
}
