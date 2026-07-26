using System.Collections.Generic;
using System.Text;
using Tessera.Terminal;

namespace Tessera.Tests;

public class InputDecoderTests
{
    private static List<InputEvent> Decode(string ascii)
    {
        var d = new InputDecoder();
        return [..d.Feed(Encoding.UTF8.GetBytes(ascii))];
    }

    private static List<InputEvent> Decode(byte[] bytes)
    {
        var d = new InputDecoder();
        return [..d.Feed(bytes)];
    }

    [Fact]
    public void PlainAscii_ProducesCharEvents()
    {
        var ev = Decode("hi");
        Assert.Equal(2, ev.Count);
        Assert.Equal("h", ((KeyEvent)ev[0]).Rune.ToString());
        Assert.Equal("i", ((KeyEvent)ev[1]).Rune.ToString());
    }

    [Fact]
    public void Utf8Multibyte_DecodesOneRune()
    {
        var ev = Decode("é"); // 2-byte UTF-8
        var k = Assert.IsType<KeyEvent>(Assert.Single(ev));
        Assert.Equal("é", k.Rune.ToString());
    }

    [Fact]
    public void CtrlC_IsControlModifiedChar()
    {
        var ev = Decode([0x03]);
        var k = Assert.IsType<KeyEvent>(Assert.Single(ev));
        Assert.Equal(KeyModifiers.Control, k.Modifiers);
        Assert.Equal("c", k.Rune.ToString());
    }

    [Fact]
    public void Enter_Tab_Backspace()
    {
        Assert.Equal(Key.Enter, ((KeyEvent)Decode([0x0d])[0]).Key);
        Assert.Equal(Key.Tab, ((KeyEvent)Decode([0x09])[0]).Key);
        Assert.Equal(Key.Backspace, ((KeyEvent)Decode([0x7f])[0]).Key);
    }

    [Fact]
    public void ArrowKeys_Csi()
    {
        Assert.Equal(Key.Up, ((KeyEvent)Decode("\x1b[A")[0]).Key);
        Assert.Equal(Key.Down, ((KeyEvent)Decode("\x1b[B")[0]).Key);
        Assert.Equal(Key.Right, ((KeyEvent)Decode("\x1b[C")[0]).Key);
        Assert.Equal(Key.Left, ((KeyEvent)Decode("\x1b[D")[0]).Key);
    }

    [Fact]
    public void ModifiedArrow_CarriesModifier()
    {
        // ESC [ 1 ; 5 A  => Ctrl+Up
        var k = (KeyEvent)Decode("\x1b[1;5A")[0];
        Assert.Equal(Key.Up, k.Key);
        Assert.Equal(KeyModifiers.Control, k.Modifiers);
    }

    [Fact]
    public void FunctionKey_Tilde()
    {
        Assert.Equal(Key.F5, ((KeyEvent)Decode("\x1b[15~")[0]).Key);
        Assert.Equal(Key.Delete, ((KeyEvent)Decode("\x1b[3~")[0]).Key);
        Assert.Equal(Key.PageUp, ((KeyEvent)Decode("\x1b[5~")[0]).Key);
    }

    [Fact]
    public void Ss3_FunctionKeys()
    {
        Assert.Equal(Key.F1, ((KeyEvent)Decode("\x1bOP")[0]).Key);
        Assert.Equal(Key.Up, ((KeyEvent)Decode("\x1bOA")[0]).Key);
    }

    [Fact]
    public void AltChar()
    {
        var k = (KeyEvent)Decode([0x1b, (byte)'a'])[0];
        Assert.Equal(KeyModifiers.Alt, k.Modifiers);
        Assert.Equal("a", k.Rune.ToString());
    }

    [Fact]
    public void SgrMouse_LeftPress()
    {
        // ESC [ < 0 ; 10 ; 5 M => left button down at (9,4) 0-based
        var m = (MouseEvent)Decode("\x1b[<0;10;5M")[0];
        Assert.Equal(MouseEventKind.Down, m.Kind);
        Assert.Equal(MouseButton.Left, m.Button);
        Assert.Equal(9, m.X);
        Assert.Equal(4, m.Y);
    }

    [Fact]
    public void SgrMouse_Release()
    {
        var m = (MouseEvent)Decode("\x1b[<0;10;5m")[0];
        Assert.Equal(MouseEventKind.Up, m.Kind);
    }

    [Fact]
    public void SgrMouse_WheelUp()
    {
        var m = (MouseEvent)Decode("\x1b[<64;3;3M")[0];
        Assert.Equal(MouseEventKind.Wheel, m.Kind);
        Assert.Equal(MouseButton.WheelUp, m.Button);
    }

    [Fact]
    public void BracketedPaste_YieldsSingleEvent()
    {
        var ev = Decode("\x1b[200~hello\x1b[201~");
        var p = Assert.IsType<PasteEvent>(Assert.Single(ev));
        Assert.Equal("hello", p.Text);
    }

    [Fact]
    public void BracketedPaste_MultibyteSplitAcrossFeeds_IsNotCorrupted()
    {
        // "café☕" pasted in two chunks whose split falls inside the é and the ☕ multibyte
        // sequences. The decoder must not cut mid-sequence (which would yield U+FFFD).
        var d = new InputDecoder();
        byte[] all = Encoding.UTF8.GetBytes("\x1b[200~café☕\x1b[201~");
        int split = 8; // lands inside a multibyte char's bytes
        d.Feed(all[..split]);
        var ev = d.Feed(all[split..]);
        var text = new StringBuilder();
        foreach (var e in ev)
        {
            if (e is PasteEvent p) text.Append(p.Text);
        }
        Assert.Equal("café☕", text.ToString());
        Assert.DoesNotContain('�', text.ToString());
    }

    [Fact]
    public void CtrlSpace_IsControlModifiedSpace()
    {
        var ev = Decode([0x00]); // NUL = Ctrl-Space / Ctrl-@
        var k = Assert.IsType<KeyEvent>(Assert.Single(ev));
        Assert.Equal(KeyModifiers.Control, k.Modifiers);
        Assert.Equal(" ", k.Rune.ToString());
    }

    [Fact]
    public void SplitSequence_AcrossFeeds()
    {
        var d = new InputDecoder();
        Assert.Empty(d.Feed(Encoding.UTF8.GetBytes("\x1b["))); // incomplete
        var ev = d.Feed(Encoding.UTF8.GetBytes("A"));           // completes Up arrow
        Assert.Equal(Key.Up, ((KeyEvent)Assert.Single(ev)).Key);
    }

    [Fact]
    public void LoneEscape_ResolvedByFlush()
    {
        var d = new InputDecoder();
        Assert.Empty(d.Feed([0x1b]));
        var ev = d.Flush();
        Assert.Equal(Key.Escape, ((KeyEvent)Assert.Single(ev)).Key);
    }
}
