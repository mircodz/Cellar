using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cellar.Terminal;
using Cellar.Theming;
using Cellar.Widgets;
using Xunit;

namespace Cellar.Tests;

public class ActivityTests
{
    private static KeyEvent K(Key key) => new(key, default, KeyModifiers.None);
    private static KeyEvent C(char c) => new(Key.Char, new Rune(c), KeyModifiers.None);

    private static string Row(Widget widget, int width, int height = 1, int y = 0) =>
        SurfaceText.Row(SurfaceText.Render(widget, width, height), y).TrimEnd();

    [Fact]
    public void Task_ReportsProgress_AndEndsOnce()
    {
        var clock = new ManualClock();
        var activity = new Activity(clock);
        int changes = 0;
        activity.Changed += () => changes++;

        var task = activity.Start("Saving notes.txt");
        Assert.Equal(ActivityState.Running, task.State);
        Assert.Equal(1, activity.RunningCount);

        task.Report(0.4, "2 of 5 files");
        Assert.Equal(0.4, task.Progress);
        Assert.Equal("2 of 5 files", task.Message);
        task.Progress = 7;
        Assert.Equal(1, task.Progress); // clamped

        clock.Advance(TimeSpan.FromSeconds(3));
        task.Succeed("saved");
        task.Fail("too late"); // ignored: it already ended
        Assert.Equal(ActivityState.Succeeded, task.State);
        Assert.Equal("saved", task.Message);
        Assert.Equal(clock.GetUtcNow(), task.Ended);
        Assert.Equal(0, activity.RunningCount);
        Assert.Equal(TimeSpan.FromSeconds(3), activity.Snapshot()[0].Elapsed(clock.GetUtcNow()));
        Assert.Equal(4, changes);
    }

    [Fact]
    public void DisposingARunningTask_CancelsIt()
    {
        var activity = new Activity();
        ActivityTask task;
        using (task = activity.Start("Scanning"))
        {
        }

        Assert.Equal(ActivityState.Canceled, task.State);
    }

    [Fact]
    public async Task Run_SucceedsFailsOrCancels_WithTheWork()
    {
        var activity = new Activity();
        Assert.Equal(42, await activity.Run("Counting", _ => Task.FromResult(42)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            activity.Run("Uploading", _ => throw new InvalidOperationException("disk full")));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            activity.Run("Indexing", _ => throw new OperationCanceledException()));

        var tasks = activity.Snapshot();
        Assert.Equal([ActivityState.Succeeded, ActivityState.Failed, ActivityState.Canceled], tasks.Select(t => t.State));
        Assert.Equal("disk full", tasks[1].Message);
    }

    [Fact]
    public void KeepFinished_DropsTheOldestFinished_ButNeverRunningTasks()
    {
        var activity = new Activity { KeepFinished = 2 };
        var running = activity.Start("long job");
        for (int i = 0; i < 5; i++)
        {
            activity.Start($"job {i}").Succeed();
        }

        Assert.Equal(["long job", "job 3", "job 4"], activity.Snapshot().Select(t => t.Title));
        activity.ClearFinished();
        Assert.Equal(["long job"], activity.Snapshot().Select(t => t.Title));
        Assert.True(running.State == ActivityState.Running);
    }

    [Fact]
    public void Updates_FromManyThreads_AreAllRecorded()
    {
        var activity = new Activity { KeepFinished = 1000 };
        Parallel.For(0, 200, i =>
        {
            var task = activity.Start($"job {i}");
            task.Report(0.5);
            if (i % 2 == 0)
            {
                task.Succeed();
            }
            else
            {
                task.Fail("no");
            }
        });

        var tasks = activity.Snapshot();
        Assert.Equal(200, tasks.Count);
        Assert.Equal(200, tasks.Select(t => t.Id).Distinct().Count());
        Assert.Equal(100, tasks.Count(t => t.State == ActivityState.Failed));
        Assert.Equal(0, activity.RunningCount);
    }

    [Fact]
    public void Strip_ShowsRunningTasksWithProgress_ThenRecentOutcomes()
    {
        var clock = new ManualClock();
        var activity = new Activity(clock);
        var strip = new ActivityStrip(activity) { IdleText = "idle" };
        Assert.Equal("idle", Row(strip, 60));

        var save = activity.Start("Saving");
        save.Report(0.42);
        activity.Start("Syncing");
        var row = Row(strip, 60);
        Assert.Equal(" Saving 42% · ", row[1..15]); // after the spinner glyph
        Assert.EndsWith(" Syncing", row);

        save.Succeed();
        activity.Start("Upload").Fail("disk full");
        row = Row(strip, 60);
        Assert.Contains("Syncing", row);
        Assert.Contains("✗ Upload: disk full", row);
        Assert.Contains("✓ Saving", row);
        Assert.True(row.IndexOf("Upload", StringComparison.Ordinal) < row.IndexOf("Saving", StringComparison.Ordinal)); // newest first

        clock.Advance(TimeSpan.FromSeconds(6)); // past ShowFinishedFor, within ShowFailedFor
        row = Row(strip, 60);
        Assert.DoesNotContain("Saving", row);
        Assert.Contains("Upload", row);

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.DoesNotContain("Upload", Row(strip, 60));
    }

    [Fact]
    public void Strip_CountsWhatDoesNotFit()
    {
        var activity = new Activity();
        for (int i = 1; i <= 5; i++)
        {
            activity.Start($"Download {i}");
        }

        var row = Row(new ActivityStrip(activity), 30);
        Assert.Contains("Download 1", row);
        Assert.EndsWith("+4", row);
    }

    [Fact]
    public void Strip_ClickRunsOnClick()
    {
        int clicks = 0;
        var strip = new ActivityStrip(new Activity()) { OnClick = () => clicks++ };
        SurfaceText.Render(strip, 20);
        Assert.True(strip.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 3, 0, KeyModifiers.None)));
        Assert.False(strip.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 3, 4, KeyModifiers.None)));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void List_ShowsRunningFirst_ThenFinishedNewestFirst_WithElapsedTime()
    {
        var clock = new ManualClock();
        var activity = new Activity(clock);
        activity.Start("Export photos").Succeed("312 photos");
        clock.Advance(TimeSpan.FromSeconds(1));
        var backup = activity.Start("Back up");
        clock.Advance(TimeSpan.FromSeconds(2));
        activity.Start("Rename files").Fail("name taken");
        backup.Report(0.5);
        clock.Advance(TimeSpan.FromSeconds(65));

        var list = new ActivityList(activity) { HasFocus = true };
        var surface = SurfaceText.Render(list, 50, 4);
        string[] rows = Enumerable.Range(0, 4).Select(y => SurfaceText.Row(surface, y).TrimEnd()).ToArray();
        Assert.Contains("Back up", rows[0]);
        Assert.EndsWith("50%  1m07s", rows[0]);
        Assert.StartsWith("✗ Rename files  name taken", rows[1]);
        Assert.EndsWith("0ms", rows[1]);
        Assert.StartsWith("✓ Export photos  312 photos", rows[2]);
        Assert.Equal(Theme.Current.SelectionBackground, surface.Get(10, 0).Style.Background);

        list.OnEvent(K(Key.Down));
        Assert.Equal(1, list.SelectedIndex);
        list.OnEvent(C('c'));
        Assert.Single(activity.Snapshot());
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal("", Row(new ActivityList(new Activity()) { EmptyText = "" }, 20));
    }

    [Fact]
    public void List_ElapsedTimesAreShort()
    {
        var clock = new ManualClock();
        var activity = new Activity(clock);
        foreach (var took in new[] { TimeSpan.FromMinutes(62), TimeSpan.FromSeconds(185), TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(850) })
        {
            var task = activity.Start("job");
            clock.Advance(took);
            task.Succeed();
        }

        var surface = SurfaceText.Render(new ActivityList(activity), 30, 4);
        string[] rows = Enumerable.Range(0, 4).Select(y => SurfaceText.Row(surface, y).TrimEnd()).ToArray();
        Assert.EndsWith(" 850ms", rows[0]);
        Assert.EndsWith(" 12s", rows[1]);
        Assert.EndsWith(" 3m05s", rows[2]);
        Assert.EndsWith(" 1h02m", rows[3]);
    }

    [Fact]
    public void LoweringKeepFinished_PublishesTheTrim()
    {
        var activity = new Activity();
        activity.Start("Old").Succeed();
        activity.Start("New").Succeed();
        var strip = new ActivityStrip(activity);
        Assert.Contains("Old", Row(strip, 40));

        int version = activity.Version;
        int changes = 0;
        activity.Changed += () => changes++;
        activity.KeepFinished = 1;
        Assert.True(activity.Version > version);
        Assert.Equal(1, changes);
        Assert.DoesNotContain("Old", Row(strip, 40));
        Assert.Contains("New", Row(strip, 40));
    }

    [Fact]
    public void KeepFinished_DropsWhatFinishedLongestAgo_NotWhatStartedFirst()
    {
        var activity = new Activity { KeepFinished = 1 };
        var a = activity.Start("A");
        var b = activity.Start("B");
        b.Succeed();
        a.Fail("broke");
        var kept = Assert.Single(activity.Snapshot());
        Assert.Equal("A", kept.Title);
        Assert.Equal(ActivityState.Failed, kept.State);
    }

    [Fact]
    public void Strip_ShowsTheLatestOutcomeFirst_WhateverTheStartOrder()
    {
        var activity = new Activity();
        var alpha = activity.Start("Alpha");
        var beta = activity.Start("Beta");
        beta.Succeed();
        alpha.Succeed();
        var row = Row(new ActivityStrip(activity), 40);
        Assert.True(row.IndexOf("Alpha", StringComparison.Ordinal) < row.IndexOf("Beta", StringComparison.Ordinal));
    }

    [Fact]
    public void Strip_ReservesRoomForTheCountOnlyWhenSomethingIsLeft()
    {
        var activity = new Activity();
        activity.Start("A").Succeed();
        activity.Start("B").Succeed();
        Assert.Equal("✓ B · ✓ A", Row(new ActivityStrip(activity), 9));
    }

    [Fact]
    public void List_KeepsElapsedTimesClearOfTheScrollbar()
    {
        var clock = new ManualClock();
        var activity = new Activity(clock);
        for (int i = 0; i < 4; i++)
        {
            var task = activity.Start("job");
            clock.Advance(TimeSpan.FromMilliseconds(850));
            task.Succeed();
        }

        var surface = SurfaceText.Render(new ActivityList(activity), 30, 2);
        Assert.EndsWith("850ms", SurfaceText.Row(surface, 0)[..29].TrimEnd());
        Assert.EndsWith("850ms", SurfaceText.Row(surface, 1)[..29].TrimEnd());
    }

    [Fact]
    public void List_SelectedRowUsesTheSelectionColors()
    {
        var activity = new Activity();
        activity.Start("Running");
        var surface = SurfaceText.Render(new ActivityList(activity) { HasFocus = true }, 30, 2);
        var theme = Theme.Current;
        Assert.Equal(theme.SelectionForeground, surface.Get(0, 0).Style.Foreground); // the spinner glyph
        Assert.Equal(theme.SelectionForeground, surface.Get(2, 0).Style.Foreground); // the title
        Assert.Equal(theme.SelectionBackground, surface.Get(2, 0).Style.Background);
    }
}
