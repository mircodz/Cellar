using System;
using System.Collections.Generic;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;

namespace Cellar.Widgets;

/// <summary>
/// Shows one of several widgets in the same place: the main area of an app that swaps between views, say. Render,
/// measure, input and focus all go to the <see cref="Current"/> widget, so a container (a <see cref="Stack"/>, a
/// <see cref="Panel"/>) holds the switcher once and never needs to know which view is showing.
/// </summary>
/// <remarks>
/// <para>Swapping moves the focus: the widget swapped out loses it and the one swapped in gets it if the switcher
/// has it. Views can be swapped in directly (<see cref="Current"/>) or registered by name (<see cref="Add"/>) and
/// shown with <see cref="Show(string)"/>, which suits a <c>:view-name</c> command.</para>
/// <para>Lifecycle: while the switcher is mounted, the current widget is mounted, and a widget swapped out is
/// unmounted, so its background work stops while it isn't visible; it's mounted again when it comes back. Set
/// <see cref="KeepHiddenMounted"/> to keep every widget that has been shown mounted until the switcher itself is
/// unmounted instead (views that should keep refreshing while hidden).</para>
/// </remarks>
public sealed class Switcher : Widget
{
    private readonly List<(string Name, Widget Widget)> _named = new();
    private readonly List<Widget> _mounted = new();
    private Widget? _current;
    private App? _app;
    private bool _focused;

    public Switcher(Widget? current = null) => _current = current;

    /// <summary>The widget shown, or null to show nothing. Setting it swaps immediately (see the remarks for
    /// what happens to focus and the mount lifecycle).</summary>
    public Widget? Current
    {
        get => _current;
        set => Swap(value);
    }

    /// <summary>The name <see cref="Current"/> was registered under with <see cref="Add"/>, or null.</summary>
    public string? CurrentName
    {
        get
        {
            foreach (var (name, widget) in _named)
            {
                if (ReferenceEquals(widget, _current))
                {
                    return name;
                }
            }

            return null;
        }
    }

    /// <summary>The names registered with <see cref="Add"/>, in order.</summary>
    public IEnumerable<string> Names
    {
        get
        {
            foreach (var (name, _) in _named)
            {
                yield return name;
            }
        }
    }

    /// <summary>Keeps widgets mounted after they're swapped out, until the switcher is unmounted. Off by default.</summary>
    public bool KeepHiddenMounted { get; set; }

    /// <summary>Called after a swap with the previous and the new widget (either may be null).</summary>
    public Action<Widget?, Widget?>? OnSwitch { get; set; }

    /// <summary>Registers <paramref name="widget"/> under <paramref name="name"/> (replacing an earlier one with
    /// the same name, case-insensitively). The first widget added is shown if nothing is yet.</summary>
    public Switcher Add(string name, Widget widget)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(widget);
        int index = IndexOf(name);
        if (index >= 0)
        {
            var old = _named[index].Widget;
            _named[index] = (name, widget);
            if (ReferenceEquals(old, _current))
            {
                Swap(widget);
            }
            else if (!ReferenceEquals(old, widget) && _mounted.Remove(old))
            {
                old.Unmount();
            }
        }
        else
        {
            _named.Add((name, widget));
        }

        if (_current is null)
        {
            Swap(widget);
        }

        return this;
    }

    /// <summary>Unregisters the widget named <paramref name="name"/>. If it's showing, nothing is shown
    /// afterwards. Returns false if there's no such name.</summary>
    public bool Remove(string name)
    {
        int index = IndexOf(name);
        if (index < 0)
        {
            return false;
        }

        var widget = _named[index].Widget;
        _named.RemoveAt(index);
        if (ReferenceEquals(widget, _current))
        {
            Swap(null);
        }

        if (_mounted.Remove(widget))
        {
            widget.Unmount();
        }

        return true;
    }

    /// <summary>The widget registered under <paramref name="name"/>, or null.</summary>
    public Widget? Get(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? null : _named[index].Widget;
    }

    /// <summary>Shows the widget registered under <paramref name="name"/>. Returns false if there's no such name.</summary>
    public bool Show(string name)
    {
        int index = IndexOf(name);
        if (index < 0)
        {
            return false;
        }

        Swap(_named[index].Widget);
        return true;
    }

    public override bool IsFocusable => _current?.IsFocusable ?? false;

    public override bool HasFocus
    {
        get => _focused;
        set
        {
            _focused = value;
            if (_current is not null)
            {
                _current.HasFocus = value;
            }
        }
    }

    public override Size Measure(Size available) => _current?.Measure(available) ?? new Size(0, 0);

    public override void Render(Surface surface, Rect area) => _current?.Render(surface, area);

    public override bool OnEvent(InputEvent e) => _current?.OnEvent(e) ?? false;

    protected override void OnMount(App app)
    {
        _app = app;
        // Mount then runs VisitChildren, which mounts what's listed here.
        if (_current is not null && !_mounted.Contains(_current))
        {
            _mounted.Add(_current);
        }
    }

    protected override void OnUnmount()
    {
        // Unmount has already run VisitChildren to unmount them.
        _mounted.Clear();
        _app = null;
    }

    protected override void VisitChildren(Action<Widget> visit)
    {
        for (int i = 0; i < _mounted.Count; i++)
        {
            visit(_mounted[i]);
        }
    }

    private void Swap(Widget? next)
    {
        var previous = _current;
        if (ReferenceEquals(previous, next))
        {
            return;
        }

        if (previous is not null)
        {
            previous.HasFocus = false;
            if (!KeepHiddenMounted && _mounted.Remove(previous))
            {
                previous.Unmount();
            }
        }

        _current = next;
        if (next is not null)
        {
            next.HasFocus = _focused;
            if (_app is { } app && !_mounted.Contains(next))
            {
                _mounted.Add(next);
                next.Mount(app);
            }
        }

        OnSwitch?.Invoke(previous, next);
    }

    private int IndexOf(string name)
    {
        for (int i = 0; i < _named.Count; i++)
        {
            if (string.Equals(_named[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
