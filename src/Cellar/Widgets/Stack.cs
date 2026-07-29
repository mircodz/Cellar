using System;
using System.Collections.Generic;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;

namespace Cellar.Widgets;

/// <summary>One child of a <see cref="Stack"/>: the widget and its size constraint.</summary>
public sealed class StackItem
{
    public Widget Widget { get; }
    public Constraint Constraint { get; }

    public StackItem(Widget widget, Constraint constraint)
    {
        Widget = widget;
        Constraint = constraint;
    }
}

/// <summary>
/// Arranges child widgets along one axis, sizing each via the <see cref="LayoutSolver"/>
/// so they tile the region exactly. This is the primary layout container — nest stacks to
/// build arbitrary rows/columns (header bar + table + footer, sidebars, etc.).
/// </summary>
public sealed class Stack : Widget
{
    public Direction Direction { get; set; }
    public List<StackItem> Children { get; } = new();

    public Stack(Direction direction) => Direction = direction;

    public Stack Add(Widget widget, Constraint constraint)
    {
        Children.Add(new StackItem(widget, constraint));
        return this;
    }

    protected override void VisitChildren(Action<Widget> visit)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            visit(Children[i].Widget);
        }
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty || Children.Count == 0)
        {
            return;
        }

        int n = Children.Count;
        if (_constraints.Length != n)
        {
            _constraints = new Constraint[n];
            _sizes = new int[n];
            _childRects = new Rect[n];
        }
        for (int i = 0; i < n; i++)
        {
            _constraints[i] = Children[i].Constraint;
        }

        int total = Direction == Direction.Horizontal ? area.Width : area.Height;
        LayoutSolver.SolveInto(total, _constraints, _sizes);

        int offset = Direction == Direction.Horizontal ? area.X : area.Y;
        for (int i = 0; i < n; i++)
        {
            var r = Direction == Direction.Horizontal
                ? new Rect(offset, area.Y, _sizes[i], area.Height)
                : new Rect(area.X, offset, area.Width, _sizes[i]);
            offset += _sizes[i];
            _childRects[i] = r; // remembered for positional mouse routing
            if (r.IsEmpty)
            {
                continue;
            }

            surface.SetClip(r);
            Children[i].Widget.Render(surface, r);
            surface.ResetClip();
        }
    }

    private Constraint[] _constraints = [];
    private int[] _sizes = [];
    private Rect[] _childRects = [];

    /// <summary>Natural size: sum of child extents along the axis, max across it. Used by
    /// containers like <see cref="ScrollView"/> to learn full content height.</summary>
    public override Size Measure(Size available)
    {
        int along = 0;
        int cross = 0;
        foreach (var item in Children)
        {
            int extent;
            if (item.Constraint.Type == Constraint.Kind.Length)
            {
                extent = item.Constraint.A;
            }
            else
            {
                var childAvail = Direction == Direction.Vertical
                    ? new Size(available.Width, int.MaxValue / 2)
                    : new Size(int.MaxValue / 2, available.Height);
                var measured = item.Widget.Measure(childAvail);
                extent = Direction == Direction.Vertical ? measured.Height : measured.Width;
            }

            along += extent;
            int otherAvail = Direction == Direction.Vertical ? available.Width : available.Height;
            var m = item.Widget.Measure(new Size(otherAvail, 1));
            cross = Math.Max(cross, Direction == Direction.Vertical ? m.Width : m.Height);
        }

        return Direction == Direction.Vertical
            ? new Size(available.Width, along)
            : new Size(along, available.Height);
    }

    public override bool OnEvent(InputEvent e)
    {
        // Mouse events route by position, except hover (Move) is sent to every child: a widget
        // the cursor just left needs to hear about it to clear its hover state, and positional
        // delivery alone would never tell it. Click/wheel/drag go only to the child under the cursor.
        if (e is MouseEvent m)
        {
            if (m.Kind == MouseEventKind.Move)
            {
                bool handled = false;
                for (int i = 0; i < Children.Count; i++)
                {
                    handled |= Children[i].Widget.OnEvent(e);
                }
                return handled;
            }

            for (int i = 0; i < Children.Count && i < _childRects.Length; i++)
            {
                if (_childRects[i].Contains(m.X, m.Y))
                {
                    return Children[i].Widget.OnEvent(e);
                }
            }
            return false;
        }

        // Keyboard events route by FOCUS. Tab / Shift+Tab move focus between focusable children.
        if (_hasFocus && e is KeyEvent { Key: Key.Tab } tab)
        {
            bool backward = (tab.Modifiers & KeyModifiers.Shift) != 0;
            if (MoveFocus(backward))
            {
                return true;
            }
            // Only one (or zero) focusable child: let the event fall through so an ancestor
            // (e.g. Tabs) can use Tab to switch tabs instead.
        }

        // Route to the focused child; if it declines, offer to the others.
        int focused = FocusedIndex();
        if (focused >= 0 && Children[focused].Widget.OnEvent(e))
        {
            return true;
        }

        for (int i = 0; i < Children.Count; i++)
        {
            if (i == focused)
            {
                continue;
            }

            if (Children[i].Widget.OnEvent(e))
            {
                return true;
            }
        }
        return false;
    }

    // The index of the currently focused focusable child, or -1.
    private int FocusedIndex()
    {
        for (int i = 0; i < Children.Count; i++)
            if (Children[i].Widget.HasFocus)
            {
                return i;
            }

        return -1;
    }

    // Moves focus to the next/previous focusable child. Returns false if there is no other
    // focusable child to move to (so the caller can let Tab bubble up).
    private bool MoveFocus(bool backward)
    {
        int count = Children.Count;
        int current = FocusedIndex();
        int step = backward ? -1 : 1;

        for (int n = 1; n <= count; n++)
        {
            int idx = ((current + step * n) % count + count) % count;
            if (Children[idx].Widget.IsFocusable)
            {
                if (idx == current)
                {
                    return false; // only one focusable child — nowhere to go
                }

                if (current >= 0)
                {
                    Children[current].Widget.HasFocus = false;
                }

                Children[idx].Widget.HasFocus = true;
                return true;
            }
        }
        return false;
    }

    private bool _hasFocus;

    /// <summary>Holds focus for exactly one focusable child (Tab/Shift+Tab cycle). Setting true
    /// focuses the first focusable child; false clears it.</summary>
    public override bool HasFocus
    {
        get => _hasFocus;
        set
        {
            _hasFocus = value;
            if (value)
            {
                if (FocusedIndex() < 0)
                {
                    for (int i = 0; i < Children.Count; i++)
                    {
                        if (Children[i].Widget.IsFocusable)
                        {
                            Children[i].Widget.HasFocus = true;
                            break;
                        }
                    }
                }
            }
            else
            {
                foreach (var child in Children)
                {
                    if (child.Widget.HasFocus)
                    {
                        child.Widget.HasFocus = false;
                    }
                }
            }
        }
    }

    /// <summary>Focusable when any child is, so nested stacks forward focus correctly.</summary>
    public override bool IsFocusable
    {
        get
        {
            foreach (var child in Children)
            {
                if (child.Widget.IsFocusable)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
