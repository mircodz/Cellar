using Cellar.Primitives;
using Cellar.Rendering;
using Cellar.Widgets;

namespace Cellar.Demo;

/// <summary>
/// A demo-only widget that paints a smooth horizontal gradient across its width using the
/// framework's <see cref="Color.Gradient"/> helper — shows off truecolor interpolation.
/// </summary>
internal sealed class GradientBar : Widget
{
    private readonly Color[] _stops;
    private readonly string _glyph;

    public GradientBar(string glyph, params Color[] stops)
    {
        _glyph = glyph;
        _stops = stops;
    }

    public override void Render(Surface surface, Rect area)
    {
        if (area.IsEmpty)
        {
            return;
        }

        var ramp = Color.Gradient(area.Width, _stops);
        for (int i = 0; i < area.Width; i++)
        {
            var style = new Style(ramp[i], Color.Default);
            for (int y = area.Top; y < area.Bottom; y++)
            {
                surface.Set(area.X + i, y, Cell.FromGrapheme(_glyph, style));
            }
        }
    }
}
