using FilePortal.Models;
using FilePortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FilePortal.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class FilesController(FileBrowserService browser, ThumbnailService thumbnails, FileArchiveService archives) : ControllerBase
{
    private static readonly Dictionary<string, HashSet<string>> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pictures"] = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff", ".tif" },
        ["videos"] = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg" },
        ["documents"] = new(StringComparer.OrdinalIgnoreCase)
            { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp" }
    };

    private List<RootItem>? _roots;
    private List<RootItem> Roots => _roots ??= browser.GetRoots((UserRecord)HttpContext.Items[typeof(UserRecord)]!);

    [HttpGet("roots")]
    public ActionResult<List<RootItem>> GetRoots() => Ok(Roots);

    [HttpGet("folder")]
    public IActionResult GetFolder([FromQuery] string path) => ListFolder(path, foldersOnly: false);

    [Authorize(Roles = "Admin")]
    [HttpGet("browse")]
    public IActionResult BrowseFolders([FromQuery] string path) => ListFolder(path, foldersOnly: true);

    private IActionResult ListFolder(string path, bool foldersOnly)
    {
        if (!Resolve(path, out var folder))
            return Forbid();
        if (!Directory.Exists(folder))
            return NotFound(new { error = "Folder not found." });

        var entries = new DirectoryInfo(folder).EnumerateFileSystemInfos()
            .Where(entry => browser.CanList(entry) && (!foldersOnly || entry is DirectoryInfo))
            .Select(ToItem)
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Ok(entries);
    }

    [HttpGet("category")]
    public IActionResult GetByCategory([FromQuery] string path, [FromQuery] string category, [FromQuery] int maxResults = 500)
    {
        if (!Resolve(path, out var folder))
            return Forbid();
        if (!Directory.Exists(folder))
            return NotFound(new { error = "Folder not found." });
        if (!Categories.TryGetValue(category, out var extensions) || maxResults is < 1 or > 10000)
            return BadRequest(new { error = "Choose a supported category and a result limit between 1 and 10,000." });

        var items = new List<FileItem>();
        var directories = new Stack<DirectoryInfo>();
        directories.Push(new DirectoryInfo(folder));
        while (directories.TryPop(out var directory) && items.Count < maxResults)
        {
            HttpContext.RequestAborted.ThrowIfCancellationRequested();
            if (!Resolve(directory.FullName, out _, allowMissing: false))
                continue;

            foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true
            }))
            {
                HttpContext.RequestAborted.ThrowIfCancellationRequested();
                if (!browser.CanList(entry))
                    continue;

                if (entry is DirectoryInfo child)
                    directories.Push(child);
                else if (extensions.Contains(entry.Extension))
                    items.Add(ToItem(entry));

                if (items.Count >= maxResults)
                    break;
            }
        }

        return Ok(items.OrderByDescending(item => item.LastModified));
    }

    [HttpGet("open")]
    public IActionResult Open([FromQuery] string path) => ServeFile(path, download: true);

    [HttpGet("file")]
    public IActionResult GetFile([FromQuery] string path) => ServeFile(path, download: false);

    private IActionResult ServeFile(string path, bool download)
    {
        if (!Resolve(path, out var file))
            return Forbid();
        if (!System.IO.File.Exists(file))
            return NotFound();

        var type = FileBrowserService.GetContentType(file);
        if (type == "text/html" || type == "application/xhtml+xml" || type == "text/xml" || type == "application/xml")
            type = "application/octet-stream";

        Response.Headers.XContentTypeOptions = "nosniff";
        if (type == "image/svg+xml")
            Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";

        var stream = FileBrowserService.OpenRead(file);
        return File(stream, type, download ? Path.GetFileName(file) : null, enableRangeProcessing: true);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromQuery] string targetPath, CancellationToken cancellationToken)
    {
        if (!Resolve(targetPath, out var target))
            return Forbid();
        if (!Directory.Exists(target))
            return NotFound(new { error = "Target folder not found." });
        if (!Request.HasFormContentType)
            return BadRequest(new { error = "Upload files using a multipart form." });

        var files = (await Request.ReadFormAsync(cancellationToken)).Files.GetFiles("files");
        if (files.Count == 0)
            return BadRequest(new { error = "No files received." });

        var destinations = new Dictionary<string, IFormFile>(FileBrowserService.PathComparer);
        foreach (var file in files)
        {
            if (!browser.TryGetUploadDestination(target, file.FileName, Roots, out var destination))
                return BadRequest(new { error = "Every upload must stay inside the target folder." });
            if (Directory.Exists(destination) || !destinations.TryAdd(destination, file))
                return Conflict(new { error = "Upload destinations must be unique file paths." });
        }

        foreach (var destination in destinations.Keys)
        {
            for (var parent = Path.GetDirectoryName(destination); parent is not null && !FileBrowserService.PathComparer.Equals(parent, target);
                parent = Path.GetDirectoryName(parent))
            {
                if (destinations.ContainsKey(parent) || System.IO.File.Exists(parent))
                    return Conflict(new { error = "An upload path is used as both a file and a folder." });
            }
        }

        var staged = new List<(string TemporaryPath, string Destination)>();
        try
        {
            foreach (var (destination, file) in destinations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!browser.TryGetUploadDestination(target, file.FileName, Roots, out _))
                    return Forbid();

                var parent = Path.GetDirectoryName(destination)!;
                Directory.CreateDirectory(parent);
                var temporaryPath = Path.Combine(parent, ".fileportal-upload-" + Guid.NewGuid().ToString("N"));
                await using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                staged.Add((temporaryPath, destination));
                await file.CopyToAsync(stream, cancellationToken);
            }

            foreach (var (temporaryPath, destination) in staged)
            {
                if (!Resolve(destination, out _))
                    return Forbid();
                System.IO.File.Move(temporaryPath, destination, overwrite: true);
            }

            return Ok(new { message = "Upload complete." });
        }
        finally
        {
            foreach (var (temporaryPath, _) in staged)
                System.IO.File.Delete(temporaryPath);
        }
    }

    [HttpDelete("delete")]
    public IActionResult Delete([FromQuery] string path)
    {
        if (!TryResolveDeletion(path, out var target))
            return Forbid();
        try
        {
            var directory = Directory.Exists(target);
            if (!DeletePath(target))
                return NotFound(new { error = "Path not found." });
            return Ok(new { message = directory ? "Directory deleted." : "File deleted." });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var error = FileDeletion.DescribeFailure(target, exception);
            return StatusCode(exception is UnauthorizedAccessException ? 403 : 409, new { error });
        }
    }

    [HttpPost("batch/delete")]
    public IActionResult DeleteBatch([FromBody] DeleteBatchRequest request, CancellationToken cancellationToken)
    {
        if (request.Paths is null or { Count: 0 } || request.Paths.Any(string.IsNullOrWhiteSpace))
            return BadRequest(new { error = "Select at least one file or folder." });

        var targets = new Dictionary<string, string>(FileBrowserService.PathComparer);
        foreach (var path in request.Paths)
        {
            if (!TryResolveDeletion(path, out var target))
                return StatusCode(403, new { error = "A selected item is outside your folder access or is a protected root." });
            targets.TryAdd(target, path);
        }

        var deleted = new List<string>();
        var failures = new List<DeleteFailure>();
        foreach (var (target, path) in targets.OrderByDescending(entry => entry.Key.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!TryResolveDeletion(target, out _))
                    failures.Add(new(path, "Access denied."));
                else if (DeletePath(target))
                    deleted.Add(path);
                else
                    failures.Add(new(path, "The file or folder no longer exists."));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new(path, FileDeletion.DescribeFailure(target, exception)));
            }
        }
        return Ok(new { deleted, failures });
    }

    private bool TryResolveDeletion(string path, out string target)
    {
        if (!Resolve(path, out target))
            return false;
        var resolved = target;
        return browser.CanDeleteTree(resolved) && !Roots.Any(root =>
            FileBrowserService.PathComparer.Equals(Path.TrimEndingDirectorySeparator(root.Path), resolved));
    }

    private static bool DeletePath(string target)
    {
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
            return true;
        }

        if (!System.IO.File.Exists(target))
            return false;

        System.IO.File.Delete(target);
        return true;
    }

    public class DeleteBatchRequest
    {
        public List<string> Paths { get; set; } = [];
    }

    private sealed record DeleteFailure(string Path, string Error);

    [HttpGet("thumbnail")]
    public IActionResult GetThumbnail([FromQuery] string path)
    {
        if (!Resolve(path, out var target))
            return Forbid();
        if (Directory.Exists(target))
            return Icon("#707be8", "M10 14h16l4 6h24v29H10z");
        if (!System.IO.File.Exists(target))
            return NotFound();

        var file = new FileInfo(target);
        var image = thumbnails.GetThumbnail(file);
        if (image is not null)
            return File(image, "image/jpeg");
        if (Categories["videos"].Contains(file.Extension))
            return Icon("#a78bfa", "M16 10h32v44H16z M27 21v22l15-11z");
        if (FileBrowserService.GetContentType(target).StartsWith("audio/", StringComparison.Ordinal))
            return Icon("#60a5fa", "M25 14v25a7 7 0 1 0 5 7V25l15-5v13a7 7 0 1 0 5 7V7z");
        if (string.Equals(file.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            return Icon("#ef4444", "M16 10h21l11 11v33H16z M21 31h22v3H21z M21 39h15v3H21z");
        return Icon("#9ca3af", "M16 10h21l11 11v33H16z M36 12v11h10z");
    }

    private ContentResult Icon(string color, string shape) => Content(
        $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'><path fill='{color}' fill-rule='evenodd' d='{shape}'/></svg>",
        "image/svg+xml");

    [HttpPost("download-zip")]
    public Task<IActionResult> DownloadZip([FromBody] DownloadZipRequest request, CancellationToken cancellationToken) =>
        CreateZip(request.Paths, cancellationToken);

    [HttpPost("download-zip-form")]
    [RequestFormLimits(ValueCountLimit = 10_000)]
    public Task<IActionResult> DownloadZipForm([FromForm] List<string> paths, CancellationToken cancellationToken) =>
        CreateZip(paths, cancellationToken);

    private async Task<IActionResult> CreateZip(List<string>? paths, CancellationToken cancellationToken)
    {
        if (paths is null or { Count: 0 } || paths.Any(string.IsNullOrWhiteSpace))
            return BadRequest(new { error = "Select at least one file or folder." });

        var sources = new HashSet<string>(FileBrowserService.PathComparer);
        foreach (var path in paths)
        {
            if (!Resolve(path, out var source))
                return Forbid();
            if (!System.IO.File.Exists(source) && !Directory.Exists(source))
                return NotFound(new { error = "A selected path no longer exists." });
            sources.Add(source);
        }

        var archive = await archives.CreateAsync(sources, Roots, cancellationToken);
        return File(archive, "application/zip", "files.zip");
    }

    private bool Resolve(string path, out string fullPath, bool allowMissing = true) =>
        browser.TryResolve(path, Roots, out fullPath, allowMissing);

    private static FileItem ToItem(FileSystemInfo entry) => new()
    {
        Name = entry.Name,
        Path = entry.FullName,
        IsDirectory = entry is DirectoryInfo,
        Size = entry is FileInfo file ? file.Length : null,
        LastModified = entry.LastWriteTime,
        Extension = entry is FileInfo ? entry.Extension : null
    };

    public class DownloadZipRequest
    {
        public List<string> Paths { get; set; } = [];
    }
}
