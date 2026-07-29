using System.Collections.Generic;
using Cellar.Primitives;

namespace Cellar.Theming;

/// <summary>Built-in themes. Modern, restrained palettes tuned for truecolor terminals.</summary>
public static class BuiltIn
{
    /// <summary>
    /// The base16 "default dark" palette (Chris Kempson). This is Cellar's default theme.
    /// Roles are mapped onto the scheme's normal/bright colors: blue is the accent, magenta
    /// the secondary, bright-black the muted/border tone.
    /// </summary>
    public static Theme Base16Dark { get; } = new()
    {
        Name = "base16-dark",
        Foreground = Color.Hex("#d0d0d0"),   // normal white
        Background = Color.Hex("#151515"),   // primary background / normal black
        Accent = Color.Hex("#6a9fb5"),       // blue
        Secondary = Color.Hex("#aa759f"),    // magenta
        Muted = Color.Hex("#505050"),        // bright black
        Border = Color.Hex("#505050"),       // bright black
        SelectionForeground = Color.Hex("#151515"),
        SelectionBackground = Color.Hex("#6a9fb5"),
        Success = Color.Hex("#90a959"),      // green
        Warning = Color.Hex("#f4bf75"),      // yellow
        Error = Color.Hex("#ac4142"),        // red
        Info = Color.Hex("#75b5aa"),         // cyan
    };

    /// <summary>A modern dark theme with a cyan accent.</summary>
    public static Theme Dark { get; } = new()
    {
        Name = "dark",
        Foreground = Color.Hex("#e6e6e6"),
        Background = Color.Hex("#1a1b26"),
        Accent = Color.Hex("#7aa2f7"),
        Secondary = Color.Hex("#bb9af7"),
        Muted = Color.Hex("#565f89"),
        Border = Color.Hex("#3b4261"),
        SelectionForeground = Color.Hex("#1a1b26"),
        SelectionBackground = Color.Hex("#7aa2f7"),
        Success = Color.Hex("#9ece6a"),
        Warning = Color.Hex("#e0af68"),
        Error = Color.Hex("#f7768e"),
        Info = Color.Hex("#7dcfff"),
    };

    /// <summary>A clean light theme with an indigo accent.</summary>
    public static Theme Light { get; } = new()
    {
        Name = "light",
        Foreground = Color.Hex("#343b58"),
        Background = Color.Hex("#f5f5fa"),
        Accent = Color.Hex("#3d59a1"),
        Secondary = Color.Hex("#8c4351"),
        Muted = Color.Hex("#9699a3"),
        Border = Color.Hex("#c4c8da"),
        SelectionForeground = Color.Hex("#f5f5fa"),
        SelectionBackground = Color.Hex("#3d59a1"),
        Success = Color.Hex("#385f0d"),
        Warning = Color.Hex("#8f5e15"),
        Error = Color.Hex("#8c4351"),
        Info = Color.Hex("#0f4b6e"),
    };

    /// <summary>A high-contrast, terminal-16-color-safe theme for maximum compatibility.</summary>
    public static Theme HighContrast { get; } = new()
    {
        Name = "high-contrast",
        Foreground = Color.BrightWhite,
        Background = Color.Black,
        Accent = Color.BrightCyan,
        Secondary = Color.BrightMagenta,
        Muted = Color.BrightBlack,
        Border = Color.White,
        SelectionForeground = Color.Black,
        SelectionBackground = Color.BrightCyan,
        Success = Color.BrightGreen,
        Warning = Color.BrightYellow,
        Error = Color.BrightRed,
        Info = Color.BrightBlue,
    };

    /// <summary>Nord — calm arctic blue-grey with muted cyan/blue accents.</summary>
    public static Theme Nord { get; } = new()
    {
        Name = "nord",
        Foreground = Color.Hex("#d8dee9"),
        Background = Color.Hex("#2e3440"),
        Accent = Color.Hex("#88c0d0"),       // frost cyan (primary)
        Secondary = Color.Hex("#81a1c1"),    // frost blue
        Muted = Color.Hex("#4c566a"),        // polar night 3
        Border = Color.Hex("#434c5e"),       // panel tone
        SelectionForeground = Color.Hex("#2e3440"),
        SelectionBackground = Color.Hex("#88c0d0"),
        Success = Color.Hex("#a3be8c"),      // aurora green
        Warning = Color.Hex("#ebcb8b"),      // aurora yellow
        Error = Color.Hex("#bf616a"),        // aurora red
        Info = Color.Hex("#b48ead"),         // aurora purple (accent)
    };

    /// <summary>Gruvbox — warm retro earth tones (browns, oranges, greens) on dark.</summary>
    public static Theme Gruvbox { get; } = new()
    {
        Name = "gruvbox",
        Foreground = Color.Hex("#fbf1c7"),
        Background = Color.Hex("#282828"),
        Accent = Color.Hex("#85a598"),       // aqua-grey (primary)
        Secondary = Color.Hex("#a89a85"),    // warm grey
        Muted = Color.Hex("#665c54"),        // bg3
        Border = Color.Hex("#504945"),       // bg2 / panel
        SelectionForeground = Color.Hex("#282828"),
        SelectionBackground = Color.Hex("#fabd2f"),
        Success = Color.Hex("#b8bb26"),      // green
        Warning = Color.Hex("#fe8019"),      // orange
        Error = Color.Hex("#fb4934"),        // red
        Info = Color.Hex("#fabd2f"),         // yellow (accent)
    };

    /// <summary>Catppuccin Mocha — soft pastels (lavender, pink, peach) on deep navy.</summary>
    public static Theme CatppuccinMocha { get; } = new()
    {
        Name = "catppuccin-mocha",
        Foreground = Color.Hex("#cdd6f4"),
        Background = Color.Hex("#181825"),
        Accent = Color.Hex("#f5c2e7"),       // pink (primary)
        Secondary = Color.Hex("#cba6f7"),    // mauve
        Muted = Color.Hex("#585b70"),        // surface2
        Border = Color.Hex("#45475a"),       // surface1 / panel
        SelectionForeground = Color.Hex("#181825"),
        SelectionBackground = Color.Hex("#f5c2e7"),
        Success = Color.Hex("#a6e3a1"),      // green
        Warning = Color.Hex("#fae3b0"),      // yellow
        Error = Color.Hex("#f38ba8"),        // red
        Info = Color.Hex("#fab387"),         // peach (accent)
    };

    /// <summary>Dracula — vivid purple/pink/green on dark grey, high energy.</summary>
    public static Theme Dracula { get; } = new()
    {
        Name = "dracula",
        Foreground = Color.Hex("#f8f8f2"),
        Background = Color.Hex("#282a36"),
        Accent = Color.Hex("#bd93f9"),       // purple (primary)
        Secondary = Color.Hex("#6272a4"),    // comment blue
        Muted = Color.Hex("#6272a4"),        // comment
        Border = Color.Hex("#44475a"),       // current line / panel
        SelectionForeground = Color.Hex("#282a36"),
        SelectionBackground = Color.Hex("#bd93f9"),
        Success = Color.Hex("#50fa7b"),      // green
        Warning = Color.Hex("#ffb86c"),      // orange
        Error = Color.Hex("#ff5555"),        // red
        Info = Color.Hex("#ff79c6"),         // pink (accent)
    };

    /// <summary>Solarized Dark — Ethan Schoonover's classic low-contrast teal/beige on deep blue-green.</summary>
    public static Theme SolarizedDark { get; } = new()
    {
        Name = "solarized-dark",
        Foreground = Color.Hex("#839496"),   // base0
        Background = Color.Hex("#002b36"),   // base03
        Accent = Color.Hex("#268bd2"),       // blue (primary)
        Secondary = Color.Hex("#2aa198"),    // cyan
        Muted = Color.Hex("#586e75"),        // base01
        Border = Color.Hex("#073642"),       // base02 / panel
        SelectionForeground = Color.Hex("#fdf6e3"),
        SelectionBackground = Color.Hex("#073642"),
        Success = Color.Hex("#859900"),      // green
        Warning = Color.Hex("#cb4b16"),      // orange
        Error = Color.Hex("#dc322f"),        // red
        Info = Color.Hex("#6c71c4"),         // violet (accent)
    };

    /// <summary>Solarized Light — the same palette on the warm beige background.</summary>
    public static Theme SolarizedLight { get; } = new()
    {
        Name = "solarized-light",
        Foreground = Color.Hex("#586e75"),   // base01
        Background = Color.Hex("#fdf6e3"),   // base3
        Accent = Color.Hex("#268bd2"),       // blue (primary)
        Secondary = Color.Hex("#2aa198"),    // cyan
        Muted = Color.Hex("#93a1a1"),        // base1
        Border = Color.Hex("#eee8d5"),       // base2 / panel
        SelectionForeground = Color.Hex("#fdf6e3"),
        SelectionBackground = Color.Hex("#268bd2"),
        Success = Color.Hex("#859900"),      // green
        Warning = Color.Hex("#cb4b16"),      // orange
        Error = Color.Hex("#dc322f"),        // red
        Info = Color.Hex("#6c71c4"),         // violet (accent)
    };

    /// <summary>All built-in themes, in a stable order (useful for a theme picker/cycle).</summary>
    public static IReadOnlyList<Theme> All { get; } = new[]
    {
        Base16Dark, Dark, Light, HighContrast,
        Nord, Gruvbox, CatppuccinMocha, Dracula, SolarizedDark, SolarizedLight,
    };
}
