using System.Linq;
using mdaiAgent;
using Xunit;

namespace mdaiAgent.Tests;

public class LspLanguageRegistryTests
{
    [Theory]
    [InlineData("tsx", "ts")]
    [InlineData("jsx", "js")]
    [InlineData("scss", "css")]
    [InlineData("less", "css")]
    [InlineData("htm", "html")]
    [InlineData("hpp", "cpp")]
    [InlineData("kts", "kt")]
    public void NormalizeExtension_UsesCanonicalLanguage(string extension, string expected)
    {
        Assert.Equal(expected, LspLanguageRegistry.NormalizeExtension(extension));
    }

    [Fact]
    public void RegistryDefinitionsHaveUniqueCanonicalExtensions()
    {
        var extensions = LspLanguageRegistry.All.Select(definition => definition.Extension).ToList();

        Assert.Equal(extensions.Count, extensions.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(LspLanguageRegistry.All, definition => definition.Extension == "html");
        Assert.Contains(LspLanguageRegistry.All, definition => definition.Extension == "cs");
        Assert.Contains(LspLanguageRegistry.All, definition => definition.Extension == "py");
    }

    [Fact]
    public void PhpDefinitionIncludesFallbackExecutable()
    {
        Assert.True(LspLanguageRegistry.TryGet("php", out var definition));
        Assert.Contains("intelephense", LspLanguageRegistry.GetExecutableNames(definition!));
        Assert.Contains("phpactor", LspLanguageRegistry.GetExecutableNames(definition!));
    }

    [Fact]
    public void HtmlAndCssDefinitionsPreferModernServerCommands()
    {
        Assert.True(LspLanguageRegistry.TryGet("html", out var htmlDefinition));
        Assert.True(LspLanguageRegistry.TryGet("css", out var cssDefinition));

        Assert.Equal("vscode-html-language-server", htmlDefinition!.ExecutableName);
        Assert.Equal("vscode-css-language-server", cssDefinition!.ExecutableName);
        Assert.Contains("html-languageserver", LspLanguageRegistry.GetExecutableNames(htmlDefinition));
        Assert.Contains("css-languageserver", LspLanguageRegistry.GetExecutableNames(cssDefinition));
    }
}
