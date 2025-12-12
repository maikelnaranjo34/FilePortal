using System.IO;
using FilePortal.Models;

namespace FilePortal.Services;

public class FileBrowserService
{
    // Local drive roots you want to show by letter
    private readonly string[] _allowedDriveRoots = new[]
    {
        "E:",
        "F:",
        "H:"
    };

    // Special roots:
    //  - Network shares for Y: and Z: (UNC paths)
    //  - Samsung S20 Syncthing folder
    private readonly Dictionary<string, string> _specialRoots;

    public FileBrowserService(IConfiguration configuration)
    {
        _specialRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 🔹 UPDATE THESE TWO LINES with your real UNC paths:
            // Example: @"\\192.168.5.1\USB_DISK1" or @"\\ROUTER\YDrive"
            { "Easystore", @"\\192.168.5.1\h" },
            { "Data", @"\\192.168.5.1\g" },

            // Samsung S20 Syncthing backup
            { "Samsung S20", @"D:\Samsung S20_Cloud\WebsiteBrowser\Files" }
        };
    }

    // This is what the UI calls to populate the left list (roots).
    public IEnumerable<string> GetRoots()
    {
        var roots = new List<string>();

        // Local drives: E:, F:, H:
        foreach (var root in _allowedDriveRoots)
        {
            var drivePath = root + Path.DirectorySeparatorChar; // e.g. "E:\"
            if (Directory.Exists(drivePath))
            {
                roots.Add(root);
            }
        }

        // Special roots (UNC + Samsung folder)
        foreach (var kvp in _specialRoots)
        {
            if (Directory.Exists(kvp.Value))
            {
                roots.Add(kvp.Key);
            }
        }

        return roots;
    }

    // Map a logical root name (E:, F:, H:, Y:, Z:, Samsung S20) to a real base path.
    private string GetBasePathForRoot(string root)
    {
        // First: special roots (Y:, Z:, Samsung S20)
        if (_specialRoots.TryGetValue(root, out var specialPath))
        {
            return specialPath;
        }

        // Then: local drive letters (E:, F:, H:)
        if (_allowedDriveRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
        {
            return root + Path.DirectorySeparatorChar;
        }

        // Fallback (shouldn't really happen)
        return root;
    }

    // Get folders/files inside a root + relative path
    public IEnumerable<FileItem> GetItems(string root, string? relativePath)
    {
        var basePath = GetBasePathForRoot(root);

        var targetPath = string.IsNullOrWhiteSpace(relativePath)
            ? basePath
            : Path.Combine(basePath, relativePath);

        var items = new List<FileItem>();

        if (!Directory.Exists(targetPath))
        {
            return items;
        }

        try
        {
            // Directories
            foreach (var dir in Directory.EnumerateDirectories(targetPath))
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    var rel = Path.GetRelativePath(basePath, di.FullName);

                    items.Add(new FileItem
                    {
                        Name = di.Name,
                        FullPath = di.FullName,
                        RelativePath = rel,
                        IsDirectory = true,
                        LastModified = di.LastWriteTime
                    });
                }
                catch
                {
                    // Skip problem dirs
                }
            }

            // Files
            foreach (var file in Directory.EnumerateFiles(targetPath))
            {
                try
                {
                    var fi = new FileInfo(file);
                    var rel = Path.GetRelativePath(basePath, fi.FullName);

                    items.Add(new FileItem
                    {
                        Name = fi.Name,
                        FullPath = fi.FullName,
                        RelativePath = rel,
                        IsDirectory = false,
                        Size = fi.Length,
                        LastModified = fi.LastWriteTime
                    });
                }
                catch
                {
                    // Skip problem files
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // If some folder under that root denies access,
            // we just return whatever we already collected.
            return items;
        }

        return items
            .OrderByDescending(i => i.IsDirectory)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
    }

    // Open a file for viewing/downloading
    public (FileStream stream, string contentType, string fileName) OpenFile(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File not found", fullPath);
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var fileName = Path.GetFileName(fullPath);
        var contentType = GetContentType(fileName);

        return (stream, contentType, fileName);
    }

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }
}
