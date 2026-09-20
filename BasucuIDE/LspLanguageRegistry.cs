using System;
using System.Collections.Generic;
using System.Linq;

namespace mdaiAgent;

public sealed record LspLanguageDefinition(
    string Extension,
    string LanguageId,
    string ExecutableName,
    string[] Arguments,
    string[] ExecutableAliases);

public static class LspLanguageRegistry
{
    private static readonly IReadOnlyDictionary<string, LspLanguageDefinition> Definitions =
        new Dictionary<string, LspLanguageDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["py"] = new("py", "python", "pyright-langserver", new[] { "--stdio" }, new[] { "pyright" }),
            ["ts"] = new("ts", "typescript", "typescript-language-server", new[] { "--stdio" }, Array.Empty<string>()),
            ["js"] = new("js", "javascript", "typescript-language-server", new[] { "--stdio" }, Array.Empty<string>()),
            ["css"] = new("css", "css", "vscode-css-language-server", new[] { "--stdio" }, new[] { "css-languageserver" }),
            ["html"] = new("html", "html", "vscode-html-language-server", new[] { "--stdio" }, new[] { "html-languageserver" }),
            ["java"] = new("java", "java", "jdtls", new[] { "-data", "%temp%/jdtls_workspace" }, Array.Empty<string>()),
            ["dart"] = new("dart", "dart", "dart", new[] { "language-server", "--protocol=lsp" }, Array.Empty<string>()),
            ["cs"] = new("cs", "csharp", "omnisharp", new[] { "-lsp" }, Array.Empty<string>()),
            ["cpp"] = new("cpp", "cpp", "clangd", new[] { "--background-index" }, Array.Empty<string>()),
            ["c"] = new("c", "c", "clangd", new[] { "--background-index" }, Array.Empty<string>()),
            ["go"] = new("go", "go", "gopls", Array.Empty<string>(), Array.Empty<string>()),
            ["rs"] = new("rs", "rust", "rust-analyzer", Array.Empty<string>(), Array.Empty<string>()),
            ["kt"] = new("kt", "kotlin", "kotlin-language-server", Array.Empty<string>(), Array.Empty<string>()),
            ["php"] = new("php", "php", "intelephense", new[] { "--stdio" }, new[] { "phpactor" }),
            ["rb"] = new("rb", "ruby", "solargraph", new[] { "stdio" }, Array.Empty<string>()),
            ["sql"] = new("sql", "sql", "sqls", Array.Empty<string>(), Array.Empty<string>()),
            ["swift"] = new("swift", "swift", "sourcekit-lsp", Array.Empty<string>(), Array.Empty<string>())
        };

    private static readonly IReadOnlyDictionary<string, string> ExtensionAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tsx"] = "ts",
            ["jsx"] = "js",
            ["scss"] = "css",
            ["sass"] = "css",
            ["less"] = "css",
            ["htm"] = "html",
            ["cxx"] = "cpp",
            ["cc"] = "cpp",
            ["h"] = "cpp",
            ["hpp"] = "cpp",
            ["kts"] = "kt"
        };

    public static IReadOnlyCollection<LspLanguageDefinition> All => Definitions.Values.ToArray();

    public static string NormalizeExtension(string? extension)
    {
        var normalized = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return ExtensionAliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : normalized;
    }

    public static bool TryGet(string? extension, out LspLanguageDefinition definition)
    {
        return Definitions.TryGetValue(NormalizeExtension(extension), out definition!);
    }

    public static string[] GetExecutableNames(LspLanguageDefinition definition)
    {
        return new[] { definition.ExecutableName }
            .Concat(definition.ExecutableAliases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
