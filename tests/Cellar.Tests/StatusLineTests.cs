using System;
using Cellar.Primitives;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Widgets;
using Xunit;

namespace Cellar.Tests;

public class StatusLineTests
{
    private static string Line(StatusLine line, int width) => SurfaceText.Row(SurfaceText.Render(line, width), 0);

    [Fact]
    public void Items_AlignLeftAndRight()
    {
        var line = new StatusLine();
        line.Add("docs");
        line.Add("12 rows");
        line.Add("updated", StatusAlignment.Right);
        Assert.Equal("docs  12 rows      updated", Line(line, 26));
    }

    [Fact]
    public void NarrowLine_ShortensLowestPriorityFirst()
    {
        var line = new StatusLine();
        line.Add(new StatusItem("important", priority: 10) { ShortText = "imp" });
        line.Add(new StatusItem("minor", priority: 1) { ShortText = "m" });
        Assert.Equal("important  minor", Line(line, 16));
        Assert.Equal("important  m", Line(line, 12).TrimEnd());
        Assert.Equal("imp  m", Line(line, 8).TrimEnd());
    }

    [Fact]
    public void NarrowLine_DropsLowestPriorityWhenShorteningIsNotEnough()
    {
        var line = new StatusLine();
        line.Add(new StatusItem("keep-this", priority: 5));
        line.Add(new StatusItem("drop-this", priority: 1));
        line.Add(new StatusItem("right", StatusAlignment.Right, priority: 3));
        Assert.Equal("keep-this  right", Line(line, 16));
        Assert.Equal("keep-this", Line(line, 10).TrimEnd());
    }

    [Fact]
    public void Ties_DropTheItemAddedLater()
    {
        var line = new StatusLine();
        line.Add("first");
        line.Add("second");
        Assert.Equal("first", Line(line, 10).TrimEnd());
    }

    [Fact]
    public void HiddenItems_TakeNoSpace()
    {
        var line = new StatusLine();
        line.Add("a");
        line.Add("b").Visible = false;
        line.Add("c");
        Assert.Equal("a  c", Line(line, 4));
    }

    [Fact]
    public void InfoMessage_TakesOverTheLineAndExpires()
    {
        var clock = new ManualClock();
        var line = new StatusLine { Clock = clock, MessageDuration = TimeSpan.FromSeconds(3) };
        line.Add("items");
        line.Show("saved");
        Assert.Equal("saved", Line(line, 10).TrimEnd());

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal("items", Line(line, 10).TrimEnd());
        Assert.Null(line.Message);
    }

    [Fact]
    public void ErrorMessage_StaysUntilDismissed()
    {
        var clock = new ManualClock();
        var line = new StatusLine { Clock = clock, ErrorHint = "esc" };
        line.ShowError("boom");
        clock.Advance(TimeSpan.FromHours(1));
        var text = Line(line, 20);
        Assert.StartsWith("✗ boom", text);
        Assert.EndsWith("esc", text);
        Assert.Equal(Theming.Theme.Current.Error, SurfaceText.Render(line, 20).Get(2, 0).Style.Foreground);

        line.Dismiss();
        Assert.Equal("", Line(line, 20).Trim());
    }

    [Fact]
    public void ExplicitDuration_OverridesTheDefault()
    {
        var clock = new ManualClock();
        var line = new StatusLine { Clock = clock };
        line.Show("careful", StatusLevel.Warning, TimeSpan.FromSeconds(1));
        Assert.StartsWith("! careful", Line(line, 12));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(line.Message);
    }

    [Fact]
    public void ClickingAnItem_RunsItsAction()
    {
        int clicks = 0;
        var line = new StatusLine();
        line.Add("x");
        line.Add(new StatusItem("click") { OnClick = () => clicks++ });
        SurfaceText.Render(line, 20);

        Assert.True(line.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 4, 0, KeyModifiers.None)));
        Assert.True(line.OnEvent(new MouseEvent(MouseEventKind.Up, MouseButton.Left, 4, 0, KeyModifiers.None)));
        Assert.Equal(1, clicks);

        // A click on a plain item isn't handled.
        Assert.False(line.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 0, 0, KeyModifiers.None)));
    }

    [Fact]
    public void Hover_HighlightsClickableItems()
    {
        var line = new StatusLine();
        line.Add(new StatusItem("click") { OnClick = () => { } });
        SurfaceText.Render(line, 10);
        Assert.True(line.OnEvent(new MouseEvent(MouseEventKind.Move, MouseButton.None, 1, 0, KeyModifiers.None)));
        var surface = SurfaceText.Render(line, 10);
        Assert.Equal(Theming.Theme.Current.SelectionBackground, surface.Get(1, 0).Style.Background);
        Assert.False(line.OnEvent(new MouseEvent(MouseEventKind.Move, MouseButton.None, 2, 0, KeyModifiers.None)));
    }

    [Fact]
    public void ClickingAMessage_DismissesIt()
    {
        var line = new StatusLine();
        line.ShowError("boom");
        SurfaceText.Render(line, 10);
        line.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 1, 0, KeyModifiers.None));
        line.OnEvent(new MouseEvent(MouseEventKind.Up, MouseButton.Left, 1, 0, KeyModifiers.None));
        Assert.Null(line.Message);
    }

    [Fact]
    public void Keymap_AddsKeyHintsThatFollowAvailabilityAndRemaps()
    {
        bool editable = true;
        int refreshed = 0;
        var keymap = new Keymap();
        keymap.Add(new AppAction("refresh", "Refresh", () => refreshed++) { Keys = ["ctrl+r"], Footer = "refresh", Priority = 2 });
        keymap.Add(new AppAction("edit", "Edit", () => { }) { Keys = ["e"], Footer = "edit", Priority = 1, When = () => editable });
        keymap.Add(new AppAction("quiet", "Not in footer", () => { }) { Keys = ["z"] });

        var line = new StatusLine { Keymap = keymap };
        Assert.Equal("^r refresh  e edit", Line(line, 18));

        editable = false;
        Assert.Equal("^r refresh", Line(line, 18).Trim());

        keymap.Remap("f5", "refresh");
        Assert.Equal("f5 refresh", Line(line, 18).Trim());

        // Short on space, hints fall back to just the key.
        editable = true;
        Assert.Equal("f5  e", Line(line, 5));

        // Clicking a hint runs its action.
        SurfaceText.Render(line, 18);
        int x = Line(line, 18).IndexOf("f5", StringComparison.Ordinal);
        line.OnEvent(new MouseEvent(MouseEventKind.Down, MouseButton.Left, x, 0, KeyModifiers.None));
        line.OnEvent(new MouseEvent(MouseEventKind.Up, MouseButton.Left, x, 0, KeyModifiers.None));
        Assert.Equal(1, refreshed);
    }

    [Fact]
    public void Badge_AndSpinnerItems()
    {
        var line = new StatusLine();
        line.Add(StatusItem.Badge("NORMAL"));
        var spinner = (SpinnerStatusItem)line.Add(new SpinnerStatusItem("loading"));
        Assert.Equal(" NORMAL ", Line(line, 20).TrimEnd() + " ");

        spinner.Active = true;
        var text = Line(line, 24);
        Assert.StartsWith(" NORMAL   ", text);
        Assert.Contains(" loading", text, StringComparison.Ordinal);
        Assert.Equal(Theming.Theme.Current.Accent, SurfaceText.Render(line, 24).Get(1, 0).Style.Background);
    }

    [Fact]
    public void StyledSpans_KeepTheirColors()
    {
        var line = new StatusLine { Style = new Style(Color.Rgb(1, 2, 3), Color.Rgb(9, 9, 9)) };
        line.Add(new StyledText("a").Append("b", new Style(Color.Rgb(200, 0, 0), Color.Default)));
        var surface = SurfaceText.Render(line, 4);
        Assert.Equal(Color.Rgb(1, 2, 3), surface.Get(0, 0).Style.Foreground);
        Assert.Equal(Color.Rgb(200, 0, 0), surface.Get(1, 0).Style.Foreground);
        Assert.Equal(Color.Rgb(9, 9, 9), surface.Get(1, 0).Style.Background);
    }
}
