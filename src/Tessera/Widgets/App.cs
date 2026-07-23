using System;
using System.Collections.Generic;
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
    private bool _running;
    private bool _dirty = true;

    // Mouse text selection state (screen-wide, terminal-like).
    private Selection? _selection;
    private bool _selecting;

    /// <summary>The root widget rendered each frame. Set before or during <see cref="RunAsync"/>.</summary>
    public Widget? Root { get; set; }

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

    // A sentinel message whose only purpose is to wake the event loop (no side effect).
    private static readonly object Wake = new();

    // ---- Overlays / layers ----

    /// <summary>The current overlay stack, bottom-to-top (last is topmost).</summary>
    public IReadOnlyList<Overlay> Overlays => _overlays;

    /// <summary>The topmost overlay, or null if none.</summary>
    public Overlay? TopOverlay => _overlays.Count > 0 ? _overlays[^1] : null;

    /// <summary>Pushes an overlay onto the top of the stack and focuses its content.</summary>
    public void PushOverlay(Overlay overlay)
    {
        _overlays.Add(overlay);
        overlay.Content.HasFocus = true;
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
        top.OnDismiss?.Invoke();
        // Restore focus to the new top overlay, if any.
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
        overlay.OnDismiss?.Invoke();
        if (_overlays.Count > 0)
        {
            _overlays[^1].Content.HasFocus = true;
        }

        _dirty = true;
    }

    /// <summary>Runs the loop until <see cref="Quit"/> is called or the token is cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _terminal.EnterRawMode();
        _running = true;

        // Wire up Repaint for the duration of the loop, so widgets (spinners, skeletons, async
        // loaders) can request a coalesced repaint from within themselves.
        Repaint.Callback = Invalidate;

        // Enable any-motion tracking so widgets receive bare hover (Move) events. This is a
        // high-frequency stream; apps on slow links can turn it off via MouseHoverEnabled.
        if (MouseHoverEnabled)
        {
            _terminal.Write(Terminal.Ansi.EnableMotion);
        }

        // Background reader: pull raw bytes and push decoded events onto the channel.
        var readerTask = Task.Run(() => InputPump(cancellationToken), cancellationToken);

        try
        {
            Root?.Mount(this); // notify the tree it entered the live app (starts lifecycles)
            Render(); // initial paint
            long lastRenderTicks = Environment.TickCount64;

            // Block until a message arrives (input, resize, or a Post), so an idle app does no
            // work. Then drain everything pending and render at most once. Renders are coalesced
            // to FrameInterval so a burst of messages can't drive the display past the frame cap.
            while (_running && !cancellationToken.IsCancellationRequested)
            {
                // Park in the kernel/scheduler until there's something to do.
                if (!await _messages.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    break; // channel completed
                }

                // Drain every pending message before rendering (coalesces a burst into 1 frame).
                while (_messages.Reader.TryRead(out var msg))
                {
                    Dispatch(msg);
                }

                if (_dirty)
                {
                    // Cap the render rate: if we rendered very recently, wait out the remainder
                    // of the frame interval so rapid updates coalesce instead of thrashing.
                    long sinceLast = Environment.TickCount64 - lastRenderTicks;
                    long frameMs = (long)FrameInterval.TotalMilliseconds;
                    if (sinceLast < frameMs)
                    {
                        await Task.Delay((int)(frameMs - sinceLast), cancellationToken).ConfigureAwait(false);
                        // Drain anything that arrived during the coalescing delay.
                        while (_messages.Reader.TryRead(out var msg))
                        {
                            Dispatch(msg);
                        }
                    }

                    Render();
                    _dirty = false;
                    lastRenderTicks = Environment.TickCount64;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            _running = false;
            Root?.Unmount(); // let the tree cancel background work / release lifecycle resources
            Repaint.Callback = null;
            if (MouseHoverEnabled)
            {
                _terminal.Write(Terminal.Ansi.DisableMotion);
            }
            _terminal.LeaveRawMode();
        }
    }

    private void Dispatch(object msg)
    {
        if (ReferenceEquals(msg, Wake))
        {
            return; // wake sentinel: its only job was to unblock the loop
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
        // Topmost overlay gets first crack at input.
        if (_overlays.Count > 0)
        {
            var top = _overlays[^1];

            // Escape dismisses a dismiss-on-escape overlay before anything else sees it.
            if (input is KeyEvent { Key: Key.Escape } && top.DismissOnEscape)
            {
                PopOverlay();
                return;
            }

            bool handledByOverlay = top.Content.OnEvent(input);
            // A modal overlay swallows all input whether or not its content used it; a
            // non-modal overlay lets unhandled input fall through to lower layers.
            if (handledByOverlay || top.Modal)
            {
                return;
            }
        }

        // Ctrl+C copies an active selection to the clipboard (when selection is enabled).
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

        // If nothing else claimed a mouse event, treat left-drag as a screen text selection.
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

        // The compositor substitutes this for any default-background cell, so a bare Clear +
        // widget draws all land on one solid background. Null keeps them transparent.
        _screen.DefaultBackground = Background ?? Theming.Theme.Current.Background;
        back.Clear(Style.Default);

        if (Root is not null)
        {
            var area = back.Bounds;
            back.SetClip(area);
            Root.Render(back, area);
            back.ResetClip();
        }

        // Overlays render on top, bottom-to-top, each dimming the content beneath it.
        foreach (var overlay in _overlays)
        {
            overlay.Render(back, _size);
        }

        // The text selection highlight sits above everything (only over the base content,
        // not overlays — a modal takes focus, so we suppress the highlight while one is open).
        if (_selection is { IsEmpty: false } sel && _overlays.Count == 0)
        {
            var theme = Theming.Theme.Current;
            back.HighlightSelection(sel, theme.SelectionForeground, theme.SelectionBackground);
        }

        // The debug HUD draws last, on top of everything. It takes no input.
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

        // Feed this frame's stats back to the HUD for the next frame's sparklines.
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
                // Idle: resolve a lone pending ESC into an Escape key.
                foreach (var ev in _decoder.Flush())
                {
                    _messages.Writer.TryWrite(ev);
                }
            }
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
