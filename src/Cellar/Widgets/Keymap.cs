using System;
using System.Collections.Generic;
using System.Text;
using Cellar.Terminal;

namespace Cellar.Widgets;

/// <summary>An action found for <c>:</c> command text, with the text typed after the command name.</summary>
public readonly record struct CommandMatch(AppAction Action, string? Argument);

/// <summary>
/// A group of <see cref="AppAction"/>s that's active together, such as the whole app, the current page or the
/// focused widget. Scopes with a higher <see cref="Precedence"/> win: their keys shadow the same keys below, and an
/// action with the same <see cref="AppAction.Id"/> replaces the one below.
/// </summary>
public sealed class KeymapScope
{
    /// <summary>The precedence of the app-wide scope (<see cref="Keymap.Global"/>).</summary>
    public const int AppPrecedence = 0;

    /// <summary>A conventional precedence for the actions of the current page.</summary>
    public const int PagePrecedence = 100;

    /// <summary>A conventional precedence for the actions of the focused widget.</summary>
    public const int WidgetPrecedence = 200;

    private readonly List<AppAction> _actions = new();
    private readonly Func<IEnumerable<AppAction>>? _provider;

    internal KeymapScope(string name, int precedence, Func<bool>? active, Func<IEnumerable<AppAction>>? provider, int order)
    {
        Name = name;
        Precedence = precedence;
        Active = active;
        _provider = provider;
        Order = order;
    }

    /// <summary>A name for debugging and the help screen.</summary>
    public string Name { get; }

    /// <summary>Higher wins. Scopes of equal precedence: the one added later wins.</summary>
    public int Precedence { get; }

    /// <summary>Whether the scope applies now, e.g. <c>() =&gt; table.HasFocus</c>. Null means always.</summary>
    public Func<bool>? Active { get; set; }

    internal int Order { get; }

    /// <summary>Whether <see cref="Active"/> allows it now.</summary>
    public bool IsActive => Active?.Invoke() ?? true;

    /// <summary>The actions added to this scope (not those from a provider).</summary>
    public IReadOnlyList<AppAction> Declared => _actions;

    /// <summary>Adds an action.</summary>
    public KeymapScope Add(AppAction action)
    {
        _actions.Add(action ?? throw new ArgumentNullException(nameof(action)));
        return this;
    }

    /// <summary>Adds several actions.</summary>
    public KeymapScope Add(IEnumerable<AppAction> actions)
    {
        foreach (var action in actions)
        {
            Add(action);
        }

        return this;
    }

    /// <summary>Removes the action with <paramref name="id"/>. Returns whether there was one.</summary>
    public bool Remove(string id) => _actions.RemoveAll(a => a.Id == id) > 0;

    /// <summary>Removes every declared action.</summary>
    public void Clear() => _actions.Clear();

    internal void Collect(List<AppAction> into)
    {
        into.AddRange(_actions);
        if (_provider is not null)
        {
            foreach (var action in _provider())
            {
                into.Add(action);
            }
        }
    }
}

/// <summary>
/// The registry of everything the user can do (<see cref="AppAction"/>), in <see cref="KeymapScope"/>s with
/// precedence. From the one declaration it handles keys (<see cref="Handle"/>), looks up <c>:</c> commands
/// (<see cref="FindCommand"/>), and generates the help screen (<see cref="ShowHelp"/>), the palette
/// (<see cref="ShowPalette"/>), command completion (<see cref="CommandNames"/>) and the footer's key hints
/// (<see cref="FooterActions"/>). Users can rebind keys with <see cref="Remap(string, string?)"/>.
/// </summary>
/// <remarks>
/// <para>Wire it to the app with <c>app.OnUnhandledEvent = keymap.Handle</c>, so the focused widget gets keys first
/// (a text box keeps its <c>q</c>), or call <see cref="Handle"/> from <see cref="App.OnEvent"/> for keys that must
/// win over widgets.</para>
/// <para>Sequences such as <c>g g</c> wait for their next key. If a single key runs an action, it can't also start a
/// sequence: the single key wins. A started sequence is forgotten after <see cref="SequenceTimeout"/> or when the
/// next key doesn't continue it (that key is then handled on its own).</para>
/// <para>Use it from the UI thread only.</para>
/// </remarks>
public sealed class Keymap
{
    private readonly List<KeymapScope> _scopes = new();
    private readonly Dictionary<KeySequence, string?> _overrides = new();
    private readonly List<KeyChord> _pending = new();
    private long _pendingSince;
    private int _scopeOrder;

    // Reused while resolving, so a key press doesn't allocate a list per scope.
    private readonly List<AppAction> _collect = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public Keymap()
    {
        Global = AddScope("app", KeymapScope.AppPrecedence);
    }

    /// <summary>The app-wide scope, lowest precedence. <see cref="Add(AppAction)"/> adds here.</summary>
    public KeymapScope Global { get; }

    /// <summary>The scopes, in the order they were added.</summary>
    public IReadOnlyList<KeymapScope> Scopes => _scopes;

    /// <summary>How long a started key sequence (<c>g</c> of <c>g g</c>) waits for its next key.</summary>
    public TimeSpan SequenceTimeout { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>The clock for <see cref="SequenceTimeout"/>; replaceable for tests.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>The keys of a started sequence waiting for the rest, e.g. <c>g</c>; empty when none is.</summary>
    public IReadOnlyList<KeyChord> Pending => _pending;

    /// <summary>The user's key overrides: a key sequence and the id of the action it runs, or null where the user
    /// unbound the key.</summary>
    public IReadOnlyDictionary<KeySequence, string?> Overrides => _overrides;

    /// <summary>Adds an action to <see cref="Global"/>.</summary>
    public Keymap Add(AppAction action)
    {
        Global.Add(action);
        return this;
    }

    /// <summary>Adds a scope of actions added to it later.</summary>
    /// <param name="precedence">Higher wins; see <see cref="KeymapScope.PagePrecedence"/> and
    /// <see cref="KeymapScope.WidgetPrecedence"/>.</param>
    /// <param name="active">When it applies, e.g. <c>() =&gt; table.HasFocus</c>; null means always.</param>
    public KeymapScope AddScope(string name, int precedence = KeymapScope.PagePrecedence, Func<bool>? active = null)
    {
        var scope = new KeymapScope(name, precedence, active, null, _scopeOrder++);
        _scopes.Add(scope);
        return scope;
    }

    /// <summary>Adds a scope whose actions come from <paramref name="provider"/> each time they're needed, e.g. the
    /// current page's actions: <c>keymap.AddScope("page", () =&gt; CurrentPage.Actions)</c>.</summary>
    public KeymapScope AddScope(string name, Func<IEnumerable<AppAction>> provider, int precedence = KeymapScope.PagePrecedence, Func<bool>? active = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var scope = new KeymapScope(name, precedence, active, provider, _scopeOrder++);
        _scopes.Add(scope);
        return scope;
    }

    /// <summary>Removes a scope.</summary>
    public bool RemoveScope(KeymapScope scope) => scope != Global && _scopes.Remove(scope);

    /// <summary>Makes <paramref name="keys"/> run the action <paramref name="actionId"/>, ahead of any default
    /// binding of those keys; null or empty unbinds them.</summary>
    /// <exception cref="FormatException"><paramref name="keys"/> isn't a key.</exception>
    public void Remap(string keys, string? actionId) =>
        _overrides[KeySequence.Parse(keys)] = string.IsNullOrEmpty(actionId) ? null : actionId;

    /// <summary>Applies the user's overrides, e.g. read from a config file (<c>{"ctrl+r": "refresh", "q": ""}</c>).
    /// Returns a description of each entry it couldn't apply (a key it can't parse, or an unknown action id), so
    /// the app can report them; the rest still apply.</summary>
    public IReadOnlyList<string> Remap(IReadOnlyDictionary<string, string?> overrides)
    {
        var problems = new List<string>();
        foreach (var (keys, actionId) in overrides)
        {
            if (!KeySequence.TryParse(keys, out var sequence))
            {
                problems.Add($"'{keys}' is not a key, like ctrl+r, ? or g g");
                continue;
            }

            if (!string.IsNullOrEmpty(actionId) && !IsKnownAction(actionId))
            {
                problems.Add($"{keys}: there is no action '{actionId}'");
                continue;
            }

            _overrides[sequence] = string.IsNullOrEmpty(actionId) ? null : actionId;
        }

        return problems;
    }

    /// <summary>Removes every override, restoring the default keys.</summary>
    public void ClearOverrides() => _overrides.Clear();

    private bool IsKnownAction(string id)
    {
        foreach (var scope in _scopes)
        {
            _collect.Clear();
            scope.Collect(_collect);
            foreach (var action in _collect)
            {
                if (action.Id == id)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The actions available now, highest scope first and in declaration order within a scope. An action
    /// shadowed by one with the same id in a higher scope, or whose <see cref="AppAction.When"/> says no, is left
    /// out.</summary>
    public IReadOnlyList<AppAction> Actions
    {
        get
        {
            var result = new List<AppAction>();
            CollectAvailable(result);
            return result;
        }
    }

    private void CollectAvailable(List<AppAction> into)
    {
        _seen.Clear();
        foreach (var scope in ScopesByPrecedence())
        {
            if (!scope.IsActive)
            {
                continue;
            }

            _collect.Clear();
            scope.Collect(_collect);
            foreach (var action in _collect)
            {
                // A higher scope's action with the same id replaces this one even when it isn't available itself.
                if (_seen.Add(action.Id) && action.IsAvailable)
                {
                    into.Add(action);
                }
            }
        }
    }

    private List<KeymapScope> ScopesByPrecedence()
    {
        var ordered = new List<KeymapScope>(_scopes);
        ordered.Sort((a, b) => a.Precedence != b.Precedence ? b.Precedence.CompareTo(a.Precedence) : b.Order.CompareTo(a.Order));
        return ordered;
    }

    /// <summary>The keys that run <paramref name="action"/> now: those the user mapped to it, then its own keys
    /// that the user didn't take for something else.</summary>
    public IReadOnlyList<KeySequence> KeysFor(AppAction action)
    {
        var keys = new List<KeySequence>();
        foreach (var (sequence, id) in _overrides)
        {
            if (id == action.Id)
            {
                keys.Add(sequence);
            }
        }

        foreach (var sequence in action.Keys)
        {
            if (!_overrides.ContainsKey(sequence) && !keys.Contains(sequence))
            {
                keys.Add(sequence);
            }
        }

        return keys;
    }

    /// <summary>The available action a single key runs, ignoring sequences and any pending keys; null if none.</summary>
    public AppAction? Find(KeyEvent key)
    {
        var chord = new[] { KeyChord.From(key) };
        return Resolve(chord, out _);
    }

    // The action bound to exactly these keys (null if none), and whether some longer binding starts with them.
    private AppAction? Resolve(IReadOnlyList<KeyChord> keys, out bool isPrefix)
    {
        isPrefix = false;
        var available = new List<AppAction>();
        CollectAvailable(available);

        AppAction? found = null;
        foreach (var (sequence, id) in _overrides)
        {
            if (sequence.Is(keys))
            {
                if (id is null)
                {
                    return null; // unbound by the user
                }

                found = available.Find(a => a.Id == id);
                if (found is not null)
                {
                    return found;
                }
            }
            else if (id is not null && sequence.StartsWith(keys) && available.Exists(a => a.Id == id))
            {
                isPrefix = true;
            }
        }

        foreach (var action in available)
        {
            foreach (var sequence in action.Keys)
            {
                if (_overrides.ContainsKey(sequence))
                {
                    continue;
                }

                if (found is null && sequence.Is(keys))
                {
                    found = action;
                }
                else if (sequence.StartsWith(keys))
                {
                    isPrefix = true;
                }
            }
        }

        return found;
    }

    /// <summary>Runs the action bound to a key event, if any; returns whether it used the event. Keys that start a
    /// sequence are used and wait for the rest. Pass any input event: only keys are looked at.</summary>
    public bool Handle(InputEvent e)
    {
        if (e is not KeyEvent key)
        {
            return false;
        }

        long now = Clock.GetTimestamp();
        if (_pending.Count > 0 && Clock.GetElapsedTime(_pendingSince, now) > SequenceTimeout)
        {
            _pending.Clear();
        }

        var chord = KeyChord.From(key);
        if (_pending.Count > 0)
        {
            _pending.Add(chord);
            var continued = Resolve(_pending, out bool longer);
            if (continued is not null)
            {
                _pending.Clear();
                continued.Run(null);
                return true;
            }

            if (longer)
            {
                _pendingSince = now;
                return true;
            }

            // Not a continuation: forget the sequence and take the key on its own.
            _pending.Clear();
        }

        var single = new[] { chord };
        var action = Resolve(single, out bool isPrefix);
        if (action is not null)
        {
            action.Run(null);
            return true;
        }

        if (isPrefix)
        {
            _pending.Add(chord);
            _pendingSince = now;
            return true;
        }

        return false;
    }

    /// <summary>Forgets a started key sequence.</summary>
    public void CancelPending() => _pending.Clear();

    /// <summary>Finds the available action for command text: <c>ctx prod</c> (or <c>:ctx prod</c>) finds the action
    /// with the command <c>ctx</c> and the argument <c>prod</c>. Names are case-insensitive. Null if there's none.</summary>
    public CommandMatch? FindCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith(':'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        int space = trimmed.IndexOfAny([' ', '\t']);
        string name = space < 0 ? trimmed : trimmed[..space];
        string? argument = space < 0 ? null : trimmed[(space + 1)..].Trim();
        if (argument is { Length: 0 })
        {
            argument = null;
        }

        foreach (var action in Actions)
        {
            foreach (var command in action.Commands)
            {
                if (string.Equals(command, name, StringComparison.OrdinalIgnoreCase))
                {
                    return new CommandMatch(action, argument);
                }
            }
        }

        return null;
    }

    /// <summary>Runs the command text (see <see cref="FindCommand"/>). Returns false if no action has that command.</summary>
    public bool RunCommand(string text)
    {
        if (FindCommand(text) is not { } match)
        {
            return false;
        }

        match.Action.Run(match.Argument);
        return true;
    }

    /// <summary>Every command name of the available actions, with its action, for completion. An action's first
    /// name comes first.</summary>
    public IEnumerable<(string Name, AppAction Action)> CommandNames()
    {
        foreach (var action in Actions)
        {
            foreach (var command in action.Commands)
            {
                yield return (command, action);
            }
        }
    }

    /// <summary>The available actions with a <see cref="AppAction.Footer"/> label and a key, highest
    /// <see cref="AppAction.Priority"/> first.</summary>
    public IReadOnlyList<AppAction> FooterActions()
    {
        var result = new List<AppAction>();
        foreach (var action in Actions)
        {
            if (action.Footer is not null && KeysFor(action).Count > 0)
            {
                result.Add(action);
            }
        }

        // List.Sort isn't stable; break ties by declaration order.
        var order = new Dictionary<AppAction, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < result.Count; i++)
        {
            order[result[i]] = i;
        }

        result.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : order[a].CompareTo(order[b]));
        return result;
    }

    /// <summary>How to run <paramref name="action"/>, for the help and the palette: <c>ctrl+r/f5  :refresh</c>.</summary>
    public string Hint(AppAction action)
    {
        var sb = new StringBuilder();
        var keys = KeysFor(action);
        for (int i = 0; i < keys.Count; i++)
        {
            sb.Append(i > 0 ? "/" : string.Empty).Append(keys[i]);
        }

        if (action.Command is { } command)
        {
            sb.Append(sb.Length > 0 ? "  :" : ":").Append(command);
            if (action.ArgumentHint is { } argument)
            {
                sb.Append(' ').Append(argument);
            }
        }

        return sb.ToString();
    }

    /// <summary>A command palette entry for each available action that allows it, subtitled with its keys and
    /// command. Running one runs the action with no argument.</summary>
    public IEnumerable<Command> PaletteCommands()
    {
        foreach (var action in Actions)
        {
            if (action.ShowInPalette)
            {
                var run = action;
                yield return new Command(action.Title, () => run.Run(null), Hint(action));
            }
        }
    }

    /// <summary>Opens a <see cref="CommandPalette"/> of <see cref="PaletteCommands"/> (plus any
    /// <paramref name="extra"/> commands) at the top of the screen. Does nothing if another overlay is open.</summary>
    public Overlay? ShowPalette(App app, IEnumerable<Command>? extra = null)
    {
        if (app.TopOverlay is not null)
        {
            return null;
        }

        var commands = new List<Command>(PaletteCommands());
        if (extra is not null)
        {
            commands.AddRange(extra);
        }

        var palette = new CommandPalette(commands);
        var overlay = new Overlay(palette)
        {
            Placement = OverlayPlacement.Top,
            WidthPercent = 60,
            Height = Math.Min(16, Math.Max(6, app.Size.Height - 6)),
            Margin = 3,
            Modal = true,
            ScrimOpacity = 0.4,
        };
        // Remove this overlay rather than the top one: the action may have opened another.
        palette.OnAfterRun = () => app.RemoveOverlay(overlay);
        palette.OnCancel = () => app.RemoveOverlay(overlay);
        app.PushOverlay(overlay);
        return overlay;
    }

    /// <summary>The help screen's content: every available action that allows it, grouped, with its keys and command.</summary>
    public KeymapHelp CreateHelp() => new(this);

    /// <summary>Opens the help screen as an overlay (esc or <c>q</c> closes it). Does nothing if another overlay is open.</summary>
    public Overlay? ShowHelp(App app, string title = " keys · esc to close ")
    {
        if (app.TopOverlay is not null)
        {
            return null;
        }

        var help = CreateHelp();
        var overlay = new Overlay(help.InPanel(title))
        {
            WidthPercent = 70,
            Height = Math.Min(help.LineCount + 2, Math.Max(5, app.Size.Height - 2)),
            Modal = true,
            DismissOnEscape = true,
            ScrimOpacity = 0.4,
        };
        help.OnClose = () => app.RemoveOverlay(overlay);
        app.PushOverlay(overlay);
        return overlay;
    }
}
