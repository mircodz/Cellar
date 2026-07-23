using Tessera.Primitives;
using Tessera.Rendering;
using Tessera.Terminal;
using Tessera.Text;

namespace Tessera.Widgets;

/// <summary>Horizontal alignment for text within a region.</summary>
public enum Alignment { Left, Center, Right }

/// <summary>
/// A text label over the styled-text engine: justifies a <see cref="StyledText"/> and
/// optionally word-wraps to fill its region. Non-wrapped labels support clickable/hoverable
/// links (via <see cref="StyledText.Link"/>).
/// </summary>
public sealed class Label : Widget
{
    public StyledText Content { get; set; }
    public Justify Justify { get; set; }

    /// <summary>When true, the text wraps across multiple lines to fill the region height.</summary>
    public bool Wrap { get; set; }

    public Label(StyledText content, Justify justify = Justify.Left, bool wrap = false)
    {
        Content = content;
        Justify = justify;
        Wrap = wrap;
    }

    /// <summary>Convenience for a single-style label from a plain string.</summary>
    public static Label Plain(string text, Style style, Alignment alignment = Alignment.Left)
    {
        var justify = alignment switch
        {
            Alignment.Center => Justify.Center,
            Alignment.Right => Justify.Right,
            _ => Justify.Left,
        };

        return new Label(new StyledText(text, style), justify);
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        if (Wrap)
        {
            TextRenderer.DrawBlock(surface, area, Content, Justify);
        }
        else
        {
            ClearLinks();
            TextRenderer.DrawLine(surface, area.X, area.Y, area.Width, Content, Justify,
                Content.HasLinks ? Links : null);
            DrawHoverEmphasis(surface);
        }
    }

    public override bool OnEvent(InputEvent e) => e switch
    {
        MouseEvent { Kind: MouseEventKind.Move } m => UpdateHover(m.X, m.Y),
        MouseEvent { Kind: MouseEventKind.Down } m => DispatchLinkClick(m.X, m.Y),
        _ => false
    };
}
