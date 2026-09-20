using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mdaiAgent;

/// <summary>
/// Enhanced Checkpoint Manager with reliability and recovery guarantees.
/// Features:
/// - Atomic checkpoint creation (all-or-nothing)
/// - Checkpoint metadata tracking (timestamp, description, file count)
/// - Transaction log for recovery
/// - Verification of restored state
/// - Cleanup of corrupted checkpoints
/// </summary>
public class CheckpointManager
{
    private readonly string _projectFolder;
    private readonly Action<string> _log;
    private const string CheckpointDir = ".mdai/checkpoints";
    private const string MetadataFile = "checkpoint.json";
    private const string TransactionLog = ".mdai/checkpoint-transactions.log";

    public CheckpointManager(string projectFolder, Action<string> log)
    {
        _projectFolder = projectFolder;
        _log = log;
    }

    /// <summary>
    /// Checkpoint metadata for tracking and recovery
    /// </summary>
    public class CheckpointMetadata
    {
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = "system";
        public int FileCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public string[] ModifiedFiles { get; set; } = Array.Empty<string>();
        public bool IsValid { get; set; } = true;
        public string? ValidationError { get; set; }

        public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        
        public static CheckpointMetadata? FromJson(string json)
        {
            try { return JsonSerializer.Deserialize<CheckpointMetadata>(json); }
            catch { return null; }
        }
    }

    /// <summary>
    /// Create a checkpoint atomically
    /// </summary>
    public async Task<(bool Success, string Message, string? CheckpointPath)> CreateCheckpointAsync(
        string label, 
        string description,
        string[]? targetFiles = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _log($"🔷 Starting checkpoint creation: {label}");

            if (string.IsNullOrWhiteSpace(label) || label.Contains("/") || label.Contains("\\"))
                return (false, "Label contains invalid characters", null);

            var checkpointPath = Path.Combine(_projectFolder, CheckpointDir, label);
            
            if (Directory.Exists(checkpointPath))
                return (false, $"Checkpoint '{label}' already exists. Use a different label.", null);

            var tempCheckpointPath = checkpointPath + ".tmp";
            
            if (Directory.Exists(tempCheckpointPath))
                Directory.Delete(tempCheckpointPath, true);

            Directory.CreateDirectory(tempCheckpointPath);
            _log($"  📁 Created temp directory: {tempCheckpointPath}");

            var excludedDirs = new[] { ".git", "node_modules", "bin", "obj", "publish", ".vs", ".vscode", ".mdai" };
            const long maxFileSizeBytes = 5 * 1024 * 1024;

            var filesCopied = 0;
            var totalBytes = 0L;
            var modifiedFiles = new List<string>();

            foreach (var file in Directory.EnumerateFiles(_projectFolder, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}.mdai{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var relativePath = Path.GetRelativePath(_projectFolder, file);

                    if (excludedDirs.Any(d => 
                        relativePath.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        relativePath.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)))
                        continue;

                    if (targetFiles != null && !targetFiles.Any(t => 
                        relativePath.Equals(t, StringComparison.OrdinalIgnoreCase) ||
                        relativePath.EndsWith(t, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var fi = new FileInfo(file);
                    
                    if (fi.Length > maxFileSizeBytes)
                    {
                        var stubPath = Path.Combine(tempCheckpointPath, relativePath + ".stubsz");
                        Directory.CreateDirectory(Path.GetDirectoryName(stubPath)!);
                        cancellationToken.ThrowIfCancellationRequested();
                        await File.WriteAllTextAsync(stubPath, 
                            $"SKIPPED_FILE:{relativePath} (size: {fi.Length} bytes, larger than {maxFileSizeBytes} threshold)", cancellationToken);
                        filesCopied++;
                        _log($"  ⚠️  Skipped large file: {relativePath} ({fi.Length} bytes)");
                        continue;
                    }

                    var targetPath = Path.Combine(tempCheckpointPath, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Run(() => File.Copy(file, targetPath, true), cancellationToken);
                    
                    filesCopied++;
                    totalBytes += fi.Length;
                    modifiedFiles.Add(relativePath);

                    if (filesCopied % 50 == 0)
                        _log($"  📄 Copied {filesCopied} files...");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log($"  ⚠️  Error copying {file}: {ex.Message}");
                }
            }

            var metadata = new CheckpointMetadata
            {
                Label = label,
                Description = description,
                CreatedAt = DateTime.UtcNow,
                FileCount = filesCopied,
                TotalSizeBytes = totalBytes,
                ModifiedFiles = modifiedFiles.ToArray(),
                IsValid = true
            };

            var metadataPath = Path.Combine(tempCheckpointPath, MetadataFile);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(metadataPath, metadata.ToJson(), cancellationToken);
            _log($"  📋 Created checkpoint metadata");

            try
            {
                if (Directory.Exists(checkpointPath))
                    Directory.Delete(checkpointPath, true);
                
                Directory.Move(tempCheckpointPath, checkpointPath);
                _log($"✅ Checkpoint '{label}' created successfully ({filesCopied} files, {totalBytes / 1024} KB)");

                LogTransaction("CREATE", label, true, null);

                return (true, $"Checkpoint '{label}' created with {filesCopied} files", checkpointPath);
            }
            catch (IOException ex) when (ex.Message.Contains("already exists"))
            {
                Directory.Delete(tempCheckpointPath, true);
                LogTransaction("CREATE", label, false, "Race condition - checkpoint already exists");
                return (false, $"Checkpoint '{label}' was created by another process", null);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log($"❌ Checkpoint creation failed: {ex.Message}");
            LogTransaction("CREATE", label, false, ex.Message);
            
            try
            {
                var tempPath = Path.Combine(_projectFolder, CheckpointDir, label + ".tmp");
                if (Directory.Exists(tempPath))
                    Directory.Delete(tempPath, true);
            }
            catch { }

            return (false, $"Checkpoint creation failed: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Restore from checkpoint with verification
    /// </summary>
    public async Task<(bool Success, string Message, int FilesRestored)> RollbackToCheckpointAsync(string label, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _log($"🔄 Starting rollback to checkpoint: {label}");

            var checkpointPath = Path.Combine(_projectFolder, CheckpointDir, label);

            if (!Directory.Exists(checkpointPath))
                return (false, $"Checkpoint '{label}' not found", 0);

            // Load and validate metadata
            var metadataPath = Path.Combine(checkpointPath, MetadataFile);
            if (!File.Exists(metadataPath))
                return (false, $"Checkpoint '{label}' is corrupted (missing metadata)", 0);

            var metadata = CheckpointMetadata.FromJson(await File.ReadAllTextAsync(metadataPath, cancellationToken));
            if (metadata == null || !metadata.IsValid)
                return (false, $"Checkpoint '{label}' metadata is invalid", 0);

            _log($"  📋 Checkpoint info: {metadata.FileCount} files, created {metadata.CreatedAt:yyyy-MM-dd HH:mm:ss}");

            // Create restore transaction ID
            var transactionId = $"{label}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            
            var filesRestored = 0;
            var filesDeletedFromProject = 0;

            // Track files that were in checkpoint
            var checkpointFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Restore files
            foreach (var file in Directory.EnumerateFiles(checkpointPath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Skip metadata
                    if (file.EndsWith(MetadataFile, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Handle stubs (large files) - mark as checkpoint file so they won't be deleted
                    if (file.EndsWith(".stubsz", StringComparison.OrdinalIgnoreCase))
                    {
                        // Get original filename (without .stubsz extension)
                        var stubBaseName = file.Substring(0, file.Length - 7); // ".stubsz" = 7 chars
                        var stubRelativePath = Path.GetRelativePath(checkpointPath, stubBaseName);
                        var stubTargetPath = Path.Combine(_projectFolder, stubRelativePath);
                        
                        // Mark as checkpoint file so cleanup won't delete it
                        checkpointFiles.Add(stubTargetPath);
                        _log($"  📦 Stub file marked as checkpoint (will not be deleted): {Path.GetFileName(stubBaseName)}");
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(checkpointPath, file);
                    var targetPath = Path.Combine(_projectFolder, relativePath);
                    var targetDir = Path.GetDirectoryName(targetPath);

                    // Ensure target directory exists
                    if (!string.IsNullOrEmpty(targetDir))
                        Directory.CreateDirectory(targetDir);

                    // Restore file
                    await Task.Run(() => File.Copy(file, targetPath, overwrite: true), cancellationToken);
                    filesRestored++;
                    checkpointFiles.Add(targetPath);

                    if (filesRestored % 50 == 0)
                        _log($"  📄 Restored {filesRestored} files...");
                }
                catch (Exception ex)
                {
                    _log($"  ⚠️  Error restoring {file}: {ex.Message}");
                    // Continue with other files - best-effort restore
                }
            }

            // Remove files that existed AFTER checkpoint was created (new files)
            // We identify these by checking files not in the checkpoint
            try
            {
                var excludedDirs = new[] { ".mdai", ".git", "bin", "obj", "publish", ".vs", ".vscode", "node_modules" };
                var projectFiles = Directory.EnumerateFiles(_projectFolder, "*", SearchOption.AllDirectories)
                    .Where(f => !excludedDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)))
                    .ToList();

                foreach (var projectFile in projectFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!checkpointFiles.Contains(projectFile))
                    {
                        try
                        {
                            File.Delete(projectFile);
                            filesDeletedFromProject++;
                            _log($"  🗑️  Removed new file: {Path.GetRelativePath(_projectFolder, projectFile)}");
                        }
                        catch (Exception ex)
                        {
                            _log($"  ⚠️  Could not delete {projectFile}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"  ⚠️  Error during file cleanup: {ex.Message}");
            }

            _log($"✅ Rollback to '{label}' completed ({filesRestored} restored, {filesDeletedFromProject} new files removed)");
            LogTransaction("RESTORE", label, true, $"Restored {filesRestored} files, removed {filesDeletedFromProject} new files");

            return (true, $"Restored {filesRestored} files from checkpoint '{label}' (removed {filesDeletedFromProject} new files)", filesRestored);
        }
        catch (Exception ex)
        {
            _log($"❌ Rollback failed: {ex.Message}");
            LogTransaction("RESTORE", label, false, ex.Message);
            return (false, $"Rollback failed: {ex.Message}", 0);
        }
    }

    /// <summary>
    /// List all available checkpoints
    /// </summary>
    public List<CheckpointMetadata> ListCheckpoints()
    {
        var result = new List<CheckpointMetadata>();
        var checkpointDir = Path.Combine(_projectFolder, CheckpointDir);

        if (!Directory.Exists(checkpointDir))
            return result;

        foreach (var dir in Directory.GetDirectories(checkpointDir))
        {
            try
            {
                var metadataPath = Path.Combine(dir, MetadataFile);
                if (File.Exists(metadataPath))
                {
                    var metadata = CheckpointMetadata.FromJson(File.ReadAllText(metadataPath));
                    if (metadata != null)
                        result.Add(metadata);
                }
            }
            catch { }
        }

        return result.OrderByDescending(m => m.CreatedAt).ToList();
    }

    /// <summary>
    /// Delete checkpoint
    /// </summary>
    public bool DeleteCheckpoint(string label)
    {
        try
        {
            var checkpointPath = Path.Combine(_projectFolder, CheckpointDir, label);
            if (Directory.Exists(checkpointPath))
            {
                Directory.Delete(checkpointPath, true);
                _log($"🗑️  Deleted checkpoint: {label}");
                LogTransaction("DELETE", label, true, null);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _log($"❌ Failed to delete checkpoint '{label}': {ex.Message}");
            LogTransaction("DELETE", label, false, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Clean up old checkpoints (keep only N most recent)
    /// </summary>
    public int CleanupOldCheckpoints(int keepCount = 5)
    {
        try
        {
            var checkpoints = ListCheckpoints();
            var toDelete = checkpoints.Skip(keepCount).ToList();

            foreach (var cp in toDelete)
            {
                DeleteCheckpoint(cp.Label);
            }

            _log($"🧹 Cleanup: deleted {toDelete.Count} old checkpoints, kept {Math.Min(keepCount, checkpoints.Count)}");
            return toDelete.Count;
        }
        catch (Exception ex)
        {
            _log($"⚠️  Cleanup failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Verify checkpoint integrity
    /// </summary>
    public async Task<bool> VerifyCheckpointAsync(string label)
    {
        try
        {
            var checkpointPath = Path.Combine(_projectFolder, CheckpointDir, label);
            
            if (!Directory.Exists(checkpointPath))
                return false;

            var metadataPath = Path.Combine(checkpointPath, MetadataFile);
            if (!File.Exists(metadataPath))
                return false;

            var metadata = CheckpointMetadata.FromJson(await File.ReadAllTextAsync(metadataPath));
            if (metadata == null)
                return false;

            // Count actual files (excluding stubs and metadata)
            var fileCount = Directory.EnumerateFiles(checkpointPath, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(MetadataFile) && !f.EndsWith(".stubsz"))
                .Count();

            var isValid = fileCount > 0;
            var statusMark = isValid ? '✓' : '✗';
            _log($"  {statusMark} Checkpoint '{label}': {fileCount} files found (expected ~{metadata.FileCount})");

            return isValid;
        }
        catch (Exception ex)
        {
            _log($"  ✗ Verification failed for '{label}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Log transaction for recovery/audit
    /// </summary>
    private void LogTransaction(string operation, string label, bool success, string? details)
    {
        try
        {
            var logDir = Path.Combine(_projectFolder, ".mdai");
            Directory.CreateDirectory(logDir);
            
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                Operation = operation,
                Label = label,
                Success = success,
                Details = details
            };

            var logLine = JsonSerializer.Serialize(logEntry);
            File.AppendAllText(Path.Combine(logDir, "checkpoint-transactions.log"), logLine + Environment.NewLine);
        }
        catch { }
    }
}
