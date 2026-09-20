using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// RAG (Retrieval-Augmented Generation) Service
/// Manages code embeddings, vector search, and context retrieval for AI prompts
/// Uses JSON-based storage for vectors and dedicated RagApiKey/RagBaseUrl/RagModel settings
/// </summary>
public class RagService : IDisposable
{
    private readonly string _indexPath;
    private readonly AppSettings _settings;
    private readonly Dictionary<string, float[]> _embeddingCache;
    private readonly int _maxCacheSize = 1000;
    private bool _isInitialized = false;
    private List<IndexEntry> _indexedChunks = new();

    /// <summary>Total number of indexed code chunks</summary>
    public int ChunkCount => _indexedChunks.Count;

    private record IndexEntry(
        string FilePath,
        string ChunkType,
        string ChunkName,
        string CodeSnippet,
        int StartLine,
        int EndLine,
        float[] Embedding,
        string Hash
    );

    /// <summary>
    /// Represents a search result from the vector database
    /// </summary>
    public record SearchResult(
        string FilePath,
        string ChunkType,
        string ChunkName,
        string CodeSnippet,
        int StartLine,
        int EndLine,
        float Similarity
    );

    public RagService(string projectPath, AppSettings settings)
    {
        var ragDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yengi", "rag");

        var projectName = Path.GetFileName(projectPath);
        _indexPath = Path.Combine(ragDir, projectName + "_index.json");

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _embeddingCache = new Dictionary<string, float[]>();

        try
        {
            if (!Directory.Exists(ragDir))
                Directory.CreateDirectory(ragDir);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create RAG directory: {ex.Message}");
        }
    }

    /// <summary>Returns a summary list of indexed files for the RAG tab UI</summary>
    public List<string> GetIndexStats()
    {
        var stats = _indexedChunks
            .GroupBy(c => Path.GetFileName(c.FilePath))
            .Select(g => $"📄 {g.Key}  ({g.Count()} blok)")
            .ToList();
        return stats;
    }

    /// <summary>Indexes an entire project folder</summary>
    public async Task IndexProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath)) return;

        var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
                               .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
                               .Take(200)
                               .ToList();

        _indexedChunks.Clear();

        foreach (var file in csFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var sourceCode = await File.ReadAllTextAsync(file, cancellationToken);
                var chunks = CodeChunker.ChunkFile(file, sourceCode);
                foreach (var chunk in chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var embedding = await GenerateEmbeddingAsync(chunk.Code, cancellationToken);
                    if (embedding == null) continue;
                    _indexedChunks.Add(new IndexEntry(
                        chunk.FilePath, chunk.Type, chunk.Name,
                        chunk.Code, chunk.StartLine, chunk.EndLine,
                        embedding, chunk.Hash));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.LogError($"RAG indexing failed for {file}: {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await SaveIndexAsync(cancellationToken);
        Logger.LogInfo($"RAG indexing complete: {_indexedChunks.Count} chunks from {csFiles.Count} files");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (File.Exists(_indexPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var json = await File.ReadAllTextAsync(_indexPath, cancellationToken);
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                _indexedChunks = JsonSerializer.Deserialize<List<IndexEntry>>(json, options) ?? new();
                var existingCount = _indexedChunks.Count;
                _indexedChunks = _indexedChunks.Where(entry => File.Exists(entry.FilePath)).ToList();
                if (_indexedChunks.Count != existingCount)
                    await SaveIndexAsync(cancellationToken);
                Logger.LogInfo($"RAG Service loaded {_indexedChunks.Count} existing chunks");
            }

            _isInitialized = true;
            Logger.LogInfo($"RAG Service initialized: {_indexPath}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError($"RAG initialization failed: {ex.Message}");
            _isInitialized = true;
        }
    }

    public async Task<int> IndexCodeChunksAsync(List<CodeChunker.CodeChunk> chunks, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isInitialized)
            throw new InvalidOperationException("RAG Service not initialized");

        var indexedCount = 0;

        try
        {
            var filePaths = chunks
                .Select(chunk => Path.GetFullPath(chunk.FilePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _indexedChunks.RemoveAll(entry => filePaths.Contains(Path.GetFullPath(entry.FilePath)));

            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var embedding = await GenerateEmbeddingAsync(chunk.Code, cancellationToken);
                if (embedding == null || embedding.Length == 0)
                {
                    Logger.LogError($"Failed to generate embedding for: {chunk.Name}");
                    continue;
                }

                var entry = new IndexEntry(
                    FilePath: chunk.FilePath,
                    ChunkType: chunk.Type,
                    ChunkName: chunk.Name,
                    CodeSnippet: chunk.Code,
                    StartLine: chunk.StartLine,
                    EndLine: chunk.EndLine,
                    Embedding: embedding,
                    Hash: chunk.Hash
                );

                _indexedChunks.Add(entry);
                indexedCount++;

                Logger.LogInfo($"Indexed: {chunk.Type}/{chunk.Name}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await SaveIndexAsync(cancellationToken);

            Logger.LogInfo($"RAG indexing complete: {indexedCount}/{chunks.Count} chunks indexed");
            return indexedCount;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError($"Chunk indexing failed: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> RemoveFileChunksAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var normalizedPath = Path.GetFullPath(filePath);
        var removed = _indexedChunks.RemoveAll(entry =>
            Path.GetFullPath(entry.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
            return false;

        cancellationToken.ThrowIfCancellationRequested();
        await SaveIndexAsync(cancellationToken);
        Logger.LogInfo($"Removed {removed} stale RAG chunks for deleted file: {filePath}");
        return true;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isInitialized)
            throw new InvalidOperationException("RAG Service not initialized");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queryEmbedding = await GenerateEmbeddingAsync(query, cancellationToken);
            if (queryEmbedding == null || queryEmbedding.Length == 0)
            {
                Logger.LogError("Failed to generate query embedding");
                return new List<SearchResult>();
            }

            var results = new List<SearchResult>();
            foreach (var entry in _indexedChunks.Where(entry => File.Exists(entry.FilePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var similarity = CosineSimilarity(queryEmbedding, entry.Embedding);
                results.Add(new SearchResult(
                    FilePath: entry.FilePath,
                    ChunkType: entry.ChunkType,
                    ChunkName: entry.ChunkName,
                    CodeSnippet: entry.CodeSnippet,
                    StartLine: entry.StartLine,
                    EndLine: entry.EndLine,
                    Similarity: similarity
                ));
            }

            var topResults = results.OrderByDescending(r => r.Similarity).Take(topK).ToList();
            Logger.LogInfo($"RAG search found {topResults.Count} similar chunks for query");
            return topResults;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError($"RAG search failed: {ex.Message}");
            return new List<SearchResult>();
        }
    }

    private async Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var cacheKey = CodeChunker.ComputeHash(text);
            if (_embeddingCache.TryGetValue(cacheKey, out var cachedEmbedding))
                return cachedEmbedding;

            if (string.IsNullOrEmpty(_settings.RagBaseUrl) || string.IsNullOrEmpty(_settings.RagModel))
            {
                Logger.LogError("RAG settings not configured (Base URL or Model missing)");
                return null;
            }

            using var client = new HttpClient();
            if (!string.IsNullOrEmpty(_settings.RagApiKey))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _settings.RagApiKey);
            client.Timeout = TimeSpan.FromSeconds(30);

            var requestBody = JsonSerializer.Serialize(new { model = _settings.RagModel, input = text });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            cancellationToken.ThrowIfCancellationRequested();
            var url = $"{_settings.RagBaseUrl.TrimEnd('/')}/embeddings";
            var response = await client.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.LogError($"Embedding API error ({(int)response.StatusCode}): {err}");
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var embedding = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();

            if (_embeddingCache.Count >= _maxCacheSize)
                _embeddingCache.Remove(_embeddingCache.Keys.First());
            _embeddingCache[cacheKey] = embedding;

            return embedding;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError($"Embedding generation failed: {ex.Message}");
            return null;
        }
    }

    private async Task SaveIndexAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            var json = JsonSerializer.Serialize(_indexedChunks, options);
            await File.WriteAllTextAsync(_indexPath, json, cancellationToken);
            Logger.LogInfo($"RAG index saved: {_indexedChunks.Count} chunks");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save index: {ex.Message}");
        }
    }

    private static float CosineSimilarity(float[] vec1, float[] vec2)
    {
        if (vec1.Length != vec2.Length || vec1.Length == 0)
            return 0f;

        float dotProduct = 0f;
        float magnitude1 = 0f;
        float magnitude2 = 0f;

        for (int i = 0; i < vec1.Length; i++)
        {
            dotProduct += vec1[i] * vec2[i];
            magnitude1 += vec1[i] * vec1[i];
            magnitude2 += vec2[i] * vec2[i];
        }

        magnitude1 = (float)Math.Sqrt(magnitude1);
        magnitude2 = (float)Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0f;

        return dotProduct / (magnitude1 * magnitude2);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _indexedChunks.Clear();
            if (File.Exists(_indexPath))
                File.Delete(_indexPath);

            _embeddingCache.Clear();
            Logger.LogInfo("RAG database cleared");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to clear RAG database: {ex.Message}");
        }
    }

    public async Task<(int TotalChunks, int CacheSize, string IndexPath)> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (_indexedChunks.Count, _embeddingCache.Count, _indexPath);
    }

    public void Dispose()
    {
        try
        {
            _embeddingCache.Clear();
            _isInitialized = false;
            Logger.LogInfo("RAG Service disposed");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error disposing RAG Service: {ex.Message}");
        }
    }
}
