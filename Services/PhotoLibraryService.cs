using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FilePortal.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Directory = System.IO.Directory;

namespace FilePortal.Services;

/// <summary>
/// Builds and caches the photo/video library by scanning the configured roots,
/// extracting EXIF metadata, and grouping by date taken for the timeline.
/// </summary>
public class PhotoLibraryService
{
    private readonly IConfiguration _configuration;
    private readonly FileBrowserService _browser;
    private readonly UserStoreService _users;
    private readonly ILogger<PhotoLibraryService> _logger;

    private readonly object _lock = new();
    private readonly object _scanLock = new();
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);
    private static readonly UserRecord Scanner = new() { IsAdmin = true };
    private static readonly string[] InstallationDirectories =
    [
        "Program Files", "Program Files (x86)", "ProgramData", "Windows", "WindowsApps",
        "WpSystem", "$RECYCLE.BIN", "System Volume Information"
    ];
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private int _refreshing;
    private readonly string _cachePath;

    // In-memory cache: id -> metadata
    private Dictionary<string, PhotoMeta> _cache = new();
    private DateTime _lastScanUtc = DateTime.MinValue;
    private volatile bool _loaded;

    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".heic", ".heif"
    };

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp"
    };

    public PhotoLibraryService(IConfiguration configuration, FileBrowserService browser, UserStoreService users, ILogger<PhotoLibraryService> logger)
    {
        _configuration = configuration;
        _browser = browser;
        _users = users;
        _logger = logger;

        var dataDir = users.DirectoryPath;
        Directory.CreateDirectory(dataDir);
        _cachePath = Path.Combine(dataDir, "photos_cache.json");
    }

    public static bool IsMediaFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext);
    }

    public static string ComputeId(string fullPath)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            if (File.Exists(_cachePath))
            {
                try
                {
                    using var stream = new FileStream(_cachePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    var list = JsonSerializer.Deserialize<List<PhotoMeta>>(stream) ?? [];
                    _cache = list.Where(item => item is not null && !string.IsNullOrEmpty(item.Id) &&
                        !string.IsNullOrEmpty(item.FullPath) && Path.IsPathFullyQualified(item.FullPath) &&
                        IsMediaFile(item.FullPath)).DistinctBy(item => item.Id).ToDictionary(item => item.Id);
                    _lastScanUtc = File.GetLastWriteTimeUtc(_cachePath);
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(exception, "Could not load the photo cache.");
                }
            }
            _loaded = true;
        }
    }

    private void PersistCache(Dictionary<string, PhotoMeta> cache)
    {
        var temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
                JsonSerializer.Serialize(stream, cache.Values);
            File.Move(temporary, _cachePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not persist the photo cache.");
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private void RefreshIfDue()
    {
        EnsureLoaded();
        lock (_lock)
        {
            if (DateTime.UtcNow - _lastScanUtc < ScanInterval || Interlocked.Exchange(ref _refreshing, 1) != 0)
                return;
        }

        _ = Task.Run(() =>
        {
            try { Scan(); }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not refresh the photo library.");
            }
            finally { Volatile.Write(ref _refreshing, 0); }
        });
    }

    public int Scan(bool force = false)
    {
        EnsureLoaded();
        lock (_scanLock)
        {
            Dictionary<string, PhotoMeta> previous;
            lock (_lock)
            {
                previous = _cache;
                if (!force && DateTime.UtcNow - _lastScanUtc < ScanInterval)
                    return previous.Count;
                _lastScanUtc = DateTime.UtcNow;
            }

            var allowed = _browser.GetRoots(Scanner);
            var configured = GetConfiguredScanRoots();
            var exclusions = configured.Length == 0 ? GetInstallationDirectories(allowed) : [];
            var candidates = configured.Length == 0 ? allowed.Select(root => root.Path) : configured;
            var roots = new HashSet<string>(FileBrowserService.PathComparer);
            foreach (var candidate in candidates)
            {
                if (_browser.TryResolve(candidate, allowed, out var root) && Directory.Exists(root))
                    roots.Add(root);
            }

            var visited = new HashSet<string>(FileBrowserService.PathComparer);
            var refreshed = new Dictionary<string, PhotoMeta>();
            var limit = Math.Clamp(_configuration.GetValue("PhotoLibrary:MaxScanFiles", 100000), 1, 1000000);
            foreach (var root in roots)
            {
                foreach (var file in EnumerateFiles(root, allowed, visited, exclusions))
                {
                    var id = ComputeId(file.FullName);
                    if (refreshed.ContainsKey(id) || !_browser.TryResolve(file.FullName, allowed, out _))
                        continue;
                    try
                    {
                        var key = file.LastWriteTimeUtc.Ticks;
                        refreshed.Add(id, previous.TryGetValue(id, out var cached) && cached.CacheKey == key
                            ? cached
                            : BuildMeta(file, id, key));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogDebug(exception, "Skipping an unavailable media file.");
                    }

                    if (refreshed.Count >= limit)
                        break;
                }

                if (refreshed.Count >= limit)
                    break;
            }

            lock (_lock)
            {
                _cache = refreshed;
                _lastScanUtc = DateTime.UtcNow;
            }

            PersistCache(refreshed);
            return refreshed.Count;
        }
    }

    private string[] GetConfiguredScanRoots() =>
        (_configuration.GetSection("PhotoLibrary:ScanRoots").Get<string[]>() ?? [])
        .Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();

    private string[] GetDefaultScanExclusions() => GetConfiguredScanRoots().Length == 0
        ? GetInstallationDirectories(_browser.GetRoots(Scanner))
        : [];

    private static string[] GetInstallationDirectories(IEnumerable<RootItem> roots) => roots
        .SelectMany(root => InstallationDirectories.Select(directory => Path.Combine(root.Path, directory))).ToArray();

    private static bool IsExcluded(string path, string[] exclusions) => exclusions.Any(directory =>
        string.Equals(path, directory, PathComparison) ||
        (path.Length > directory.Length && path[directory.Length] == Path.DirectorySeparatorChar &&
            path.StartsWith(directory, PathComparison)));

    private IEnumerable<FileInfo> EnumerateFiles(string root, List<RootItem> allowed, HashSet<string> visited, string[] exclusions)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            if (IsExcluded(folder, exclusions) || !visited.Add(folder) || !_browser.TryResolve(folder, allowed, out _))
                continue;

            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(folder).GetFileSystemInfos("*", new EnumerationOptions
                {
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = true
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (!_browser.CanList(entry))
                    continue;
                if (entry is DirectoryInfo directory)
                    pending.Push(directory.FullName);
                else if (entry is FileInfo file && IsMediaFile(file.FullName))
                    yield return file;
            }
        }
    }

    private PhotoMeta BuildMeta(FileInfo fi, string id, long cacheKey)
    {
        var ext = fi.Extension;
        var isVideo = VideoExtensions.Contains(ext);
        var meta = new PhotoMeta
        {
            Id = id,
            FullPath = fi.FullName,
            FileName = fi.Name,
            DirectoryPath = fi.DirectoryName ?? string.Empty,
            Extension = ext,
            IsVideo = isVideo,
            Size = fi.Length,
            DateModified = fi.LastWriteTime,
            DateTaken = fi.LastWriteTime,
            CacheKey = cacheKey
        };

        if (!isVideo)
        {
            TryExtractExif(fi.FullName, meta);
        }
        return meta;
    }

    private void TryExtractExif(string path, PhotoMeta meta)
    {
        try
        {
            using var stream = FileBrowserService.OpenRead(path);
            var directories = ImageMetadataReader.ReadMetadata(stream);

            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd != null)
            {
                if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dto))
                    meta.DateTaken = dto;
                else if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var dtd))
                    meta.DateTaken = dtd;

                if (subIfd.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var w)) meta.Width = w;
                if (subIfd.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var h)) meta.Height = h;
            }

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 != null)
            {
                meta.CameraMake = ifd0.GetDescription(ExifDirectoryBase.TagMake);
                meta.CameraModel = ifd0.GetDescription(ExifDirectoryBase.TagModel);
            }

            var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
            var geo = gps?.GetGeoLocation();
            if (geo.HasValue && !geo.Value.IsZero)
            {
                meta.Latitude = geo.Value.Latitude;
                meta.Longitude = geo.Value.Longitude;
            }

            // Fallback dimensions from JPEG/PNG directories
            if (meta.Width == null || meta.Height == null)
            {
                foreach (var d in directories)
                {
                    foreach (var tag in d.Tags)
                    {
                        if (tag.Name.Equals("Image Width", StringComparison.OrdinalIgnoreCase) && meta.Width == null
                            && d.TryGetInt32(tag.Type, out var iw)) meta.Width = iw;
                        if (tag.Name.Equals("Image Height", StringComparison.OrdinalIgnoreCase) && meta.Height == null
                            && d.TryGetInt32(tag.Type, out var ih)) meta.Height = ih;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "EXIF extraction failed for {Path}", path);
        }
    }

    public void ForgetMissing(IEnumerable<PhotoMeta> photos)
    {
        var missing = photos.Where(photo => !File.Exists(photo.FullPath)).Select(photo => photo.Id).ToList();
        if (missing.Count == 0)
            return;

        lock (_scanLock)
        {
            Dictionary<string, PhotoMeta> updated;
            lock (_lock)
            {
                updated = new Dictionary<string, PhotoMeta>(_cache);
                foreach (var id in missing)
                    updated.Remove(id);
                _cache = updated;
            }
            PersistCache(updated);
        }
    }

    private List<RootItem> GetAllowedRoots(string username)
    {
        var user = _users.GetUser(username);
        return user is null ? [] : _browser.GetRoots(user);
    }

    private Dictionary<string, PhotoMeta> GetSnapshot()
    {
        RefreshIfDue();
        lock (_lock) return _cache;
    }

    public List<PhotoMeta> GetVisiblePhotos(string username)
    {
        var roots = GetAllowedRoots(username);
        if (roots.Count == 0)
            return [];

        var exclusions = GetDefaultScanExclusions();
        var directories = new Dictionary<string, bool>(FileBrowserService.PathComparer);
        return GetSnapshot().Values.Where(photo =>
        {
            if (!_browser.IsPathInScope(photo.FullPath, roots))
                return false;

            var directory = Path.GetDirectoryName(photo.FullPath);
            if (directory is null)
                return false;
            if (!directories.TryGetValue(directory, out var allowed))
            {
                allowed = !IsExcluded(directory, exclusions) && _browser.TryResolve(directory, roots, out _) && Directory.Exists(directory);
                directories.Add(directory, allowed);
            }
            return allowed;
        }).OrderByDescending(photo => photo.DateTaken).ToList();
    }

    public PhotoMeta? GetById(string id, string username) => GetByIds([id], username).FirstOrDefault();

    public List<PhotoMeta> GetByIds(IEnumerable<string> ids, string username)
    {
        var roots = GetAllowedRoots(username);
        if (roots.Count == 0)
            return [];

        var exclusions = GetDefaultScanExclusions();
        var snapshot = GetSnapshot();
        var result = new List<PhotoMeta>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (!string.IsNullOrEmpty(id) && snapshot.TryGetValue(id, out var photo) && !IsExcluded(photo.FullPath, exclusions) &&
                _browser.TryResolve(photo.FullPath, roots, out _) && File.Exists(photo.FullPath))
            {
                result.Add(photo);
            }
        }
        return result;
    }
}
