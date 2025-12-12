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

namespace FilePortal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FilesController : ControllerBase
    {
        // 🔵 Phone (Syncthing) root
        private static readonly string PhoneRoot = @"D:\Samsung S20_Cloud\WebsiteBrowser\Files";

        // 🔵 TODO: PUT YOUR REAL UNC PATHS HERE
        // Example only – REPLACE these with the UNC paths you used before
        private static readonly string RouterYPath = @"\\192.168.5.1\h";  // <--- CHANGE THIS
        private static readonly string RouterZPath = @"\\192.168.5.1\g";  // <--- CHANGE THIS

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

        // ================== ROOTS ==================
        [HttpGet("roots")]
        public ActionResult<IEnumerable<RootDto>> GetRoots()
        {
            var list = new List<RootDto>();

            // Local / directly visible drives (E, F, H, etc) - exclude C drive
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                
                // Skip C drive
                if (drive.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                    continue;

                list.Add(new RootDto
                {
                    Name = drive.Name.TrimEnd('\\'),
                    Path = drive.RootDirectory.FullName
                });
            }

            // Phone (Syncthing) root
            if (Directory.Exists(PhoneRoot))
            {
                list.Add(new RootDto
                {
                    Name = "Samsung S20",
                    Path = PhoneRoot
                });
            }

            // 🔵 Router shares via UNC (these do NOT depend on Z: / Y: mappings)
            if (Directory.Exists(RouterYPath))
            {
                list.Add(new RootDto
                {
                    Name = "Easystore",
                    Path = RouterYPath
                });
            }

            if (Directory.Exists(RouterZPath))
            {
                list.Add(new RootDto
                {
                    Name = "Data",
                    Path = RouterZPath
                });
            }

            return Ok(list);
        }

        // ================== FOLDER LIST ==================
        [HttpGet("folder")]
        public ActionResult<IEnumerable<ItemDto>> GetFolder([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(new { error = "path is required" });

            if (!Directory.Exists(path))
                return NotFound(new { error = "directory not found" });

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
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Access denied." });
            }
            catch (IOException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
            }

            // Folders first, then files
            items = items
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Ok(items);
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
    }
}
