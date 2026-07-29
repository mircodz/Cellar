using System;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>Where a <see cref="ProgressBar"/>'s label sits relative to the bar.</summary>
public enum LabelPlacement { None, Left, Right }

/// <summary>What a <see cref="ProgressBar"/>'s label displays.</summary>
public enum ProgressLabel
{
    /// <summary>A percentage, e.g. "42%".</summary>
    Percent,
    /// <summary>Current over total, e.g. "42/100".</summary>
    Fraction,
    /// <summary>Byte-formatted current over total, e.g. "4.2 MB / 10 MB".</summary>
    Bytes,
    /// <summary>Transfer rate and ETA, e.g. "4.2 MB/s · 3s left" (needs <see cref="ProgressBar.Rate"/>).</summary>
    RateEta,
    /// <summary>A caller-supplied string via <see cref="ProgressBar.CustomLabel"/>.</summary>
    Custom,
}

/// <summary>
/// A horizontal progress bar: a solid filled block over a track, with sub-cell precision at
/// the leading edge. An optional label (percent, fraction, bytes, or rate + ETA) sits left or right.
/// </summary>
public sealed class ProgressBar : Widget
{
    // Left-anchored partial block fills (1/8 .. 7/8 of a cell from the left).
    private static readonly string[] LeftPartials =
        { "", "▏", "▎", "▍", "▌", "▋", "▊", "▉" };

    /// <summary>Progress amount. Paired with <see cref="Total"/> to derive the fraction.</summary>
    public double Current { get; set; }

    /// <summary>The value representing 100%. Defaults to 1 so <see cref="Current"/> reads as [0,1].</summary>
    public double Total { get; set; } = 1.0;

    /// <summary>Convenience [0,1] view of progress. Get returns Current/Total; set assigns Current (Total=1).</summary>
    public double Value
    {
        get => Total <= 0 ? 0 : Math.Clamp(Current / Total, 0, 1);
        set { Current = Math.Clamp(value, 0, 1); Total = 1.0; }
    }

    /// <summary>Rate in units per second, used by <see cref="ProgressLabel.RateEta"/>.</summary>
    public double Rate { get; set; }

    public LabelPlacement LabelPlacement { get; set; } = LabelPlacement.None;
    public ProgressLabel LabelType { get; set; } = ProgressLabel.Percent;

    /// <summary>Fixed width reserved for the label column (0 = auto, min 12).</summary>
    public int LabelWidth { get; set; }

    /// <summary>Formatter for <see cref="ProgressLabel.Custom"/>.</summary>
    public Func<ProgressBar, string>? CustomLabel { get; set; }

    /// <summary>Fill color. Null uses the theme accent.</summary>
    public Color? FillColor { get; set; }

    /// <summary>Track (unfilled) background color. Null derives a dark tone from the theme.</summary>
    public Color? TrackColor { get; set; }

    public ProgressBar(double value = 0) => Value = value;

    /// <summary>Keeps the old (value-only) construction working: 0..1 progress.</summary>
    public bool ShowPercent
    {
        get => LabelPlacement != LabelPlacement.None && LabelType == ProgressLabel.Percent;
        set
        {
            LabelType = ProgressLabel.Percent;
            LabelPlacement = value ? LabelPlacement.Right : LabelPlacement.None;
        }
    }

    public override Size Measure(Size available) => new(available.Width, available.Height);

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        var theme = Theme.Current;
        var fill = FillColor ?? theme.Accent;
        var track = TrackColor ?? TrackFromTheme(theme);

        Rect barArea = area;
        string? label = LabelPlacement == LabelPlacement.None ? null : BuildLabel();

        if (label is not null)
        {
            int labelW = LabelWidth > 0 ? LabelWidth : Math.Max(12, Unicode.StringWidth(label) + 1);
            labelW = Math.Min(labelW, area.Width);
            var labelStyle = new Style(theme.Foreground, Color.Default);

            if (LabelPlacement == LabelPlacement.Left)
            {
                DrawLabel(surface, new Rect(area.X, area.Y, labelW, area.Height), label, labelStyle, Justify.Left);
                barArea = new Rect(area.X + labelW, area.Y, area.Width - labelW, area.Height);
            }
            else
            {
                DrawLabel(surface, new Rect(area.Right - labelW, area.Y, labelW, area.Height), label, labelStyle, Justify.Right);
                barArea = new Rect(area.X, area.Y, area.Width - labelW, area.Height);
            }
        }

        RenderBar(surface, barArea, Value, fill, track);
    }

    private void RenderBar(Surface surface, Rect area, double value, Color fill, Color track)
    {
        if (area.Width <= 0)
        {
            return;
        }

        var fillStyle = new Style(Color.Default, fill);
        var trackStyle = new Style(Color.Default, track);
        // Leading edge: a left-partial block, fill in the FG over the track BG.
        double exact = value * area.Width;
        int fullCells = (int)Math.Floor(exact);
        int eighths = (int)Math.Round((exact - fullCells) * 8);
        if (eighths >= 8) { fullCells++; eighths = 0; }

        for (int i = 0; i < area.Width; i++)
        {
            int x = area.X + i;
            Cell cell;
            if (i < fullCells)
            {
                cell = Cell.Blank(fillStyle);
            }
            else if (i == fullCells && eighths > 0)
            {
                cell = Cell.FromGrapheme(LeftPartials[eighths], new Style(fill, track));
            }
            else
            {
                cell = Cell.Blank(trackStyle);
            }

            for (int y = area.Top; y < area.Bottom; y++)
                surface.Set(x, y, cell);
        }
    }

    private void DrawLabel(Surface surface, Rect area, string label, Style style, Justify justify)
    {
        int y = area.Y + area.Height / 2;
        surface.SetClip(area);
        TextRenderer.DrawLine(surface, area.X, y, area.Width, new StyledText(label, style), justify);
        surface.ResetClip();
    }

    private string BuildLabel() => LabelType switch
    {
        ProgressLabel.Percent => $"{(int)Math.Round(Value * 100)}%",
        ProgressLabel.Fraction => $"{Format(Current)}/{Format(Total)}",
        ProgressLabel.Bytes => $"{FormatBytes(Current)} / {FormatBytes(Total)}",
        ProgressLabel.RateEta => BuildRateEta(),
        ProgressLabel.Custom => CustomLabel?.Invoke(this) ?? string.Empty,
        _ => string.Empty,
    };

    private string BuildRateEta()
    {
        string rate = Rate > 0 ? $"{FormatBytes(Rate)}/s" : "—";
        if (Rate > 0 && Total > Current)
        {
            double secs = (Total - Current) / Rate;
            return $"{rate} · {FormatDuration(secs)} left";
        }
        return rate;
    }

    private static string Format(double v) =>
        v == Math.Floor(v) ? ((long)v).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                           : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatBytes(double bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double b = bytes;
        int u = 0;
        while (b >= 1024 && u < units.Length - 1) { b /= 1024; u++; }
        return u == 0
            ? $"{(long)b} {units[u]}"
            : $"{b.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} {units[u]}";
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds < 1)
        {
            return "<1s";
        }

        int s = (int)Math.Round(seconds);
        if (s < 60)
        {
            return $"{s}s";
        }

        int m = s / 60; s %= 60;
        if (m < 60)
        {
            return $"{m}m {s}s";
        }

        int h = m / 60; m %= 60;
        return $"{h}h {m}m";
    }

    // A track background a touch different from the theme background so it reads as a groove.
    private static Color TrackFromTheme(Theme theme)
    {
        if (theme.Background.IsRgb)
        {
            bool dark = theme.Background.R + theme.Background.G + theme.Background.B < 384;
            int d = dark ? 24 : -24;
            return Color.Rgb(Color.ClampChannel(theme.Background.R + d), Color.ClampChannel(theme.Background.G + d), Color.ClampChannel(theme.Background.B + d));
        }
        return theme.Muted;
    }
}
