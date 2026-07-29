using System;
using System.Text;

namespace Cellar.Terminal;

/// <summary>
/// Builds OSC 52 clipboard-set sequences. OSC 52 is widely supported and works over SSH
/// (the emulator does the write, not the host).
/// </summary>
public static class Clipboard
{
    /// <summary>The OSC 52 sequence copying <paramref name="text"/> to the clipboard (empty if empty).</summary>
    public static string SetClipboardSequence(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        // ESC ] 52 ; c ; <base64> BEL  — 'c' = the "clipboard" selection.
        return $"\x1b]52;c;{b64}\x07";
    }
}
