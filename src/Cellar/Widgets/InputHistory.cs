using System;
using System.Collections.Generic;

namespace Cellar.Widgets;

/// <summary>
/// Earlier entries of an <see cref="Input"/> (or a <see cref="CommandLine"/> mode), oldest first, which
/// <c>↑</c>/<c>↓</c> browse. In memory only: the app decides whether and where to keep it, by calling
/// <see cref="Load"/> at start and saving from <see cref="OnAdd"/> or <see cref="Entries"/>.
/// </summary>
public sealed class InputHistory
{
    private readonly List<string> _entries = new();
    private int _capacity;

    /// <param name="capacity">How many entries to keep; the oldest go first.</param>
    public InputHistory(int capacity = 500) => Capacity = capacity;

    /// <summary>How many entries to keep; the oldest are dropped beyond it. At least 1.</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Max(1, value);
            Trim();
        }
    }

    /// <summary>When true (the default), adding an entry that's already in the history moves it to the end
    /// instead of keeping both, so browsing back doesn't step through the same command again and again.</summary>
    public bool RemoveDuplicates { get; set; } = true;

    /// <summary>The entries, oldest first.</summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Raised after an entry is added, with the entry, so the app can append it to a file.</summary>
    public Action<string>? OnAdd { get; set; }

    /// <summary>Adds an entry as the newest. Blank entries are ignored, and so is one equal to the newest.</summary>
    public void Add(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry) || (_entries.Count > 0 && _entries[^1] == entry))
        {
            return;
        }

        if (RemoveDuplicates)
        {
            _entries.Remove(entry);
        }

        _entries.Add(entry);
        Trim();
        OnAdd?.Invoke(entry);
    }

    /// <summary>Replaces the entries with <paramref name="entries"/> (oldest first), e.g. read from a file at start.
    /// Doesn't raise <see cref="OnAdd"/>.</summary>
    public void Load(IEnumerable<string> entries)
    {
        _entries.Clear();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            if (RemoveDuplicates)
            {
                _entries.Remove(entry);
            }

            _entries.Add(entry);
        }
        Trim();
    }

    /// <summary>Removes every entry.</summary>
    public void Clear() => _entries.Clear();

    private void Trim()
    {
        if (_entries.Count > _capacity)
        {
            _entries.RemoveRange(0, _entries.Count - _capacity);
        }
    }
}
