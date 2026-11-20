using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cellar.Widgets;

/// <summary>Where an <see cref="ActivityTask"/> is in its life.</summary>
public enum ActivityState
{
    Running,
    Succeeded,
    Failed,
    Canceled,
}

/// <summary>
/// A thread-safe record of the work an app is doing in the background: loading, saving, syncing. Each piece of work
/// is an <see cref="ActivityTask"/> that reports progress and ends in success or failure, from whatever thread does
/// the work. An <see cref="ActivityStrip"/> shows it on one line and an <see cref="ActivityList"/> in full.
/// </summary>
/// <remarks>
/// <para>Every member can be called from any thread. Changes are made under one lock, bump <see cref="Version"/>
/// and request a repaint (<see cref="Repaint.Request"/>), so the widgets that show the activity redraw on the UI
/// thread without the worker posting anything. <see cref="Changed"/> is raised on the thread that made the change,
/// outside the lock: a handler that touches widgets should hop to the UI thread with <see cref="App.Post"/>.</para>
/// <para>Finished tasks are kept (newest first, up to <see cref="KeepFinished"/>) so the list can show what
/// happened; running tasks are always kept.</para>
/// </remarks>
public sealed class Activity
{
    private readonly object _gate = new();
    private readonly List<ActivityTask> _tasks = new(); // in start order
    private long _nextId;
    private int _keepFinished = 50;

    public Activity(TimeProvider? clock = null) => Clock = clock ?? TimeProvider.System;

    /// <summary>The clock task start and end times come from; a fake one makes tests deterministic.</summary>
    public TimeProvider Clock { get; }

    /// <summary>How many finished tasks are kept; older ones are dropped. 50 by default.</summary>
    public int KeepFinished
    {
        get => _keepFinished;
        set
        {
            lock (_gate)
            {
                _keepFinished = Math.Max(0, value);
                Trim();
            }
        }
    }

    /// <summary>Increases on every change, so a widget can tell cheaply whether to take a new
    /// <see cref="CopyTo">snapshot</see>.</summary>
    public int Version => Volatile.Read(ref _version);

    private int _version;

    /// <summary>Raised after every change, on the thread that made it.</summary>
    public event Action? Changed;

    /// <summary>How many tasks are running.</summary>
    public int RunningCount
    {
        get
        {
            lock (_gate)
            {
                int running = 0;
                foreach (var task in _tasks)
                {
                    if (task.StateUnlocked == ActivityState.Running)
                    {
                        running++;
                    }
                }

                return running;
            }
        }
    }

    /// <summary>Starts tracking a task. Report on it, and end it with <see cref="ActivityTask.Succeed"/>,
    /// <see cref="ActivityTask.Fail(string?)"/> or <see cref="ActivityTask.Cancel"/>.</summary>
    public ActivityTask Start(string title, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ActivityTask task;
        lock (_gate)
        {
            task = new ActivityTask(this, ++_nextId, title, message, Clock.GetUtcNow());
            _tasks.Add(task);
        }

        OnChanged();
        return task;
    }

    /// <summary>Runs <paramref name="work"/> as a task: it succeeds when the work completes, is canceled when the
    /// work throws <see cref="OperationCanceledException"/>, and fails (with the exception's message) when it
    /// throws anything else. The exception is rethrown either way.</summary>
    public async Task Run(string title, Func<ActivityTask, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        await Run<bool>(title, async task =>
        {
            await work(task).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc cref="Run(string, Func{ActivityTask, Task})"/>
    public async Task<T> Run<T>(string title, Func<ActivityTask, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var task = Start(title);
        try
        {
            var result = await work(task).ConfigureAwait(false);
            task.Succeed();
            return result;
        }
        catch (OperationCanceledException)
        {
            task.Cancel();
            throw;
        }
        catch (Exception error)
        {
            task.Fail(error);
            throw;
        }
    }

    /// <summary>A copy of every task, running and finished, in start order.</summary>
    public IReadOnlyList<ActivitySnapshot> Snapshot()
    {
        var list = new List<ActivitySnapshot>();
        CopyTo(list);
        return list;
    }

    /// <summary>Fills <paramref name="into"/> with a copy of every task in start order, reusing the list so a
    /// widget can refresh its copy without allocating. Returns the <see cref="Version"/> the copy is of.</summary>
    public int CopyTo(List<ActivitySnapshot> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        lock (_gate)
        {
            into.Clear();
            foreach (var task in _tasks)
            {
                into.Add(task.SnapshotUnlocked());
            }

            return _version;
        }
    }

    /// <summary>Forgets finished tasks; running ones stay.</summary>
    public void ClearFinished()
    {
        lock (_gate)
        {
            _tasks.RemoveAll(static t => t.StateUnlocked != ActivityState.Running);
        }

        OnChanged();
    }

    internal object Gate => _gate;

    internal void Finished()
    {
        lock (_gate)
        {
            Trim();
        }
    }

    internal void OnChanged()
    {
        Interlocked.Increment(ref _version);
        Repaint.Request();
        Changed?.Invoke();
    }

    // Under the lock: drops the oldest finished tasks beyond KeepFinished.
    private void Trim()
    {
        int finished = 0;
        foreach (var task in _tasks)
        {
            if (task.StateUnlocked != ActivityState.Running)
            {
                finished++;
            }
        }

        for (int i = 0; i < _tasks.Count && finished > _keepFinished;)
        {
            if (_tasks[i].StateUnlocked != ActivityState.Running)
            {
                _tasks.RemoveAt(i);
                finished--;
            }
            else
            {
                i++;
            }
        }
    }
}

/// <summary>
/// One piece of work tracked by an <see cref="Activity"/>. Report on it from any thread; it ends once, with the
/// first of <see cref="Succeed"/>, <see cref="Fail(string?)"/> or <see cref="Cancel"/> (later calls are ignored).
/// Disposing a task that is still running cancels it, so <c>using var task = activity.Start(…)</c> never leaves a
/// spinner behind.
/// </summary>
public sealed class ActivityTask : IDisposable
{
    private readonly Activity _owner;
    private string _title;
    private string? _message;
    private double? _progress;
    private ActivityState _state;
    private DateTimeOffset? _ended;

    internal ActivityTask(Activity owner, long id, string title, string? message, DateTimeOffset started)
    {
        _owner = owner;
        Id = id;
        _title = title;
        _message = message;
        Started = started;
    }

    /// <summary>Unique within its <see cref="Activity"/>, increasing in start order.</summary>
    public long Id { get; }

    /// <summary>When the task started.</summary>
    public DateTimeOffset Started { get; }

    /// <summary>What the task is doing, e.g. "Saving notes.txt".</summary>
    public string Title
    {
        get { lock (_owner.Gate) { return _title; } }
        set => Update(() => _title = value ?? string.Empty);
    }

    /// <summary>A detail: the step it's on while running, the outcome or error once finished.</summary>
    public string? Message
    {
        get { lock (_owner.Gate) { return _message; } }
        set => Update(() => _message = value);
    }

    /// <summary>How far along it is, 0 to 1, or null when that isn't known.</summary>
    public double? Progress
    {
        get { lock (_owner.Gate) { return _progress; } }
        set => Update(() => _progress = value is { } p ? Math.Clamp(p, 0, 1) : null);
    }

    /// <summary>Where the task is in its life.</summary>
    public ActivityState State
    {
        get { lock (_owner.Gate) { return _state; } }
    }

    /// <summary>When the task ended, or null while it runs.</summary>
    public DateTimeOffset? Ended
    {
        get { lock (_owner.Gate) { return _ended; } }
    }

    internal ActivityState StateUnlocked => _state;

    /// <summary>Sets <see cref="Progress"/> and, if given, <see cref="Message"/> in one change.</summary>
    public void Report(double? progress, string? message = null)
    {
        Update(() =>
        {
            _progress = progress is { } p ? Math.Clamp(p, 0, 1) : null;
            if (message is not null)
            {
                _message = message;
            }
        });
    }

    /// <summary>Ends the task as done; <paramref name="message"/>, if given, replaces <see cref="Message"/>.</summary>
    public void Succeed(string? message = null) => End(ActivityState.Succeeded, message);

    /// <summary>Ends the task as failed, with what went wrong.</summary>
    public void Fail(string? message = null) => End(ActivityState.Failed, message);

    /// <summary>Ends the task as failed, with the exception's message.</summary>
    public void Fail(Exception error) => End(ActivityState.Failed, error?.Message);

    /// <summary>Ends the task as canceled.</summary>
    public void Cancel(string? message = null) => End(ActivityState.Canceled, message);

    /// <summary>Cancels the task if it's still running.</summary>
    public void Dispose() => Cancel();

    internal ActivitySnapshot SnapshotUnlocked() => new(Id, _title, _state, _progress, _message, Started, _ended);

    private void End(ActivityState state, string? message)
    {
        lock (_owner.Gate)
        {
            if (_state != ActivityState.Running)
            {
                return;
            }

            _state = state;
            _ended = _owner.Clock.GetUtcNow();
            if (message is not null)
            {
                _message = message;
            }

            if (state == ActivityState.Succeeded)
            {
                _progress = 1;
            }
        }

        _owner.Finished();
        _owner.OnChanged();
    }

    private void Update(Action change)
    {
        lock (_owner.Gate)
        {
            change();
        }

        _owner.OnChanged();
    }
}

/// <summary>A copy of an <see cref="ActivityTask"/> at one moment, safe to read on any thread.</summary>
public readonly record struct ActivitySnapshot(
    long Id,
    string Title,
    ActivityState State,
    double? Progress,
    string? Message,
    DateTimeOffset Started,
    DateTimeOffset? Ended)
{
    /// <summary>Whether the task is still running.</summary>
    public bool IsRunning => State == ActivityState.Running;

    /// <summary>How long it ran, or has been running as of <paramref name="now"/>.</summary>
    public TimeSpan Elapsed(DateTimeOffset now) => (Ended ?? now) - Started;
}
