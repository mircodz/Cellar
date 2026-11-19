using System;
using System.Collections.Generic;
using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Theming;

namespace Cellar.Widgets;

/// <summary>
/// A <see cref="Form"/> in a bordered box with a title and the keys it understands at the bottom, plus how many
/// values are changed. <see cref="Show"/> opens one as a modal overlay and closes it when the form is submitted
/// or cancelled.
/// </summary>
/// <example><code>
/// FormDialog.Show(app, "scale web", [
///     new FormField("replicas", "Replicas", "3") { Kind = FormFieldKind.Number, Min = 1, Max = 20, Integer = true },
///     FormField.Choice("strategy", "Strategy", "rolling", ["rolling", "recreate"]),
/// ], changes => Apply(changes));
/// </code></example>
public sealed class FormDialog : Widget
{
    private readonly Panel _panel;
    private readonly DialogBody _body;
    private App? _app;
    private Overlay? _overlay;
    private int _hintState = -1;

    public FormDialog(string title, IEnumerable<FormField> fields)
    {
        Form = new Form(fields);
        _body = new DialogBody(Form);
        _panel = new Panel(_body, title)
        {
            BorderStyle = BorderStyle.Rounded,
            Background = Theme.Current.Background,
            BorderLineStyle = new Style(Theme.Current.Accent, Theme.Current.Background),
        };
    }

    /// <summary>The form inside, for its fields, callbacks and settings.</summary>
    public Form Form { get; }

    /// <summary>The title in the top border.</summary>
    public string? Title
    {
        get => _panel.Title;
        set => _panel.Title = value;
    }

    /// <summary>The lines the dialog needs to show every field: the form, the key hints and the border.</summary>
    public int PreferredHeight => Form.LineCount + 1 + 2;

    public override bool IsFocusable => true;

    public override bool HasFocus
    {
        get => Form.HasFocus;
        set => Form.HasFocus = value;
    }

    /// <summary>Opens a form dialog as a modal overlay, <paramref name="width"/> columns wide (narrower on a small
    /// screen) and as tall as its fields. Submitting closes it and then calls <paramref name="onSubmit"/> with the
    /// changed values; cancelling closes it and calls <paramref name="onCancel"/>.</summary>
    public static FormDialog Show(
        App app,
        string title,
        IEnumerable<FormField> fields,
        Action<IReadOnlyDictionary<string, string>> onSubmit,
        Action? onCancel = null,
        int width = 72)
    {
        var dialog = new FormDialog(title, fields);
        dialog.Form.OnSubmit = changes =>
        {
            dialog.Close();
            onSubmit(changes);
        };
        dialog.Form.OnCancel = () =>
        {
            dialog.Close();
            onCancel?.Invoke();
        };

        var overlay = new Overlay(dialog)
        {
            Width = width,
            Height = Math.Min(dialog.PreferredHeight, Math.Max(5, app.Size.Height - 2)),
            Modal = true,
            DismissOnEscape = false,
            ScrimOpacity = 0.4,
        };
        dialog._app = app;
        dialog._overlay = overlay;
        app.PushOverlay(overlay);
        return dialog;
    }

    /// <summary>Removes the dialog's overlay, if <see cref="Show"/> opened it.</summary>
    public void Close()
    {
        if (_app is { } app && _overlay is { } overlay)
        {
            app.RemoveOverlay(overlay);
        }

        _app = null;
        _overlay = null;
    }

    public override void Render(Surface surface, Rect area)
    {
        UpdateHints();
        _panel.Render(surface, area);
    }

    public override bool OnEvent(InputEvent e) => Form.OnEvent(e);

    private void UpdateHints()
    {
        bool toggles = Form.Selected?.Kind is FormFieldKind.Choice or FormFieldKind.Flag;
        int changes = Form.ChangeCount;
        int state = (changes << 2) | (Form.IsEditing ? 1 : 0) | (toggles ? 2 : 0);
        if (state == _hintState)
        {
            return;
        }

        _hintState = state;
        // The count goes first so a narrow dialog cuts off hints rather than it.
        var hints = StyledText.Empty();
        if (changes > 0)
        {
            hints.Append($"{changes} change{(changes == 1 ? "" : "s")}  ", new Style(Theme.Current.Warning, Color.Default).Bold);
        }

        hints.Append(Form.IsEditing
            ? DialogBody.KeyHints(("enter", "keep"), ("esc", "cancel"), ("↑↓", "keep and move"))
            : DialogBody.KeyHints((toggles ? "enter/←→" : "enter", "change"), ("u", "revert"), ("ctrl+s", "apply"), ("esc", "close")));
        _body.Hints = hints;
    }
}

/// <summary>A dialog's inside: its content above a line of key hints.</summary>
internal sealed class DialogBody(Widget content) : Widget
{
    public Widget Content { get; } = content;

    public StyledText Hints { get; set; } = StyledText.Empty();

    public override bool IsFocusable => Content.IsFocusable;

    public override bool HasFocus
    {
        get => Content.HasFocus;
        set => Content.HasFocus = value;
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        int hints = area.Height > 1 ? 1 : 0;
        Content.Render(surface, new Rect(area.X, area.Y, area.Width, area.Height - hints));
        if (hints > 0)
        {
            TextRenderer.DrawLine(surface, area.X, area.Bottom - 1, area.Width, Hints, Justify.Left);
        }
    }

    public override bool OnEvent(InputEvent e) => Content.OnEvent(e);

    /// <summary>"key label  key label  " with keys in the accent color and labels dimmed.</summary>
    public static StyledText KeyHints(params ReadOnlySpan<(string Key, string Label)> hints)
    {
        var theme = Theme.Current;
        var keyStyle = new Style(theme.Accent, Color.Default).Bold;
        var labelStyle = new Style(theme.Muted, Color.Default);
        var text = StyledText.Empty();
        foreach (var (key, label) in hints)
        {
            text.Append(key, keyStyle).Append(" " + label + "  ", labelStyle);
        }

        return text;
    }
}
