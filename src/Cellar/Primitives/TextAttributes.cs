using System;

namespace Cellar.Primitives;

/// <summary>Text rendition flags that can be combined on a cell.</summary>
[Flags]
public enum TextAttributes
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Blink = 1 << 4,
    Reverse = 1 << 5,
    Strikethrough = 1 << 6,
}
