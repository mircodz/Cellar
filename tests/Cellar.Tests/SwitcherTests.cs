using System.Collections.Generic;
using Cellar.Terminal;
using Cellar.Widgets;
using Xunit;

namespace Cellar.Tests;

public class SwitcherTests
{
    private static KeyEvent K(Key key) => new(key, default, KeyModifiers.None);

    [Fact]
    public void RendersAndForwardsEventsToTheCurrentWidget()
    {
        var first = new Label("first");
        var sink = new KeySink(Key.Enter);
        var switcher = new Switcher(first);
        Assert.Equal("first", SurfaceText.Row(SurfaceText.Render(switcher, 10), 0).TrimEnd());
        Assert.False(switcher.OnEvent(K(Key.Enter)));

        switcher.Current = sink;
        Assert.True(switcher.OnEvent(K(Key.Enter)));
        Assert.Equal(1, sink.Seen);

        switcher.Current = null;
        Assert.Equal("", SurfaceText.Row(SurfaceText.Render(switcher, 10), 0).TrimEnd());
        Assert.False(switcher.OnEvent(K(Key.Enter)));
    }

    [Fact]
    public void Swapping_MovesTheFocus()
    {
        var a = new Input();
        var b = new Input();
        var switcher = new Switcher(a) { HasFocus = true };
        Assert.True(a.HasFocus);

        switcher.Current = b;
        Assert.False(a.HasFocus);
        Assert.True(b.HasFocus);
        Assert.True(switcher.HasFocus);

        switcher.HasFocus = false;
        switcher.Current = a;
        Assert.False(a.HasFocus);
        Assert.False(b.HasFocus);
    }

    [Fact]
    public void NamedWidgets_ShowByName_CaseInsensitively()
    {
        var swaps = new List<(Widget? From, Widget? To)>();
        var inbox = new Label("inbox");
        var notes = new Label("notes");
        var switcher = new Switcher { OnSwitch = (from, to) => swaps.Add((from, to)) }
            .Add("Inbox", inbox)
            .Add("Notes", notes);

        Assert.Same(inbox, switcher.Current); // the first one added shows
        Assert.Equal("Inbox", switcher.CurrentName);
        Assert.Equal(["Inbox", "Notes"], switcher.Names);

        Assert.True(switcher.Show("notes"));
        Assert.Same(notes, switcher.Current);
        Assert.False(switcher.Show("calendar"));
        Assert.Same(notes, switcher.Current);
        Assert.Same(inbox, switcher.Get("INBOX"));
        Assert.Equal([(null, inbox), (inbox, notes)], swaps);

        Assert.True(switcher.Remove("Notes"));
        Assert.Null(switcher.Current);
        Assert.Null(switcher.CurrentName);
    }

    [Fact]
    public void Lifecycle_MountsTheCurrentWidget_AndUnmountsWhatIsSwappedOut()
    {
        using var app = new App(new FakeTerminal());
        var a = new MountProbe();
        var b = new MountProbe();
        var switcher = new Switcher(a);
        switcher.Mount(app);
        Assert.True(a.IsMounted);

        switcher.Current = b;
        Assert.False(a.IsMounted);
        Assert.True(b.IsMounted);

        switcher.Current = a;
        Assert.True(a.IsMounted);
        Assert.Equal(2, a.Mounts);

        switcher.Unmount();
        Assert.False(a.IsMounted);
        Assert.False(b.IsMounted);
    }

    [Fact]
    public void KeepHiddenMounted_KeepsShownWidgetsLiveUntilTheSwitcherUnmounts()
    {
        using var app = new App(new FakeTerminal());
        var a = new MountProbe();
        var b = new MountProbe();
        var switcher = new Switcher(a) { KeepHiddenMounted = true };
        switcher.Mount(app);
        switcher.Current = b;
        switcher.Current = a;
        Assert.True(a.IsMounted);
        Assert.True(b.IsMounted);
        Assert.Equal(1, a.Mounts);

        switcher.Unmount();
        Assert.False(a.IsMounted);
        Assert.False(b.IsMounted);
    }

    [Fact]
    public void WidgetsSwappedInBeforeMounting_WaitForTheMount()
    {
        using var app = new App(new FakeTerminal());
        var a = new MountProbe();
        var switcher = new Switcher();
        switcher.Current = a;
        Assert.False(a.IsMounted);
        switcher.Mount(app);
        Assert.True(a.IsMounted);
        Assert.Equal(1, a.Mounts);
    }
}
