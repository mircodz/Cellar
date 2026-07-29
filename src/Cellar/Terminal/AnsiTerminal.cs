using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Cellar.Primitives;

namespace Cellar.Terminal;

/// <summary>
/// The real-terminal <see cref="ITerminal"/>: raw mode, alt-screen + mouse + bracketed paste,
/// stdin reads, and a guaranteed restore on dispose/exit/Ctrl-C so the shell is never left raw.
/// </summary>
public sealed class AnsiTerminal : ITerminal
{
    private readonly Stream _stdout;
    private readonly Stream _stdin;
    private object? _savedMode;
    private bool _rawActive;
    private bool _disposed;

    private PosixSignalRegistration? _winchReg;
    private Thread? _winPoll;
    private volatile bool _pollRunning;
    private Size _lastSize;

    // Reused by Write(StringBuilder) so a steady frame doesn't allocate. Only the loop thread
    // writes, so no locking is needed.
    private byte[] _writeBuffer = new byte[4096];
    private char[] _charBuffer = new char[1024];

    public event Action<Size>? Resized;

    public AnsiTerminal()
    {
        _stdout = Console.OpenStandardOutput();
        _stdin = OpenStdin();
        ColorDepth = DetectColorDepth();
        _lastSize = Size;
    }

    // On Unix, Console.OpenStandardInput() routes through .NET's console layer, which keeps its
    // own cooked-mode line processing and fights our termios raw mode (echoed keys, scrolling
    // instead of raw bytes). Duplicating fd 0 and wrapping a bare FileStream around it reads
    // straight from the terminal, so raw mode actually takes effect. Windows keeps the console
    // stream because its ReadInput path waits on the real console handle.
    private static Stream OpenStdin()
    {
        if (TerminalMode.IsWindows)
        {
            return Console.OpenStandardInput();
        }

        int dupFd = dup(STDIN_FILENO_UNIX);
        if (dupFd < 0)
        {
            return Console.OpenStandardInput(); // fall back if dup fails
        }

        var handle = new SafeFileHandle((IntPtr)dupFd, ownsHandle: true);
        return new FileStream(handle, FileAccess.Read, bufferSize: 1, isAsync: false);
    }

    private const int STDIN_FILENO_UNIX = 0;

    [DllImport("libc", SetLastError = true)]
    private static extern int dup(int fd);

    public ColorDepth ColorDepth { get; }

    public Size Size
    {
        get
        {
            try
            {
                int w = Console.WindowWidth;
                int h = Console.WindowHeight;
                return new Size(w <= 0 ? 80 : w, h <= 0 ? 24 : h);
            }
            catch
            {
                return new Size(80, 24);
            }
        }
    }

    public void EnterRawMode()
    {
        if (_rawActive)
        {
            return;
        }

        _savedMode = TerminalMode.Enter();
        _rawActive = true;

        // Ensure restoration even on abnormal termination.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnCancelKeyPress;

        Write(Ansi.EnterAltScreen);
        Write(Ansi.HideCursor);
        Write(Ansi.EnableMouse);
        Write(Ansi.EnableBracketedPaste);
        Write(Ansi.ClearScreen);
        Write(Ansi.CursorHome);

        StartResizeWatch();
    }

    private void StartResizeWatch()
    {
        _lastSize = Size;

        // SIGWINCH is available on Linux and macOS; Windows has no equivalent signal, so
        // there we fall back to a low-frequency size poll on a background thread.
        if (!OperatingSystem.IsWindows())
        {
            _winchReg = PosixSignalRegistration.Create(
                PosixSignal.SIGWINCH, _ => RaiseIfResized());
        }
        else
        {
            _pollRunning = true;
            _winPoll = new Thread(() =>
            {
                while (_pollRunning)
                {
                    RaiseIfResized();
                    Thread.Sleep(100);
                }
            })
            { IsBackground = true, Name = "tessera-resize-poll" };
            _winPoll.Start();
        }
    }

    private void StopResizeWatch()
    {
        _pollRunning = false;
        _winPoll = null;
        _winchReg?.Dispose();
        _winchReg = null;
    }

    private void RaiseIfResized()
    {
        var current = Size;
        if (current != _lastSize)
        {
            _lastSize = current;
            Resized?.Invoke(current);
        }
    }

    public void LeaveRawMode()
    {
        if (!_rawActive)
        {
            return;
        }

        StopResizeWatch();

        Write(Ansi.DisableBracketedPaste);
        Write(Ansi.DisableMouse);
        Write(Ansi.ShowCursor);
        Write(Ansi.Reset);
        Write(Ansi.LeaveAltScreen);

        if (_savedMode is not null)
        {
            TerminalMode.Restore(_savedMode);
        }

        _savedMode = null;
        _rawActive = false;

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        Console.CancelKeyPress -= OnCancelKeyPress;
    }

    public void Write(string ansi)
    {
        if (string.IsNullOrEmpty(ansi))
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(ansi);
        _stdout.Write(bytes, 0, bytes.Length);
        _stdout.Flush();
    }

    public void Write(StringBuilder ansi)
    {
        int len = ansi.Length;
        if (len == 0)
        {
            return;
        }

        // Copy to a contiguous char span first so encoding sees whole surrogate pairs (chunk
        // boundaries can split one). UTF-8 is at most 4 bytes per UTF-16 char.
        if (_charBuffer.Length < len)
        {
            _charBuffer = new char[Math.Max(len, _charBuffer.Length * 2)];
        }

        int maxBytes = len * 4;
        if (_writeBuffer.Length < maxBytes)
        {
            _writeBuffer = new byte[Math.Max(maxBytes, _writeBuffer.Length * 2)];
        }

        ansi.CopyTo(0, _charBuffer, 0, len);
        int total = Encoding.UTF8.GetBytes(_charBuffer.AsSpan(0, len), _writeBuffer);

        _stdout.Write(_writeBuffer, 0, total);
        _stdout.Flush();
    }

    public int ReadInput(Span<byte> buffer, TimeSpan timeout)
    {
        // On Unix, termios VTIME makes the read itself block with a timeout, so we just read.
        // Windows has no VTIME, so we wait on the stdin handle instead of busy-polling with
        // KeyAvailable + Sleep, which keeps the CPU idle while nothing arrives.
        if (TerminalMode.IsWindows)
        {
            var handle = GetStdHandle(STD_INPUT_HANDLE);
            uint ms = (uint)Math.Clamp(timeout.TotalMilliseconds, 0, uint.MaxValue - 1);
            uint result = WaitForSingleObject(handle, ms);
            if (result != WAIT_OBJECT_0)
            {
                return 0; // timed out (or abandoned) — no input this round
            }
        }

        int read = _stdin.Read(buffer);
        return read > 0 ? read : 0;
    }

    private const int STD_INPUT_HANDLE = -10;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    private void OnProcessExit(object? sender, EventArgs e) => SafeRestore();
    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) => SafeRestore();

    private void SafeRestore()
    {
        try { LeaveRawMode(); }
        catch { /* best effort on teardown */ }
    }

    private static ColorDepth DetectColorDepth()
    {
        // An explicit override wins over any heuristic (TESSERA_COLOR=truecolor|256|16|none).
        string? forced = Environment.GetEnvironmentVariable("TESSERA_COLOR");
        if (TryParseDepth(forced, out var forcedDepth))
        {
            return forcedDepth;
        }

        // COLORTERM is the most reliable truecolor signal.
        string? colorterm = Environment.GetEnvironmentVariable("COLORTERM");
        if (colorterm is not null &&
            (colorterm.Contains("truecolor", StringComparison.OrdinalIgnoreCase) ||
             colorterm.Contains("24bit", StringComparison.OrdinalIgnoreCase)))
        {
            return ColorDepth.TrueColor;
        }

        // Check emulator markers before TERM because some truecolor terminals report xterm-256color.
        if (IsKnownTrueColorTerminal())
        {
            return ColorDepth.TrueColor;
        }

        string? term = Environment.GetEnvironmentVariable("TERM");
        if (term is not null)
        {
            if (term.Contains("truecolor", StringComparison.Ordinal) ||
                term.Contains("24bit", StringComparison.Ordinal))
            {
                return ColorDepth.TrueColor;
            }

            if (term.Contains("256color", StringComparison.Ordinal))
            {
                return ColorDepth.Ansi256;
            }

            if (term.Equals("dumb", StringComparison.Ordinal))
            {
                return ColorDepth.NoColor;
            }

            if (term.Contains("color", StringComparison.Ordinal))
            {
                return ColorDepth.Ansi16;
            }
        }

        // Modern Windows consoles support truecolor.
        if (TerminalMode.IsWindows)
        {
            return ColorDepth.TrueColor;
        }

        // Conservative fallback.
        return ColorDepth.Ansi256;
    }

    // Recognizes terminal emulators known to support 24-bit color via their marker env vars.
    private static bool IsKnownTrueColorTerminal()
    {
        if (Environment.GetEnvironmentVariable("WT_SESSION") is not null)
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable("KITTY_WINDOW_ID") is not null)
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable("ALACRITTY_WINDOW_ID") is not null ||
            Environment.GetEnvironmentVariable("ALACRITTY_SOCKET") is not null)
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable("WEZTERM_PANE") is not null ||
            Environment.GetEnvironmentVariable("WEZTERM_EXECUTABLE") is not null)
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable("KONSOLE_VERSION") is not null)
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable("VTE_VERSION") is { } vte &&
            int.TryParse(vte, out int v) && v >= 3600)
        {
            return true; // gnome-terminal etc. ≥0.36
        }

        if (Environment.GetEnvironmentVariable("TERMINAL_EMULATOR") is { } je &&
            je.Contains("JetBrains", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? prog = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        if (prog is "iTerm.app" or "vscode" or "WezTerm" or "Hyper" or "Tabby" or "ghostty")
        {
            return true;
        }

        return false;
    }

    private static bool TryParseDepth(string? value, out ColorDepth depth)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "truecolor" or "24bit" or "24": depth = ColorDepth.TrueColor; return true;
            case "256" or "8bit": depth = ColorDepth.Ansi256; return true;
            case "16" or "ansi": depth = ColorDepth.Ansi16; return true;
            case "none" or "0" or "mono": depth = ColorDepth.NoColor; return true;
            default: depth = ColorDepth.TrueColor; return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SafeRestore();
        _stdin.Dispose(); // closes the dup'd fd on Unix
    }
}
