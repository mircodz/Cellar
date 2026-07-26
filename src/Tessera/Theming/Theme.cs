using System;
using Tessera.Primitives;

namespace Tessera.Theming;

/// <summary>
/// A named palette of semantic color roles widgets read from instead of hardcoding colors.
/// Swapping <see cref="Theme.Current"/> restyles the whole app. Roles are semantic
/// ("accent", "muted"), not literal, so widgets stay theme-agnostic.
/// </summary>
public sealed class Theme
{
    public required string Name { get; init; }

    /// <summary>Default text color for body content.</summary>
    public required Color Foreground { get; init; }

    /// <summary>The app/background fill color.</summary>
    public required Color Background { get; init; }

    /// <summary>The primary brand/accent color (highlights, active elements, focus).</summary>
    public required Color Accent { get; init; }

    /// <summary>A secondary accent, for less prominent emphasis.</summary>
    public required Color Secondary { get; init; }

    /// <summary>De-emphasized text: hints, disabled items, secondary info.</summary>
    public required Color Muted { get; init; }

    /// <summary>Border and divider lines.</summary>
    public required Color Border { get; init; }

    /// <summary>Foreground for selected/highlighted rows.</summary>
    public required Color SelectionForeground { get; init; }

    /// <summary>Background for selected/highlighted rows.</summary>
    public required Color SelectionBackground { get; init; }

    public required Color Success { get; init; }
    public required Color Warning { get; init; }
    public required Color Error { get; init; }
    public required Color Info { get; init; }

    // Backgrounds are left as Color.Default ("inherit"); the compositor's DefaultBackground
    // substitutes a concrete color at emit time (or leaves it transparent when unset).

    public Style TextStyle => new(Foreground, Color.Default);
    public Style MutedStyle => new(Muted, Color.Default);
    public Style AccentStyle => new(Accent, Color.Default);
    public Style BorderStyle => new(Border, Color.Default);
    public Style SelectionStyle => new(SelectionForeground, SelectionBackground);
    public Style HeaderStyle => new Style(Accent, Color.Default).Bold;

    /// <summary>A subtle alternating-row background for zebra striping, nudged from the theme
    /// background (small fixed delta when not truecolor).</summary>
    public Color StripeBackground
    {
        get
        {
            if (Background.IsRgb)
            {
                // Lift toward the foreground by ~8% for dark themes, or darken for light.
                bool dark = Background.R + Background.G + Background.B < 384;
                int d = dark ? 14 : -14;
                return Color.Rgb(Color.ClampChannel(Background.R + d), Color.ClampChannel(Background.G + d), Color.ClampChannel(Background.B + d));
            }
            return Background;
        }
    }

    private static Theme _current = BuiltIn.Base16Dark;

    /// <summary>The theme widgets read from when not given an explicit one.</summary>
    public static Theme Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }
}
