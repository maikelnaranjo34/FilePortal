using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using System.IO.Compression;
using System.ComponentModel.DataAnnotations;
using FilePortal.Services;

namespace FilePortal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FilesController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly UserStoreService _userStore;
        // 🔵 Phone (Syncthing) root
        private static readonly string PhoneRoot = @"D:\Samsung S20_Cloud\WebsiteBrowser\Files";

        // File category extension mappings
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff", ".tif"
        };

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg"
        };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp"
        };

        public class RootDto
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
        }

        public class ItemDto
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public bool IsDirectory { get; set; }
            public long? Size { get; set; }
            public DateTime? LastModified { get; set; }
            public string? Extension { get; set; }
        }

        public FilesController(IConfiguration configuration, UserStoreService userStore)
        {
            _configuration = configuration;
            _userStore = userStore;
        }

        // Helper: Check if user has access to a path
        private bool HasAccessToPath(string requestedPath)
        {
            var username = User.Identity?.Name ?? string.Empty;
            var isAdmin = User.IsInRole("Admin");
            
            if (isAdmin) return true; // Admins can access everything
            
            var user = _userStore.GetUser(username);
            if (user == null) return false;
            
            // If AllowedFolders is empty, non-admins have no access
            if (user.AllowedFolders.Count == 0) return false;
            
            // Check if requested path is within any allowed folder
            var normalizedPath = Path.GetFullPath(requestedPath);
            foreach (var allowedFolder in user.AllowedFolders)
            {
                var normalizedAllowed = Path.GetFullPath(allowedFolder);
                if (normalizedPath == normalizedAllowed || normalizedPath.StartsWith(normalizedAllowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }

        // ================== ROOTS ==================
        [HttpGet("roots")]
        public ActionResult<IEnumerable<RootDto>> GetRoots()
        {
            var list = new List<RootDto>();
            var username = User.Identity?.Name ?? string.Empty;
            var isAdmin = User.IsInRole("Admin");

            // For non-admins with AllowedFolders, show ONLY their allowed folders as roots
            if (!isAdmin)
            {
                var user = _userStore.GetUser(username);
                if (user?.AllowedFolders.Count > 0)
                {
                    foreach (var folder in user.AllowedFolders)
                    {
                        if (Directory.Exists(folder))
                        {
                            var folderName = Path.GetFileName(folder) ?? folder;
                            list.Add(new RootDto { Name = folderName, Path = folder });
                        }
                    }
                    return Ok(list);
                }
                // If no allowed folders, return empty list (user has no access)
                return Ok(list);
            }

            // For admins, show configured roots
            var rootsSection = _configuration.GetSection("Roots");
            if (rootsSection.Exists())
            {
                foreach (var child in rootsSection.GetChildren())
                {
                    var name = child.Key;
                    var path = child.Value ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        list.Add(new RootDto { Name = name, Path = path });
                    }
                }
            }
            else
            {
                // Fallback to enumerating drives (if config missing)
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;
                    if (drive.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) continue;

                    var letter = drive.Name.TrimEnd('\\');
                    list.Add(new RootDto { Name = letter, Path = drive.RootDirectory.FullName });
                }
                if (Directory.Exists(PhoneRoot))
                {
                    list.Add(new RootDto { Name = "Samsung S20", Path = PhoneRoot });
                }
            }

            return Ok(list);
        }

        // ================== FOLDER LIST ==================
        [HttpGet("folder")]
        public ActionResult<IEnumerable<ItemDto>> GetFolder([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ModelState.AddModelError("path", "The path field is required.");
                return BadRequest(ModelState);
            }

            // Check folder access permission
            if (!HasAccessToPath(path))
            {
                return StatusCode(403, new { error = "You do not have access to this folder" });
            }

            // Debug logging
            Console.WriteLine($"GetFolder called with path: '{path}'");

            if (!Directory.Exists(path))
                return NotFound(new { error = "directory not found" });

            // For UNC paths that fail to access, return a helpful message
            if (path.StartsWith("\\\\"))
            {
                return Ok(new[] 
                {
                    new 
                    {
                        Name = "Network Share Access",
                        Path = "",
                        IsDirectory = false,
                        Size = (long?)null,
                        LastModified = (DateTime?)null,
                        Extension = "",
                        Message = "This network share is accessible through your mapped drives (Y: for Easystore, Z: for Data). Please use Windows Explorer to browse these locations directly."
                    }
                });
            }

            var items = new List<ItemDto>();

            try
            {
                // Directories
                foreach (var dir in Directory.GetDirectories(path))
                {
                    DirectoryInfo di;
                    try { di = new DirectoryInfo(dir); }
                    catch { continue; }

                    items.Add(new ItemDto
                    {
                        Name = di.Name,
                        Path = di.FullName,
                        IsDirectory = true,
                        Size = null,
                        LastModified = di.LastWriteTime,
                        Extension = null
                    });
                }

                // Files
                foreach (var file in Directory.GetFiles(path))
                {
                    FileInfo fi;
                    try { fi = new FileInfo(file); }
                    catch { continue; }

                    items.Add(new ItemDto
                    {
                        Name = fi.Name,
                        Path = fi.FullName,
                        IsDirectory = false,
                        Size = fi.Length,
                        LastModified = fi.LastWriteTime,
                        Extension = fi.Extension
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(403, new { error = "Access denied to network share. Please ensure IIS has permissions to access this location." });
            }
            catch (Exception ex)
            {
                // For UNC paths, provide a more user-friendly error
                if (path.StartsWith("\\\\"))
                {
                    return StatusCode(500, new { error = $"Unable to access network share. The share may require different credentials or permissions. Details: {ex.Message}" });
                }
                return StatusCode(500, new { error = $"Failed to list directory contents: {ex.Message}" });
            }

            // Folders first, then files
            items = items
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Ok(items);
        }

        // ================== BROWSE FOLDERS (for folder picker) ==================
        [HttpGet("browse")]
        public ActionResult<IEnumerable<ItemDto>> BrowseFolders([FromQuery] string path)
        {
            // Allow browsing any path for admin (no permission check for browsing)
            // Regular users still need to be authenticated
            
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "C:\\";
            }

            if (!Directory.Exists(path))
                return NotFound(new { error = "directory not found" });

            var items = new List<ItemDto>();

            try
            {
                // Return only directories (for folder selection)
                foreach (var dir in Directory.GetDirectories(path))
                {
                    DirectoryInfo di;
                    try { di = new DirectoryInfo(dir); }
                    catch { continue; }

                    items.Add(new ItemDto
                    {
                        Name = di.Name,
                        Path = di.FullName,
                        IsDirectory = true,
                        Size = null,
                        LastModified = di.LastWriteTime,
                        Extension = null
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(403, new { error = "Access denied to this folder" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }

            // Sort by name
            items = items
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Ok(items);
        }

        // ================== CATEGORY FILTER (NEW) ==================
        [HttpGet("category")]
        public ActionResult<IEnumerable<ItemDto>> GetByCategory(
            [FromQuery] string path, 
            [FromQuery] string category,
            [FromQuery] int maxResults = 500)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(new { error = "path is required" });

            if (string.IsNullOrWhiteSpace(category))
                return BadRequest(new { error = "category is required (pictures, videos, documents)" });

            if (!Directory.Exists(path))
                return NotFound(new { error = "directory not found" });

            HashSet<string> allowedExtensions;
            switch (category.ToLowerInvariant())
            {
                case "pictures":
                    allowedExtensions = ImageExtensions;
                    break;
                case "videos":
                    allowedExtensions = VideoExtensions;
                    break;
                case "documents":
                    allowedExtensions = DocumentExtensions;
                    break;
                default:
                    return BadRequest(new { error = "Invalid category. Use: pictures, videos, or documents" });
            }

            var items = new List<ItemDto>();

            try
            {
                // Recursively search for files matching the category
                SearchDirectoryRecursive(path, allowedExtensions, items, maxResults);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Access denied." });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
            }

            // Sort by last modified (newest first)
            items = items
                .OrderByDescending(i => i.LastModified)
                .ToList();

            return Ok(items);
        }

        private void SearchDirectoryRecursive(string path, HashSet<string> allowedExtensions, List<ItemDto> results, int maxResults)
        {
            if (results.Count >= maxResults)
                return;

            try
            {
                // Add matching files from current directory
                foreach (var file in Directory.GetFiles(path))
                {
                    if (results.Count >= maxResults)
                        break;

                    try
                    {
                        var fi = new FileInfo(file);
                        if (allowedExtensions.Contains(fi.Extension))
                        {
                            results.Add(new ItemDto
                            {
                                Name = fi.Name,
                                Path = fi.FullName,
                                IsDirectory = false,
                                Size = fi.Length,
                                LastModified = fi.LastWriteTime,
                                Extension = fi.Extension
                            });
                        }
                    }
                    catch
                    {
                        // Skip files we can't access
                        continue;
                    }
                }

                // Recurse into subdirectories
                foreach (var dir in Directory.GetDirectories(path))
                {
                    if (results.Count >= maxResults)
                        break;

                    try
                    {
                        SearchDirectoryRecursive(dir, allowedExtensions, results, maxResults);
                    }
                    catch
                    {
                        // Skip directories we can't access
                        continue;
                    }
                }
            }
            catch
            {
                // Silently skip problematic directories
            }
        }

        // ================== OPEN FILE ==================
        [HttpGet("open")]
        public IActionResult Open([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return NotFound();

            string contentType = "application/octet-stream";
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext is ".jpg" or ".jpeg") contentType = "image/jpeg";
            else if (ext is ".png") contentType = "image/png";
            else if (ext is ".gif") contentType = "image/gif";
            else if (ext is ".pdf") contentType = "application/pdf";
            else if (ext is ".mp4") contentType = "video/mp4";

            var fileName = Path.GetFileName(path);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            return File(stream, contentType, fileName);
        }

        // ================== UPLOAD ==================
        [HttpPost("upload")]
        [RequestSizeLimit(10L * 1024 * 1024 * 1024)] // 10 GB
        [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)]
        public async Task<IActionResult> Upload(
            [FromQuery] string targetPath,
            [FromForm] List<IFormFile> files)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return BadRequest(new { error = "targetPath is required" });

            if (!Directory.Exists(targetPath))
                return NotFound(new { error = "Target directory not found" });

            if (files == null || files.Count == 0)
                return BadRequest(new { error = "No files received" });

            foreach (var file in files)
            {
                // Preserve folder structure when uploading a folder
                var relative = file.FileName
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);

                var destFullPath = Path.Combine(targetPath, relative);

                var destDir = Path.GetDirectoryName(destFullPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                await using var stream = new FileStream(
                    destFullPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);

                await file.CopyToAsync(stream);
            }

            return Ok(new { message = "Upload complete" });
        }

        // ================== DELETE FILE / FOLDER ==================
        [HttpDelete("delete")]
        public IActionResult Delete([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(new { error = "path is required" });

            try
            {
                if (Directory.Exists(path))
                {
                    // Delete directory recursively
                    Directory.Delete(path, recursive: true);
                    return Ok(new { message = "Directory deleted" });
                }

                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                    return Ok(new { message = "File deleted" });
                }

                return NotFound(new { error = "Path not found" });
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Access denied" });
            }
            catch (IOException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
            }
        }

        // ================== THUMBNAIL ==================
        [HttpGet("thumbnail")]
        public IActionResult GetThumbnail([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(new { error = "path is required" });

            try
            {
                // Folder icon (SVG) - Purple-blue background with folder icon
                if (Directory.Exists(path))
                {
                    var folderSvg = "<svg xmlns='http://www.w3.org/2000/svg' width='120' height='120' viewBox='0 0 120 120'>" +
                        "<rect width='120' height='120' fill='#707be8'/>" +
                        "<path d='M50 30H25c-5.5 0-10 4.5-10 10v60c0 5.5 4.5 10 10 10h80c5.5 0 10-4.5 10-10V45c0-5.5-4.5-10-10-10H60L50 30z' fill='#ffffff' opacity='0.95'/>" +
                        "</svg>";
                    return Content(folderSvg, "image/svg+xml");
                }

                // Check if file exists
                if (!System.IO.File.Exists(path))
                    return NotFound();

                string ext = Path.GetExtension(path).ToLowerInvariant();

                // Image files: generate & cache thumbnail using ImageSharp
                if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp")
                {
                    var cacheDir = Path.Combine(Path.GetTempPath(), "FilePortalThumbnails");
                    Directory.CreateDirectory(cacheDir);

                    var lastWrite = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                    using var sha = SHA1.Create();
                    var keyRaw = System.Text.Encoding.UTF8.GetBytes(path + ":" + lastWrite);
                    var hash = BitConverter.ToString(sha.ComputeHash(keyRaw)).Replace("-", "").ToLowerInvariant();
                    var cachedFile = Path.Combine(cacheDir, hash + ".jpg");

                    if (System.IO.File.Exists(cachedFile))
                    {
                        var fs = new FileStream(cachedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                        return File(fs, "image/jpeg");
                    }

                    // Generate thumbnail
                    using var image = Image.Load(path);
                    var size = new SixLabors.ImageSharp.Size(240, 240);
                    image.Mutate(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                    {
                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
                        Size = size
                    }));

                    var encoder = new JpegEncoder { Quality = 80 };
                    using (var outStream = System.IO.File.OpenWrite(cachedFile))
                    {
                        image.Save(outStream, encoder);
                    }

                    var fs2 = new FileStream(cachedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return File(fs2, "image/jpeg");
                }

                // Video files: return file type icon
                if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".flv" or ".wmv")
                {
                    var videoSvg = @"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%23a78bfa'><path d='M18 3H6c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5 9.5L9 17V7l4 5.5z'/></svg>";
                    return Content(videoSvg, "image/svg+xml");
                }

                // Audio files
                if (ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg")
                {
                    var audioSvg = @"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%2360a5fa'><path d='M12 3v9.28c-.47-.46-1.12-.75-1.84-.75-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3V7h4V3h-4z'/></svg>";
                    return Content(audioSvg, "image/svg+xml");
                }

                // PDF
                if (ext is ".pdf")
                {
                    var pdfSvg = @"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%23ef4444'><path d='M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-8-6zm0 18H6V4h5v5h3v11z'/><text x='9' y='15' font-size='6' fill='white' font-weight='bold'>PDF</text></svg>";
                    return Content(pdfSvg, "image/svg+xml");
                }

                // Document files (Word, Excel, etc.)
                if (ext is ".doc" or ".docx" or ".xlsx" or ".xls" or ".ppt" or ".pptx")
                {
                    var docSvg = @"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%234b5563'><path d='M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-8-6zM8 18h8v2H8zm0-4h8v2H8zm0-4h8v2H8zm0-4h5v2H8z'/></svg>";
                    return Content(docSvg, "image/svg+xml");
                }

                // Default file icon
                var fileSvg = @"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%239ca3af'><path d='M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-8-6zm0 18H6V4h5v5h3v11z'/></svg>";
                return Content(fileSvg, "image/svg+xml");
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // ================== FILE SERVING ==================
        [HttpGet("file")]
        public IActionResult GetFile([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(new { error = "path is required" });

            try
            {
                if (!System.IO.File.Exists(path))
                    return NotFound();

                var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var contentType = GetContentType(path);
                return File(fileStream, contentType);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        private string GetContentType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".mp4" => "video/mp4",
                ".mkv" => "video/x-matroska",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                ".flv" => "video/x-flv",
                ".wmv" => "video/x-ms-wmv",
                ".webm" => "video/webm",
                ".m4v" => "video/mp4",
                ".mpg" => "video/mpeg",
                ".mpeg" => "video/mpeg",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".tiff" => "image/tiff",
                ".tif" => "image/tiff",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".flac" => "audio/flac",
                ".aac" => "audio/aac",
                ".m4a" => "audio/mp4",
                ".ogg" => "audio/ogg",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".txt" => "text/plain",
                ".rtf" => "application/rtf",
                ".odt" => "application/vnd.oasis.opendocument.text",
                ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
                ".odp" => "application/vnd.oasis.opendocument.presentation",
                _ => "application/octet-stream"
            };
        }

        [HttpPost("download-zip")]
        public async Task<IActionResult> DownloadZip([FromBody] DownloadZipRequest request)
        {
            if (request?.Paths == null || request.Paths.Count == 0)
            {
                return BadRequest(new { error = "No paths provided" });
            }

            var memoryStream = new MemoryStream();
            try
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var fileCount = 0;
                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    
                    foreach (var path in request.Paths)
                    {
                        if (string.IsNullOrWhiteSpace(path)) continue;

                        var fullPath = Path.GetFullPath(path);

                        if (System.IO.File.Exists(fullPath))
                        {
                            var fileName = Path.GetFileName(fullPath);
                            // Ensure unique names in zip
                            var entryName = fileName;
                            var counter = 1;
                            while (usedNames.Contains(entryName))
                            {
                                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                                var ext = Path.GetExtension(fileName);
                                entryName = $"{nameWithoutExt}_{counter}{ext}";
                                counter++;
                            }
                            usedNames.Add(entryName);

                            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                            using var entryStream = entry.Open();
                            using var fileStream = System.IO.File.OpenRead(fullPath);
                            await fileStream.CopyToAsync(entryStream);
                            fileCount++;
                        }
                        else if (Directory.Exists(fullPath))
                        {
                            // Add folder and its contents recursively
                            await AddDirectoryToZip(archive, fullPath, Path.GetFileName(fullPath));
                            fileCount++;
                        }
                    }

                    if (fileCount == 0)
                    {
                        memoryStream.Dispose();
                        return NotFound(new { error = "No valid files found" });
                    }
                }

                memoryStream.Position = 0;
                return File(memoryStream, "application/zip", "files.zip");
            }
            catch (Exception ex)
            {
                memoryStream.Dispose();
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task AddDirectoryToZip(ZipArchive archive, string directoryPath, string entryPrefix)
        {
            try
            {
                foreach (var file in Directory.GetFiles(directoryPath))
                {
                    var relativePath = Path.Combine(entryPrefix, Path.GetFileName(file)).Replace('\\', '/');
                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    using var fileStream = System.IO.File.OpenRead(file);
                    await fileStream.CopyToAsync(entryStream);
                }

                foreach (var subDir in Directory.GetDirectories(directoryPath))
                {
                    var relativePath = Path.Combine(entryPrefix, Path.GetFileName(subDir));
                    await AddDirectoryToZip(archive, subDir, relativePath);
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        public class DownloadZipRequest
        {
            public List<string> Paths { get; set; } = new();
        }
    }
}
