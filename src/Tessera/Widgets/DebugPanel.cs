using System;
using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Widgets.Charts;

namespace Tessera.Widgets;

/// <summary>
/// A minimal debug HUD: FPS and frame-time sparklines plus per-frame allocation and diff size.
/// Fed by the <see cref="App"/> loop via <see cref="PushFrame"/>. Fixed light-gray-on-black
/// styling so it stands out over any theme.
/// </summary>
public sealed class DebugPanel : Widget
{
    private static readonly Color Bg = Color.Rgb(200, 200, 200);
    private static readonly Color Fg = Color.Rgb(0, 0, 0);

    // FPS is the number of frames rendered in the last WindowMs (a true rolling rate, so it has
    // 1-unit resolution instead of the 10-step jumps a per-bucket count gives). The loop is
    // event-driven, so we don't want the graph to scroll faster just because more input events
    // arrived — the graph advances one column per ColumnMs of wall-clock instead of one per
    // render. Together: correct value, activity-independent scroll speed.
    private const long WindowMs = 1000; // rolling window for the FPS count
    private const long ColumnMs = 100;  // one graph column per 100ms

    private readonly Sparkline _fps = new() { Color = Fg, Background = Bg, BaselineZero = true };
    private readonly Sparkline _frame = new() { Color = Fg, Background = Bg, BaselineZero = true };

    // Ring of recent frame timestamps (ms since boot) and their render times. At the 60fps cap a
    // 1s window holds ~60 entries, so 128 slots is ample; a full ring overwrites its oldest.
    private readonly long[] _stampMs = new long[128];
    private readonly double[] _stampFrameMs = new double[128];
    private int _head;  // next write slot
    private int _count; // live entries

    private long _nextColumnAt = -1; // wall-clock ms the next graph column is due; -1 until first frame

    private double _lastFps;
    private double _lastFrameMs;
    private long _lastAllocBytes;
    private int _lastDiffLen;

    /// <summary>Records one frame's stats: render duration, bytes allocated, and diff length.</summary>
    public void PushFrame(double frameMs, long allocBytes, int diffLen)
    {
        long now = Environment.TickCount64;
        _lastFrameMs = frameMs;
        _lastAllocBytes = allocBytes;
        _lastDiffLen = diffLen;

        // Append this frame, then read the rolling window for the headline.
        _stampMs[_head] = now;
        _stampFrameMs[_head] = frameMs;
        _head = (_head + 1) % _stampMs.Length;
        if (_count < _stampMs.Length)
        {
            _count++;
        }

        _lastFps = WindowFps(now, out _);

        // Advance the graph on a wall-clock cadence, plotting the rolling value. A long idle gap
        // backfills several columns at the (now low) current rate, so the graph reflects the pause.
        // Guarded so a very long gap can't spin.
        if (_nextColumnAt < 0)
        {
            _nextColumnAt = now + ColumnMs;
        }

        double fps = WindowFps(now, out double avgMs);
        int guard = 0;
        while (now >= _nextColumnAt && guard++ < 256)
        {
            _fps.Push(fps);
            _frame.Push(avgMs);
            _nextColumnAt += ColumnMs;
        }

        if (now >= _nextColumnAt)
        {
            _nextColumnAt = now + ColumnMs; // resync after a gap longer than the guard covers
        }
    }

    // Frames per second over the trailing WindowMs, plus the average render time across them.
    private double WindowFps(long now, out double avgFrameMs)
    {
        long cutoff = now - WindowMs;
        int cap = _stampMs.Length;
        int live = 0;
        double sum = 0;
        for (int i = 1; i <= _count; i++)
        {
            int idx = (_head - i + cap) % cap;
            if (_stampMs[idx] <= cutoff)
            {
                break; // entries are in time order, so everything older stops the scan
            }

            live++;
            sum += _stampFrameMs[idx];
        }

        avgFrameMs = live > 0 ? sum / live : 0;
        return live * 1000.0 / WindowMs;
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.Width < 12 || area.Height < 8)
        {
            return;
        }

        var style = new Style(Fg, Bg);
        surface.FillRect(area, style);

        int x = area.X + 1;
        int right = area.Right - 1;
        int w = right - x;
        int y = area.Y;

        // Headline numbers.
        surface.DrawText(x, y, $"FPS {_lastFps,5:0.0}   {_lastFrameMs,5:0.00}ms", style);
        y += 1;

        // FPS sparkline band (label row + multi-row chart).
        surface.DrawText(x, y, "fps", style);
        y += 1;
        int fpsRows = Math.Max(1, (area.Bottom - y - 5));
        _fps.Render(surface, new Rect(x, y, w, fpsRows));
        y += fpsRows;

        // Frame-time sparkline band.
        surface.DrawText(x, y, "frame ms", style);
        y += 1;
        int frameRows = Math.Max(1, area.Bottom - y - 1);
        _frame.Render(surface, new Rect(x, y, w, frameRows));
        y += frameRows;

        // Footer: allocation + diff size for the frame.
        surface.DrawText(x, y, $"alloc {Bytes(_lastAllocBytes)}  diff {Bytes(_lastDiffLen)}", style);
    }

    private static string Bytes(long n)
    {
        if (n < 1024)
        {
            return $"{n}B";
        }
        if (n < 1024 * 1024)
        {
            return $"{n / 1024.0:0.0}K";
        }
        return $"{n / (1024.0 * 1024.0):0.0}M";
    }
}
