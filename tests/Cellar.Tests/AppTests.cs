using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Widgets;

namespace Cellar.Tests;

public class AppTests
{
    [Fact]
    public async Task InputFailure_FaultsRunLoopAndRestoresTerminal()
    {
        var terminal = new FailingTerminal();
        using var app = new App(terminal) { MouseHoverEnabled = false };

        var error = await Assert.ThrowsAsync<IOException>(() => app.RunAsync());

        Assert.Equal("read failed", error.Message);
        Assert.Equal(1, terminal.RawModeEntries);
        Assert.True(terminal.RawModeLeaves >= 1);
    }

    [Fact]
    public async Task InvalidationDuringRender_IsPreserved()
    {
        var terminal = new FakeTerminal();
        using var app = new App(terminal)
        {
            MouseHoverEnabled = false,
            FrameInterval = TimeSpan.Zero,
        };
        var widget = new InvalidatingWidget();
        app.Root = widget;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await app.RunAsync(timeout.Token);

        Assert.Equal(3, widget.RenderCount);
    }

    [Fact]
    public async Task ReplacingRootWhileRunning_IsRejected()
    {
        var terminal = new FakeTerminal();
        using var app = new App(terminal) { MouseHoverEnabled = false };
        app.Root = new PostingWidget();
        app.OnMessage = _ => app.Root = new Label("replacement");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.RunAsync());

        Assert.Contains("Navigator", error.Message);
    }

    private sealed class InvalidatingWidget : Widget
    {
        private App? _app;
        public int RenderCount { get; private set; }

        protected override void OnMount(App app)
        {
            _app = app;
            app.Post(new object());
        }

        public override void Render(Surface surface, Rect area)
        {
            RenderCount++;
            if (RenderCount == 2)
            {
                _app!.Invalidate();
            }
            else if (RenderCount == 3)
            {
                _app!.Quit();
            }
        }
    }

    private sealed class FailingTerminal : ITerminal
    {
        public int RawModeEntries { get; private set; }
        public int RawModeLeaves { get; private set; }
        public Size Size => new(80, 24);
        public ColorDepth ColorDepth => ColorDepth.TrueColor;
        public event Action<Size>? Resized { add { } remove { } }

        public void EnterRawMode() => RawModeEntries++;
        public void LeaveRawMode() => RawModeLeaves++;
        public void Write(string ansi) { }
        public int ReadInput(Span<byte> buffer, TimeSpan timeout) =>
            throw new IOException("read failed");
        public void Dispose() { }
    }

    private sealed class PostingWidget : Widget
    {
        protected override void OnMount(App app) => app.Post(new object());
        public override void Render(Surface surface, Rect area) { }
    }
}
