using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;

namespace Tessera.Widgets;

/// <summary>
/// The application host and event loop: funnels input, resize, and posted messages through
/// one channel, dispatches them, re-renders the root into the back buffer, and flushes the
/// diff. Use <see cref="Post"/> to feed a live data stream into the loop.
/// </summary>
public sealed class App : IDisposable
{
    private readonly ITerminal _terminal;
    private readonly InputDecoder _decoder = new();
    private readonly Channel<object> _messages = Channel.CreateUnbounded<object>();
    private readonly List<Overlay> _overlays = new();
    private Screen _screen;
    private Size _size;
    private Widget? _root;
    private volatile bool _running;
    private volatile bool _dirty = true;

    private Selection? _selection;
    private bool _selecting;

    /// <summary>The root widget. Set it before calling <see cref="RunAsync"/>.</summary>
    public Widget? Root
    {
        get => _root;
        set
        {
            if (_running && !ReferenceEquals(_root, value))
            {
                throw new InvalidOperationException(
                    "Root cannot be replaced while the app is running. Use a container such as Navigator.");
            }

            _root = value;
        }
    }

    /// <summary>The current terminal size in cells.</summary>
    public Size Size => _size;

    /// <summary>Optional debug HUD, drawn on top of everything. It never receives input (clicks
    /// pass through to the content beneath). Set a <see cref="DebugPanel"/> to enable; null to hide.</summary>
    public DebugPanel? Debug { get; set; }

    /// <summary>Where the debug HUD sits, in cells from the top-left. Ignored when <see cref="Debug"/> is null.</summary>
    public Rect DebugBounds { get; set; } = new(0, 0, 34, 12);

    /// <summary>The screen background behind everything (default resolves to the theme's).
    /// Substituted for any default/"inherit" background cell. <see cref="Color.Default"/> = transparent.</summary>
    public Color? Background { get; set; }

    /// <summary>Makes backgrounds transparent (terminal background shows through everywhere).</summary>
    public void UseTransparentBackground() => Background = Color.Default;

    /// <summary>Screen-wide mouse text selection: drag to highlight, Ctrl+C/mouse-up copies via
    /// OSC 52, Alt+drag for a block selection. Framework-drawn (mouse tracking disables the
    /// terminal's own). On by default.</summary>
    public bool MouseSelectionEnabled { get; set; } = true;

    /// <summary>Copies to the system clipboard automatically when a drag selection ends.</summary>
    public bool CopyOnSelect { get; set; } = true;

    /// <summary>Enables bare-motion (hover) reporting so widgets receive Move events. On by
    /// default; turn off on slow/SSH links to avoid the any-motion event stream. Only takes
    /// effect at <see cref="RunAsync"/> start.</summary>
    public bool MouseHoverEnabled { get; set; } = true;

    /// <summary>The current text selection, or null when nothing is selected.</summary>
    public Selection? CurrentSelection => _selection;

    /// <summary>The currently selected text (empty if none).</summary>
    public string SelectedText =>
        _selection is { } s ? _screen.Back.ExtractText(s) : string.Empty;

    /// <summary>Clears any active selection and its highlight.</summary>
    public void ClearSelection()
    {
        if (_selection is not null || _selecting)
        {
            _selection = null;
            _selecting = false;
            _dirty = true;
        }
    }

    /// <summary>Copies the current selection to the system clipboard (OSC 52). No-op if empty.</summary>
    public void CopySelection()
    {
        var text = SelectedText;
        if (text.Length == 0)
        {
            return;
        }

        _terminal.Write(Terminal.Clipboard.SetClipboardSequence(text));
    }

    /// <summary>Invoked for every input event; return true if handled. Any event marks the UI dirty.</summary>
    public Func<InputEvent, bool>? OnEvent { get; set; }

    /// <summary>Invoked for each user-posted message (anything that isn't an input event).</summary>
    public Action<object>? OnMessage { get; set; }

    /// <summary>Frame cap; the screen is flushed at most this often when dirty.</summary>
    public TimeSpan FrameInterval { get; set; } = TimeSpan.FromMilliseconds(16); // ~60fps

    public App(ITerminal terminal)
    {
        _terminal = terminal;
        _size = terminal.Size;
        _screen = new Screen(_size.Width, _size.Height, terminal.ColorDepth);
        _terminal.Resized += OnTerminalResized;
    }

    private void OnTerminalResized(Size size) => Post(new ResizeEvent(size.Width, size.Height));

    /// <summary>Posts a message onto the loop from any thread (e.g. an allocation update).</summary>
    public void Post(object message) => _messages.Writer.TryWrite(message);

    /// <summary>Requests a repaint and wakes the idle loop. Safe from any thread.</summary>
    public void Invalidate()
    {
        _dirty = true;
        _messages.Writer.TryWrite(Wake);
    }

    /// <summary>Stops the loop. Wakes the idle loop so it observes the stopped state promptly.</summary>
    public void Quit()
    {
        _running = false;
        _messages.Writer.TryWrite(Wake);
    }

    private static readonly object Wake = new();
    private sealed record InputPumpFailure(Exception Error);

    /// <summary>The current overlay stack, bottom-to-top (last is topmost).</summary>
    public IReadOnlyList<Overlay> Overlays => _overlays;

    /// <summary>The topmost overlay, or null if none.</summary>
    public Overlay? TopOverlay => _overlays.Count > 0 ? _overlays[^1] : null;

    /// <summary>Pushes an overlay onto the top of the stack and focuses its content.</summary>
    public void PushOverlay(Overlay overlay)
    {
        _overlays.Add(overlay);
        overlay.Content.HasFocus = true;
        MountOverlay(overlay);
        _dirty = true;
    }

    /// <summary>Removes and returns the topmost overlay, invoking its dismiss callback.</summary>
    public Overlay? PopOverlay()
    {
        if (_overlays.Count == 0)
        {
            return null;
        }

        var top = _overlays[^1];
        _overlays.RemoveAt(_overlays.Count - 1);
        top.Content.HasFocus = false;
        UnmountOverlay(top);
        top.OnDismiss?.Invoke();
        if (_overlays.Count > 0)
        {
            _overlays[^1].Content.HasFocus = true;
        }

        _dirty = true;
        return top;
    }

    /// <summary>Removes a specific overlay wherever it sits in the stack.</summary>
    public void RemoveOverlay(Overlay overlay)
    {
        int idx = _overlays.IndexOf(overlay);
        if (idx < 0)
        {
            return;
        }

        _overlays.RemoveAt(idx);
        overlay.Content.HasFocus = false;
        UnmountOverlay(overlay);
        overlay.OnDismiss?.Invoke();
        if (_overlays.Count > 0)
        {
            _overlays[^1].Content.HasFocus = true;
        }

        _dirty = true;
    }

    private readonly HashSet<Overlay> _mountedOverlays = new();

    private void MountOverlay(Overlay overlay)
    {
        if (_running && _mountedOverlays.Add(overlay))
        {
            overlay.Content.Mount(this);
        }
    }

    private void UnmountOverlay(Overlay overlay)
    {
        if (_mountedOverlays.Remove(overlay))
        {
            overlay.Content.Unmount();
        }
    }

    /// <summary>Runs the loop until <see cref="Quit"/> is called or the token is cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _running = true;
        bool rawModeEntered = false;
        bool motionEnabled = false;
        Task? readerTask = null;

        try
        {
            _terminal.EnterRawMode();
            rawModeEntered = true;
            Repaint.Callback = Invalidate;

            if (MouseHoverEnabled)
            {
                _terminal.Write(Terminal.Ansi.EnableMotion);
                motionEnabled = true;
            }

            readerTask = Task.Run(() => InputPump(cancellationToken), CancellationToken.None);

            Root?.Mount(this);
            foreach (var overlay in _overlays)
            {
                MountOverlay(overlay);
            }

            _dirty = false;
            Render();
            long lastRenderTicks = Environment.TickCount64;

            while (_running && !cancellationToken.IsCancellationRequested)
            {
                if (!await _messages.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                while (_messages.Reader.TryRead(out var msg))
                {
                    Dispatch(msg);
                }

                if (_dirty)
                {
                    long sinceLast = Environment.TickCount64 - lastRenderTicks;
                    long frameMs = (long)FrameInterval.TotalMilliseconds;
                    if (sinceLast < frameMs)
                    {
                        await Task.Delay((int)(frameMs - sinceLast), cancellationToken).ConfigureAwait(false);
                        while (_messages.Reader.TryRead(out var msg))
                        {
                            Dispatch(msg);
                        }
                    }

                    _dirty = false;
                    Render();
                    lastRenderTicks = Environment.TickCount64;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _running = false;
            try
            {
                if (readerTask is not null)
                {
                    await readerTask.ConfigureAwait(false);
                }

                for (int i = _overlays.Count - 1; i >= 0; i--)
                {
                    UnmountOverlay(_overlays[i]);
                }
                Root?.Unmount();
            }
            finally
            {
                Repaint.Callback = null;
                try
                {
                    if (motionEnabled)
                    {
                        _terminal.Write(Terminal.Ansi.DisableMotion);
                    }
                }
                finally
                {
                    if (rawModeEntered)
                    {
                        _terminal.LeaveRawMode();
                    }
                }
            }
        }
    }

    private void Dispatch(object msg)
    {
        if (ReferenceEquals(msg, Wake))
        {
            return;
        }

        if (msg is InputPumpFailure failure)
        {
            ExceptionDispatchInfo.Capture(failure.Error).Throw();
        }

        if (msg is ResizeEvent resize)
        {
            var newSize = new Size(resize.Width, resize.Height);
            if (newSize != _size)
            {
                _size = newSize;
                _screen.Resize(newSize.Width, newSize.Height);
            }
            OnEvent?.Invoke(resize);
            Root?.OnEvent(resize);
            _dirty = true;
        }
        else if (msg is InputEvent input)
        {
            DispatchInput(input);
            _dirty = true;
        }
        else
        {
            OnMessage?.Invoke(msg);
            _dirty = true;
        }
    }

    private void DispatchInput(InputEvent input)
    {
        if (_overlays.Count > 0)
        {
            var top = _overlays[^1];

            if (input is KeyEvent { Key: Key.Escape } && top.DismissOnEscape)
            {
                PopOverlay();
                return;
            }

            bool handledByOverlay = top.Content.OnEvent(input);
            if (handledByOverlay || top.Modal)
            {
                return;
            }
        }

        if (MouseSelectionEnabled && _selection is { IsEmpty: false } &&
            input is KeyEvent { Modifiers: KeyModifiers.Control } k && k.Rune.Value == 'c')
        {
            CopySelection();
            return;
        }

        bool appHandled = OnEvent?.Invoke(input) ?? false;
        if (!appHandled)
        {
            appHandled = Root?.OnEvent(input) ?? false;
        }

        if (!appHandled && MouseSelectionEnabled && input is MouseEvent m)
        {
            HandleSelectionMouse(m);
        }
    }

    private void HandleSelectionMouse(MouseEvent m)
    {
        var mode = (m.Modifiers & KeyModifiers.Alt) != 0
            ? SelectionMode.Block : SelectionMode.Linear;

        switch (m.Kind)
        {
            case MouseEventKind.Down when m.Button == MouseButton.Left:
                _selecting = true;
                _selection = new Selection(new Point(m.X, m.Y), new Point(m.X, m.Y), mode);
                _dirty = true;
                break;

            case MouseEventKind.Drag when _selecting && _selection is { } sel:
                _selection = new Selection(sel.Anchor, new Point(m.X, m.Y), mode);
                _dirty = true;
                break;

            case MouseEventKind.Up when _selecting:
                _selecting = false;
                if (_selection is { IsEmpty: false } && CopyOnSelect)
                {
                    CopySelection();
                }

                _dirty = true;
                break;
        }
    }

    private void Render()
    {
        long startTicks = Debug is not null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        long startAlloc = Debug is not null ? GC.GetAllocatedBytesForCurrentThread() : 0;

        var back = _screen.Back;

        _screen.DefaultBackground = Background ?? Theming.Theme.Current.Background;
        back.Clear(Style.Default);

        if (Root is not null)
        {
            var area = back.Bounds;
            back.SetClip(area);
            Root.Render(back, area);
            back.ResetClip();
        }

        foreach (var overlay in _overlays)
        {
            overlay.Render(back, _size);
        }

        if (_selection is { IsEmpty: false } sel && _overlays.Count == 0)
        {
            var theme = Theming.Theme.Current;
            back.HighlightSelection(sel, theme.SelectionForeground, theme.SelectionBackground);
        }

        if (Debug is { } debug)
        {
            var area = DebugBounds.Intersect(back.Bounds);
            back.SetClip(area);
            debug.Render(back, area);
            back.ResetClip();
        }

        var diff = _screen.BuildDiff();
        if (diff.Length > 0)
        {
            _terminal.Write(diff);
        }

        if (Debug is { } d)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread() - startAlloc;
            double frameMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks)
                * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            d.PushFrame(frameMs, allocated, diff.Length);
        }
    }

    private void InputPump(CancellationToken ct)
    {
        try
        {
            var buffer = new byte[1024];
            while (!ct.IsCancellationRequested && _running)
            {
                int read = _terminal.ReadInput(buffer, TimeSpan.FromMilliseconds(50));
                if (read > 0)
                {
                    foreach (var ev in _decoder.Feed(buffer.AsSpan(0, read)))
                    {
                        _messages.Writer.TryWrite(ev);
                    }
                }
                else
                {
                    foreach (var ev in _decoder.Flush())
                    {
                        _messages.Writer.TryWrite(ev);
                    }
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && _running)
        {
            _messages.Writer.TryWrite(new InputPumpFailure(ex));
        }
    }

    /// <summary>Stops the loop, unsubscribes, and restores the terminal.</summary>
    public void Dispose()
    {
        _running = false;
        _terminal.Resized -= OnTerminalResized;
        _messages.Writer.TryComplete();
        _terminal.LeaveRawMode();
    }
}
