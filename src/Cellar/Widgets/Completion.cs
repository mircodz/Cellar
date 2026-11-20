using System;
using System.Collections.Generic;
using System.Linq;

namespace Cellar.Widgets;

/// <summary>A completion candidate: the whole text the line would hold if accepted, and an optional note shown
/// next to it in the popup (e.g. what a command does).</summary>
public sealed record CompletionItem(string Text, string? Description = null);

/// <summary>
/// Suggests how to finish what's been typed in a <see cref="CommandLine"/>. Candidates replace the whole line, so a
/// source completing an argument returns "open notes.txt", not "notes.txt"; the best one comes first.
/// </summary>
public interface ICompletionSource
{
    /// <summary>Candidates for <paramref name="text"/>, best first. Called when the text changes, not per frame.</summary>
    IReadOnlyList<CompletionItem> Complete(string text);
}

/// <summary>Ready-made <see cref="ICompletionSource"/>s.</summary>
public static class CompletionSource
{
    /// <summary>A source backed by a function.</summary>
    public static ICompletionSource From(Func<string, IEnumerable<CompletionItem>> complete) => new FuncSource(complete);

    /// <summary>Words that start with what's been typed (ignoring case), in the order given.</summary>
    public static ICompletionSource Words(IEnumerable<string> words) => Words(() => words);

    /// <summary>Words, re-read on every change, that start with what's been typed (ignoring case).</summary>
    public static ICompletionSource Words(Func<IEnumerable<string>> words) =>
        new FuncSource(text => words()
            .Where(w => w.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .Select(w => new CompletionItem(w)));

    private sealed class FuncSource(Func<string, IEnumerable<CompletionItem>> complete) : ICompletionSource
    {
        public IReadOnlyList<CompletionItem> Complete(string text) => complete(text).ToList();
    }
}

/// <summary>
/// Completes <c>:</c> commands from a <see cref="Keymap"/>: command names (with the action's title as the
/// description) while the first word is typed, and the command's arguments after a space when
/// <see cref="Arguments"/> knows them. Only actions available right now are offered.
/// </summary>
public sealed class KeymapCompletionSource(Keymap keymap) : ICompletionSource
{
    /// <summary>Argument values for an action, given what's been typed of the argument so far; e.g. the file
    /// names for "open". The source keeps the ones that start with the typed text.</summary>
    public Func<AppAction, string, IEnumerable<string>>? Arguments { get; set; }

    public IReadOnlyList<CompletionItem> Complete(string text)
    {
        var result = new List<CompletionItem>();
        string trimmed = text.TrimStart().TrimStart(':');
        int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, action) in keymap.CommandNames())
            {
                if (name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) && seen.Add(name))
                {
                    string description = action.ArgumentHint is { } hint ? $"{action.Title}  {hint}" : action.Title;
                    result.Add(new CompletionItem(name, description));
                }
            }

            return result;
        }

        if (Arguments is null || keymap.FindCommand(trimmed) is not { } match)
        {
            return result;
        }

        string command = trimmed[..space];
        string typed = trimmed[(space + 1)..].TrimStart();
        foreach (var value in Arguments(match.Action, typed))
        {
            if (value.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new CompletionItem(command + " " + value));
            }
        }

        return result;
    }
}
