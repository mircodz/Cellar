using System;
using System.Collections.Generic;
using Cellar.Primitives;

namespace Cellar.Text;

/// <summary>
/// A sequence of styled <see cref="Span"/>s with a fluent builder. Style methods
/// (<see cref="Bold"/>, <see cref="Fg"/>, ...) decorate the most recent span:
/// <code>StyledText.Of("error: ").Bold().Fg(Color.Red).Append("not found").Fg(Color.White);</code>
/// Mutable during building; call <see cref="ToSpans"/> to snapshot.
/// </summary>
public sealed class StyledText
{
    private readonly List<Span> _spans = new();

    public StyledText() { }

    public StyledText(string text, Primitives.Style style = default)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _spans.Add(new Span(text, style));
        }
    }

    /// <summary>Starts a new styled text with an initial run (default style).</summary>
    public static StyledText Of(string text) => new(text, Style.Default);

    /// <summary>An empty styled text, ready to <see cref="Append(string)"/> into.</summary>
    public static StyledText Empty() => new();

    /// <summary>Appends a new run. Subsequent style calls decorate this run.</summary>
    public StyledText Append(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _spans.Add(new Span(text, Style.Default));
        }

        return this;
    }

    /// <summary>Appends a run with an explicit style, leaving it as the current span.</summary>
    public StyledText Append(string text, Primitives.Style style)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _spans.Add(new Span(text, style));
        }

        return this;
    }

    /// <summary>Appends a run with an explicit style and link payload (preserved through recolor).</summary>
    public StyledText Append(string text, Primitives.Style style, object? link)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _spans.Add(new Span(text, style, link));
        }

        return this;
    }

    /// <summary>Appends a pre-built span verbatim (preserves link + hover style through recolor).</summary>
    public StyledText Append(Span span)
    {
        if (!string.IsNullOrEmpty(span.Text))
        {
            _spans.Add(span);
        }

        return this;
    }

    /// <summary>Appends all spans of another styled text.</summary>
    public StyledText Append(StyledText other)
    {
        _spans.AddRange(other._spans);
        return this;
    }

    /// <summary>Total display width across all spans.</summary>
    public int Width
    {
        get
        {
            int w = 0;
            foreach (var s in _spans)
            {
                w += s.Width;
            }

            return w;
        }
    }

    /// <summary>The concatenated plain text with styling stripped.</summary>
    public string PlainText
    {
        get
        {
            if (_spans.Count == 1)
            {
                return _spans[0].Text;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var s in _spans)
            {
                sb.Append(s.Text);
            }

            return sb.ToString();
        }
    }

    public IReadOnlyList<Span> Spans => _spans;

    /// <summary>Returns an immutable snapshot of the spans for rendering.</summary>
    public Span[] ToSpans() => _spans.ToArray();

    public StyledText Fg(Color color) => MutateCurrent(s => s.WithStyle(s.Style.WithForeground(color)));
    public StyledText Bg(Color color) => MutateCurrent(s => s.WithStyle(s.Style.WithBackground(color)));
    public StyledText Bold() => AddAttr(TextAttributes.Bold);
    public StyledText Dim() => AddAttr(TextAttributes.Dim);
    public StyledText Italic() => AddAttr(TextAttributes.Italic);
    public StyledText Underline() => AddAttr(TextAttributes.Underline);
    public StyledText Reverse() => AddAttr(TextAttributes.Reverse);
    public StyledText Strikethrough() => AddAttr(TextAttributes.Strikethrough);

    /// <summary>Applies an explicit style to the current span, replacing its style entirely.</summary>
    public StyledText WithStyle(Primitives.Style style) => MutateCurrent(s => s.WithStyle(style));

    /// <summary>Marks the current span as a clickable link carrying <paramref name="payload"/>,
    /// with an optional <paramref name="hoverStyle"/> applied while the cursor is over it. Widgets
    /// that render styled text (e.g. TreeView) surface the payload on click and apply the hover style.</summary>
    public StyledText Link(object payload, Primitives.Style? hoverStyle = null) =>
        MutateCurrent(s => s.WithLink(hoverStyle is { } hs ? new LinkInfo(payload, hs) : payload));

    /// <summary>True when any span carries a link payload — lets renderers skip hit-collection otherwise.</summary>
    public bool HasLinks
    {
        get
        {
            for (int i = 0; i < _spans.Count; i++)
            {
                if (_spans[i].Link is not null)
                {
                    return true;
                }
            }
            return false;
        }
    }

    private StyledText AddAttr(TextAttributes attr) =>
        MutateCurrent(s => s.WithStyle(s.Style.AddAttributes(attr)));

    private StyledText MutateCurrent(Func<Span, Span> f)
    {
        if (_spans.Count == 0)
        {
            return this;
        }

        int i = _spans.Count - 1;
        _spans[i] = f(_spans[i]);
        return this;
    }

    /// <summary>Implicitly wraps a bare string as an unstyled styled-text.</summary>
    public static implicit operator StyledText(string text) => Of(text);
}
