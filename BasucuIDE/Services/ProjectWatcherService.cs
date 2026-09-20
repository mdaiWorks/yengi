using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// Monitors project directory for code file changes and triggers RAG re-indexing
/// </summary>
public class ProjectWatcherService : IDisposable
{
    private readonly string _projectPath;
    private readonly RagService _ragService;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private readonly HashSet<string> _pendingChanges = new();
    private readonly object _syncLock = new();
    private bool _isIndexing = false;
    private readonly int _debounceDelayMs = 2000; // Wait 2 seconds after last change

    // Event: Fired when indexing completes
    public event EventHandler<ProjectIndexingCompleteEventArgs>? IndexingComplete;

    public class ProjectIndexingCompleteEventArgs : EventArgs
    {
        public int ChunksIndexed { get; init; }
        public int FilesProcessed { get; init; }
        public TimeSpan Duration { get; init; }
    }

    public ProjectWatcherService(string projectPath, RagService ragService)
    {
        _projectPath = projectPath ?? throw new ArgumentNullException(nameof(projectPath));
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
    }

    /// <summary>
    /// Start watching project directory for changes
    /// </summary>
    public void Start()
    {
        if (_watcher != null)
        {
            Logger.LogError("ProjectWatcher already started");
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(_projectPath)
            {
                Filter = "*.cs",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Deleted += OnFileDeleted;
            _watcher.EnableRaisingEvents = true;

            Logger.LogInfo($"🔍 ProjectWatcher started for: {_projectPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to start ProjectWatcher: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop watching for changes
    /// </summary>
    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _debounceTimer?.Dispose();
        _debounceTimer = null;

        Logger.LogInfo("🛑 ProjectWatcher stopped");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnoreFile(e.FullPath))
            return;

        lock (_syncLock)
        {
            _pendingChanges.Add(e.FullPath);
        }

        ScheduleReindex();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!ShouldIgnoreFile(e.FullPath))
        {
            lock (_syncLock)
            {
                _pendingChanges.Add(e.FullPath);
            }

            ScheduleReindex();
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!ShouldIgnoreFile(e.FullPath))
        {
            Logger.LogInfo($"🗑️  File deleted: {e.Name}");
            _ = RemoveDeletedFileChunksAsync(e.FullPath);
        }
    }

    private async Task RemoveDeletedFileChunksAsync(string filePath)
    {
        try
        {
            await _ragService.RemoveFileChunksAsync(filePath);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to remove deleted file from RAG index ({filePath}): {ex.Message}");
        }
    }

    private void ScheduleReindex()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(
            callback: _ => _ = ReindexAsync(),
            state: null,
            dueTime: _debounceDelayMs,
            period: Timeout.Infinite);
    }

    /// <summary>
    /// Re-index changed files
    /// </summary>
    private async Task ReindexAsync()
    {
        if (_isIndexing)
            return;

        _isIndexing = true;

        try
        {
            var startTime = DateTime.Now;
            List<string> filesToIndex;

            lock (_syncLock)
            {
                filesToIndex = _pendingChanges.ToList();
                _pendingChanges.Clear();
            }

            if (filesToIndex.Count == 0)
            {
                _isIndexing = false;
                return;
            }

            Logger.LogInfo($"📝 Re-indexing {filesToIndex.Count} file(s)...");

            var totalChunksIndexed = 0;

            foreach (var filePath in filesToIndex)
            {
                try
                {
                    if (!File.Exists(filePath))
                        continue;

                    var sourceCode = await File.ReadAllTextAsync(filePath);
                    var chunks = CodeChunker.ChunkFile(filePath, sourceCode);

                    if (chunks.Count > 0)
                    {
                        var indexed = await _ragService.IndexCodeChunksAsync(chunks);
                        totalChunksIndexed += indexed;
                        Logger.LogInfo($"✅ Re-indexed: {Path.GetFileName(filePath)} ({indexed} chunks)");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to re-index {filePath}: {ex.Message}");
                }
            }

            var duration = DateTime.Now - startTime;

            Logger.LogInfo($"✅ Re-indexing complete: {totalChunksIndexed} chunks in {duration.TotalSeconds:F2}s");

            IndexingComplete?.Invoke(this, new ProjectIndexingCompleteEventArgs
            {
                ChunksIndexed = totalChunksIndexed,
                FilesProcessed = filesToIndex.Count,
                Duration = duration
            });
        }
        catch (Exception ex)
        {
            Logger.LogError($"Re-indexing failed: {ex.Message}");
        }
        finally
        {
            _isIndexing = false;
        }
    }

    /// <summary>
    /// Perform full project indexing (initial indexing)
    /// </summary>
    public async Task<int> FullIndexAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Logger.LogInfo("🔄 Starting full project indexing...");
            var startTime = DateTime.Now;

            var codeFiles = Directory.GetFiles(_projectPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !ShouldIgnoreFile(f))
                .ToList();

            Logger.LogInfo($"📂 Found {codeFiles.Count} C# files to index");

            var totalChunksIndexed = 0;

            foreach (var filePath in codeFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var sourceCode = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var chunks = CodeChunker.ChunkFile(filePath, sourceCode);

                    if (chunks.Count > 0)
                    {
                        var indexed = await _ragService.IndexCodeChunksAsync(chunks, cancellationToken);
                        totalChunksIndexed += indexed;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to index {filePath}: {ex.Message}");
                }
            }

            var duration = DateTime.Now - startTime;
            Logger.LogInfo($"✅ Full indexing complete: {totalChunksIndexed} chunks from {codeFiles.Count} files in {duration.TotalSeconds:F2}s");

            IndexingComplete?.Invoke(this, new ProjectIndexingCompleteEventArgs
            {
                ChunksIndexed = totalChunksIndexed,
                FilesProcessed = codeFiles.Count,
                Duration = duration
            });

            return totalChunksIndexed;
        }
        catch (OperationCanceledException)
        {
            Logger.LogInfo("Full indexing canceled by user");
            return 0;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Full indexing failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Check if file should be ignored
    /// </summary>
    private static bool ShouldIgnoreFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLower();
        var dirName = Path.GetDirectoryName(filePath)?.ToLower() ?? "";

        // Ignore certain directories
        var ignoreDirs = new[] { "bin", "obj", ".git", "node_modules", ".mdai", "publish" };
        if (ignoreDirs.Any(d => dirName.Contains($"\\{d}\\") || dirName.Contains($"/{d}/")))
            return true;

        // Ignore certain files
        var ignoreFiles = new[] { "*.designer.cs", "*.xaml.cs" };
        if (ignoreFiles.Any(pattern => fileName.EndsWith(pattern.TrimStart('*'))))
            return false; // Actually, do index XAML code-behind

        return false;
    }

    public void Dispose()
    {
        Stop();
        Logger.LogInfo("ProjectWatcherService disposed");
    }
}
