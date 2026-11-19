using System;
using System.Collections.Generic;
using Cellar.Terminal;

namespace Cellar.Widgets;

/// <summary>
/// Something the user can do, declared once: its keys, its <c>:</c> command names, where it shows in the help, the
/// palette and the footer, and when it's available. A <see cref="Keymap"/> turns a list of these into key
/// handling, command lookup, the help screen, palette entries, command completion and footer hints, so none of
/// them can disagree with the others.
/// </summary>
/// <example>
/// <code>
/// new AppAction("restart", "Restart the selected node", _ =&gt; Restart(Selected))
/// {
///     Group = "Node",
///     Keys = ["ctrl+r"],
///     Commands = ["restart"],
///     Footer = "restart",
///     When = () =&gt; Selected is not null,
/// };
/// </code>
/// </example>
public sealed class AppAction
{
    /// <param name="id">A stable name, used to remap keys (<see cref="Keymap.Remap(string, string?)"/>) and by
    /// scopes to replace an action of the same id below them.</param>
    /// <param name="title">What it does, as the help and the palette show it.</param>
    /// <param name="run">Runs it, with the text typed after the command name (<c>:ctx prod</c> passes "prod"), or
    /// null when run by key, from the palette or with no argument.</param>
    public AppAction(string id, string title, Action<string?> run)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Run = run ?? throw new ArgumentNullException(nameof(run));
    }

    /// <summary>An action that takes no argument.</summary>
    public AppAction(string id, string title, Action run)
        : this(id, title, run is null ? throw new ArgumentNullException(nameof(run)) : _ => run())
    {
    }

    /// <summary>A stable name for remapping and for replacing it from a higher scope.</summary>
    public string Id { get; }

    /// <summary>What it does, as the help and the palette show it.</summary>
    public string Title { get; init; }

    /// <summary>Runs the action with an optional argument.</summary>
    public Action<string?> Run { get; }

    /// <summary>The help screen's heading for it. Actions keep the order they were declared in within a group.</summary>
    public string Group { get; init; } = "General";

    /// <summary>The keys that run it, most familiar first: the first is the one the footer shows. Written as text,
    /// e.g. <c>["ctrl+r", "f5"]</c> or <c>["g g"]</c> for a sequence.</summary>
    public IReadOnlyList<KeySequence> Keys { get; init; } = [];

    /// <summary>Names accepted after <c>:</c>, case-insensitive; the first is the one shown.</summary>
    public IReadOnlyList<string> Commands { get; init; } = [];

    /// <summary>How the argument of the command reads in the help and completion, e.g. <c>&lt;name&gt;</c>.</summary>
    public string? ArgumentHint { get; init; }

    /// <summary>Whether it can run now, e.g. only when a row is selected. Unavailable actions don't take keys and
    /// don't show anywhere. Null means always.</summary>
    public Func<bool>? When { get; init; }

    /// <summary>A short label for the footer's key hints (<c>restart</c>), or null to leave it out of the footer.</summary>
    public string? Footer { get; init; }

    /// <summary>Orders the footer's key hints, highest first, and decides which go first when the footer is too
    /// narrow (lowest first).</summary>
    public int Priority { get; init; }

    /// <summary>Whether the command palette lists it. Turn off for movement keys and the like.</summary>
    public bool ShowInPalette { get; init; } = true;

    /// <summary>Whether the help screen lists it.</summary>
    public bool ShowInHelp { get; init; } = true;

    /// <summary>The command name shown for it, or null if it has none.</summary>
    public string? Command => Commands.Count > 0 ? Commands[0] : null;

    /// <summary>Whether <see cref="When"/> allows it now.</summary>
    public bool IsAvailable => When?.Invoke() ?? true;

    public override string ToString() => $"{Id}: {Title}";
}
