using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Widgets;

namespace Cellar.Tests;

public class AsyncContentTests
{
    private static string RowText(Surface s, int y)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < s.Width; x++)
        {
            var g = s.Get(x, y).Grapheme;
            sb.Append(g.Length == 0 ? " " : g);
        }
        return sb.ToString();
    }

    private static void RenderOnce(Widget w, Surface s)
    {
        s.Clear(Style.Default);
        s.SetClip(s.Bounds);
        w.Render(s, s.Bounds);
        s.ResetClip();
    }

    // Spin-renders until the async factory publishes (or times out), mirroring how the app loop
    // repaints on completion. Keeps tests deterministic without a live terminal.
    private static bool WaitLoaded(AsyncContent ac, Surface s, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            RenderOnce(ac, s);
            if (ac.IsLoaded)
            {
                return true;
            }
            Thread.Sleep(5);
        }
        return false;
    }

    [Fact]
    public void FirstRender_ShowsPlaceholder_AndStartsWorkOnce()
    {
        int calls = 0;
        var gate = new ManualResetEventSlim(false);
        var ac = new AsyncContent(
            _ => { Interlocked.Increment(ref calls); gate.Wait(500); return Widgets.Label.Plain("done", Style.Default); },
            placeholder: Widgets.Label.Plain("PLACEHOLDER", Style.Default));

        var s = new Surface(20, 1);
        RenderOnce(ac, s);   // first render: paints placeholder, kicks off work
        RenderOnce(ac, s);   // second render: must not start a second worker
        RenderOnce(ac, s);

        Assert.False(ac.IsLoaded);
        Assert.Contains("PLACEHOLDER", RowText(s, 0));
        gate.Set();
        // Give the single worker a moment; the count must never exceed 1.
        Thread.Sleep(50);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Swap_RendersChild_WhenFactoryCompletes()
    {
        var ac = new AsyncContent(_ => Widgets.Label.Plain("REALCONTENT", Style.Default));
        var s = new Surface(20, 1);

        Assert.True(WaitLoaded(ac, s), "factory did not complete in time");
        RenderOnce(ac, s);
        Assert.Contains("REALCONTENT", RowText(s, 0));
    }

    [Fact]
    public void Exception_ShowsErrorWidget()
    {
        var ac = new AsyncContent((Func<CancellationToken, Widget>)(_ => throw new InvalidOperationException("boom")));
        var s = new Surface(40, 3);

        Assert.True(WaitLoaded(ac, s), "error path did not resolve");
        RenderOnce(ac, s);
        var joined = RowText(s, 0) + RowText(s, 1) + RowText(s, 2);
        Assert.Contains("boom", joined);
    }

    [Fact]
    public void CustomOnError_IsUsed()
    {
        var ac = new AsyncContent(
            (Func<CancellationToken, Widget>)(_ => throw new Exception("x")),
            onError: ex => Widgets.Label.Plain("CUSTOMERR", Style.Default));
        var s = new Surface(20, 1);

        Assert.True(WaitLoaded(ac, s));
        RenderOnce(ac, s);
        Assert.Contains("CUSTOMERR", RowText(s, 0));
    }

    [Fact]
    public void Unmount_CancelsToken_AndLateCompleterDoesNotPublish()
    {
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        bool observedCancel = false;

        var ac = new AsyncContent(ct =>
        {
            started.Set();
            release.Wait(1000);
            observedCancel = ct.IsCancellationRequested;
            ct.ThrowIfCancellationRequested(); // cooperative bail-out
            return Widgets.Label.Plain("SHOULD-NOT-SHOW", Style.Default);
        });

        var s = new Surface(20, 1);
        RenderOnce(ac, s);
        Assert.True(started.Wait(1000));
        ac.Unmount();                 // navigate away → cancel
        release.Set();                // let the worker finish (into cancellation)

        Thread.Sleep(80);
        Assert.True(observedCancel);
        Assert.False(ac.IsLoaded);    // a cancelled late completer must not publish
    }

    [Fact]
    public void Dispose_CancelsToken()
    {
        var started = new ManualResetEventSlim(false);
        CancellationToken captured = default;
        var ac = new AsyncContent(ct => { captured = ct; started.Set(); ct.WaitHandle.WaitOne(1000); return Widgets.Label.Plain("x", Style.Default); });

        var s = new Surface(10, 1);
        RenderOnce(ac, s);
        Assert.True(started.Wait(1000));
        ac.Dispose();
        Assert.True(captured.IsCancellationRequested);
    }

    [Fact]
    public void NeverRendered_DoesNoWork()
    {
        int calls = 0;
        _ = new AsyncContent(_ => { Interlocked.Increment(ref calls); return Widgets.Label.Plain("x", Style.Default); });
        // Constructed but never rendered → lazy: the factory must not run.
        Thread.Sleep(30);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AsyncFactoryOverload_Works()
    {
        var ac = new AsyncContent(async ct =>
        {
            await Task.Delay(20, ct);
            return Widgets.Label.Plain("ASYNCDONE", Style.Default);
        });
        var s = new Surface(20, 1);
        Assert.True(WaitLoaded(ac, s));
        RenderOnce(ac, s);
        Assert.Contains("ASYNCDONE", RowText(s, 0));
        await Task.CompletedTask;
    }
}
