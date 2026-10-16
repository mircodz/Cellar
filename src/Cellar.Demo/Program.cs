using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;
using Cellar.Widgets;

namespace Cellar.Demo;

// A widget catalog ("storybook") for Cellar: a left sidebar lists every widget; the right pane
// shows the selected one large, with a title/blurb header and a footer hint. Each demo is a
// small self-contained free function in Demos.cs — adding a widget is one registry entry.

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selfcheck")
        {
            SelfCheck();
            return;
        }

        using var terminal = new AnsiTerminal();
        using var app = new App(terminal);
        using var cts = new CancellationTokenSource();

        var ctx = new DemoContext();
        var themes = BuiltIn.All;
        int themeIndex = 0;

        // Build the shell: a sidebar list of demos beside the selected demo's pane.
        var registry = Demos.All;
        var detail = new DemoHost(ctx, registry);
        var sidebar = BuildSidebar(registry, detail);

        var split = new Stack(Direction.Horizontal)
            .Add(new Panel(sidebar, " Widgets ") { BorderStyle = BorderStyle.Rounded }, Constraint.Length(24))
            .Add(detail, Constraint.Fill());

        var debug = new DebugPanel();

        void CycleTheme()
        {
            themeIndex = (themeIndex + 1) % themes.Count;
            Theme.Current = themes[themeIndex];
            app.Invalidate();
        }

        void ToggleDebug()
        {
            app.Debug = app.Debug is null ? debug : null;
            app.DebugBounds = new Rect(app.Size.Width - 34, 0, 34, 12);
            app.Invalidate();
        }

        // A clickable footer: each shortcut is a real Button (hover-highlights, click-activates),
        // with its key char bolded — e.g. [q Quit].
        var footer = new Stack(Direction.Horizontal)
            .Add(FooterButton("q", "Quit", app.Quit), Constraint.Length(9))
            .Add(FooterButton("t", "Theme", CycleTheme), Constraint.Length(10))
            .Add(FooterButton("d", "Debug", ToggleDebug), Constraint.Length(10))
            .Add(FooterButton("^P", "Palette", () => OpenPalette(app, registry, sidebar)), Constraint.Length(13))
            .Add(new Label(StyledText.Empty()), Constraint.Fill());

        app.Root = new Stack(Direction.Vertical)
            .Add(split, Constraint.Fill())
            .Add(footer, Constraint.Length(1));

        // The shortcuts only get keys the demo doesn't use, so q, t, and d can be typed into a text field
        // and Escape zooms a flamegraph out before it quits.
        app.OnUnhandledEvent = e =>
        {
            switch (e)
            {
                case KeyEvent { Key: Key.Escape }:
                case KeyEvent { IsChar: true } q when q.Rune.Value == 'q':
                    app.Quit();
                    return true;
                case KeyEvent { IsChar: true } t when t.Rune.Value == 't':
                    CycleTheme();
                    return true;
                case KeyEvent { IsChar: true } d when d.Rune.Value == 'd':
                    ToggleDebug();
                    return true;
                case KeyEvent { Modifiers: KeyModifiers.Control } cp when cp.Rune.Value == 'p':
                    OpenPalette(app, registry, sidebar);
                    return true;
            }
            return false;
        };

        app.OnMessage = msg =>
        {
            if (msg is TickMessage)
            {
                ctx.Advance();
            }
        };

        // A single animation ticker drives every live demo via ctx.Tick.
        var ticker = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                app.Post(new TickMessage());
                try { await Task.Delay(80, cts.Token); }
                catch (TaskCanceledException) { break; }
            }
        });

        await app.RunAsync(cts.Token);
        cts.Cancel();
        try { await ticker; } catch { /* ignore */ }
    }

    // A footer shortcut button: "<key> <label>" with the key char orange + bold. Transparent
    // normal background; hovering fills the theme's striped-row background.
    private static Button FooterButton(string key, string label, Action onClick)
    {
        var hoverBg = Theme.Current.StripeBackground;
        return new Button
        {
            Content = StyledText.Of(key).Bold().Fg(Theme.Current.Warning).Append($" {label}").Fg(Theme.Current.Muted),
            OnClick = onClick,
            Style = new Style(Theme.Current.Muted, Color.Default),
            HoverStyle = new Style(Theme.Current.Foreground, hoverBg),
            PressedStyle = new Style(Theme.Current.SelectionForeground, Theme.Current.Accent),
        };
    }

    // The demo list on the left: a single-column table; selecting a row swaps the detail pane.
    private static Table BuildSidebar(IReadOnlyList<Demo> demos, DemoHost host)
    {
        var list = new Table { ShowHeader = false, SelectedIndex = 0, ShowScrollbar = true };
        list.Columns.Add(new Column("", Constraint.Fill()));
        foreach (var d in demos)
        {
            list.Rows.Add(new[] { d.Name });
        }
        list.HasFocus = true;
        list.OnSelect = i => host.Show(i);
        host.Show(0);
        return list;
    }

    // Renders every demo offscreen (with a few animation ticks) to catch build/render errors
    // without a live terminal. Run with `dotnet run -- --selfcheck`.
    private static void SelfCheck()
    {
        var ctx = new DemoContext();
        var surface = new Surface(100, 30);
        foreach (var demo in Demos.All)
        {
            ctx.ResetSubscribers();
            Widget w = demo.Build(ctx);
            for (int i = 0; i < 5; i++)
            {
                ctx.Advance();
                surface.Clear(Style.Default);
                surface.SetClip(surface.Bounds);
                w.Render(surface, surface.Bounds);
                surface.ResetClip();
            }
            Console.WriteLine($"ok  {demo.Name}");
        }
        Console.WriteLine($"selfcheck passed: {Demos.All.Count} demos rendered.");
    }

    private static void OpenPalette(App app, IReadOnlyList<Demo> demos, Table sidebar)
    {
        if (app.TopOverlay is not null)
        {
            return;
        }

        var palette = new CommandPalette();
        for (int i = 0; i < demos.Count; i++)
        {
            int index = i;
            // Drive the sidebar's selection (the single source of truth) so the list and the
            // detail pane stay in sync — the sidebar's OnSelect swaps the detail pane.
            palette.Add($"Show {demos[i].Name}", () => sidebar.Select(index), "widget");
        }

        var overlay = new Overlay(palette)
        {
            Placement = OverlayPlacement.Top,
            WidthPercent = 60,
            Height = 14,
            Margin = 3,
            Modal = true,
            ScrimOpacity = 0.4,
        };
        palette.OnAfterRun = () => app.RemoveOverlay(overlay);
        app.PushOverlay(overlay);
    }
}

/// <summary>Signals a periodic animation tick.</summary>
internal sealed record TickMessage;

/// <summary>
/// Shared services a demo can use without a god-class: an animation clock (a monotonically
/// rising phase, plus a per-tick event) that live demos subscribe to.
/// </summary>
internal sealed class DemoContext
{
    /// <summary>Rises by a small step each tick; demos derive animation from it.</summary>
    public double Phase { get; private set; }

    /// <summary>Raised once per animation tick. Live demos subscribe to update their widgets.</summary>
    public event Action<double>? Tick;

    public void Advance()
    {
        Phase += 0.05;
        Tick?.Invoke(Phase);
    }

    /// <summary>Clears all Tick subscribers — called when the shown demo changes so a hidden
    /// demo's widgets stop being animated.</summary>
    public void ResetSubscribers() => Tick = null;
}

/// <summary>One catalog entry: a name, a one-line blurb, and a builder that produces the widget.</summary>
internal sealed record Demo(string Name, string Blurb, Func<DemoContext, Widget> Build);

/// <summary>
/// The right-hand pane: renders the currently-selected demo wrapped in a titled panel + blurb,
/// and rebuilds it when the selection changes (resetting the animation subscriptions).
/// </summary>
internal sealed class DemoHost : Widget
{
    private readonly DemoContext _ctx;
    private readonly IReadOnlyList<Demo> _demos;
    private Widget _current = new Label(StyledText.Empty());
    private string _title = "";
    private string _blurb = "";
    private App? _app;

    public DemoHost(DemoContext ctx, IReadOnlyList<Demo> demos)
    {
        _ctx = ctx;
        _demos = demos;
    }

    // Forward the mount lifecycle to whatever demo is currently shown, so demos with background
    // work (AsyncContent) start when displayed and are cancelled when swapped away or on quit.
    protected override void OnMount(App app)
    {
        _app = app;
        _current.Mount(app);
    }

    protected override void OnUnmount()
    {
        _current.Unmount();
        _app = null;
    }

    public void Show(int index)
    {
        if (index < 0 || index >= _demos.Count)
        {
            return;
        }
        var demo = _demos[index];
        _ctx.ResetSubscribers(); // drop the previous demo's animation hooks
        if (_app is not null)
        {
            _current.Unmount(); // cancel the outgoing demo's background work
        }
        _current = demo.Build(_ctx);
        _title = demo.Name;
        _blurb = demo.Blurb;
        if (_app is { } app)
        {
            _current.Mount(app); // start the incoming demo's lifecycle
        }
    }

    public override bool HasFocus
    {
        get => _current.HasFocus;
        set => _current.HasFocus = value;
    }

    public override bool OnEvent(Terminal.InputEvent e) => _current.OnEvent(e);

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        var rows = LayoutSolver.Split(area, Direction.Vertical,
            Constraint.Length(1), Constraint.Fill());
        var header = rows[0];
        var bodyArea = rows[1];

        // Blurb header.
        surface.FillRect(header, new Style(Theme.Current.Foreground, Color.Default));
        TextRenderer.DrawLine(surface, header.X + 1, header.Y, header.Width - 2,
            StyledText.Of(_title).Bold().Fg(Theme.Current.Accent)
                .Append("  " + _blurb).Fg(Theme.Current.Muted),
            Justify.Left);

        var panel = new Panel(_current, $" {_title} ") { BorderStyle = BorderStyle.Rounded };
        surface.SetClip(bodyArea);
        panel.Render(surface, bodyArea);
        surface.ResetClip();
    }
}
