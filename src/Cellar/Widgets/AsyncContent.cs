using System;
using System.Threading;
using System.Threading.Tasks;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// Loads an expensive widget off the UI thread so the caller doesn't have to wire up threading.
/// The first render paints a placeholder (a <see cref="Spinner"/> by default) and never blocks;
/// a factory runs on a background <see cref="Task"/> exactly once, started lazily on first render
/// so a tab that's never shown does no work. When it finishes, the produced widget is swapped in
/// and a repaint is requested. On failure it shows an error widget.
/// </summary>
/// <remarks>
/// Render/Measure/OnEvent just read one cached reference (<c>_loaded ?? _placeholder</c>) and
/// delegate, so steady state allocates nothing per frame. Completion pushes a single
/// <see cref="Repaint.Request"/> instead of polling. The worker writes <c>_loaded</c> once
/// (volatile) and the render thread reads it, so no lock spans a render. <see cref="Interlocked"/>
/// keeps the launch lazy and exactly-once. Unmount/dispose cancels the token; a completer that
/// finds itself cancelled neither publishes nor repaints.
/// </remarks>
public sealed class AsyncContent : Widget, IDisposable
{
    // The render thread reads this; the worker writes it exactly once.
    private volatile Widget? _loaded;

    private readonly Widget _placeholder;
    private readonly Func<CancellationToken, Task<Widget>> _factory; // normalized to the async form
    private readonly Func<Exception, Widget> _onError;
    private readonly CancellationTokenSource _cts = new();

    // 0 = not started, 1 = started. CompareExchange makes the launch lazy and exactly-once.
    private int _started;
    private bool _disposed;

    // Focus requested before the content exists; applied when it's published and kept in sync on render.
    private volatile bool _wantsFocus;

    // The app captured at mount, used to mount the produced child once it is published (so a nested
    // Spinner/AsyncContent inside the loaded widget starts its own lifecycle). Volatile: written on
    // the UI thread at mount, read by the worker at publish. Null if not mounted yet.
    private volatile App? _mountedApp;

    /// <summary>Wraps a synchronous factory that builds the widget on a background thread.</summary>
    /// <param name="factory">Builds the real widget; honor the token to stop early on navigate-away.</param>
    /// <param name="placeholder">Shown while loading. Default: an auto-animating <see cref="Spinner"/>.</param>
    /// <param name="onError">Renders a widget for a factory exception. Default: a themed error label.</param>
    public AsyncContent(
        Func<CancellationToken, Widget> factory,
        Widget? placeholder = null,
        Func<Exception, Widget>? onError = null)
        : this(Wrap(factory), placeholder, onError)
    {
    }

    /// <summary>Wraps a naturally-async factory (e.g. one that awaits I/O).</summary>
    public AsyncContent(
        Func<CancellationToken, Task<Widget>> factory,
        Widget? placeholder = null,
        Func<Exception, Widget>? onError = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _placeholder = placeholder ?? new Spinner(SpinnerFrames.Dots, "Loading…") { AutoAnimate = true };
        _onError = onError ?? DefaultError;
    }

    // Runs a synchronous factory on the thread pool as a Task, so both ctors share one code path.
    private static Func<CancellationToken, Task<Widget>> Wrap(Func<CancellationToken, Widget> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }
        return ct => Task.Run(() => factory(ct), ct);
    }

    /// <summary>The widget currently shown: the loaded child once ready, else the placeholder.</summary>
    private Widget Current => _loaded ?? _placeholder;

    /// <summary>True once the factory has produced a widget (or an error widget) and it is live.</summary>
    public bool IsLoaded => _loaded is not null;

    protected override void OnMount(App app)
    {
        _mountedApp = app;
        // The loaded child is mounted later by Publish.
        _placeholder.Mount(app);
    }

    protected override void OnUnmount()
    {
        _cts.Cancel();
        _mountedApp = null;
    }

    protected override void VisitChildren(Action<Widget> visit)
    {
        // Initial mounting is explicit above; traversal reaches the loaded child during teardown.
        var loaded = _loaded;
        if (loaded is not null)
        {
            visit(loaded);
        }
    }

    public override Size Measure(Size available) => Current.Measure(available);

    public override void Render(Surface surface, Rect area)
    {
        // Focus may have changed between Publish reading it and the content becoming visible.
        if (_loaded is { } loaded && loaded.HasFocus != _wantsFocus)
        {
            loaded.HasFocus = _wantsFocus;
        }
        EnsureStarted();
        Current.Render(surface, area);
    }

    public override bool OnEvent(InputEvent e)
    {
        // While loading, the placeholder is inert (a spinner ignores input), so a loading tab does
        // not trap focus/keys. Once loaded, the real child handles input.
        return _loaded?.OnEvent(e) ?? false;
    }

    /// <summary>Focus flows to the loaded child once ready. Focus given while loading is remembered and passed on
    /// when the content arrives, so a view built in the background is focused like any other.</summary>
    public override bool HasFocus
    {
        get => _loaded?.HasFocus ?? _wantsFocus;
        set
        {
            _wantsFocus = value;
            if (_loaded is { } loaded)
            {
                loaded.HasFocus = value;
            }
        }
    }

    public override bool IsFocusable => _loaded?.IsFocusable ?? false;

    private void EnsureStarted()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return;
        }

        var token = _cts.Token;
        // Fire-and-forget: the continuation publishes on completion. We never await on the UI thread.
        _ = RunAsync(token);
    }

    private async Task RunAsync(CancellationToken token)
    {
        Widget produced;
        try
        {
            produced = await _factory(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return; // cancelled: do not publish or repaint
        }
        catch (Exception ex)
        {
            produced = _onError(ex);
        }

        Publish(produced);
    }

    private void Publish(Widget widget)
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        // Mount the produced child so its own lifecycle (e.g. a nested AsyncContent/Spinner) runs.
        // Mount is safe to call off the UI thread here: the widget isn't yet reachable by the render
        // thread (it's published on the next line), and OnMount implementations only capture state.
        if (_mountedApp is { } app)
        {
            widget.Mount(app);
        }

        widget.HasFocus = _wantsFocus; // not reachable by the UI thread until published below
        _loaded = widget;   // volatile publish — the render thread sees a fully-constructed widget
        Repaint.Request();  // coalesced, thread-safe; wakes the loop to paint the swap
    }

    private static Widget DefaultError(Exception ex) =>
        new Label(
            StyledText.Of("⚠ ").Fg(Theme.Current.Error)
                .Append(ex.Message).Fg(Theme.Current.Foreground),
            Justify.Left,
            wrap: true);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}
