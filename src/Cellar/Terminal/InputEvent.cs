using System;

namespace Cellar.Terminal;

/// <summary>Modifier keys held during a key or mouse event.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1 << 0,
    Alt = 1 << 1,
    Control = 1 << 2,
}

/// <summary>Non-printable keys recognized by the input decoder.</summary>
public enum Key
{
    None,
    Char,       // a printable rune; see KeyEvent.Rune
    Enter,
    Escape,
    Backspace,
    Tab,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
    Insert,
    Delete,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
}

/// <summary>Mouse button / wheel actions.</summary>
public enum MouseButton
{
    None,
    Left,
    Middle,
    Right,
    WheelUp,
    WheelDown,
}

/// <summary>The kind of mouse interaction.</summary>
public enum MouseEventKind
{
    Down,
    Up,
    Move,
    Drag,
    Wheel,
}

/// <summary>Base type for all decoded input events.</summary>
public abstract record InputEvent;

/// <summary>A key press. For printable input, <see cref="Key"/> is <see cref="Key.Char"/> and <see cref="Rune"/> holds the character.</summary>
public sealed record KeyEvent(Key Key, System.Text.Rune Rune, KeyModifiers Modifiers) : InputEvent
{
    public bool IsChar => Key == Key.Char;
}

/// <summary>A mouse event at cell coordinates (0-based).</summary>
public sealed record MouseEvent(MouseEventKind Kind, MouseButton Button, int X, int Y, KeyModifiers Modifiers) : InputEvent;

/// <summary>The terminal was resized to the given cell dimensions.</summary>
public sealed record ResizeEvent(int Width, int Height) : InputEvent;

/// <summary>A bracketed-paste payload delivered as a single event.</summary>
public sealed record PasteEvent(string Text) : InputEvent;
