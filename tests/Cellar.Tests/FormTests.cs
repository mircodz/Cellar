using System;
using System.Collections.Generic;
using System.Text;
using Cellar.Primitives;
using Cellar.Terminal;
using Cellar.Widgets;
using Xunit;

namespace Cellar.Tests;

public class FormTests
{
    private static KeyEvent K(Key key, KeyModifiers mods = KeyModifiers.None) => new(key, default, mods);
    private static KeyEvent C(char c, KeyModifiers mods = KeyModifiers.None) => new(Key.Char, new Rune(c), mods);

    private static void Type(Form form, string text)
    {
        foreach (var c in text)
        {
            Assert.True(form.OnEvent(C(c)));
        }
    }

    private static Form Settings() => new([
        new FormField("name", "Name", "web") { ReadOnly = true, Section = "General" },
        new FormField("attempts", "Attempts", "3") { Kind = FormFieldKind.Number, Min = 1, Max = 10, Integer = true, Hint = "how many tries", Section = "General" },
        new FormField("timeout", "Timeout", "30s") { Kind = FormFieldKind.Duration, Note = "default 30s", Section = "Timing" },
        FormField.Choice("conflict", "Conflict", "ask", ["ask", "keep", "newest"]),
        FormField.Flag("paused", "Paused", false),
        new FormField("token", "Token", "hunter2") { Kind = FormFieldKind.Secret },
    ])
    { HasFocus = true };

    private static string[] Rows(Form form, int width = 40, int height = 9)
    {
        var surface = SurfaceText.Render(form, width, height);
        var rows = new string[height];
        for (int y = 0; y < height; y++)
        {
            rows[y] = SurfaceText.Row(surface, y).TrimEnd();
        }

        return rows;
    }

    [Fact]
    public void Renders_SectionsLabelsValuesNotesAndTheHint()
    {
        var form = Settings();
        var rows = Rows(form);
        Assert.Equal("General", rows[0]);
        Assert.Equal("  Name      web", rows[1]);
        Assert.Equal("  Attempts  3", rows[2]);
        Assert.Equal("Timing", rows[3]);
        Assert.Equal("  Timeout   30s  (default 30s)", rows[4]);
        Assert.Equal("  Conflict  ask", rows[5]);
        Assert.Equal("  Paused    [ ] off", rows[6]);
        Assert.Equal("  Token     •••••••", rows[7]);
        Assert.Equal("how many tries", rows[8]);
        Assert.Equal("attempts", form.Selected!.Key); // read-only fields are skipped at first
    }

    [Fact]
    public void Enter_EditsInPlace_AndEnterKeepsAValidValue()
    {
        var form = Settings();
        form.OnEvent(K(Key.Enter));
        Assert.True(form.IsEditing);
        Assert.Equal("3", form.EditText);
        form.OnEvent(K(Key.Backspace));
        Type(form, "5");
        form.OnEvent(K(Key.Enter));

        Assert.False(form.IsEditing);
        Assert.Equal("5", form.Selected!.Value);
        Assert.True(form.Selected.IsChanged);
        Assert.Equal("* Attempts  5  was 3", Rows(form)[2]);
    }

    [Fact]
    public void InvalidValues_AreRefusedWithTheReasonInTheFooter()
    {
        var form = Settings();
        form.OnEvent(K(Key.Enter));
        form.OnEvent(K(Key.Backspace));
        Type(form, "12");
        form.OnEvent(K(Key.Enter));
        Assert.True(form.IsEditing);
        Assert.Equal("Attempts: expected 1 to 10", form.Message);
        Assert.Equal("Attempts: expected 1 to 10", Rows(form)[8]);

        // The message follows the edit once it's shown.
        form.OnEvent(K(Key.Backspace));
        Assert.Null(form.Message);
        Type(form, ".5");
        form.OnEvent(K(Key.Enter));
        Assert.Equal("Attempts: expected a whole number", form.Message);

        form.OnEvent(K(Key.Escape));
        Assert.False(form.IsEditing);
        Assert.Equal("3", form.Selected!.Value);
    }

    [Fact]
    public void Durations_AreChecked()
    {
        var field = new FormField("t", "T", "5m") { Kind = FormFieldKind.Duration };
        Assert.Null(field.Error);
        Assert.Null(field.Check("1h30m"));
        Assert.NotNull(field.Check("soon"));
    }

    [Fact]
    public void ChoicesCycle_AndFlagsToggle()
    {
        var form = Settings();
        form.OnEvent(K(Key.Down)); // timeout
        form.OnEvent(K(Key.Down)); // conflict
        form.OnEvent(K(Key.Right));
        Assert.Equal("keep", form.Selected!.Value);
        form.OnEvent(K(Key.Enter));
        Assert.Equal("newest", form.Selected.Value);
        form.OnEvent(K(Key.Right));
        Assert.Equal("ask", form.Selected.Value);
        form.OnEvent(K(Key.Left));
        Assert.Equal("newest", form.Selected.Value);

        form.OnEvent(K(Key.Down)); // paused
        form.OnEvent(C(' '));
        Assert.True(form.Selected.IsOn);
        Assert.Equal("* Paused    [x] on  was [ ] off", Rows(form, 50)[6]);
    }

    [Fact]
    public void U_RevertsTheSelectedField()
    {
        var form = Settings();
        form.OnEvent(K(Key.Enter));
        Type(form, "0");
        form.OnEvent(K(Key.Enter)); // "30" is out of range
        form.OnEvent(K(Key.Escape));
        form.Selected!.Value = "4";
        form.OnEvent(C('u'));
        Assert.Equal("3", form.Selected.Value);
        Assert.Equal(0, form.ChangeCount);
    }

    [Fact]
    public void CtrlS_SubmitsOnlyTheChangedValues()
    {
        var form = Settings();
        IReadOnlyDictionary<string, string>? submitted = null;
        form.OnSubmit = changes => submitted = changes;

        form.OnEvent(C('s', KeyModifiers.Control));
        Assert.Null(submitted);
        Assert.Equal("nothing changed", form.Message);

        form.OnEvent(K(Key.Down));
        form.OnEvent(K(Key.Down));
        form.OnEvent(K(Key.Right)); // conflict → keep
        form.OnEvent(C('s', KeyModifiers.Control));
        Assert.Equal(new Dictionary<string, string> { ["conflict"] = "keep" }, submitted);
    }

    [Fact]
    public void Submit_SelectsTheFirstInvalidField()
    {
        var form = Settings();
        form.Fields[5].Value = "";
        var required = new Form([new FormField("a", "A", "x") { Required = true }, new FormField("b", "B", "y")]) { HasFocus = true };
        required.Fields[0].Value = "";
        required.SelectedIndex = 1;
        Assert.False(required.Submit());
        Assert.Equal(0, required.SelectedIndex);
        Assert.Equal("A: required", required.Message);
    }

    [Fact]
    public void Esc_WithChanges_NeedsASecondEsc()
    {
        var form = Settings();
        int cancelled = 0;
        form.OnCancel = () => cancelled++;
        form.Fields[1].Value = "4";

        form.OnEvent(K(Key.Escape));
        Assert.Equal(0, cancelled);
        Assert.Equal("esc again to discard 1 change", form.Message);

        form.OnEvent(K(Key.Down)); // anything else resets it
        form.OnEvent(K(Key.Escape));
        Assert.Equal(0, cancelled);
        form.OnEvent(K(Key.Escape));
        Assert.Equal(1, cancelled);

        var clean = Settings();
        clean.OnCancel = () => cancelled++;
        clean.OnEvent(K(Key.Escape));
        Assert.Equal(2, cancelled);
    }

    [Fact]
    public void ReadOnlyFields_CannotBeEdited()
    {
        var form = Settings();
        form.SelectedIndex = 0;
        form.OnEvent(K(Key.Enter));
        Assert.False(form.IsEditing);
        Assert.Equal("Name can't be changed here", form.Message);
    }

    [Fact]
    public void Secrets_StayMaskedWhileEditing()
    {
        var form = Settings();
        form.SelectedIndex = 5;
        form.OnEvent(K(Key.Enter));
        Assert.Equal("  Token     •••••••", Rows(form)[7]);
        Assert.Equal("hunter2", form.EditText);
    }

    [Fact]
    public void Arrows_WhileEditing_KeepTheValueAndMove()
    {
        var form = Settings();
        form.OnEvent(K(Key.Enter));
        form.OnEvent(K(Key.Backspace));
        Type(form, "7");
        form.OnEvent(K(Key.Down));
        Assert.False(form.IsEditing);
        Assert.Equal("7", form.Fields[1].Value);
        Assert.Equal("timeout", form.Selected!.Key);
    }

    [Fact]
    public void Scrolls_ToKeepTheSelectionAndItsHeadingInView()
    {
        var form = Settings();
        form.SelectedIndex = 5;
        var rows = Rows(form, 40, 4);
        Assert.Equal("  Token     •••••••", rows[2]);
        form.SelectedIndex = 2;
        rows = Rows(form, 40, 4);
        Assert.Equal("Timing", rows[0]);
    }

    [Fact]
    public void Dialog_ShowsTitleAndKeyHints_AndClosesOnSubmit()
    {
        using var app = new App(new FakeTerminal { Size = new(60, 20) });
        IReadOnlyDictionary<string, string>? submitted = null;
        var dialog = FormDialog.Show(app, "sync settings", [new FormField("attempts", "Attempts", "3")], changes => submitted = changes);
        Assert.Same(dialog, app.TopOverlay!.Content);
        Assert.Equal(5, dialog.PreferredHeight); // border, field, footer, hints, border

        var surface = SurfaceText.Render(dialog, 60, 5);
        Assert.Contains("sync settings", SurfaceText.Row(surface, 0));
        Assert.Contains("enter change  u revert  ctrl+s apply  esc close", SurfaceText.Row(surface, 3));

        dialog.OnEvent(K(Key.Enter));
        dialog.OnEvent(C('4'));
        dialog.OnEvent(K(Key.Enter));
        surface = SurfaceText.Render(dialog, 60, 5);
        Assert.Contains("1 change", SurfaceText.Row(surface, 3));

        dialog.OnEvent(C('s', KeyModifiers.Control));
        Assert.Equal("34", submitted!["attempts"]);
        Assert.Null(app.TopOverlay);
    }
}
