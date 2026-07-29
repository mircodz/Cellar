namespace Cellar.Terminal;

/// <summary>Raw ANSI/DEC control sequences for terminal mode switching.</summary>
internal static class Ansi
{
    public const string EnterAltScreen = "\x1b[?1049h";
    public const string LeaveAltScreen = "\x1b[?1049l";

    public const string HideCursor = "\x1b[?25l";
    public const string ShowCursor = "\x1b[?25h";

    public const string ClearScreen = "\x1b[2J";
    public const string CursorHome = "\x1b[H";

    // SGR 1006 (extended coordinates) + 1002 (button-event tracking): reports clicks, wheel,
    // and drags. Bare hover motion (1003) is enabled separately via EnableMotion when the app
    // opts into hover, since any-motion tracking is a high-frequency event stream.
    public const string EnableMouse = "\x1b[?1000h\x1b[?1002h\x1b[?1006h";
    public const string DisableMouse = "\x1b[?1006l\x1b[?1002l\x1b[?1000l";

    // 1003: any-motion tracking (reports motion with no button held) — enables hover.
    public const string EnableMotion = "\x1b[?1003h";
    public const string DisableMotion = "\x1b[?1003l";

    public const string EnableBracketedPaste = "\x1b[?2004h";
    public const string DisableBracketedPaste = "\x1b[?2004l";

    public const string Reset = "\x1b[0m";
}
