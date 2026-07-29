namespace Cellar.Primitives;

/// <summary>A cell's full style: foreground, background, and text attributes. Immutable.</summary>
public readonly record struct Style(Color Foreground, Color Background, TextAttributes Attributes)
{
    /// <summary>Default terminal colors, no attributes.</summary>
    public static Style Default => new(Color.Default, Color.Default, TextAttributes.None);

    public Style(Color foreground, Color background)
        : this(foreground, background, TextAttributes.None) { }

    public Style WithForeground(Color fg) => this with { Foreground = fg };
    public Style WithBackground(Color bg) => this with { Background = bg };
    public Style WithAttributes(TextAttributes attrs) => this with { Attributes = attrs };
    public Style AddAttributes(TextAttributes attrs) => this with { Attributes = Attributes | attrs };

    public Style Bold => AddAttributes(TextAttributes.Bold);
    public Style Dim => AddAttributes(TextAttributes.Dim);
    public Style Italic => AddAttributes(TextAttributes.Italic);
    public Style Underline => AddAttributes(TextAttributes.Underline);
    public Style Reverse => AddAttributes(TextAttributes.Reverse);
}
