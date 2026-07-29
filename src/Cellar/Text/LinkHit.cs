using Cellar.Primitives;

namespace Cellar.Text;

/// <summary>A rendered link's screen location and payload, recorded during draw for hit-testing.</summary>
public readonly struct LinkHit
{
    /// <summary>Row the link was drawn on.</summary>
    public int Y { get; }

    /// <summary>Inclusive start column.</summary>
    public int Start { get; }

    /// <summary>Exclusive end column.</summary>
    public int End { get; }

    /// <summary>The payload from <see cref="StyledText.Link"/>.</summary>
    public object Payload { get; }

    /// <summary>Optional per-link hover style; null lets the widget choose a default.</summary>
    public Style? HoverStyle { get; }

    public LinkHit(int y, int start, int end, object payload, Style? hoverStyle = null)
    {
        Y = y;
        Start = start;
        End = end;
        Payload = payload;
        HoverStyle = hoverStyle;
    }

    /// <summary>Whether cell (x,y) falls within this link's drawn range.</summary>
    public bool Contains(int x, int y) => y == Y && x >= Start && x < End;
}
