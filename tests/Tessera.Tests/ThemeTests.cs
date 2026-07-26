using System;
using Tessera.Primitives;
using Tessera.Theming;

namespace Tessera.Tests;

public class ThemeTests
{
    [Fact]
    public void DefaultCurrent_IsBase16Dark()
    {
        Assert.Equal("base16-dark", Theme.Current.Name);
    }

    [Fact]
    public void Current_CanBeSwapped()
    {
        var original = Theme.Current;
        try
        {
            Theme.Current = BuiltIn.Light;
            Assert.Equal("light", Theme.Current.Name);
        }
        finally
        {
            Theme.Current = original;
        }
    }

    [Fact]
    public void Current_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => Theme.Current = null!);
    }

    [Fact]
    public void SemanticStyles_DeriveFromRoles()
    {
        var t = BuiltIn.Dark;
        Assert.Equal(t.Accent, t.AccentStyle.Foreground);
        Assert.Equal(t.Muted, t.MutedStyle.Foreground);
        Assert.Equal(t.SelectionForeground, t.SelectionStyle.Foreground);
        Assert.Equal(t.SelectionBackground, t.SelectionStyle.Background);
        Assert.True((t.HeaderStyle.Attributes & TextAttributes.Bold) != 0);
    }
}
