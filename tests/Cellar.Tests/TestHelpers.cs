using System;
using System.Text;
using Cellar.Primitives;
using Cellar.Terminal;
using Cellar.Widgets;

namespace Cellar.Tests;

// A no-op terminal so tests can build an App without a real TTY. Nothing is drawn or read.
internal sealed class FakeTerminal : ITerminal
{
    public ColorDepth ColorDepth => ColorDepth.TrueColor;
    public Size Size { get; set; } = new(80, 24);
    public event Action<Size>? Resized { add { } remove { } }

    public void EnterRawMode() { }
    public void LeaveRawMode() { }
    public void Write(string ansi) { }
    public int ReadInput(Span<byte> buffer, TimeSpan timeout) => 0;
    public void Dispose() { }
}

// A leaf widget that records how many times it was mounted and unmounted, for lifecycle tests.
internal sealed class MountProbe : Widget
{
    public int Mounts { get; private set; }
    public int Unmounts { get; private set; }
    public bool IsMounted => Mounts > Unmounts;

    protected override void OnMount(App app) => Mounts++;
    protected override void OnUnmount() => Unmounts++;

    public override void Render(Rendering.Surface surface, Rect area) { }
}

// A leaf widget that handles one key and counts how often it saw it.
internal sealed class KeySink(Key key) : Widget
{
    public int Seen { get; private set; }

    public override void Render(Rendering.Surface surface, Rect area) { }

    public override bool OnEvent(InputEvent e)
    {
        if (e is KeyEvent k && k.Key == key)
        {
            Seen++;
            return true;
        }
        return false;
    }
}
