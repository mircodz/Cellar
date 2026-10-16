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
