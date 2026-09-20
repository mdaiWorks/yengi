using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace mdaiAgent;

public sealed record LspExecutableResolution(string ExecutablePath, string Source);

public static class LspExecutableResolver
{
    private static readonly string[] WindowsExecutableExtensions = { ".exe", ".cmd", ".bat" };

    public static LspExecutableResolution? Resolve(string? projectPath, params string[] executableNames)
    {
        var names = executableNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var name in names)
        {
            var projectPathResult = FindInProjectNodeModules(name, projectPath);
            if (projectPathResult != null)
                return new LspExecutableResolution(
                    projectPathResult,
                    LocalizationManager.Instance.IsEnglish ? "Project node_modules/.bin" : "Proje node_modules/.bin");
        }

        foreach (var name in names)
        {
            var localPathResult = FindInLocalLspFolder(name);
            if (localPathResult != null)
                return new LspExecutableResolution(
                    localPathResult,
                    LocalizationManager.Instance.IsEnglish ? "mdaiAgent LSP folder" : "mdaiAgent LSP klasoru");
        }

        foreach (var name in names)
        {
            var pathResult = FindOnPath(name);
            if (pathResult != null)
                return new LspExecutableResolution(
                    pathResult,
                    LocalizationManager.Instance.IsEnglish ? "System PATH" : "Sistem PATH");
        }

        return null;
    }

    private static string? FindInProjectNodeModules(string executableName, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return null;

        var current = new DirectoryInfo(projectPath);
        while (current != null)
        {
            var binDirectory = Path.Combine(current.FullName, "node_modules", ".bin");
            var candidate = FindInDirectory(executableName, binDirectory);
            if (candidate != null)
                return candidate;

            current = current.Parent;
        }

        return null;
    }

    private static string? FindInLocalLspFolder(string executableName)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Yengi",
                "LspServers");

            if (!Directory.Exists(root))
                return null;

            return FindInDirectory(root, executableName)
                ?? Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                    .Select(directory => FindInDirectory(executableName, directory))
                    .FirstOrDefault(candidate => candidate != null);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindOnPath(string executableName)
    {
        var directories = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            directories.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        }

        if (OperatingSystem.IsWindows())
        {
            var userNpmDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm");
            directories.Add(userNpmDirectory);
        }

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = FindInDirectory(executableName, directory);
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    private static string? FindInDirectory(string executableName, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        var directCandidate = Path.Combine(directory, executableName);
        if (!OperatingSystem.IsWindows() && File.Exists(directCandidate))
            return directCandidate;

        foreach (var extension in WindowsExecutableExtensions)
        {
            var candidate = directCandidate + extension;
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
