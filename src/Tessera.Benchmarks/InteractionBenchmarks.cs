using BenchmarkDotNet.Attributes;
using Tessera.Layout;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Text;
using Tessera.Widgets;
using Tessera.Widgets.Charts.Trees;

namespace Tessera.Benchmarks;

/// <summary>
/// Benchmarks the interactive paths that run on every frame while active or on every
/// keystroke: overlay scrim compositing, the command-palette fuzzy filter, Navigator page
/// rendering, and the progress bar. These grew in after the original suite and were
/// previously unmeasured.
/// </summary>
[MemoryDiagnoser]
public class InteractionBenchmarks
{
    private Surface _surface = null!;
    private Rect _area;
    private Overlay _overlay = null!;
    private Size _screen;
    private CommandPalette _palette = null!;
    private Navigator _navigator = null!;
    private ProgressBar _progress = null!;

    [GlobalSetup]
    public void Setup()
    {
        _surface = new Surface(120, 40);
        _area = _surface.Bounds;
        _screen = new Size(120, 40);
        // Pre-fill the surface so the overlay's Dim scrim has real (truecolor) cells to darken.
        for (int y = 0; y < 40; y++)
            _surface.DrawText(0, y, new string('x', 120), new Style(Color.Rgb(200, 200, 200), Color.Rgb(20, 20, 30)));

        // A modal overlay (centered, dimmed) over a command palette — the whole scrim +
        // palette-render path that runs every frame while the palette is open.
        _palette = new CommandPalette();
        string[] verbs = { "Open", "Close", "Go to", "Toggle", "Show", "Hide", "Run", "Copy" };
        string[] nouns = { "File", "Folder", "Snapshot", "Type list", "Instance", "Backtrace", "Retained set", "Theme" };
        foreach (var v in verbs)
        {
            foreach (var n in nouns)
            {
                _palette.Add($"{v} {n}", () => { }, "cmd");
            }
        }

        _palette.HasFocus = true;
        _overlay = new Overlay(_palette)
        {
            Placement = OverlayPlacement.Top, WidthPercent = 60, Height = 16, ScrimOpacity = 0.4,
        };

        // A Navigator 3 pages deep (breadcrumb + top page render).
        _navigator = new Navigator();
        _navigator.Reset(new Page("Snapshots", new Label("root")));
        _navigator.Push("heap.snap", new Label("types"));
        _navigator.Push("System.String", new Label("instances"));

        _progress = new ProgressBar(0.42)
        {
            LabelPlacement = LabelPlacement.Right, LabelType = ProgressLabel.Bytes,
            Current = 4_200_000, Total = 10_000_000, LabelWidth = 20,
        };
    }

    [Benchmark(Description = "Overlay scrim + palette render (open modal frame)")]
    public void OverlayFrame() => _overlay.Render(_surface, _screen);

    [Benchmark(Description = "CommandPalette fuzzy filter (64 commands)")]
    public bool PaletteFilter()
    {
        // Simulate a keystroke: change the query, which refilters all commands.
        _palette.OnEvent(new Terminal.KeyEvent(Terminal.Key.Char, new System.Text.Rune('o'), Terminal.KeyModifiers.None));
        bool has = _palette.Selected is not null;
        _palette.OnEvent(new Terminal.KeyEvent(Terminal.Key.Backspace, default, Terminal.KeyModifiers.None));
        return has;
    }

    [Benchmark(Description = "Navigator render (breadcrumb + top page)")]
    public void NavigatorRender() => _navigator.Render(_surface, _area);

    [Benchmark(Description = "ProgressBar render (sub-cell + bytes label)")]
    public void ProgressBarRender() => _progress.Render(_surface, new Rect(0, 0, 120, 1));
}

/// <summary>
/// Benchmarks the mouse-interaction paths added with full position support. With 1003 any-motion
/// tracking, every cursor move dispatches through the widget tree, hit-tests, and updates hover —
/// potentially every frame — so this path must stay cheap and allocation-free. Covers moving over
/// a link tree (with and without a hover state change), clicking, and Button events.
/// </summary>
[MemoryDiagnoser]
public class MouseInteractionBenchmarks
{
    private Surface _surface = null!;
    private Rect _area;
    private Widget _root = null!;          // a realistic two-pane layout with a link tree
    private TreeView<long> _tree = null!;
    private Button _button = null!;
    private Surface _btnSurface = null!;

    private int _clicks;

    [GlobalSetup]
    public void Setup()
    {
        _surface = new Surface(120, 40);
        _area = _surface.Bounds;

        // A tree whose rows carry a clickable/hoverable hex-address link (value-type payload, so
        // the boxing-safe value-equality hover path is exercised).
        _tree = new TreeView<long>
        {
            RenderLabel = n => StyledText.Of($"Frame_{n.Value}  ").Fg(Color.Rgb(220, 220, 220))
                .Append($"0x{n.Value:x8}").Fg(Color.Rgb(120, 160, 200)).Underline().Link(n.Value),
            ShowHeader = true,
            Striped = true,
        };
        var rng = new System.Random(1);
        var root = _tree.AddRoot(0x1000);
        for (int i = 1; i <= 40; i++)
        {
            var c = root.AddChild(0x1000L + rng.Next(0x1000, 0xffffff) * i);
            for (int j = 0; j < 3; j++) c.AddChild(0x2000L + rng.Next(0x1000, 0xffffff));
        }
        root.ExpandAll();
        _tree.MarkDirty();
        _tree.HasFocus = true;

        // A realistic app root: a sidebar list beside the link tree, over a footer button row.
        var sidebar = new Table { ShowHeader = false };
        sidebar.Columns.Add(new Column("", Constraint.Fill()));
        for (int i = 0; i < 20; i++) sidebar.Rows.Add(new[] { $"item {i}" });

        var footer = new Stack(Direction.Horizontal)
            .Add(new Button("q Quit"), Constraint.Length(9))
            .Add(new Button("t Theme"), Constraint.Length(10))
            .Add(new Label(StyledText.Empty()), Constraint.Fill());

        _root = new Stack(Direction.Vertical)
            .Add(new Stack(Direction.Horizontal)
                .Add(new Panel(sidebar, " Widgets "), Constraint.Length(24))
                .Add(new Panel(_tree, " Tree "), Constraint.Fill()),
                Constraint.Fill())
            .Add(footer, Constraint.Length(1));

        // Render once so all widgets capture their layout rects (hit-testing needs them).
        _surface.Clear(Style.Default);
        _root.Render(_surface, _area);

        // A standalone button for the isolated button-event benchmark.
        _clicks = 0;
        _button = new Button("Click me", () => _clicks++);
        _btnSurface = new Surface(20, 1);
        _button.Render(_btnSurface, new Rect(0, 0, 12, 1));
    }

    // Moving the cursor over the tree area but NOT changing the hovered link (cursor stays on the
    // same link cell). The common case for the 1003 firehose: a Move that costs a full dispatch +
    // hit-test but produces no state change. Must be allocation-free.
    [Benchmark(Description = "Mouse move over tree (no hover change)")]
    public bool MoveNoHoverChange() =>
        _root.OnEvent(new MouseEvent(MouseEventKind.Move, MouseButton.None, 40, 6, KeyModifiers.None));

    // Moving between two different link rows — a hover change that flips HoveredLink and (in a real
    // loop) triggers a re-render with DrawHoverEmphasis.
    private bool _toggle;
    [Benchmark(Description = "Mouse move over tree (hover changes)")]
    public bool MoveHoverChanges()
    {
        _toggle = !_toggle;
        int y = _toggle ? 5 : 6;
        return _root.OnEvent(new MouseEvent(MouseEventKind.Move, MouseButton.None, 40, y, KeyModifiers.None));
    }

    // A full re-render after a hover change (what actually happens each dirty frame): the tree
    // redraws and paints the hover emphasis.
    [Benchmark(Description = "Tree render with hovered link (emphasis repaint)")]
    public void RenderWithHover()
    {
        _root.OnEvent(new MouseEvent(MouseEventKind.Move, MouseButton.None, 40, 6, KeyModifiers.None));
        _surface.Clear(Style.Default);
        _root.Render(_surface, _area);
    }

    // Positional click routing: a Down travels through the nested stacks/panels to the tree.
    [Benchmark(Description = "Mouse click routed to tree link")]
    public bool ClickRouting() =>
        _root.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 40, 6, KeyModifiers.None));

    // A Button's hover + click event cost in isolation.
    [Benchmark(Description = "Button hover + click cycle")]
    public int ButtonCycle()
    {
        _button.OnEvent(new MouseEvent(MouseEventKind.Move, MouseButton.None, 4, 0, KeyModifiers.None));
        _button.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 4, 0, KeyModifiers.None));
        _button.OnEvent(new MouseEvent(MouseEventKind.Up, MouseButton.None, 4, 0, KeyModifiers.None));
        _button.OnEvent(new MouseEvent(MouseEventKind.Move, MouseButton.None, 50, 0, KeyModifiers.None));
        return _clicks;
    }
}
