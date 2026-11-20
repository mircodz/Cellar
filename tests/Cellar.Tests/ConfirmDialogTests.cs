using System.Collections.Generic;
using System.Text;
using Cellar.Terminal;
using Cellar.Text;
using Cellar.Widgets;
using Xunit;

namespace Cellar.Tests;

public class ConfirmDialogTests
{
    private static KeyEvent K(Key key) => new(key, default, KeyModifiers.None);
    private static KeyEvent C(char c, KeyModifiers mods = KeyModifiers.None) => new(Key.Char, new Rune(c), mods);

    private static string[] Rows(Widget widget, int width, int height)
    {
        var surface = SurfaceText.Render(widget, width, height);
        var rows = new string[height];
        for (int y = 0; y < height; y++)
        {
            rows[y] = SurfaceText.Row(surface, y).TrimEnd();
        }

        return rows;
    }

    [Fact]
    public void Normal_YConfirms_NCancels_AndTogglesFlip()
    {
        var results = new List<ConfirmResult>();
        var dialog = new ConfirmDialog("Empty trash?")
        {
            Details = ["3 files are removed for good."],
            Toggles = [new ConfirmToggle('f', "force")],
            OnResult = results.Add,
            HasFocus = true,
        };

        var rows = Rows(dialog, 40, dialog.PreferredHeight(40));
        Assert.Equal(7, dialog.PreferredHeight(40));
        Assert.Contains("confirm", rows[0]);
        Assert.Equal("│ Empty trash?                         │", rows[1]);
        Assert.Contains("3 files are removed", rows[2]);
        Assert.Contains("[ ] f  force", rows[4]);
        Assert.Contains("y/enter confirm  n/esc cancel", rows[5]);

        dialog.OnEvent(C('f'));
        Assert.Contains("[x] f  force", Rows(dialog, 40, 7)[4]);
        dialog.OnEvent(C('y'));
        Assert.True(results[0].Confirmed);
        Assert.True(results[0].IsOn('f'));

        dialog.OnEvent(C('n'));
        Assert.False(results[1].Confirmed);
        dialog.OnEvent(K(Key.Escape));
        Assert.False(results[2].Confirmed);
        Assert.True(dialog.OnEvent(C('x'))); // modal: other keys are swallowed
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void Destructive_NeedsTheNameTyped()
    {
        var results = new List<ConfirmResult>();
        var dialog = new ConfirmDialog("Delete report.pdf?")
        {
            Risk = ConfirmRisk.Destructive,
            ConfirmText = "report.pdf",
            Toggles = [new ConfirmToggle('f', "force")],
            OnResult = results.Add,
            HasFocus = true,
        };

        // y and f are text here; alt+f flips the toggle.
        dialog.OnEvent(C('y'));
        Assert.Empty(results);
        Assert.Equal("y", dialog.TypedText);
        dialog.OnEvent(K(Key.Enter));
        Assert.Empty(results);
        Assert.Contains("that doesn't match; esc cancels", string.Join("\n", Rows(dialog, 50, dialog.PreferredHeight(50))));

        dialog.OnEvent(K(Key.Backspace));
        foreach (var c in "report.pdf")
        {
            dialog.OnEvent(C(c));
        }

        dialog.OnEvent(C('f', KeyModifiers.Alt));
        var rows = string.Join("\n", Rows(dialog, 50, dialog.PreferredHeight(50)));
        Assert.Contains("confirm · destructive", rows);
        Assert.Contains("[x] alt+f  force", rows);
        Assert.Contains("Type report.pdf to confirm.", rows);
        Assert.Contains("report.pdf", rows);
        dialog.OnEvent(K(Key.Enter));
        Assert.True(results[0].Confirmed);
        Assert.True(results[0].IsOn('f'));
    }

    [Fact]
    public void Show_OpensAModalOverlay_AndClosesOnAnswer()
    {
        using var app = new App(new FakeTerminal());
        ConfirmResult? result = null;
        ConfirmDialog.Show(app, new ConfirmDialog("Go?") { Details = [new StyledText("really")] }, r => result = r);
        Assert.True(app.TopOverlay!.Modal);
        Assert.True(app.TopOverlay.Content.OnEvent(K(Key.Enter)));
        Assert.True(result!.Confirmed);
        Assert.Null(app.TopOverlay);
    }

    [Fact]
    public void ChangingTheHeadline_RedrawsIt()
    {
        var dialog = new ConfirmDialog("Delete a.txt?");
        Assert.Contains("Delete a.txt?", Rows(dialog, 40, 6)[1]);
        dialog.Headline = "Delete b.txt?";
        Assert.Contains("Delete b.txt?", Rows(dialog, 40, 6)[1]);
    }

    [Fact]
    public void ConfirmText_IsComparedWithoutItsOwnSurroundingSpaces()
    {
        var results = new List<ConfirmResult>();
        var dialog = new ConfirmDialog("Delete?")
        {
            Risk = ConfirmRisk.Destructive,
            ConfirmText = " web-01 ",
            OnResult = results.Add,
            HasFocus = true,
        };

        foreach (var c in "web-01")
        {
            dialog.OnEvent(C(c));
        }

        Assert.True(dialog.Confirm());
        Assert.True(results[0].Confirmed);
    }

    [Fact]
    public void LongDetails_ScrollSoNothingIsConfirmedUnseen()
    {
        var details = new List<StyledText>();
        for (int i = 1; i <= 12; i++)
        {
            details.Add(new StyledText($"detail {i}"));
        }

        var dialog = new ConfirmDialog("Apply?") { Details = details, HasFocus = true };
        var rows = string.Join("\n", Rows(dialog, 40, 8));
        Assert.Contains("Apply?", rows);
        Assert.DoesNotContain("detail 12", rows);

        Assert.True(dialog.OnEvent(K(Key.PageDown)));
        Assert.True(dialog.OnEvent(K(Key.PageDown)));
        Assert.True(dialog.OnEvent(K(Key.PageDown)));
        rows = string.Join("\n", Rows(dialog, 40, 8));
        Assert.Contains("detail 12", rows);
        Assert.DoesNotContain("Apply?", rows);

        dialog.OnEvent(new MouseEvent(MouseEventKind.Wheel, MouseButton.WheelUp, 1, 1, KeyModifiers.None));
        dialog.OnEvent(new MouseEvent(MouseEventKind.Wheel, MouseButton.WheelUp, 1, 1, KeyModifiers.None));
        dialog.OnEvent(new MouseEvent(MouseEventKind.Wheel, MouseButton.WheelUp, 1, 1, KeyModifiers.None));
        dialog.OnEvent(new MouseEvent(MouseEventKind.Wheel, MouseButton.WheelUp, 1, 1, KeyModifiers.None));
        dialog.OnEvent(new MouseEvent(MouseEventKind.Wheel, MouseButton.WheelUp, 1, 1, KeyModifiers.None));
        Assert.Contains("Apply?", string.Join("\n", Rows(dialog, 40, 8)));
    }
}
