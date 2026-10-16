# Cellar

Cellar is a retained-mode terminal UI framework for .NET 8. It provides cell-diffed ANSI
rendering, styled text, constraint-based layouts, themes, mouse and keyboard input, and a
collection of interactive widgets.

![Demo](https://github.com/user-attachments/assets/27193ea9-bfba-4a50-b022-30dcac493877)

## Install

```sh
dotnet add package Cellar
```

## Quick start

```csharp
using Cellar.Layout;
using Cellar.Terminal;
using Cellar.Widgets;

using var terminal = new AnsiTerminal();
using var app = new App(terminal);

app.Root = new Stack(Direction.Vertical)
    .Add(new Panel(new Label("Hello, Cellar!"), " Welcome "), Constraint.Fill())
    .Add(new Label("Press q to quit"), Constraint.Length(1));

// Gets the keys no widget handled, so q still reaches a focused text box.
app.OnUnhandledEvent = input =>
{
    if (input is KeyEvent { IsChar: true } key && key.Rune.Value == 'q')
    {
        app.Quit();
        return true;
    }

    return false;
};

await app.RunAsync();
```
