using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FilePortal.Models;

namespace FilePortal.Services;

public sealed class DuplicateFinderService(FileBrowserService browser)
{
    private static readonly Regex CopySuffix = new(
        @"(?:\s*[-_]\s*copy|\s+copy|\s*\(\d+\)|\s*[-_]\s*\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed record DuplicateItem(string Name, string Path, long Size, DateTime LastModified);
    public sealed record ExactGroup(string Sha256, long Size, long ReclaimableBytes, IReadOnlyList<DuplicateItem> Files);
    public sealed record NameGroup(string NormalizedName, long Size, IReadOnlyList<DuplicateItem> Files);
    public sealed record Analysis(IReadOnlyList<ExactGroup> ExactContent, IReadOnlyList<NameGroup> SimilarNames);
    public sealed record Report(string RootName, string RootPath, int FilesScanned, bool Truncated,
        IReadOnlyList<ExactGroup> ExactContent, IReadOnlyList<NameGroup> SimilarNames);

    public async Task<Report> FindAsync(UserRecord user, string rootName, int maximumFiles,
        CancellationToken cancellationToken)
    {
        var roots = browser.GetRoots(user);
        var root = roots.FirstOrDefault(item => string.Equals(item.Name, rootName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Choose an available FilePortal root by name.");
        if (!browser.TryResolve(root.Path, roots, out var rootPath, allowMissing: false) || !Directory.Exists(rootPath))
            throw new DirectoryNotFoundException("The selected root is unavailable.");

        var files = EnumerateFiles(rootPath, roots, maximumFiles + 1, cancellationToken);
        var truncated = files.Count > maximumFiles;
        if (truncated) files.RemoveRange(maximumFiles, files.Count - maximumFiles);
        var analysis = await AnalyzeAsync(files, cancellationToken);
        return new Report(root.Name, rootPath, files.Count, truncated, analysis.ExactContent, analysis.SimilarNames);
    }

    public async Task<Analysis> AnalyzeAsync(IEnumerable<FileInfo> sourceFiles, CancellationToken cancellationToken)
    {
        var files = sourceFiles.Where(file => file.Exists).DistinctBy(file => file.FullName, FileBrowserService.PathComparer).ToList();
        var items = files.ToDictionary(file => file.FullName, ToItem, FileBrowserService.PathComparer);

        var exact = new List<ExactGroup>();
        foreach (var sizeGroup in files.GroupBy(file => file.Length).Where(group => group.Count() > 1))
        {
            var hashes = new Dictionary<string, List<FileInfo>>(StringComparer.Ordinal);
            foreach (var file in sizeGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = FileBrowserService.OpenRead(file.FullName);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                    if (!hashes.TryGetValue(hash, out var matching)) hashes[hash] = matching = [];
                    matching.Add(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A file can disappear or be locked while the scan is running.
                }
            }
            exact.AddRange(hashes.Where(group => group.Value.Count > 1).Select(group => new ExactGroup(
                group.Key, sizeGroup.Key, sizeGroup.Key * (group.Value.Count - 1),
                group.Value.Select(file => items[file.FullName]).ToList())));
        }

        var names = files.GroupBy(file => (Name: NormalizeName(file.Name), file.Length))
            .Where(group => group.Count() > 1)
            .Select(group => new NameGroup(group.Key.Name, group.Key.Length, group.Select(file => items[file.FullName]).ToList()))
            .OrderByDescending(group => group.Files.Count)
            .ThenBy(group => group.NormalizedName, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
        return new Analysis(exact.OrderByDescending(group => group.ReclaimableBytes).ToList(), names);
    }

    private List<FileInfo> EnumerateFiles(string rootPath, IReadOnlyList<RootItem> roots, int maximumFiles,
        CancellationToken cancellationToken)
    {
        var results = new List<FileInfo>();
        var directories = new Stack<DirectoryInfo>();
        directories.Push(new DirectoryInfo(rootPath));
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        while (directories.TryPop(out var directory) && results.Count < maximumFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!browser.TryResolve(directory.FullName, roots, out var safeDirectory, allowMissing: false)) continue;
            FileSystemInfo[] entries;
            try { entries = new DirectoryInfo(safeDirectory).EnumerateFileSystemInfos("*", options).ToArray(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if (!browser.CanList(entry) || !browser.TryResolve(entry.FullName, roots, out var safePath, allowMissing: false)) continue;
                if (entry is DirectoryInfo && Directory.Exists(safePath)) directories.Push(new DirectoryInfo(safePath));
                else if (entry is FileInfo && File.Exists(safePath))
                {
                    try { results.Add(new FileInfo(safePath)); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                }
                if (results.Count >= maximumFiles) break;
            }
        }
        return results;
    }

    private static DuplicateItem ToItem(FileInfo file) => new(file.Name, file.FullName, file.Length, file.LastWriteTime);

    private static string NormalizeName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        string previous;
        do { previous = stem; stem = CopySuffix.Replace(stem, "").Trim(); } while (stem != previous);
        return (stem + extension).ToLowerInvariant();
    }
}
