using System;

namespace Tessera.Widgets;

/// <summary>
/// Lets a widget request a repaint from inside itself without holding an <see cref="App"/>
/// reference. Works like <see cref="Theming.Theme.Current"/>: a running <see cref="App"/> wires
/// its <see cref="App.Invalidate"/> in for the duration of the loop and clears it on exit.
/// </summary>
/// <remarks>
/// Assumes one live <see cref="App"/> per process (true for a TUI). <see cref="Request"/> is just
/// a null-check and a delegate call, safe from any thread. The wired <see cref="App.Invalidate"/>
/// goes through the app's wake/coalesce path, so a burst of requests collapses into one frame.
/// </remarks>
public static class Repaint
{
    /// <summary>The active app's invalidate hook, or null when no app is pumping.</summary>
    internal static volatile Action? Callback;

    /// <summary>Requests a coalesced repaint. Safe from any thread. No-op if no app is running.</summary>
    public static void Request() => Callback?.Invoke();
}
