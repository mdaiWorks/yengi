using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace mdaiAgent.Services;

/// <summary>
/// Tracks external folders approved by the user for the current agent session.
/// </summary>
public sealed class ExternalPathAccessManager
{
    private readonly string? _projectFolder;
    private readonly Func<string, Task<bool>>? _requestApproval;
    private readonly HashSet<string> _approvedRoots = new(StringComparer.OrdinalIgnoreCase);

    public ExternalPathAccessManager(string? projectFolder, Func<string, Task<bool>>? requestApproval = null)
    {
        _projectFolder = projectFolder;
        _requestApproval = requestApproval;
    }

    public bool IsAllowed(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var resolvedPath = ResolveLinkTarget(fullPath);
        if (resolvedPath == null)
            return false;

        if (!IsWithinApprovedBoundary(fullPath) || !IsWithinApprovedBoundary(resolvedPath))
            return false;

        return true;
    }

    private bool IsWithinApprovedBoundary(string path)
    {
        if (!string.IsNullOrWhiteSpace(_projectFolder) && IsWithinBoundary(path, _projectFolder))
            return true;

        foreach (var approvedRoot in _approvedRoots)
        {
            if (IsWithinBoundary(path, approvedRoot))
                return true;
        }

        return false;
    }

    private static string? ResolveLinkTarget(string path)
    {
        try
        {
            if (File.Exists(path))
                return File.ResolveLinkTarget(path, true)?.FullName ?? Path.GetFullPath(path);

            if (Directory.Exists(path))
                return Directory.ResolveLinkTarget(path, true)?.FullName ?? Path.GetFullPath(path);

            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent)) return null;
            var resolvedParent = ResolveLinkTarget(parent);
            return resolvedParent == null ? null : Path.Combine(resolvedParent, Path.GetFileName(path));
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> EnsureAllowedAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (IsAllowed(fullPath))
            return true;

        if (_requestApproval == null)
            return false;

        var approvalRoot = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(approvalRoot))
            return false;

        approvalRoot = Path.GetFullPath(approvalRoot);
        if (!await _requestApproval(approvalRoot))
            return false;

        _approvedRoots.Add(approvalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return true;
    }

    private static bool IsWithinBoundary(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
