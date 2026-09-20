using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace mdaiAgent.Services;

/// <summary>
/// Roslyn-based semantic code chunking for RAG (Retrieval-Augmented Generation)
/// Breaks source code into meaningful chunks (classes, methods, properties, etc.)
/// for vector embedding and context retrieval.
/// </summary>
public class CodeChunker
{
    /// <summary>
    /// Represents a chunk of code for embedding
    /// </summary>
    public record CodeChunk(
        string FilePath,
        string Type,           // "class", "method", "property", "namespace", etc.
        string Name,           // Qualified name (e.g., "MyNamespace.MyClass.MyMethod")
        string Code,           // Source code text
        int StartLine,
        int EndLine,
        int StartColumn,
        int EndColumn,
        string Hash            // SHA256 hash for deduplication
    );

    /// <summary>
    /// Parse source code file and return semantic chunks
    /// </summary>
    public static List<CodeChunk> ChunkFile(string filePath, string sourceCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chunks = new List<CodeChunk>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Parse C# code using Roslyn
            var tree = CSharpSyntaxTree.ParseText(sourceCode, cancellationToken: cancellationToken);
            var root = tree.GetRoot(cancellationToken) as CompilationUnitSyntax;

            cancellationToken.ThrowIfCancellationRequested();
            // Extract namespace-level chunks
            var namespaceChunks = ExtractNamespaceChunks(root, filePath, sourceCode, cancellationToken);
            chunks.AddRange(namespaceChunks);

            cancellationToken.ThrowIfCancellationRequested();
            // Extract class/struct/interface chunks
            var typeChunks = ExtractTypeChunks(root, filePath, sourceCode, null, cancellationToken);
            chunks.AddRange(typeChunks);

            return chunks;
        }
        catch (OperationCanceledException)
        {
            Logger.LogInfo($"Code chunking canceled for {filePath}");
            return new List<CodeChunk>();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Code chunking failed for {filePath}: {ex.Message}");
            // Fallback: return entire file as single chunk if parsing fails
            return new List<CodeChunk>
            {
                new CodeChunk(
                    FilePath: filePath,
                    Type: "file",
                    Name: System.IO.Path.GetFileNameWithoutExtension(filePath),
                    Code: sourceCode,
                    StartLine: 1,
                    EndLine: sourceCode.Split('\n').Length,
                    StartColumn: 1,
                    EndColumn: 1,
                    Hash: ComputeHash(sourceCode)
                )
            };
        }
    }

    /// <summary>
    /// Extract namespace-level chunks
    /// </summary>
    private static List<CodeChunk> ExtractNamespaceChunks(CompilationUnitSyntax root, string filePath, string sourceCode, CancellationToken cancellationToken = default)
    {
        var chunks = new List<CodeChunk>();

        foreach (var ns in root.Members.OfType<NamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var namespaceName = ns.Name.ToString();
            var code = ExtractNodeCode(sourceCode, ns);

            chunks.Add(new CodeChunk(
                FilePath: filePath,
                Type: "namespace",
                Name: namespaceName,
                Code: code,
                StartLine: root.SyntaxTree.GetLineSpan(ns.Span).StartLinePosition.Line + 1,
                EndLine: root.SyntaxTree.GetLineSpan(ns.Span).EndLinePosition.Line + 1,
                StartColumn: root.SyntaxTree.GetLineSpan(ns.Span).StartLinePosition.Character + 1,
                EndColumn: root.SyntaxTree.GetLineSpan(ns.Span).EndLinePosition.Character + 1,
                Hash: ComputeHash(code)
            ));
        }

        return chunks;
    }

    /// <summary>
    /// Extract type (class, struct, interface, record, enum) chunks
    /// </summary>
    private static List<CodeChunk> ExtractTypeChunks(
        SyntaxNode root,
        string filePath,
        string sourceCode,
        string? parentName,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<CodeChunk>();

        // Classes
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var qualifiedName = parentName != null
                ? $"{parentName}.{classDecl.Identifier.Text}"
                : classDecl.Identifier.Text;

            var code = ExtractNodeCode(sourceCode, classDecl);
            var lineSpan = ((CSharpSyntaxTree)classDecl.SyntaxTree).GetLineSpan(classDecl.Span);

            chunks.Add(new CodeChunk(
                FilePath: filePath,
                Type: "class",
                Name: qualifiedName,
                Code: code,
                StartLine: lineSpan.StartLinePosition.Line + 1,
                EndLine: lineSpan.EndLinePosition.Line + 1,
                StartColumn: lineSpan.StartLinePosition.Character + 1,
                EndColumn: lineSpan.EndLinePosition.Character + 1,
                Hash: ComputeHash(code)
            ));

            // Extract methods from class
            var methodChunks = ExtractMethodChunks(classDecl, filePath, sourceCode, qualifiedName, cancellationToken);
            chunks.AddRange(methodChunks);

            // Extract properties from class
            var propertyChunks = ExtractPropertyChunks(classDecl, filePath, sourceCode, qualifiedName, cancellationToken);
            chunks.AddRange(propertyChunks);
        }

        // Structs
        foreach (var structDecl in root.DescendantNodes().OfType<StructDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var qualifiedName = parentName != null
                ? $"{parentName}.{structDecl.Identifier.Text}"
                : structDecl.Identifier.Text;

            var code = ExtractNodeCode(sourceCode, structDecl);
            var lineSpan = ((CSharpSyntaxTree)structDecl.SyntaxTree).GetLineSpan(structDecl.Span);

            chunks.Add(new CodeChunk(
                FilePath: filePath,
                Type: "struct",
                Name: qualifiedName,
                Code: code,
                StartLine: lineSpan.StartLinePosition.Line + 1,
                EndLine: lineSpan.EndLinePosition.Line + 1,
                StartColumn: lineSpan.StartLinePosition.Character + 1,
                EndColumn: lineSpan.EndLinePosition.Character + 1,
                Hash: ComputeHash(code)
            ));
        }

        // Interfaces
        foreach (var interfaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var qualifiedName = parentName != null
                ? $"{parentName}.{interfaceDecl.Identifier.Text}"
                : interfaceDecl.Identifier.Text;

            var code = ExtractNodeCode(sourceCode, interfaceDecl);
            var lineSpan = ((CSharpSyntaxTree)interfaceDecl.SyntaxTree).GetLineSpan(interfaceDecl.Span);

            chunks.Add(new CodeChunk(
                FilePath: filePath,
                Type: "interface",
                Name: qualifiedName,
                Code: code,
                StartLine: lineSpan.StartLinePosition.Line + 1,
                EndLine: lineSpan.EndLinePosition.Line + 1,
                StartColumn: lineSpan.StartLinePosition.Character + 1,
                EndColumn: lineSpan.EndLinePosition.Character + 1,
                Hash: ComputeHash(code)
            ));
        }

        return chunks;
    }

    /// <summary>
    /// Extract method chunks from a type
    /// </summary>
    private static List<CodeChunk> ExtractMethodChunks(
        TypeDeclarationSyntax typeDecl,
        string filePath,
        string sourceCode,
        string parentName,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<CodeChunk>();

        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var methodName = $"{parentName}.{method.Identifier.Text}";
            var code = ExtractNodeCode(sourceCode, method);
            var lineSpan = ((CSharpSyntaxTree)method.SyntaxTree).GetLineSpan(method.Span);

            // Only include non-trivial methods (> 2 lines)
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line;
            if (lineCount > 2)
            {
                chunks.Add(new CodeChunk(
                    FilePath: filePath,
                    Type: "method",
                    Name: methodName,
                    Code: code,
                    StartLine: lineSpan.StartLinePosition.Line + 1,
                    EndLine: lineSpan.EndLinePosition.Line + 1,
                    StartColumn: lineSpan.StartLinePosition.Character + 1,
                    EndColumn: lineSpan.EndLinePosition.Character + 1,
                    Hash: ComputeHash(code)
                ));
            }
        }

        return chunks;
    }

    /// <summary>
    /// Extract property chunks from a type
    /// </summary>
    private static List<CodeChunk> ExtractPropertyChunks(
        TypeDeclarationSyntax typeDecl,
        string filePath,
        string sourceCode,
        string parentName,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<CodeChunk>();

        foreach (var property in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var propName = $"{parentName}.{property.Identifier.Text}";
            var code = ExtractNodeCode(sourceCode, property);
            var lineSpan = ((CSharpSyntaxTree)property.SyntaxTree).GetLineSpan(property.Span);

            chunks.Add(new CodeChunk(
                FilePath: filePath,
                Type: "property",
                Name: propName,
                Code: code,
                StartLine: lineSpan.StartLinePosition.Line + 1,
                EndLine: lineSpan.EndLinePosition.Line + 1,
                StartColumn: lineSpan.StartLinePosition.Character + 1,
                EndColumn: lineSpan.EndLinePosition.Character + 1,
                Hash: ComputeHash(code)
            ));
        }

        return chunks;
    }

    /// <summary>
    /// Extract source code text for a syntax node
    /// </summary>
    private static string ExtractNodeCode(string sourceCode, SyntaxNode node)
    {
        var span = node.Span;
        return sourceCode.Substring(span.Start, span.Length);
    }

    /// <summary>
    /// Compute SHA256 hash of code for deduplication
    /// </summary>
    public static string ComputeHash(string code)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(code));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    /// <summary>
    /// Check if two chunks are identical (same code hash)
    /// </summary>
    public static bool IsDuplicate(CodeChunk chunk1, CodeChunk chunk2)
    {
        return chunk1.Hash == chunk2.Hash;
    }

    /// <summary>
    /// Merge similar chunks if needed (e.g., for context window optimization)
    /// </summary>
    public static CodeChunk MergeChunks(List<CodeChunk> chunks, string mergedName)
    {
        var mergedCode = string.Join("\n\n", chunks.Select(c => c.Code));
        var startLine = chunks.First().StartLine;
        var endLine = chunks.Last().EndLine;

        return new CodeChunk(
            FilePath: chunks.First().FilePath,
            Type: "merged",
            Name: mergedName,
            Code: mergedCode,
            StartLine: startLine,
            EndLine: endLine,
            StartColumn: 1,
            EndColumn: 1,
            Hash: ComputeHash(mergedCode)
        );
    }

    /// <summary>
    /// Get statistics about chunked code
    /// </summary>
    public static (int TotalChunks, int TotalLines, Dictionary<string, int> TypeCounts) GetStatistics(List<CodeChunk> chunks)
    {
        var typeCounts = chunks
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var totalLines = chunks.Sum(c => c.EndLine - c.StartLine);

        return (chunks.Count, totalLines, typeCounts);
    }
}
