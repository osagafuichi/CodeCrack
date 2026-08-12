using CodeCrack.App.Core.Editor;
using Xunit;

namespace CodeCrack.Tests;

public class LanguageMapTests
{
    [Theory]
    [InlineData("py", "source.python")]
    [InlineData("pyw", "source.python")]
    [InlineData("js", "source.js")]
    [InlineData("mjs", "source.js")]
    [InlineData("ts", "source.ts")]
    [InlineData("cs", "source.cs")]
    [InlineData("java", "source.java")]
    [InlineData("go", "source.go")]
    [InlineData("rs", "source.rust")]
    [InlineData("rb", "source.ruby")]
    [InlineData("json", "source.json")]
    [InlineData("md", "text.html.markdown")]
    [InlineData("yaml", "source.yaml")]
    [InlineData("sh", "source.shell")]
    public void ScopeForExtension_MapsKnown(string ext, string expected)
        => Assert.Equal(expected, LanguageMap.ScopeForExtension(ext));

    [Theory]
    [InlineData("PY", "source.python")]   // case-insensitive
    [InlineData(".py", "source.python")]  // leading dot tolerated
    public void ScopeForExtension_Normalizes(string ext, string expected)
        => Assert.Equal(expected, LanguageMap.ScopeForExtension(ext));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bin")]
    [InlineData("xyz")]
    public void ScopeForExtension_UnknownReturnsNull(string? ext)
        => Assert.Null(LanguageMap.ScopeForExtension(ext));
}
