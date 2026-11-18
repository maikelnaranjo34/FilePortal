using FilePortal.Models;

using System.Runtime.InteropServices;
using System.Text;

namespace FilePortal.Services;

public sealed class FileBrowserService
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IConfiguration _configuration;
    private readonly string[] _privateDirectories;

    public FileBrowserService(IConfiguration configuration, IWebHostEnvironment environment, UserStoreService users)
    {
        _configuration = configuration;
        WorkingDirectory = Path.Combine(users.DirectoryPath, "archives");
        _privateDirectories =
        [
            Path.GetFullPath(environment.ContentRootPath),
            Path.GetFullPath(AppContext.BaseDirectory),
            users.DirectoryPath
        ];
    }

    public string WorkingDirectory { get; }

    public List<RootItem> GetRoots(UserRecord user)
    {
        var configured = new List<RootItem>();
        foreach (var entry in _configuration.GetSection("Roots").GetChildren())
        {
            if (entry.Value is not null && TryNormalize(entry.Value, out var path) && IsAvailableDirectory(path))
                configured.Add(new RootItem { Name = entry.Key, Path = path });
        }

        if (user.IsAdmin)
            return configured;

        var roots = new List<RootItem>();
        foreach (var folder in user.AllowedFolders)
        {
            if (!TryNormalize(folder, out var path) || !IsAvailableDirectory(path) ||
                !configured.Any(root => IsWithin(path, root.Path)))
                continue;

            var label = configured.Find(root => PathsEqual(path, root.Path))?.Name ?? Path.GetFileName(path);
            roots.Add(new RootItem { Name = label, Path = path });
        }

        return roots;
    }

    public bool TryResolve(string path, IReadOnlyList<RootItem> roots, out string fullPath, bool allowMissing = false)
    {
        if (!TryNormalize(path, out fullPath))
            return false;

        var candidate = fullPath;
        return roots.Any(root => IsWithin(candidate, root.Path)) &&
            !IsPrivate(candidate) && HasSafeComponents(candidate, allowMissing);
    }

    public bool TryGetUploadDestination(string target, string fileName, IReadOnlyList<RootItem> roots, out string destination)
    {
        destination = "";
        var relative = fileName.Replace('\\', '/');
        if (relative.Split('/').Any(segment => segment.Length == 0 || segment is "." or "..") ||
            Path.IsPathRooted(relative) || relative.Contains(':'))
            return false;

        return TryResolve(Path.Combine(target, relative), roots, out destination, allowMissing: true) &&
            !PathsEqual(destination, target) && IsWithin(destination, target);
    }

    public bool CanDeleteTree(string path) => !_privateDirectories.Any(directory => IsWithin(directory, path));

    public bool CanList(FileSystemInfo entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) == 0 && !IsPrivate(entry.FullName);

    public bool IsAvailableDirectory(string path) =>
        TryNormalize(path, out var fullPath) && !IsPrivate(fullPath) && HasSafeComponents(fullPath, allowMissing: false) &&
        Directory.Exists(fullPath);

    private bool IsPrivate(string path) =>
        _privateDirectories.Any(directory => IsWithin(path, directory)) ||
        path.Split(Path.DirectorySeparatorChar).Any(segment => segment.StartsWith(".fileportal-upload-", StringComparison.OrdinalIgnoreCase));

    private static bool TryNormalize(string path, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var windowsPath = path.Replace('/', '\\');
                if (windowsPath.StartsWith(@"\\?\", StringComparison.Ordinal) || windowsPath.StartsWith(@"\\.\", StringComparison.Ordinal))
                    return false;

                var drivePrefix = windowsPath.Length >= 2 && windowsPath[1] == ':' ? 2 : 0;
                var parts = windowsPath[drivePrefix..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Any(part => part is not "." and not ".." &&
                    (part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                        IsWindowsDeviceName(part))))
                    return false;
            }

            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsWindowsDeviceName(string part)
    {
        var name = part.Split('.')[0].TrimEnd().ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) &&
                "0123456789¹²³".Contains(name[3]));
    }

    private static bool HasSafeComponents(string path, bool allowMissing)
    {
        var root = Path.GetPathRoot(path)!;
        var current = root;
        foreach (var segment in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return allowMissing && HasCanonicalWindowsName(Path.GetDirectoryName(current)!);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return HasCanonicalWindowsName(path);
    }

    private static bool HasCanonicalWindowsName(string path)
    {
        if (!OperatingSystem.IsWindows() || PathsEqual(path, Path.GetPathRoot(path)!))
            return true;

        var canonical = new StringBuilder(path.Length + 1);
        var length = GetLongPathName(path, canonical, (uint)canonical.Capacity);
        return length > 0 && length < canonical.Capacity && PathsEqual(path, canonical.ToString());
    }

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferLength);

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), PathComparison);

    private static bool IsWithin(string path, string directory)
    {
        var prefix = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
        return PathsEqual(path, directory) || path.StartsWith(prefix, PathComparison);
    }
}
