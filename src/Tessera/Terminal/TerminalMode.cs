using System;
using System.Runtime.InteropServices;

namespace Tessera.Terminal;

/// <summary>
/// P/Invoke shims to switch a terminal in and out of raw mode: console-mode flags on Windows,
/// <c>termios</c> on Unix. <see cref="Enter"/> returns an opaque token for <see cref="Restore"/>.
/// </summary>
internal static class TerminalMode
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>Switches the terminal to raw mode, returning saved state for restoration.</summary>
    public static object Enter() => IsWindows ? Windows.Enter() : Unix.Enter();

    /// <summary>Restores the terminal to the state captured by <see cref="Enter"/>.</summary>
    public static void Restore(object saved)
    {
        if (IsWindows)
        {
            Windows.Restore(saved);
        }
        else
        {
            Unix.Restore(saved);
        }
    }

    private static class Windows
    {
        private const int STD_INPUT_HANDLE = -10;
        private const int STD_OUTPUT_HANDLE = -11;

        private const uint ENABLE_PROCESSED_INPUT = 0x0001;
        private const uint ENABLE_LINE_INPUT = 0x0002;
        private const uint ENABLE_ECHO_INPUT = 0x0004;
        private const uint ENABLE_WINDOW_INPUT = 0x0008;
        private const uint ENABLE_MOUSE_INPUT = 0x0010;
        private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

        private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        internal sealed class Saved
        {
            public IntPtr InHandle;
            public IntPtr OutHandle;
            public uint InMode;
            public uint OutMode;
        }

        public static object Enter()
        {
            var inH = GetStdHandle(STD_INPUT_HANDLE);
            var outH = GetStdHandle(STD_OUTPUT_HANDLE);
            GetConsoleMode(inH, out uint inMode);
            GetConsoleMode(outH, out uint outMode);

            var saved = new Saved { InHandle = inH, OutHandle = outH, InMode = inMode, OutMode = outMode };

            uint newIn = inMode;
            newIn &= ~(ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT | ENABLE_PROCESSED_INPUT);
            newIn |= ENABLE_VIRTUAL_TERMINAL_INPUT | ENABLE_WINDOW_INPUT | ENABLE_MOUSE_INPUT;
            SetConsoleMode(inH, newIn);

            uint newOut = outMode | ENABLE_PROCESSED_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING;
            SetConsoleMode(outH, newOut);

            return saved;
        }

        public static void Restore(object savedObj)
        {
            var saved = (Saved)savedObj;
            SetConsoleMode(saved.InHandle, saved.InMode);
            SetConsoleMode(saved.OutHandle, saved.OutMode);
        }
    }

    // The termios struct and its flag/index constants differ between Linux and the BSD
    // family (macOS). Linux: 4-byte tcflag_t, a c_line byte, NCCS=32. macOS: 8-byte
    // tcflag_t (unsigned long), no c_line, NCCS=20. We keep a distinct layout + constant
    // set per platform and dispatch on the OS so raw mode is correct on both.
    private static class Unix
    {
        private static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static object Enter() => IsMac ? Mac.Enter() : Linux.Enter();

        public static void Restore(object saved)
        {
            if (IsMac)
            {
                Mac.Restore(saved);
            }
            else
            {
                Linux.Restore(saved);
            }
        }
    }

    private static class Linux
    {
        private const int STDIN_FILENO = 0;
        private const int TCSANOW = 0;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Termios
        {
            public uint c_iflag;
            public uint c_oflag;
            public uint c_cflag;
            public uint c_lflag;
            public byte c_line;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] c_cc;
            public uint c_ispeed;
            public uint c_ospeed;
        }

        private const uint ICANON = 0x0002, ECHO = 0x0008, ISIG = 0x0001, IEXTEN = 0x8000;
        private const uint IXON = 0x0400, ICRNL = 0x0100, BRKINT = 0x0002, INPCK = 0x0010, ISTRIP = 0x0020;
        private const uint OPOST = 0x0001;
        private const int VMIN = 6, VTIME = 5;

        [DllImport("libc", SetLastError = true)]
        private static extern int tcgetattr(int fd, out Termios termios);

        [DllImport("libc", SetLastError = true)]
        private static extern int tcsetattr(int fd, int optionalActions, ref Termios termios);

        public static object Enter()
        {
            tcgetattr(STDIN_FILENO, out var original);
            var raw = original;
            var cc = (byte[])(original.c_cc ?? new byte[32]).Clone();

            raw.c_lflag &= ~(ICANON | ECHO | ISIG | IEXTEN);
            raw.c_iflag &= ~(IXON | ICRNL | BRKINT | INPCK | ISTRIP);
            raw.c_oflag &= ~OPOST;
            cc[VMIN] = 0;
            cc[VTIME] = 1; // 0.1s
            raw.c_cc = cc;

            tcsetattr(STDIN_FILENO, TCSANOW, ref raw);
            return original;
        }

        public static void Restore(object savedObj)
        {
            var original = (Termios)savedObj;
            tcsetattr(STDIN_FILENO, TCSANOW, ref original);
        }
    }

    private static class Mac
    {
        private const int STDIN_FILENO = 0;
        private const int TCSANOW = 0;

        // On 64-bit Darwin tcflag_t and speed_t are `unsigned long` (8 bytes); NCCS = 20.
        [StructLayout(LayoutKind.Sequential)]
        internal struct Termios
        {
            public ulong c_iflag;
            public ulong c_oflag;
            public ulong c_cflag;
            public ulong c_lflag;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
            public byte[] c_cc;
            public ulong c_ispeed;
            public ulong c_ospeed;
        }

        private const ulong ICANON = 0x00000100, ECHO = 0x00000008, ISIG = 0x00000080, IEXTEN = 0x00000400;
        private const ulong IXON = 0x00000200, ICRNL = 0x00000100, BRKINT = 0x00000002, INPCK = 0x00000010, ISTRIP = 0x00000020;
        private const ulong OPOST = 0x00000001;
        private const int VMIN = 16, VTIME = 17;

        [DllImport("libc", SetLastError = true)]
        private static extern int tcgetattr(int fd, out Termios termios);

        [DllImport("libc", SetLastError = true)]
        private static extern int tcsetattr(int fd, int optionalActions, ref Termios termios);

        public static object Enter()
        {
            tcgetattr(STDIN_FILENO, out var original);
            var raw = original;
            var cc = (byte[])(original.c_cc ?? new byte[20]).Clone();

            raw.c_lflag &= ~(ICANON | ECHO | ISIG | IEXTEN);
            raw.c_iflag &= ~(IXON | ICRNL | BRKINT | INPCK | ISTRIP);
            raw.c_oflag &= ~OPOST;
            cc[VMIN] = 0;
            cc[VTIME] = 1; // 0.1s
            raw.c_cc = cc;

            tcsetattr(STDIN_FILENO, TCSANOW, ref raw);
            return original;
        }

        public static void Restore(object savedObj)
        {
            var original = (Termios)savedObj;
            tcsetattr(STDIN_FILENO, TCSANOW, ref original);
        }
    }
}
