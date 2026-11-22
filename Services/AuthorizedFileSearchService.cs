using FilePortal.Models;

namespace FilePortal.Services;

public sealed class AuthorizedFileSearchService(FileBrowserService browser)
{
    public IReadOnlyList<FileItem> Search(UserRecord user, string query, int maximumResults,
        CancellationToken cancellationToken = default)
    {
        var roots = browser.GetRoots(user);
        var directories = new Queue<DirectoryInfo>();
        foreach (var root in roots.OrderBy(root => root.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(root => root.Path, FileBrowserService.PathComparer))
            if (browser.TryResolve(root.Path, roots, out var folder, allowMissing: false) && Directory.Exists(folder))
                directories.Enqueue(new DirectoryInfo(folder));

        var visited = new HashSet<string>(FileBrowserService.PathComparer);
        var resultPaths = new HashSet<string>(FileBrowserService.PathComparer);
        var results = new List<FileItem>();
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };
        while (directories.TryDequeue(out var directory) && results.Count < maximumResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(directory.FullName) ||
                !browser.TryResolve(directory.FullName, roots, out var resolvedDirectory, allowMissing: false))
                continue;

            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(resolvedDirectory).EnumerateFileSystemInfos("*", options)
                    .OrderByDescending(entry => entry is DirectoryInfo)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.FullName, FileBrowserService.PathComparer)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!browser.CanList(entry) ||
                        !browser.TryResolve(entry.FullName, roots, out var resolvedEntry, allowMissing: false))
                        continue;

                    FileSystemInfo resolvedInfo;
                    if (entry is DirectoryInfo)
                    {
                        if (!Directory.Exists(resolvedEntry)) continue;
                        var child = new DirectoryInfo(resolvedEntry);
                        directories.Enqueue(child);
                        resolvedInfo = child;
                    }
                    else
                    {
                        if (!File.Exists(resolvedEntry)) continue;
                        resolvedInfo = new FileInfo(resolvedEntry);
                    }

                    if (resolvedInfo.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                        resultPaths.Add(resolvedInfo.FullName))
                        results.Add(ToItem(resolvedInfo));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // The filesystem may change during a recursive search.
                }
                if (results.Count >= maximumResults) break;
            }
        }

        return results.OrderBy(item => string.Equals(item.Name, query, StringComparison.OrdinalIgnoreCase) ? 0 :
                item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Path, FileBrowserService.PathComparer)
            .ToList();
    }

    private static FileItem ToItem(FileSystemInfo entry) => new()
    {
        Name = entry.Name,
        Path = entry.FullName,
        IsDirectory = entry is DirectoryInfo,
        Size = entry is FileInfo file ? file.Length : null,
        LastModified = entry.LastWriteTime,
        Extension = entry is FileInfo ? entry.Extension : null
    };
}
