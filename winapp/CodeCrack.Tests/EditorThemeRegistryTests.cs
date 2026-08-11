using System.Linq;
using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class EditorThemeRegistryTests
{
    [Fact]
    public void All_ContainsTheSixMenuThemes()
    {
        var keys = EditorThemeRegistry.All.Select(t => t.Key).ToArray();
        Assert.Equal(
            new[] { "system", "atom-one-light", "atom-one-dark", "solarized-dark", "monokai", "github" },
            keys);
    }

    [Fact]
    public void Resolve_SystemFollowsOsAppearance()
    {
        Assert.Equal("atom-one-dark", EditorThemeRegistry.Resolve("system", systemIsDark: true).Key);
        Assert.Equal("atom-one-light", EditorThemeRegistry.Resolve("system", systemIsDark: false).Key);
    }

    [Fact]
    public void Resolve_NeverReturnsSystem()
        => Assert.All(EditorThemeRegistry.All,
            t => Assert.NotEqual("system", EditorThemeRegistry.Resolve(t.Key, true).Key));

    [Theory]
    [InlineData("atom-one-dark", true, "SolarizedDark_or_DarkPlus")]
    [InlineData("monokai", true, "Monokai")]
    [InlineData("solarized-dark", true, "SolarizedDark")]
    [InlineData("github", false, "Light")]
    public void Resolve_CarriesDarknessAndTmId(string key, bool isDark, string _)
    {
        var spec = EditorThemeRegistry.Resolve(key, systemIsDark: true);
        Assert.Equal(isDark, spec.IsDark);
        Assert.False(string.IsNullOrWhiteSpace(spec.TmThemeId));
        Assert.StartsWith("#", spec.Background);
        Assert.StartsWith("#", spec.Caret);
    }

    [Fact]
    public void Resolve_UnknownKeyFallsBackToSystem()
        => Assert.Equal("atom-one-light", EditorThemeRegistry.Resolve("bogus", systemIsDark: false).Key);
}
