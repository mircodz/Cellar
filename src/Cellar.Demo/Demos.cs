using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cellar.Layout;
using Cellar.Primitives;
using Cellar.Text;
using Cellar.Theming;
using Cellar.Widgets;
using Cellar.Widgets.Charts;
using Cellar.Widgets.Charts.Trees;

namespace Cellar.Demo;

/// <summary>One frame in the tree demo: a name, self time, share, and a synthetic heap address.</summary>
internal readonly record struct TraceFrame(string Name, double Ms, double Percent, long Addr = 0);

/// <summary>One row of the table views demo.</summary>
internal sealed record DemoTask(string Name, string State, int Attempts, TimeSpan Age);

/// <summary>One row of the table demo: where something allocates, and how much.</summary>
internal sealed record AllocationSite(string Type, string Method, string Allocator, double MiB, int Count, double OwnPercent)
{
    public string Location => $"{Type}.{Method}";
}

// The catalog registry + one free function per widget family. Each function is self-contained:
// it owns its widgets, and live ones subscribe to ctx.Tick to animate.
internal static class Demos
{
    public static readonly IReadOnlyList<Demo> All =
    [
        new("Table", "rows as objects: styled cells, sort by value, filter", Table),
        new("Table views", "live refresh by key, query filter, groups, marks", TableViews),
        new("Tree", "collapsible, lazy, clickable links", Tree),
        new("Tabs", "tabbed container with a lens bar", TabsDemo),
        new("Dashboard", "many widgets composed on one page", Dashboard),
        new("Async load", "off-thread widget loading with a spinner", AsyncLoad),
        new("Line chart", "multi-series braille plot (live)", LineChartDemo),
        new("Sparkline", "compact rolling series (live)", SparklineDemo),
        new("Bar chart", "horizontal bars, sub-cell precision", BarChartDemo),
        new("Flamegraph", "zoomable icicle, width = weight", Flamegraph),
        new("Proportion bar", "stacked shares + legend", Proportion),
        new("Progress", "sub-cell bars with labels (live)", Progress),
        new("Spinners", "indeterminate animations (live)", Spinners),
        new("Input & forms", "text fields, masking, live filter", Input),
        new("Readline input", "ctrl+w, alt+b, ctrl+u… and ↑/↓ history", ReadlineInput),
        new("Keymap", "one declaration: keys, :commands, help, palette", KeymapDemo),
        new("Status line", "items that give way by priority, messages, key hints", StatusLineDemo),
        new("Command line", ":commands with completion, / filter, questions", CommandLineDemo),
        new("Form", "settings to review and change, as a page or a dialog", FormDemo),
        new("Confirm dialog", "y/n, or type the name; toggles like force", ConfirmDemo),
        new("Panels & borders", "framed containers, border styles", Panels),
        new("Overlay", "modal dialog over dimmed content", OverlayDemo),
        new("Text & wrap", "styled runs, word wrapping, justify", TextDemo),
        new("Custom: gradient", "composed widget — truecolor interpolation", Gradient),
        new("Custom: swatches", "composed widget — color swatch grid", SwatchGridDemo),
        new("Custom: heatmap", "composed widget — contribution heatmap", HeatmapDemo),
        new("Custom: toggle", "composed widget — pill toggle switch", ToggleDemo),
    ];

    // ---- Data widgets ----

    // A Table<T>: each row is an object. Columns draw their cells as styled text and sort by the row's
    // values (Count shows "1,204" but sorts as a number), and the callbacks get the row itself, wherever
    // sorting or filtering moved it. SetRows replaces the rows and keeps the sort.
    private static Widget Table(DemoContext ctx)
    {
        var rng = new Random(7);
        string[] types = { "Parser", "Serializer", "Compiler", "Reader", "Encoder", "Matcher", "Builder", "Hasher" };
        string[] methods = { "Parse", "Write", "Emit", "Read", "Encode", "Match", "Build", "Hash" };
        string[] allocators = { "GC.Allocate", "new byte[]", "String.Concat", "List.Add", "Box" };
        var sites = new List<AllocationSite>();
        for (int i = 0; i < 60; i++)
        {
            sites.Add(new AllocationSite(types[i % types.Length], $"{methods[i % methods.Length]}_{i}",
                allocators[i % allocators.Length], rng.NextDouble() * 200, rng.Next(1, 9000), rng.NextDouble() * 100));
        }

        var hint = new StyledText("type to filter · click a header to sort · click the selected row again to open it",
            Theme.Current.MutedStyle);
        var status = new Label(hint);
        var table = new Table<AllocationSite>
        {
            ShowHeader = true,
            Striped = true,
            ShowScrollbar = true,
            Sortable = true,
            OnSelect = site => status.Content = StyledText.Of("selected ").Fg(Theme.Current.Muted)
                .Append(site.Location).Fg(Theme.Current.Foreground),
            OnActivate = site => status.Content = StyledText.Of("→ opened ").Fg(Theme.Current.Muted)
                .Append(site.Location).Bold().Fg(Theme.Current.Accent)
                .Append(FormattableString.Invariant($"  {site.MiB:0.0} MiB in {site.Count:N0} allocations")).Fg(Theme.Current.Muted),
        };

        // Shares above 60% are hot, above 30% warm.
        static Style Heat(double share) => new(
            share > 0.6 ? Theme.Current.Error : share > 0.3 ? Theme.Current.Warning : Theme.Current.Success, Color.Default);

        // No SortKey: sorts by the cell's text.
        table.Columns.Add(new TableColumn<AllocationSite>("Location", Constraint.Fill(3),
            site => StyledText.Of(site.Type + ".").Fg(Theme.Current.Muted).Append(site.Method).Fg(Theme.Current.Accent)));
        table.Columns.Add(new TableColumn<AllocationSite>("Allocator", Constraint.Fill(2),
            site => new StyledText(site.Allocator, Theme.Current.MutedStyle)));
        table.Columns.Add(new TableColumn<AllocationSite>("MiB", Constraint.Length(10),
            site => new StyledText(site.MiB.ToString("0.0", CultureInfo.InvariantCulture), Heat(site.MiB / 200)), Justify.Right)
        {
            SortKey = site => site.MiB,
        });
        table.Columns.Add(new TableColumn<AllocationSite>("Count", Constraint.Length(9),
            site => site.Count.ToString("N0", CultureInfo.InvariantCulture), Justify.Right)
        {
            SortKey = site => site.Count,
        });
        table.Columns.Add(new TableColumn<AllocationSite>("Own%", Constraint.Length(7),
            site => new StyledText(site.OwnPercent.ToString("0.0", CultureInfo.InvariantCulture), Heat(site.OwnPercent / 100)), Justify.Right)
        {
            SortKey = site => site.OwnPercent,
        });
        table.SetRows(sites);

        var filter = new Input { Placeholder = "filter by location…" };
        filter.OnChange = query =>
        {
            table.SetRows(sites.Where(site => site.Location.Contains(query, StringComparison.OrdinalIgnoreCase)));
            status.Content = hint;
        };

        var body = new Stack(Direction.Vertical)
            .Add(new Panel(filter), Constraint.Length(3))
            .Add(table, Constraint.Fill())
            .Add(new Padding(status, new Thickness(1, 0)), Constraint.Length(1));
        body.HasFocus = true;
        return Padded(body);
    }

    private static Widget Tree(DemoContext ctx)
    {
        var addrRng = new Random(0xBEEF);
        var tree = new TreeView<TraceFrame>
        {
            RenderLabel = n =>
            {
                var label = StyledText.Of(n.Value.Name).Fg(Theme.Current.Foreground).Append("  ");
                if (n.Value.Addr != 0)
                {
                    label.Append($"0x{n.Value.Addr:x8}").Fg(Theme.Current.Accent).Underline().Link(n.Value.Addr);
                }
                return label;
            },
            ShowHeader = true,
            Striped = true,
            ShowGuides = true,
        };
        tree.Columns.Add(new TreeColumn<TraceFrame>("time", 9,
            n => new StyledText($"{n.Value.Ms:0.0}ms", Theme.Current.MutedStyle)));
        tree.Columns.Add(new TreeColumn<TraceFrame>("self%", 5, n =>
        {
            double p = n.Value.Percent;
            var c = p > 40 ? Theme.Current.Error : p > 15 ? Theme.Current.Warning : Theme.Current.Success;
            return new StyledText($"{p:0}%", new Style(c, Color.Default));
        }));

        TraceFrame F(string name, double ms, double pct) =>
            new(name, ms, pct, 0x7f0000000000L + addrRng.Next(0x1000, 0xfffffff));

        var root = tree.AddRoot(F("HttpServer.HandleRequest", 128.4, 100));
        var router = root.AddChild(F("Router.Match", 12.3, 10));
        router.AddChild(F("Regex.Compile", 8.1, 6));
        router.AddChild(F("Trie.Lookup", 3.9, 3));
        var ctrl = root.AddChild(F("UserController.Get", 96.2, 75));
        var db = ctrl.AddChild(F("Db.Query(users)", 71.5, 56));
        db.AddChild(F("Connection.Open", 12.0, 9));
        var exec = db.AddChild(F("Sql.Execute", 55.1, 43));
        exec.AddChild(F("Network.RoundTrip", 48.0, 37));
        exec.AddChild(F("ResultSet.Read", 7.1, 6));
        var json = ctrl.AddChild(F("Json.Serialize", 21.0, 16));
        json.AddChild(F("Utf8.Encode", 14.1, 11));
        root.AddChild(F("Middleware.Log", 6.7, 5));
        root.ExpandAll();
        tree.MarkDirty();
        tree.HasFocus = true;

        var jumped = new Label(new StyledText("click a 0x… address to jump", Theme.Current.MutedStyle));
        tree.OnLinkClick = p => jumped.Content =
            StyledText.Of($"→ jumped to 0x{(long)p:x8}").Bold().Fg(Theme.Current.Accent);

        return new Stack(Direction.Vertical)
            .Add(new Padding(tree, new Thickness(1, 0)), Constraint.Fill())
            .Add(new Padding(jumped, new Thickness(1, 0)), Constraint.Length(1));
    }

    private static Widget TabsDemo(DemoContext ctx) =>
        Padded(new Tabs()
            .Add("Overview", CenteredNote("Tab 1 — Tab/Shift+Tab, Left/Right, or 1-9 to switch"))
            .Add("Details", CenteredNote("Tab 2 — each tab holds its own widget"))
            .Add("Settings", CenteredNote("Tab 3 — the active tab keeps focus")));

    // ---- Dashboard: many widgets composed on a single page ----

    // A 2×2 grid of panels — table, flamegraph, sparkline, bar chart — plus a proportion strip and a
    // live spinner header. Shows Cellar composing heterogeneous widgets with nested Stacks; the
    // sparkline animates off ctx.Tick like the other live demos.
    private static Widget Dashboard(DemoContext ctx)
    {
        // Top-left: a compact allocation table.
        var table = new Table { ShowHeader = true, Striped = true, SelectedIndex = 0 };
        table.Columns.Add(new Column("Type", Constraint.Fill()));
        table.Columns.Add(new Column("MiB", Constraint.Length(7), Justify.Right));
        table.Columns.Add(new Column("Count", Constraint.Length(7), Justify.Right));
        string[] types = { "String", "Byte[]", "Dictionary", "Task", "Node<T>", "Buffer" };
        var rng = new Random(11);
        for (int i = 0; i < types.Length; i++)
        {
            table.Rows.Add(new[] { types[i], $"{rng.Next(4, 220)}.{rng.Next(0, 9)}", $"{rng.Next(50, 9000)}" });
        }

        // Top-right: a flamegraph.
        var flame = new FlameGraph<int> { LabelSelector = n => n.Label };
        var froot = new FlameNode<int>(0, "root", 1_000_000);
        BuildFlame(froot, 0, 5, 3, froot.Weight, new Random(2));
        flame.Root = froot;

        // Bottom-left: a live sparkline.
        var spark = new Sparkline { BaselineZero = true };
        double v = 0.5;
        var srng = new Random(3);
        void PushSpark() => spark.Push(v = Math.Clamp(v + (srng.NextDouble() - 0.5) * 0.3, 0, 1));
        for (int i = 0; i < 80; i++) PushSpark();
        ctx.Tick += _ => PushSpark();

        // Bottom-right: a bar chart.
        var bar = new BarChart
        {
            Values = { ("heap", 42), ("stack", 18), ("code", 27), ("meta", 9), ("jit", 33) },
            BarColors =
            {
                Color.Hex("#6a9fb5"), Color.Hex("#90a959"), Color.Hex("#f4bf75"),
                Color.Hex("#aa759f"), Color.Hex("#ac4142"),
            },
        };

        // A proportion strip across the bottom.
        var strip = new ProportionBar { ShowInlineLabels = true };
        strip.Segments.Add(new Segment("Gen0", 55, Color.Hex("#6a9fb5")));
        strip.Segments.Add(new Segment("Gen1", 28, Color.Hex("#90a959")));
        strip.Segments.Add(new Segment("Gen2", 17, Color.Hex("#f4bf75")));

        // A live spinner in the header, self-animating (no manual Advance).
        var spinner = new Spinner(SpinnerFrames.Dots, "live") { AutoAnimate = true };

        var topRow = new Stack(Direction.Horizontal)
            .Add(new Panel(table, " Allocations ") { BorderStyle = BorderStyle.Rounded }, Constraint.Fill())
            .Add(new Panel(flame, " Flamegraph ") { BorderStyle = BorderStyle.Rounded }, Constraint.Fill());

        var bottomRow = new Stack(Direction.Horizontal)
            .Add(new Panel(spark, " Throughput ") { BorderStyle = BorderStyle.Rounded }, Constraint.Fill())
            .Add(new Panel(bar, " Segments ") { BorderStyle = BorderStyle.Rounded }, Constraint.Fill());

        var header = new Stack(Direction.Horizontal)
            .Add(new Label(StyledText.Of("Profiler overview").Bold().Fg(Theme.Current.Accent)), Constraint.Fill())
            .Add(spinner, Constraint.Length(10));

        return Padded(new Stack(Direction.Vertical)
            .Add(header, Constraint.Length(1))
            .Add(topRow, Constraint.Fill(2))
            .Add(bottomRow, Constraint.Fill(2))
            .Add(new Panel(strip, " GC heaps ") { BorderStyle = BorderStyle.Rounded }, Constraint.Length(3)));
    }

    // ---- Async loading ----

    // Wraps each tab's expensive content in an AsyncContent: the tab paints an animated spinner on
    // its very first frame (no UI freeze), the factory runs off-thread honoring its CancellationToken
    // (simulated here with a wait), then the real widget swaps in. Switching away before it finishes
    // leaves the work running harmlessly off-thread; quitting cancels it.
    private static Widget AsyncLoad(DemoContext ctx) =>
        Padded(new Tabs()
            .Add("Fast (0.8s)", () => new AsyncContent(ct => BuildAfterDelay(ct, 800,
                "Fast task", "Loaded after a short delay.")))
            .Add("Slow (2.5s)", () => new AsyncContent(ct => BuildAfterDelay(ct, 2500,
                "Slow task", "A longer job — the spinner keeps animating and input stays live.")))
            .Add("Fails", () => new AsyncContent((Func<System.Threading.CancellationToken, Widget>)(ct =>
            {
                ct.WaitHandle.WaitOne(1000);
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("the factory threw — this is the error widget");
            }))));

    // Simulates an expensive off-thread task: waits (respecting cancellation), then returns a real
    // widget. Runs on a background thread — the UI stays responsive throughout.
    private static Widget BuildAfterDelay(System.Threading.CancellationToken ct, int ms, string title, string body)
    {
        // Cooperative wait: bail out promptly if the token is cancelled (tab removed / app quit).
        if (ct.WaitHandle.WaitOne(ms))
        {
            ct.ThrowIfCancellationRequested();
        }
        return new Stack(Direction.Vertical)
            .Add(new Label(StyledText.Of(title).Bold().Fg(Theme.Current.Accent)), Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Length(1))
            .Add(new Label(new StyledText(body, Theme.Current.TextStyle), Justify.Left) { Wrap = true }, Constraint.Fill());
    }

    // ---- Charts (live) ----

    private static Widget LineChartDemo(DemoContext ctx)
    {
        var chart = new LineChart { UseBraille = true };
        var sine = new Series("sin", Color.Hex("#6a9fb5"));
        var cosine = new Series("cos", Color.Hex("#90a959"));
        var walk = new Series("walk", Color.Hex("#aa759f"));
        chart.Series.Add(sine);
        chart.Series.Add(cosine);
        chart.Series.Add(walk);
        double t = 0;
        var rng = new Random(3);
        double last = 0;
        void Seed()
        {
            for (int i = 0; i < 120; i++) Step();
        }
        void Step()
        {
            t += 0.25;
            sine.Add(t, Math.Sin(t));
            cosine.Add(t, Math.Cos(t * 0.7));
            last = Math.Clamp(last + (rng.NextDouble() - 0.5) * 0.4, -1.2, 1.2);
            walk.Add(t, last);
            double cutoff = t - 30;
            sine.TrimBefore(cutoff);
            cosine.TrimBefore(cutoff);
            walk.TrimBefore(cutoff);
        }
        Seed();
        ctx.Tick += _ => Step();

        var legend = new Label(StyledText.Of("● sin ").Fg(Color.Hex("#6a9fb5"))
            .Append("● cos ").Fg(Color.Hex("#90a959"))
            .Append("● walk").Fg(Color.Hex("#aa759f")));
        return new Stack(Direction.Vertical)
            .Add(new Padding(legend, new Thickness(1, 0)), Constraint.Length(1))
            .Add(new Padding(chart, new Thickness(1, 0)), Constraint.Fill());
    }

    private static Widget SparklineDemo(DemoContext ctx)
    {
        var spark = new Sparkline { BaselineZero = true };
        var rng = new Random(11);
        double v = 0.5;
        for (int i = 0; i < 80; i++) spark.Push(v = Math.Clamp(v + (rng.NextDouble() - 0.5) * 0.3, 0, 1));
        ctx.Tick += _ => spark.Push(v = Math.Clamp(v + (rng.NextDouble() - 0.5) * 0.3, 0, 1));
        return new Stack(Direction.Vertical)
            .Add(CenteredNote("a compact rolling series — great for inline metrics"), Constraint.Fill())
            .Add(new Padding(spark, new Thickness(1, 0)), Constraint.Length(4))
            .Add(new Label(StyledText.Empty()), Constraint.Fill());
    }

    private static Widget BarChartDemo(DemoContext ctx)
    {
        var bar = new BarChart
        {
            Values = { ("heap", 42), ("stack", 18), ("code", 27), ("metadata", 9), ("jit", 33), ("threads", 14) },
            BarColors =
            {
                Color.Hex("#6a9fb5"), Color.Hex("#90a959"), Color.Hex("#f4bf75"),
                Color.Hex("#aa759f"), Color.Hex("#ac4142"), Color.Hex("#75b5aa"),
            },
        };
        return Padded(bar);
    }

    private static Widget Flamegraph(DemoContext ctx)
    {
        var flame = new FlameGraph<int> { LabelSelector = n => n.Label };
        var root = new FlameNode<int>(0, "root", 1_000_000);
        BuildFlame(root, 0, 7, 3, root.Weight, new Random(1));
        flame.Root = root;
        flame.HasFocus = true;
        return Padded(flame);
    }

    private static void BuildFlame(FlameNode<int> node, int depth, int maxDepth, int fanout, double weight, Random rng)
    {
        if (depth >= maxDepth) return;
        double remaining = weight * 0.9;
        for (int i = 0; i < fanout; i++)
        {
            double w = remaining / fanout;
            var child = node.Add(depth * 10 + i, Method(rng), w);
            BuildFlame(child, depth + 1, maxDepth, fanout, w, rng);
        }
    }

    private static readonly string[] MethodNames =
        { "Parse", "Read", "Write", "Encode", "Decode", "Match", "Hash", "Compile", "Serialize", "Lookup" };

    private static string Method(Random rng) => MethodNames[rng.Next(MethodNames.Length)];

    private static Widget Proportion(DemoContext ctx)
    {
        Color[] palette =
        {
            Color.Hex("#6a9fb5"), Color.Hex("#90a959"), Color.Hex("#f4bf75"),
            Color.Hex("#aa759f"), Color.Hex("#ac4142"),
        };
        (string, double)[] parts = { ("String", 42), ("Byte[]", 23), ("Dictionary", 15), ("Node", 12), ("other", 8) };
        var bar = new ProportionBar { ShowInlineLabels = true };
        var legend = new Legend { Horizontal = true };
        double total = 0;
        foreach (var (_, v) in parts)
        {
            total += v;
        }

        for (int i = 0; i < parts.Length; i++)
        {
            bar.Segments.Add(new Segment(parts[i].Item1, parts[i].Item2, palette[i]));
            legend.Items.Add(new LegendItem(parts[i].Item1, palette[i], $"{100 * parts[i].Item2 / total:0}%"));
        }
        return new Stack(Direction.Vertical)
            .Add(new Label(StyledText.Of("heap composition").Bold().Fg(Theme.Current.Accent)), Constraint.Length(1))
            .Add(new Padding(bar, new Thickness(1, 0)), Constraint.Length(1))
            .Add(new Padding(legend, new Thickness(1, 0)), Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Fill());
    }

    // ---- Live indicators ----

    private static Widget Progress(DemoContext ctx)
    {
        var download = new ProgressBar { LabelPlacement = LabelPlacement.Right, LabelType = ProgressLabel.Bytes, Total = 10_000_000, LabelWidth = 20 };
        var cpu = new ProgressBar { LabelPlacement = LabelPlacement.Right, LabelType = ProgressLabel.Percent };
        var mem = new ProgressBar { LabelPlacement = LabelPlacement.Right, LabelType = ProgressLabel.RateEta, Total = 100, LabelWidth = 22 };
        ctx.Tick += phase =>
        {
            double p = (phase * 0.15) % 1.0;
            download.Current = p * download.Total;
            cpu.Value = (p * 1.7) % 1.0;
            mem.Current = (0.3 + 0.4 * Math.Abs(Math.Sin(phase))) * mem.Total;
            mem.Rate = 500_000 + 1_500_000 * p;
        };
        return new Stack(Direction.Vertical)
            .Add(LabeledBar("download", download), Constraint.Length(1))
            .Add(LabeledBar("cpu", cpu), Constraint.Length(1))
            .Add(LabeledBar("memory", mem), Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Fill());
    }

    private static Widget Spinners(DemoContext ctx)
    {
        var spinners = new[]
        {
            new Spinner(SpinnerFrames.Dots, "Dots"),
            new Spinner(SpinnerFrames.Line, "Line"),
            new Spinner(SpinnerFrames.Arc, "Arc"),
            new Spinner(SpinnerFrames.Bounce, "Bounce"),
            new Spinner(SpinnerFrames.Circle, "Circle"),
        };
        ctx.Tick += _ => { foreach (var s in spinners)
            {
                s.Advance();
            }
        };
        var row = new Stack(Direction.Horizontal);
        foreach (var s in spinners)
        {
            row.Add(new Padding(s, new Thickness(1, 0)), Constraint.Fill());
        }

        return new Stack(Direction.Vertical)
            .Add(new Label(StyledText.Empty()), Constraint.Fill())
            .Add(new Padding(row, new Thickness(1, 0)), Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Fill());
    }

    // ---- Input / layout / text ----

    private static Widget Input(DemoContext ctx)
    {
        var name = new Input { Placeholder = "type your name…" };
        var secret = new Input { Placeholder = "password", Mask = '•' };
        var echo = new Label(new StyledText("(your input echoes here)", Theme.Current.MutedStyle));
        name.OnChange = v => echo.Content = v.Length == 0
            ? new StyledText("(your input echoes here)", Theme.Current.MutedStyle)
            : StyledText.Of("Hello, ").Fg(Theme.Current.Foreground).Append(v).Bold().Fg(Theme.Current.Accent).Append("!").Fg(Theme.Current.Foreground);

        string[] types =
        {
            "System.String", "System.Byte[]", "System.Int32[]", "Dictionary<string,object>",
            "List<Frame>", "HashSet<long>", "StringBuilder", "System.Uri", "MemoryStream",
        };
        var filter = new Input { Placeholder = "filter types…" };
        var results = new Label(StyledText.Empty()) { Wrap = true };
        void Refilter(string q)
        {
            var st = StyledText.Empty();
            int shown = 0;
            foreach (var ty in types)
            {
                if (q.Length > 0 && ty.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                st.Append((shown++ > 0 ? "\n" : "") + "• ").Fg(Theme.Current.Muted).Append(ty).Fg(Theme.Current.Foreground);
            }
            results.Content = shown == 0 ? new StyledText("no matches", Theme.Current.MutedStyle) : st;
        }
        filter.OnChange = Refilter;
        Refilter("");

        var body = new Stack(Direction.Vertical)
            .Add(new Label(new StyledText("Name:", Theme.Current.MutedStyle)), Constraint.Length(1))
            .Add(new Panel(name), Constraint.Length(3))
            .Add(new Label(new StyledText("Password (masked):", Theme.Current.MutedStyle)), Constraint.Length(1))
            .Add(new Panel(secret), Constraint.Length(3))
            .Add(echo, Constraint.Length(1))
            .Add(new Rule(StyledText.Of("live filter (Tab to switch fields)")), Constraint.Length(1))
            .Add(new Panel(filter), Constraint.Length(3))
            .Add(new Padding(results, new Thickness(1, 0)), Constraint.Fill());
        body.HasFocus = true;
        return Padded(body);
    }

    // Every Input understands the readline keys, and browses an InputHistory with ↑/↓ when given one.
    private static Widget ReadlineInput(DemoContext ctx)
    {
        var history = new InputHistory();
        history.Load(["get nodes", "describe node _Node_3", "logs --tail 100 --follow"]);
        var log = new Label(new StyledText("(submitted lines show here)", Theme.Current.MutedStyle)) { Wrap = true };
        var lines = new List<string>();
        var shell = new Input { Placeholder = "type a command, enter submits, ↑ recalls…", History = history };
        shell.OnSubmit = text =>
        {
            lines.Insert(0, "❯ " + text);
            log.Content = new StyledText(string.Join("\n", lines.Take(8)), Theme.Current.TextStyle);
            shell.Text = "";
        };

        var keys = StyledText.Empty();
        foreach (var (key, what) in new[]
        {
            ("ctrl+a / ctrl+e", "start / end of line"),
            ("alt+b / alt+f, ctrl+← / ctrl+→", "word left / right"),
            ("ctrl+w", "delete back to whitespace"),
            ("alt+backspace / alt+d", "delete word back / forward"),
            ("ctrl+u / ctrl+k", "delete to start / end"),
            ("ctrl+y", "paste what was deleted"),
            ("↑ / ↓", "browse history (keeps what you were typing)"),
        })
        {
            keys.Append(key.PadRight(34)).Bold().Fg(Theme.Current.Accent).Append(what + "\n").Fg(Theme.Current.Muted);
        }

        var body = new Stack(Direction.Vertical)
            .Add(new Panel(shell, "shell"), Constraint.Length(3))
            .Add(new Padding(new Label(keys) { Wrap = true }, new Thickness(1, 1)), Constraint.Length(9))
            .Add(new Rule(StyledText.Of("submitted")), Constraint.Length(1))
            .Add(new Padding(log, new Thickness(1, 0)), Constraint.Fill());
        body.HasFocus = true;
        return Padded(body);
    }

    // A Keymap of AppActions: the keys below, the help screen (?) and the palette (ctrl+o here) all come from
    // the same declarations. A page scope rebinds a key while it's switched on.
    private static Widget KeymapDemo(DemoContext ctx)
    {
        var ran = new List<string>();
        var log = new Label(StyledText.Empty()) { Wrap = true };
        void Ran(string what)
        {
            ran.Insert(0, $"{DateTime.Now:HH:mm:ss}  {what}");
            log.Content = new StyledText(string.Join("\n", ran.Take(12)), Theme.Current.TextStyle);
        }

        var keymap = new Keymap();
        keymap.Add(new AppAction("refresh", "Refresh", () => Ran("refresh")) { Keys = ["r", "f5"], Commands = ["refresh"], Footer = "refresh" });
        keymap.Add(new AppAction("top", "Go to the top", () => Ran("top (a two-key sequence)")) { Group = "Movement", Keys = ["g g"] });
        keymap.Add(new AppAction("bottom", "Go to the bottom", () => Ran("bottom")) { Group = "Movement", Keys = ["G"] });
        keymap.Add(new AppAction("describe", "Describe", () => Ran("describe (app scope)")) { Group = "Row", Keys = ["e"] });
        keymap.Add(new AppAction("help", "Show these keys", () => keymap.ShowHelp(ctx.App!)) { Keys = ["?"], When = () => ctx.App is not null });
        keymap.Add(new AppAction("palette", "Command palette", () => keymap.ShowPalette(ctx.App!)) { Keys = ["ctrl+o"], When = () => ctx.App is not null, ShowInPalette = false });

        bool pageOn = false;
        var page = keymap.AddScope("page", KeymapScope.PagePrecedence, () => pageOn);
        page.Add(new AppAction("edit", "Edit (page scope, takes e)", () => Ran("edit (page scope)")) { Group = "Row", Keys = ["e"] });
        keymap.Add(new AppAction("scope", "Toggle the page scope", () => { pageOn = !pageOn; Ran(pageOn ? "page scope on: e edits" : "page scope off: e describes"); }) { Keys = ["s"] });

        var help = keymap.CreateHelp();
        var body = new Stack(Direction.Horizontal)
            .Add(new Panel(new KeyHost(log, keymap, help.Refresh), "what ran"), Constraint.Fill())
            .Add(new Panel(help, "generated help"), Constraint.Fill());
        Ran("press r, g g, G, e, s, ? or ctrl+o");
        return Padded(body);
    }

    // A StatusLine: items left and right that shorten and then drop by priority as the line narrows, key hints
    // generated from a Keymap (click them), a mode badge, a spinner, and messages that take the line over.
    private static Widget StatusLineDemo(DemoContext ctx)
    {
        var keymap = new Keymap();
        var wide = new StatusLine { Keymap = keymap, ErrorHint = new StyledText("d dismiss", Theme.Current.MutedStyle) };
        var narrow = new StatusLine { Keymap = keymap };
        var lines = new[] { wide, narrow };

        var normal = StatusItem.Badge("NORMAL");
        var insert = StatusItem.Badge("INSERT", Theme.Current.Success);
        insert.Visible = false;
        var spinners = new List<SpinnerStatusItem>();
        foreach (var line in lines)
        {
            line.Add(normal);
            line.Add(insert);
            line.Add(new StatusItem(new StyledText("cluster ", Theme.Current.MutedStyle).Append("prod-west"), priority: 50) { ShortText = "prod-west" });
            line.Add(new StatusItem("42 nodes", priority: 5) { ShortText = "42n" });
            spinners.Add((SpinnerStatusItem)line.Add(new SpinnerStatusItem("refreshing", priority: 20)));
        }

        void Each(Action<StatusLine> act)
        {
            foreach (var line in lines)
            {
                act(line);
            }
        }

        keymap.Add(new AppAction("info", "Show an info message", () => Each(l => l.Show("Saved 3 settings"))) { Keys = ["i"], Footer = "info", Priority = 5 });
        keymap.Add(new AppAction("error", "Show an error", () => Each(l => l.ShowError("Couldn't reach the cluster: connection refused"))) { Keys = ["e"], Footer = "error", Priority = 4 });
        keymap.Add(new AppAction("warn", "Show a warning", () => Each(l => l.Show("2 nodes are down", StatusLevel.Warning))) { Keys = ["w"], Footer = "warn", Priority = 3 });
        keymap.Add(new AppAction("dismiss", "Dismiss the message", () => Each(l => l.Dismiss())) { Keys = ["d"] });
        keymap.Add(new AppAction("spin", "Toggle the spinner", () => spinners.ForEach(s => s.Active = !s.Active)) { Keys = ["s"], Footer = "spinner", Priority = 2 });
        keymap.Add(new AppAction("mode", "Toggle the mode", () => (normal.Visible, insert.Visible) = (insert.Visible, normal.Visible)) { Keys = ["m"], Footer = "mode", Priority = 1 });

        var note = new Label(new StyledText(
            "Press i, w or e for messages (info and warnings fade after 5s, errors stay until d), s for the spinner, " +
            "m to switch the mode badge. The key hints on the right come from the Keymap and are clickable.\n\n" +
            "The second line is narrower: items fall back to their short text from the lowest priority up (hints keep " +
            "just their keys), and when that isn't enough the least important drop out.", Theme.Current.TextStyle)) { Wrap = true };

        var body = new Stack(Direction.Vertical)
            .Add(new KeyHost(note, keymap), Constraint.Fill())
            .Add(new Rule(StyledText.Of("full width")), Constraint.Length(1))
            .Add(wide, Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Length(1))
            .Add(new Rule(StyledText.Of("44 columns")), Constraint.Length(1))
            .Add(new Stack(Direction.Horizontal).Add(narrow, Constraint.Length(44)).Add(new Label(StyledText.Empty()), Constraint.Fill()), Constraint.Length(1));
        return Padded(body);
    }

    // A CommandLine sharing the bottom row with a StatusLine: ":" runs the keymap's commands (tab completes names
    // and node arguments), "/" filters as you type, "r" asks for a new name and refuses invalid ones.
    private static Widget CommandLineDemo(DemoContext ctx)
    {
        var nodes = new List<string> { "node-east-01", "node-east-02", "node-west-01", "node-west-02", "node-west-03", "gpu-pool-01", "gpu-pool-02", "ingress-01" };
        string filter = "";
        var list = new Table { ShowHeader = false, ShowScrollbar = true, HasFocus = true };
        list.Columns.Add(new Column("", Constraint.Fill()));

        var keymap = new Keymap();
        var line = new CommandLine();
        var count = line.Status.Add(new StatusItem("", priority: 10));
        line.Status.Keymap = keymap;

        void Reload(string? select = null)
        {
            string? current = select ?? (list.SelectedIndex >= 0 && list.SelectedIndex < list.Rows.Count ? list.Rows[list.SelectedIndex][0] : null);
            list.Rows.Clear();
            foreach (var n in nodes.Where(n => n.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                list.Rows.Add(new[] { n });
            }

            int at = current is null ? -1 : list.Rows.FindIndex(r => r[0] == current);
            list.SelectedIndex = list.Rows.Count == 0 ? -1 : Math.Max(0, at);
            count.Text = new StyledText($"{list.Rows.Count} of {nodes.Count} nodes", Theme.Current.MutedStyle)
                .Append(filter.Length > 0 ? $"  /{filter}" : "", Theme.Current.AccentStyle);
        }

        string? Selected() => list.SelectedIndex >= 0 && list.SelectedIndex < list.Rows.Count ? list.Rows[list.SelectedIndex][0] : null;

        var commands = line.CommandsFor(keymap);
        ((KeymapCompletionSource)commands.Completion!).Arguments = (action, typed) => action.Id == "goto" ? nodes : [];
        var filterMode = new CommandMode("/")
        {
            History = new InputHistory(),
            Placeholder = "filter nodes…",
            OnChange = text => { filter = text; Reload(); },
            OnCancel = () => { filter = ""; Reload(); },
        };

        string? ValidName(string name, string? except) =>
            string.IsNullOrWhiteSpace(name) ? "a name is required"
            : name.Contains(' ', StringComparison.Ordinal) ? "no spaces"
            : name != except && nodes.Contains(name) ? $"{name} already exists"
            : null;

        keymap.Add(new AppAction("cmd", "Command", () => line.Open(commands)) { Keys = [":"], Footer = "cmd", Priority = 3, ShowInPalette = false });
        keymap.Add(new AppAction("filter", "Filter nodes", () => line.Open(filterMode, filter)) { Keys = ["/"], Footer = "filter", Priority = 2 });
        keymap.Add(new AppAction("rename", "Rename the node", () =>
        {
            if (Selected() is not { } old)
            {
                return;
            }

            line.Ask("Rename to:", old, name => ValidName(name, old), name =>
            {
                nodes[nodes.IndexOf(old)] = name;
                Reload(name);
                line.Status.Show($"Renamed {old} to {name}", StatusLevel.Success);
            });
        }) { Keys = ["r"], Commands = ["rename"], Footer = "rename", Priority = 1, When = () => Selected() is not null });
        keymap.Add(new AppAction("add", "Add a node", name =>
        {
            string? problem = name is null ? "usage: add <name>" : ValidName(name, null);
            if (problem is not null)
            {
                line.Status.ShowError(problem);
                return;
            }

            nodes.Add(name!);
            Reload(name);
        }) { Commands = ["add"], ArgumentHint = "<name>" });
        keymap.Add(new AppAction("goto", "Select a node", name =>
        {
            filter = "";
            Reload(name);
        }) { Commands = ["goto", "go"], ArgumentHint = "<node>" });
        keymap.Add(new AppAction("delete", "Delete the node", () =>
        {
            nodes.Remove(Selected()!);
            Reload();
        }) { Commands = ["delete", "rm"], When = () => Selected() is not null });

        Reload();
        ctx.Intercept = line.Intercept;
        line.Status.Show("Try :add db-01, :go <tab>, /west, or r to rename", StatusLevel.Info, TimeSpan.FromSeconds(30));
        var body = new Stack(Direction.Vertical)
            .Add(new Panel(list, " nodes "), Constraint.Fill())
            .Add(line, Constraint.Length(1));
        return Padded(new KeyHost(body, keymap));
    }

    // Table<T> as a live task queue: rows refresh every two seconds and keep the selection by RowKey (changed
    // cells flash bold), "/" filters as you type, "g" groups by state, space and * mark rows.
    private static Widget TableViews(DemoContext ctx)
    {
        var rng = new Random(11);
        string[] states = ["Running", "Running", "Done", "Queued", "Failed"];
        string[] kinds = ["backup", "export", "resize", "sync"];
        var tasks = Enumerable.Range(0, 40).Select(i => new DemoTask(
            $"{kinds[i % kinds.Length]}-{i:00}",
            states[rng.Next(states.Length)], rng.Next(0, 6), TimeSpan.FromMinutes(rng.Next(1, 600)))).ToList();

        var table = new Table<DemoTask>
        {
            ShowHeader = true, Striped = true, ShowScrollbar = true, Sortable = true, HasFocus = true,
            RowKey = t => t.Name, HighlightChanges = true, Markable = true,
            EmptyText = new StyledText("no tasks match"),
            GroupSummary = g => new StyledText($"{g.Rows.Sum(t => t.Attempts)} attempts", Theme.Current.MutedStyle),
        };
        static Style StateStyle(string state) => new(state switch
        {
            "Running" => Theme.Current.Info,
            "Done" => Theme.Current.Success,
            "Queued" => Theme.Current.Warning,
            _ => Theme.Current.Error,
        }, Color.Default);
        table.Columns.Add(new TableColumn<DemoTask>("Name", Constraint.Fill(2), t => t.Name));
        table.Columns.Add(new TableColumn<DemoTask>("State", Constraint.Length(9), t => new StyledText(t.State, StateStyle(t.State))));
        table.Columns.Add(new TableColumn<DemoTask>("Attempts", Constraint.Length(9), t => t.Attempts.ToString(CultureInfo.InvariantCulture), Justify.Right) { SortKey = t => t.Attempts });
        table.Columns.Add(new TableColumn<DemoTask>("Age", Constraint.Length(8), t => FormatAge(t.Age), Justify.Right) { SortKey = t => t.Age });
        table.SetRows(tasks);

        var keymap = new Keymap();
        var line = new CommandLine();
        var count = line.Status.Add(new StatusItem("", priority: 10));
        line.Status.Keymap = keymap;
        string query = "";
        void UpdateCount() => count.Text = new StyledText($"{table.FilteredRows.Count}/{tasks.Count} tasks", Theme.Current.MutedStyle)
            .Append(table.Marked.Count > 0 ? $"  {table.Marked.Count} marked" : "", Theme.Current.AccentStyle)
            .Append(query.Length > 0 ? $"  /{query}" : "", Theme.Current.AccentStyle);
        table.OnMarksChanged = UpdateCount;

        void SetFilter(string text)
        {
            query = text;
            table.Filter = text.Length == 0 ? null : t => table.ContainsText(t, text);
            UpdateCount();
        }

        var filter = new CommandMode("/")
        {
            History = new InputHistory(),
            Placeholder = "text in any column, e.g. failed or backup",
            OnChange = SetFilter,
            OnCancel = () => SetFilter(""),
        };
        keymap.Add(new AppAction("filter", "Filter", () => line.Open(filter, query)) { Keys = ["/"], Footer = "filter", Priority = 4 });
        keymap.Add(new AppAction("group", "Group by state", () =>
        {
            if (table.GroupColumn < 0)
            {
                table.GroupBy("State");
            }
            else
            {
                table.Ungroup();
            }
        }) { Keys = ["g"], Footer = "group", Priority = 3 });
        keymap.Add(new AppAction("mark", "Mark", () =>
        {
            if (table.Selected is { } task)
            {
                table.ToggleMark(task);
            }
        }) { Keys = ["space"], Footer = "mark", Priority = 2 });
        keymap.Add(new AppAction("delete", "Remove marked", () =>
        {
            var doomed = table.Marked.Select(t => t.Name).ToHashSet();
            tasks.RemoveAll(t => doomed.Contains(t.Name));
            table.SetRows(tasks);
            line.Status.Show($"Removed {doomed.Count} tasks", StatusLevel.Success);
            UpdateCount();
        }) { Keys = ["ctrl+d"], Footer = "remove marked", Priority = 1, When = () => table.Marked.Count > 0 });

        // Every two seconds a few tasks retry or change state, and now and then one finishes and a new one queues.
        int ticks = 0;
        ctx.Tick += _ =>
        {
            if (++ticks % 25 != 0)
            {
                return;
            }

            for (int i = 0; i < 3 && tasks.Count > 0; i++)
            {
                int at = rng.Next(tasks.Count);
                tasks[at] = tasks[at] with { Attempts = tasks[at].Attempts + 1, State = states[rng.Next(states.Length)] };
            }

            if (rng.Next(3) == 0 && tasks.Count > 0)
            {
                tasks.RemoveAt(rng.Next(tasks.Count));
                tasks.Add(new DemoTask($"upload-{ticks / 25:000}", "Queued", 0, TimeSpan.Zero));
            }

            tasks = tasks.Select(t => t with { Age = t.Age + TimeSpan.FromSeconds(2) }).ToList();
            table.SetRows(tasks);
            UpdateCount();
        };

        UpdateCount();
        ctx.Intercept = line.Intercept;
        var body = new Stack(Direction.Vertical)
            .Add(table, Constraint.Fill())
            .Add(line, Constraint.Length(1));
        return Padded(new KeyHost(body, keymap));
    }

    private static string FormatAge(TimeSpan age) => age.TotalHours >= 1
        ? $"{(int)age.TotalHours}h{age.Minutes:00}m"
        : $"{age.Minutes}m{age.Seconds:00}s";

    // A Form embedded in the page, and the same fields in a FormDialog on "o". Submitting shows what changed.
    private static Widget FormDemo(DemoContext ctx)
    {
        static List<FormField> Fields() =>
        [
            new FormField("service", "Service", "fabric:/shop/web") { ReadOnly = true, Section = "Service", Hint = "the service these settings apply to" },
            new FormField("instances", "Instances", "3") { Kind = FormFieldKind.Number, Min = 1, Max = 50, Integer = true, Section = "Service", Hint = "how many instances run, 1 to 50" },
            new FormField("placement", "Placement", "spread") { Choices = ["spread", "pack", "zone-aware"], Section = "Service", Hint = "←/→ or enter to pick" },
            new FormField("grace", "Close grace", "30s") { Kind = FormFieldKind.Duration, Note = "default 30s", Section = "Upgrade", Hint = "how long an instance gets to close: 90s, 5m, 1h30m" },
            new FormField("monitored", "Monitored", "true") { Kind = FormFieldKind.Flag, Section = "Upgrade", Hint = "roll back when health checks fail" },
            new FormField("token", "Deploy token", "s3cr3t-t0ken") { Kind = FormFieldKind.Secret, Section = "Upgrade", Hint = "never shown, also not while typing" },
            new FormField("owner", "Owner", "team-web") { Section = "Upgrade", Validate = v => v.Contains(' ') ? "no spaces" : null },
        ];

        var result = new Label(new StyledText("Nothing submitted yet.", Theme.Current.MutedStyle)) { Wrap = true };
        void Report(IReadOnlyDictionary<string, string> changes) =>
            result.Content = new StyledText("Submitted: ", Theme.Current.TextStyle)
                .Append(string.Join(", ", changes.Select(c => $"{c.Key}={c.Value}")), Theme.Current.AccentStyle);

        var form = new Form(Fields()) { HasFocus = true };
        form.OnSubmit = changes =>
        {
            Report(changes);
            foreach (var field in form.Fields)
            {
                field.Accept();
            }
        };
        form.OnCancel = () =>
        {
            form.RevertAll();
            result.Content = new StyledText("Cancelled: changes discarded.", Theme.Current.MutedStyle);
        };

        var keymap = new Keymap();
        keymap.Add(new AppAction("open", "Open as a dialog", () =>
        {
            if (ctx.App is { } app)
            {
                FormDialog.Show(app, "web settings", Fields(), Report,
                    () => result.Content = new StyledText("Dialog cancelled.", Theme.Current.MutedStyle));
            }
        }) { Keys = ["o"] });

        var note = new Label(new StyledText(
            "↑/↓ move, enter edits in place (choices and flags cycle, as do ←/→ and space), u reverts a field, " +
            "ctrl+s or a applies the changed values only, esc cancels (twice when there are changes). " +
            "While editing, the readline keys work and enter refuses invalid values.\n\n" +
            "Press o for the same fields in a FormDialog.", Theme.Current.TextStyle)) { Wrap = true };

        var right = new Stack(Direction.Vertical)
            .Add(note, Constraint.Length(10))
            .Add(result, Constraint.Fill());
        var body = new Stack(Direction.Horizontal)
            .Add(new KeyHost(new Panel(form, "settings") { BorderStyle = BorderStyle.Rounded }, keymap), Constraint.Fill(3))
            .Add(new Label(StyledText.Empty()), Constraint.Length(2))
            .Add(right, Constraint.Fill(2));
        return Padded(body);
    }

    // Previews of both kinds of ConfirmDialog; r and d open them for real.
    private static Widget ConfirmDemo(DemoContext ctx)
    {
        static ConfirmDialog Restart() => new("Restart web on node-west-02?")
        {
            Details = [new StyledText("3 instances restart one at a time; requests drain first.", Theme.Current.TextStyle)],
            Toggles = [new ConfirmToggle('f', "force: skip draining")],
        };

        static ConfirmDialog Delete() => new("Delete node node-west-02?")
        {
            Details = [new StyledText("Its 12 replicas move to other nodes first.", Theme.Current.TextStyle)],
            Warning = "This can't be undone.",
            Risk = ConfirmRisk.Destructive,
            ConfirmText = "node-west-02",
            Toggles = [new ConfirmToggle('f', "force: don't wait for replicas")],
        };

        var result = new Label(new StyledText("Press r or d.", Theme.Current.MutedStyle));
        void Report(string what, ConfirmResult r) =>
            result.Content = r.Confirmed
                ? new StyledText($"{what}: confirmed", new Style(Theme.Current.Success, Color.Default)).Append(r.IsOn('f') ? " (forced)" : "", Theme.Current.MutedStyle)
                : new StyledText($"{what}: cancelled", Theme.Current.MutedStyle);

        var keymap = new Keymap();
        keymap.Add(new AppAction("restart", "Ask to restart", () =>
        {
            if (ctx.App is { } app)
            {
                ConfirmDialog.Show(app, Restart(), r => Report("restart", r));
            }
        }) { Keys = ["r"] });
        keymap.Add(new AppAction("delete", "Ask to delete", () =>
        {
            if (ctx.App is { } app)
            {
                ConfirmDialog.Show(app, Delete(), r => Report("delete", r));
            }
        }) { Keys = ["d"] });

        var restart = Restart();
        var delete = Delete();
        var note = new Label(new StyledText(
            "r asks with y/n (f toggles force); d makes you type the node's name (alt+f toggles force). " +
            "Esc cancels either. Below: what they look like.", Theme.Current.TextStyle)) { Wrap = true };
        var previews = new Stack(Direction.Horizontal)
            .Add(restart, Constraint.Length(48))
            .Add(new Label(StyledText.Empty()), Constraint.Length(2))
            .Add(delete, Constraint.Length(48))
            .Add(new Label(StyledText.Empty()), Constraint.Fill());
        var body = new Stack(Direction.Vertical)
            .Add(new KeyHost(note, keymap), Constraint.Length(3))
            .Add(result, Constraint.Length(2))
            .Add(previews, Constraint.Length(Math.Max(restart.PreferredHeight(48), delete.PreferredHeight(48))))
            .Add(new Label(StyledText.Empty()), Constraint.Fill());
        return Padded(body);
    }

    private static Widget Panels(DemoContext ctx) =>
        Padded(new Stack(Direction.Horizontal)
            .Add(new Panel(CenteredNote("Rounded"), "rounded") { BorderStyle = BorderStyle.Rounded }, Constraint.Fill())
            .Add(new Panel(CenteredNote("Single"), "single") { BorderStyle = BorderStyle.Single }, Constraint.Fill())
            .Add(new Panel(CenteredNote("Double"), "double") { BorderStyle = BorderStyle.Double }, Constraint.Fill())
            .Add(new Panel(CenteredNote("Thick"), "thick") { BorderStyle = BorderStyle.Thick }, Constraint.Fill()));

    private static Widget OverlayDemo(DemoContext ctx) =>
        CenteredNote("Press ^P for a real modal palette — overlays dim the content beneath and\ncapture input until dismissed.");

    private static Widget TextDemo(DemoContext ctx)
    {
        var wrapped = new Label(StyledText.Of(
            "Cellar renders ").Fg(Theme.Current.Foreground)
            .Append("styled").Bold().Fg(Theme.Current.Accent)
            .Append(", ").Fg(Theme.Current.Foreground)
            .Append("multi-color").Fg(Theme.Current.Success)
            .Append(" text that word-wraps to any width. Wide glyphs 你好 and combining marks é are measured correctly, so alignment always holds.")
            .Fg(Theme.Current.Foreground)) { Wrap = true };

        // A clickable link embedded in a text line, with its own custom hover style.
        var status = new Label(new StyledText("(click the link)", Theme.Current.MutedStyle));
        var linkLine = new Label(
            StyledText.Of("Links live inside text too — visit ").Fg(Theme.Current.Foreground)
                .Append("the docs").Fg(Theme.Current.Accent).Underline()
                    .Link("docs", new Style(Theme.Current.Background, Theme.Current.Accent))
                .Append(" or ").Fg(Theme.Current.Foreground)
                .Append("the repo").Fg(Theme.Current.Info).Underline()
                    .Link("repo", new Style(Theme.Current.Background, Theme.Current.Info))
                .Append(".").Fg(Theme.Current.Foreground));
        linkLine.OnLinkClick = p => status.Content =
            StyledText.Of($"→ opened: {p}").Bold().Fg(Theme.Current.Accent);

        return new Stack(Direction.Vertical)
            .Add(new Rule(StyledText.Of("word wrap")), Constraint.Length(1))
            .Add(new Padding(wrapped, new Thickness(1, 1)), Constraint.Length(6))
            .Add(new Rule(StyledText.Of("inline links (hover + click)")), Constraint.Length(1))
            .Add(new Padding(linkLine, new Thickness(1, 0)), Constraint.Length(1))
            .Add(new Padding(status, new Thickness(1, 0)), Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Fill());
    }

    private static Widget Gradient(DemoContext ctx) =>
        Padded(new Stack(Direction.Vertical)
            .Add(new Label(StyledText.Empty()), Constraint.Length(1))
            .Add(new GradientBar("█", Color.Hex("#ac4142"), Color.Hex("#f4bf75"), Color.Hex("#90a959")), Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Length(1))
            .Add(new GradientBar("█", Color.Hex("#6a9fb5"), Color.Hex("#aa759f")), Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Length(1))
            .Add(new GradientBar("█", Color.Hsl(0, 0.7, 0.5), Color.Hsl(120, 0.7, 0.5), Color.Hsl(240, 0.7, 0.5)), Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Fill()));

    // ---- helpers ----

    // ---- Custom (composed) widgets: not part of the framework, shown to demonstrate that a widget
    // is just Render(surface, area). Their source lives in CustomWidgets.cs. ----

    private static Widget SwatchGridDemo(DemoContext ctx)
    {
        var colors = new[]
        {
            Color.Hex("#6a9fb5"), Color.Hex("#90a959"), Color.Hex("#f4bf75"), Color.Hex("#aa759f"),
            Color.Hex("#ac4142"), Color.Hex("#75b5aa"), Color.Hex("#d28445"), Color.Hex("#8f5536"),
        };
        var grid = new ColorSwatchGrid { Colors = colors, Columns = 8, Selected = 2, HasFocus = true };
        return Padded(new Stack(Direction.Vertical)
            .Add(grid, Constraint.Length(5))
            .Add(new Label(new StyledText("↑↓←→ or click to pick a swatch", Theme.Current.MutedStyle)), Constraint.Length(1)));
    }

    private static Widget HeatmapDemo(DemoContext ctx)
    {
        var rng = new Random(5);
        var vals = new double[20, 7];
        for (int w = 0; w < 20; w++)
            for (int d = 0; d < 7; d++)
                vals[w, d] = Math.Pow(rng.NextDouble(), 1.5);
        return Padded(new Heatmap { Values = vals });
    }

    private static Widget ToggleDemo(DemoContext ctx)
    {
        var toggle = new ToggleSwitch { On = true, HasFocus = true };
        return Padded(new Stack(Direction.Vertical)
            .Add(toggle, Constraint.Length(1))
            .Add(new Label(StyledText.Empty()), Constraint.Length(1))
            .Add(new Label(new StyledText("Space/Enter or click to toggle", Theme.Current.MutedStyle)), Constraint.Length(1)));
    }

    // ---- helpers ----

    private static Widget Padded(Widget w) => new Padding(w, new Thickness(1, 1));

    private static Widget CenteredNote(string text) =>
        new Label(new StyledText(text, Theme.Current.MutedStyle), Justify.Center) { Wrap = true };

    private static Widget LabeledBar(string name, ProgressBar bar) =>
        new Stack(Direction.Horizontal)
            .Add(new Label(new StyledText(name.PadRight(10), Theme.Current.MutedStyle)), Constraint.Length(11))
            .Add(bar, Constraint.Fill());

}
