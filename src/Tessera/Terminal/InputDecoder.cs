using System;
using System.Collections.Generic;
using System.Text;

namespace Tessera.Terminal;

/// <summary>
/// Incrementally decodes a raw terminal byte stream into <see cref="InputEvent"/>s. Handles
/// UTF-8 text, C0 controls, CSI/SS3 sequences, SGR mouse reports (1006), and bracketed paste
/// (2004). Pure with respect to I/O.
/// </summary>
public sealed class InputDecoder
{
    private readonly List<byte> _buffer = new();
    private readonly StringBuilder _paste = new();
    private bool _inPaste;

    private static readonly byte[] PasteTerminator = { 0x1b, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~' };

    /// <summary>Feeds raw bytes and returns any events completed by this chunk.</summary>
    public IReadOnlyList<InputEvent> Feed(ReadOnlySpan<byte> bytes)
    {
        var events = new List<InputEvent>();
        foreach (var b in bytes)
        {
            _buffer.Add(b);
        }

        Drain(events);
        return events;
    }

    /// <summary>Resolves a pending ambiguous sequence: a lone <c>ESC</c> surfaces as
    /// <see cref="Key.Escape"/>. Call after a short idle timeout.</summary>
    public IReadOnlyList<InputEvent> Flush()
    {
        var events = new List<InputEvent>();
        if (!_inPaste && _buffer.Count == 1 && _buffer[0] == 0x1b)
        {
            events.Add(new KeyEvent(Key.Escape, default, KeyModifiers.None));
            _buffer.Clear();
        }
        return events;
    }

    private void Drain(List<InputEvent> events)
    {
        while (_buffer.Count > 0)
        {
            if (_inPaste)
            {
                if (!ConsumePasteContent(events))
                {
                    return;
                }

                continue;
            }

            byte b = _buffer[0];

            if (b == 0x1b) // ESC
            {
                int consumed = TryParseEscape(events);
                if (consumed == 0)
                {
                    return; // incomplete sequence; wait for more bytes
                }

                _buffer.RemoveRange(0, consumed);
                continue;
            }

            // Plain control characters and UTF-8 text.
            int textConsumed = ConsumeText(events);
            if (textConsumed == 0)
            {
                return; // incomplete UTF-8 sequence
            }

            _buffer.RemoveRange(0, textConsumed);
        }
    }

    private int ConsumeText(List<InputEvent> events)
    {
        byte b = _buffer[0];

        switch (b)
        {
            case 0x00: events.Add(new KeyEvent(Key.Char, new Rune(' '), KeyModifiers.Control)); return 1; // Ctrl-Space / Ctrl-@
            case 0x0d: events.Add(new KeyEvent(Key.Enter, default, KeyModifiers.None)); return 1;
            case 0x0a: events.Add(new KeyEvent(Key.Enter, default, KeyModifiers.None)); return 1;
            case 0x09: events.Add(new KeyEvent(Key.Tab, default, KeyModifiers.None)); return 1;
            case 0x7f: events.Add(new KeyEvent(Key.Backspace, default, KeyModifiers.None)); return 1;
            case 0x08: events.Add(new KeyEvent(Key.Backspace, default, KeyModifiers.None)); return 1;
        }

        // Ctrl-A..Ctrl-Z (0x01..0x1a), excluding the ones handled above.
        if (b is >= 0x01 and <= 0x1a)
        {
            var rune = new Rune((char)('a' + (b - 1)));
            events.Add(new KeyEvent(Key.Char, rune, KeyModifiers.Control));
            return 1;
        }

        int len = Utf8SequenceLength(b);
        if (len == 0)
        {
            return 1;
        }
        if (_buffer.Count < len)
        {
            return 0; // wait for the rest of the sequence
        }

        Span<byte> seq = stackalloc byte[len];
        for (int i = 0; i < len; i++)
        {
            seq[i] = _buffer[i];
        }

        if (Rune.DecodeFromUtf8(seq, out var decoded, out _) == System.Buffers.OperationStatus.Done)
        {
            events.Add(new KeyEvent(Key.Char, decoded, KeyModifiers.None));
        }
        return len;
    }

    private static int Utf8SequenceLength(byte lead)
    {
        if (lead < 0x80)
        {
            return 1;
        }

        if ((lead & 0xE0) == 0xC0)
        {
            return 2;
        }

        if ((lead & 0xF0) == 0xE0)
        {
            return 3;
        }

        if ((lead & 0xF8) == 0xF0)
        {
            return 4;
        }

        return 0;
    }

    // Returns the number of bytes consumed, or 0 if the sequence is incomplete.

    private int TryParseEscape(List<InputEvent> events)
    {
        if (_buffer.Count < 2)
        {
            // Lone ESC — could be the start of a sequence. We only surface a bare Escape
            // once we can be sure nothing follows; here we wait for more bytes.
            return 0;
        }

        byte second = _buffer[1];

        // CSI: ESC [
        if (second == (byte)'[')
        {
            return TryParseCsi(events);
        }

        // SS3: ESC O  (application-cursor function keys)
        if (second == (byte)'O')
        {
            return TryParseSs3(events);
        }

        // ESC followed by a printable char => Alt+char.
        if (second is >= 0x20 and < 0x7f)
        {
            events.Add(new KeyEvent(Key.Char, new Rune((char)second), KeyModifiers.Alt));
            return 2;
        }

        // ESC ESC or unknown — treat the first ESC as a standalone Escape key.
        events.Add(new KeyEvent(Key.Escape, default, KeyModifiers.None));
        return 1;
    }

    private int TryParseSs3(List<InputEvent> events)
    {
        if (_buffer.Count < 3)
        {
            return 0;
        }

        byte code = _buffer[2];
        Key key = code switch
        {
            (byte)'A' => Key.Up,
            (byte)'B' => Key.Down,
            (byte)'C' => Key.Right,
            (byte)'D' => Key.Left,
            (byte)'H' => Key.Home,
            (byte)'F' => Key.End,
            (byte)'P' => Key.F1,
            (byte)'Q' => Key.F2,
            (byte)'R' => Key.F3,
            (byte)'S' => Key.F4,
            _ => Key.None,
        };
        if (key != Key.None)
        {
            events.Add(new KeyEvent(key, default, KeyModifiers.None));
        }

        return 3;
    }

    private int TryParseCsi(List<InputEvent> events)
    {
        // Find the final byte (0x40-0x7e) that terminates the CSI sequence.
        int end = -1;
        for (int i = 2; i < _buffer.Count; i++)
        {
            byte c = _buffer[i];
            if (c is >= 0x40 and <= 0x7e)
            {
                end = i;
                break;
            }
        }
        if (end == -1)
        {
            return 0; // incomplete
        }

        byte final = _buffer[end];

        // Mouse: ESC [ < ... M/m  (SGR 1006). Parse fields straight from the bytes.
        if (end > 2 && _buffer[2] == (byte)'<')
        {
            ParseSgrMouse(3, end, final, events);
            return end + 1;
        }

        // Bracketed paste start: ESC [ 200 ~
        if (final == (byte)'~' && ParamEquals(2, end, "200"))
        {
            _inPaste = true;
            _paste.Clear();
            return end + 1;
        }

        // Split the params on the (at most one) ';' into first and optional modifier field.
        int semi = -1;
        for (int i = 2; i < end; i++)
        {
            if (_buffer[i] == (byte)';')
            {
                semi = i;
                break;
            }
        }

        var mods = KeyModifiers.None;
        // Modified keys encode as ESC [ 1 ; <mod> <final>.
        if (semi >= 0 && TryParseInt(semi + 1, end, out int modCode))
        {
            mods = DecodeModifier(modCode);
        }

        Key letterKey = final switch
        {
            (byte)'A' => Key.Up,
            (byte)'B' => Key.Down,
            (byte)'C' => Key.Right,
            (byte)'D' => Key.Left,
            (byte)'H' => Key.Home,
            (byte)'F' => Key.End,
            _ => Key.None,
        };
        if (letterKey != Key.None)
        {
            events.Add(new KeyEvent(letterKey, default, mods));
            return end + 1;
        }

        // Numeric tilde sequences: ESC [ <n> ~
        int firstEnd = semi >= 0 ? semi : end;
        if (final == (byte)'~' && TryParseInt(2, firstEnd, out int n))
        {
            Key tildeKey = n switch
            {
                1 or 7 => Key.Home,
                2 => Key.Insert,
                3 => Key.Delete,
                4 or 8 => Key.End,
                5 => Key.PageUp,
                6 => Key.PageDown,
                11 => Key.F1,
                12 => Key.F2,
                13 => Key.F3,
                14 => Key.F4,
                15 => Key.F5,
                17 => Key.F6,
                18 => Key.F7,
                19 => Key.F8,
                20 => Key.F9,
                21 => Key.F10,
                23 => Key.F11,
                24 => Key.F12,
                _ => Key.None,
            };
            if (tildeKey != Key.None)
            {
                events.Add(new KeyEvent(tildeKey, default, mods));
            }

            return end + 1;
        }

        // Unknown CSI — consume and ignore.
        return end + 1;
    }

    private static KeyModifiers DecodeModifier(int code)
    {
        // xterm encodes modifier as (bitmask + 1): 1=none,2=Shift,3=Alt,4=Shift+Alt,5=Ctrl...
        int bits = code - 1;
        var mods = KeyModifiers.None;
        if ((bits & 1) != 0)
        {
            mods |= KeyModifiers.Shift;
        }

        if ((bits & 2) != 0)
        {
            mods |= KeyModifiers.Alt;
        }

        if ((bits & 4) != 0)
        {
            mods |= KeyModifiers.Control;
        }

        return mods;
    }

    // Parses a base-10 int from _buffer[start..end) without allocating. False if empty/non-digit.
    private bool TryParseInt(int start, int end, out int value)
    {
        value = 0;
        if (start >= end)
        {
            return false;
        }
        for (int i = start; i < end; i++)
        {
            byte b = _buffer[i];
            if (b < (byte)'0' || b > (byte)'9')
            {
                return false;
            }
            value = value * 10 + (b - '0');
        }
        return true;
    }

    private int IndexOf(byte needle, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (_buffer[i] == needle)
            {
                return i;
            }
        }
        return -1;
    }

    private bool ParamEquals(int start, int end, string ascii)
    {
        if (end - start != ascii.Length)
        {
            return false;
        }
        for (int i = 0; i < ascii.Length; i++)
        {
            if (_buffer[start + i] != (byte)ascii[i])
            {
                return false;
            }
        }
        return true;
    }

    // Parses "<btn>;<x>;<y>" from _buffer[start..end); final 'M' = press/motion, 'm' = release.
    private void ParseSgrMouse(int start, int end, byte final, List<InputEvent> events)
    {
        int s1 = IndexOf((byte)';', start, end);
        if (s1 < 0)
        {
            return;
        }
        int s2 = IndexOf((byte)';', s1 + 1, end);
        if (s2 < 0)
        {
            return;
        }

        if (!TryParseInt(start, s1, out int btnCode) ||
            !TryParseInt(s1 + 1, s2, out int col) ||
            !TryParseInt(s2 + 1, end, out int row))
        {
            return;
        }

        int x = col - 1; // SGR mouse coords are 1-based
        int y = row - 1;

        var mods = KeyModifiers.None;
        if ((btnCode & 4) != 0)
        {
            mods |= KeyModifiers.Shift;
        }

        if ((btnCode & 8) != 0)
        {
            mods |= KeyModifiers.Alt;
        }

        if ((btnCode & 16) != 0)
        {
            mods |= KeyModifiers.Control;
        }

        bool isMotion = (btnCode & 32) != 0;
        bool isWheel = (btnCode & 64) != 0;
        int baseBtn = btnCode & 0x3;

        if (isWheel)
        {
            var wheel = baseBtn == 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            events.Add(new MouseEvent(MouseEventKind.Wheel, wheel, x, y, mods));
            return;
        }

        MouseButton button = baseBtn switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            _ => MouseButton.None,
        };

        MouseEventKind kind;
        if (isMotion)
        {
            kind = button == MouseButton.None ? MouseEventKind.Move : MouseEventKind.Drag;
        }
        else
        {
            kind = final == (byte)'m' ? MouseEventKind.Up : MouseEventKind.Down;
        }

        events.Add(new MouseEvent(kind, button, x, y, mods));
    }

    private bool ConsumePasteContent(List<InputEvent> events)
    {
        byte[] term = PasteTerminator;
        for (int i = 0; i < _buffer.Count; i++)
        {
            if (MatchesAt(term, i))
            {
                var raw = new byte[i];
                for (int k = 0; k < i; k++)
                {
                    raw[k] = _buffer[k];
                }

                _paste.Append(Encoding.UTF8.GetString(raw));
                events.Add(new PasteEvent(_paste.ToString()));
                _paste.Clear();
                _inPaste = false;
                _buffer.RemoveRange(0, i + term.Length);
                return true;
            }
        }

        // No terminator yet. If a partial terminator might straddle the end, keep those
        // bytes buffered; otherwise fold everything into the paste and clear.
        int safe = Math.Max(0, _buffer.Count - term.Length);
        // Don't cut in the middle of a multibyte UTF-8 sequence, or the trailing bytes decode
        // as U+FFFD. Back up to the start of the last code point (a non-continuation byte).
        while (safe > 0 && (_buffer[safe] & 0xC0) == 0x80)
        {
            safe--;
        }
        if (safe > 0)
        {
            var raw = new byte[safe];
            for (int k = 0; k < safe; k++)
            {
                raw[k] = _buffer[k];
            }

            _paste.Append(Encoding.UTF8.GetString(raw));
            _buffer.RemoveRange(0, safe);
        }
        return false; // wait for more bytes
    }

    private bool MatchesAt(byte[] pattern, int offset)
    {
        if (offset + pattern.Length > _buffer.Count)
        {
            return false;
        }

        for (int i = 0; i < pattern.Length; i++)
        {
            if (_buffer[offset + i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }
}
