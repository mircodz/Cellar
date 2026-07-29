using Cellar.Primitives;

namespace Cellar.Text;

/// <summary>A contiguous run of text sharing one <see cref="Primitives.Style"/>. Immutable.</summary>
public readonly struct Span
{
    public string Text { get; }
    public Style Style { get; }

    /// <summary>Optional payload marking this run as a clickable link; null for plain text. A
    /// custom hover style is carried by wrapping the payload in a <see cref="LinkInfo"/>, so the
    /// common (no-hover-style) case keeps this struct small.</summary>
    public object? Link { get; }

    public Span(string text, Style style, object? link = null)
    {
        Text = text ?? string.Empty;
        Style = style;
        Link = link;
    }

    /// <summary>The terminal display width of this span's text.</summary>
    public int Width => Unicode.StringWidth(Text);

    public Span WithStyle(Style style) => new(Text, style, Link);
    public Span WithText(string text) => new(text, Style, Link);
    public Span WithLink(object? link) => new(Text, Style, link);
}

/// <summary>Wraps a link payload together with a per-link hover style. Only allocated when a link
/// specifies a custom hover style, so plain links stay a bare payload reference.</summary>
public sealed record LinkInfo(object Payload, Style HoverStyle);
