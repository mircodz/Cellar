using System;
using System.Collections.Generic;
using System.Text;

namespace Cellar.Terminal;

/// <summary>
/// One key with its modifiers, as a binding names it: <c>r</c>, <c>?</c>, <c>G</c>, <c>ctrl+r</c>, <c>alt+b</c>,
/// <c>enter</c>, <c>shift+tab</c>, <c>f5</c>. Parses and prints that notation, and matches
/// <see cref="KeyEvent"/>s the way terminals actually send them.
/// </summary>
/// <remarks>
/// Terminals fold Shift into the character, so a chord for a character never carries Shift: <c>shift+g</c> parses
/// as <c>G</c>, and an event for <c>G</c> matches whether or not it reported Shift. Ctrl+letter arrives as the
/// lower-case letter, so <c>ctrl+R</c> and <c>ctrl+r</c> are the same chord. Some ctrl keys are indistinguishable
/// from named keys on the wire (<c>ctrl+m</c> is Enter, <c>ctrl+i</c> is Tab, <c>ctrl+h</c> is Backspace,
/// <c>ctrl+[</c> is Escape), so they parse as those keys.
/// </remarks>
public readonly record struct KeyChord(Key Key, Rune Rune, KeyModifiers Modifiers)
{
    private static readonly Dictionary<string, Key> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = Key.Enter, ["return"] = Key.Enter, ["ret"] = Key.Enter,
        ["esc"] = Key.Escape, ["escape"] = Key.Escape,
        ["tab"] = Key.Tab,
        ["backspace"] = Key.Backspace, ["bksp"] = Key.Backspace, ["bs"] = Key.Backspace,
        ["up"] = Key.Up, ["↑"] = Key.Up,
        ["down"] = Key.Down, ["↓"] = Key.Down,
        ["left"] = Key.Left, ["←"] = Key.Left,
        ["right"] = Key.Right, ["→"] = Key.Right,
        ["home"] = Key.Home, ["end"] = Key.End,
        ["pgup"] = Key.PageUp, ["pageup"] = Key.PageUp,
        ["pgdn"] = Key.PageDown, ["pagedown"] = Key.PageDown,
        ["ins"] = Key.Insert, ["insert"] = Key.Insert,
        ["del"] = Key.Delete, ["delete"] = Key.Delete,
        ["f1"] = Key.F1, ["f2"] = Key.F2, ["f3"] = Key.F3, ["f4"] = Key.F4, ["f5"] = Key.F5, ["f6"] = Key.F6,
        ["f7"] = Key.F7, ["f8"] = Key.F8, ["f9"] = Key.F9, ["f10"] = Key.F10, ["f11"] = Key.F11, ["f12"] = Key.F12,
    };

    /// <summary>A plain character key, e.g. <c>KeyChord.Of('?')</c>.</summary>
    public static KeyChord Of(char c) => Normalize(new KeyChord(Key.Char, new Rune(c), KeyModifiers.None));

    /// <summary>Ctrl plus a character, e.g. <c>KeyChord.Ctrl('r')</c>.</summary>
    public static KeyChord Ctrl(char c) => Normalize(new KeyChord(Key.Char, new Rune(c), KeyModifiers.Control));

    /// <summary>Alt plus a character, e.g. <c>KeyChord.Alt('b')</c>.</summary>
    public static KeyChord Alt(char c) => Normalize(new KeyChord(Key.Char, new Rune(c), KeyModifiers.Alt));

    /// <summary>A named key, e.g. <c>KeyChord.Named(Key.Tab, KeyModifiers.Shift)</c>.</summary>
    public static KeyChord Named(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        Normalize(new KeyChord(key, default, modifiers));

    /// <summary>The chord a key event stands for, normalized so it compares equal to a parsed binding.</summary>
    public static KeyChord From(KeyEvent e) => Normalize(new KeyChord(e.Key, e.Key == Key.Char ? e.Rune : default, e.Modifiers));

    /// <summary>Whether <paramref name="e"/> is this key.</summary>
    public bool Matches(KeyEvent e) => From(e) == this;

    /// <summary>Parses <c>ctrl+r</c>, <c>alt+shift+left</c>, <c>?</c>, <c>G</c>, <c>shift+tab</c>, <c>space</c>, <c>f5</c>,
    /// or the <c>^r</c> shorthand for ctrl. Modifier and key names are case-insensitive.</summary>
    /// <exception cref="FormatException">The text isn't a key.</exception>
    public static KeyChord Parse(string text) =>
        TryParse(text, out var chord) ? chord : throw new FormatException($"'{text}' is not a key, like ctrl+r, ?, enter or shift+tab.");

    /// <summary>Like <see cref="Parse"/>, without throwing.</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        string keyPart;
        string modifierPart;
        if (text.Length == 2 && text[0] == '^' && text[1] != '^')
        {
            keyPart = text[1..];
            modifierPart = "ctrl";
        }
        else if (text == "+" || text.EndsWith("++", StringComparison.Ordinal))
        {
            keyPart = "+";
            modifierPart = text.Length > 2 ? text[..^2] : string.Empty;
        }
        else
        {
            int plus = text.LastIndexOf('+');
            keyPart = text[(plus + 1)..];
            modifierPart = plus > 0 ? text[..plus] : string.Empty;
            if (keyPart.Length == 0 || plus == 0)
            {
                return false;
            }
        }

        var modifiers = KeyModifiers.None;
        if (modifierPart.Length > 0)
        {
            foreach (var name in modifierPart.Split('+'))
            {
                switch (name.Trim().ToLowerInvariant())
                {
                    case "ctrl" or "control" or "ctl": modifiers |= KeyModifiers.Control; break;
                    case "alt" or "meta" or "opt" or "option": modifiers |= KeyModifiers.Alt; break;
                    case "shift": modifiers |= KeyModifiers.Shift; break;
                    default: return false;
                }
            }
        }

        if (keyPart.Equals("space", StringComparison.OrdinalIgnoreCase) || keyPart.Equals("spc", StringComparison.OrdinalIgnoreCase))
        {
            chord = Normalize(new KeyChord(Key.Char, new Rune(' '), modifiers));
            return true;
        }

        if (Names.TryGetValue(keyPart, out var key))
        {
            chord = Normalize(new KeyChord(key, default, modifiers));
            return true;
        }

        // Anything else must be a single character.
        if (Rune.DecodeFromUtf16(keyPart, out var rune, out int length) != System.Buffers.OperationStatus.Done ||
            length != keyPart.Length)
        {
            return false;
        }

        chord = Normalize(new KeyChord(Key.Char, rune, modifiers));
        return true;
    }

    // One spelling per key, so equality is enough to match.
    private static KeyChord Normalize(KeyChord chord)
    {
        if (chord.Key != Key.Char)
        {
            return chord with { Rune = default };
        }

        var mods = chord.Modifiers;
        var rune = chord.Rune;
        bool ctrl = (mods & KeyModifiers.Control) != 0;
        if (ctrl && (mods & KeyModifiers.Alt) == 0)
        {
            // Control codes that terminals send for these are decoded as the named keys.
            switch (rune.Value)
            {
                case 'm' or 'M' or 'j' or 'J': return new KeyChord(Key.Enter, default, mods & ~KeyModifiers.Control & ~KeyModifiers.Shift);
                case 'i' or 'I': return new KeyChord(Key.Tab, default, mods & ~KeyModifiers.Control & ~KeyModifiers.Shift);
                case 'h' or 'H': return new KeyChord(Key.Backspace, default, mods & ~KeyModifiers.Control & ~KeyModifiers.Shift);
                case '[': return new KeyChord(Key.Escape, default, mods & ~KeyModifiers.Control & ~KeyModifiers.Shift);
            }
        }

        if ((mods & KeyModifiers.Shift) != 0)
        {
            // Shift is already in the character: shift+g is G.
            mods &= ~KeyModifiers.Shift;
            if (!ctrl)
            {
                rune = Rune.ToUpperInvariant(rune);
            }
        }

        if (ctrl)
        {
            rune = Rune.ToLowerInvariant(rune);
        }

        return new KeyChord(Key.Char, rune, mods);
    }

    /// <summary>The binding notation: <c>ctrl+r</c>, <c>?</c>, <c>enter</c>, <c>shift+tab</c>, <c>space</c>. Parses back
    /// to the same chord.</summary>
    public override string ToString() => Format(shortForm: false);

    /// <summary>A compact form for tight spaces such as a footer: <c>^r</c>, <c>bksp</c>, <c>↑</c>, <c>pgdn</c>.</summary>
    public string ToShortString() => Format(shortForm: true);

    private string Format(bool shortForm)
    {
        var sb = new StringBuilder();
        if ((Modifiers & KeyModifiers.Control) != 0)
        {
            sb.Append(shortForm ? "^" : "ctrl+");
        }

        if ((Modifiers & KeyModifiers.Alt) != 0)
        {
            sb.Append("alt+");
        }

        if ((Modifiers & KeyModifiers.Shift) != 0)
        {
            sb.Append("shift+");
        }

        if (Key == Key.Char)
        {
            sb.Append(Rune.Value == ' ' ? "space" : Rune.ToString());
        }
        else
        {
            sb.Append(Name(Key, shortForm));
        }

        return sb.ToString();
    }

    private static string Name(Key key, bool shortForm) => key switch
    {
        Key.Escape => "esc",
        Key.Backspace => shortForm ? "bksp" : "backspace",
        Key.PageUp => "pgup",
        Key.PageDown => "pgdn",
        Key.Up => shortForm ? "↑" : "up",
        Key.Down => shortForm ? "↓" : "down",
        Key.Left => shortForm ? "←" : "left",
        Key.Right => shortForm ? "→" : "right",
        Key.Delete => shortForm ? "del" : "delete",
        _ => key.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// One or more <see cref="KeyChord"/>s pressed in turn, written space-separated: <c>g g</c>, <c>ctrl+x ctrl+s</c>.
/// A single chord is the common case. Strings convert implicitly, so bindings can be written as text:
/// <c>Keys = ["ctrl+r", "f5"]</c>.
/// </summary>
public sealed class KeySequence : IEquatable<KeySequence>
{
    private readonly KeyChord[] _chords;

    public KeySequence(params KeyChord[] chords)
    {
        if (chords.Length == 0)
        {
            throw new ArgumentException("A key sequence needs at least one key.", nameof(chords));
        }

        _chords = (KeyChord[])chords.Clone();
    }

    /// <summary>The keys, in the order they're pressed.</summary>
    public IReadOnlyList<KeyChord> Chords => _chords;

    /// <summary>Number of keys.</summary>
    public int Length => _chords.Length;

    /// <summary>The first key, the one that starts the sequence.</summary>
    public KeyChord First => _chords[0];

    /// <summary>Parses space-separated chords, each as <see cref="KeyChord.Parse"/> reads it.</summary>
    /// <exception cref="FormatException">A part isn't a key.</exception>
    public static KeySequence Parse(string text) =>
        TryParse(text, out var sequence) ? sequence : throw new FormatException($"'{text}' is not a key or key sequence, like ctrl+r or g g.");

    /// <summary>Like <see cref="Parse"/>, without throwing.</summary>
    public static bool TryParse(string? text, out KeySequence sequence)
    {
        sequence = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chords = new KeyChord[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!KeyChord.TryParse(parts[i], out chords[i]))
            {
                return false;
            }
        }

        sequence = new KeySequence(chords);
        return true;
    }

    /// <summary>Whether this sequence begins with every chord of <paramref name="prefix"/> (and is longer).</summary>
    public bool StartsWith(IReadOnlyList<KeyChord> prefix)
    {
        if (prefix.Count >= _chords.Length)
        {
            return false;
        }

        for (int i = 0; i < prefix.Count; i++)
        {
            if (_chords[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether this sequence is exactly <paramref name="chords"/>.</summary>
    public bool Is(IReadOnlyList<KeyChord> chords)
    {
        if (chords.Count != _chords.Length)
        {
            return false;
        }

        for (int i = 0; i < chords.Count; i++)
        {
            if (_chords[i] != chords[i])
            {
                return false;
            }
        }

        return true;
    }

    public static implicit operator KeySequence(string text) => Parse(text);

    public static implicit operator KeySequence(KeyChord chord) => new(chord);

    /// <summary>The binding notation, e.g. <c>g g</c>.</summary>
    public override string ToString() => string.Join(" ", Array.ConvertAll(_chords, c => c.ToString()));

    /// <summary>The compact notation, e.g. <c>^x ^s</c>.</summary>
    public string ToShortString() => string.Join(" ", Array.ConvertAll(_chords, c => c.ToShortString()));

    public bool Equals(KeySequence? other) => other is not null && Is(other._chords);

    public override bool Equals(object? obj) => obj is KeySequence other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var chord in _chords)
        {
            hash.Add(chord);
        }

        return hash.ToHashCode();
    }
}
