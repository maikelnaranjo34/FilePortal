using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FilePortal.Services;

namespace FilePortal.Controllers
{
    [ApiController]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Route("api/[controller]")]
    public class SharingController : ControllerBase
    {
        private readonly ShareStoreService _shares;
        private readonly AlbumStoreService _albums;
        private readonly PhotoLibraryService _library;
        private readonly ThumbnailService _thumbnails;

        public SharingController(ShareStoreService shares, AlbumStoreService albums,
            PhotoLibraryService library, ThumbnailService thumbnails)
        {
            _shares = shares;
            _albums = albums;
            _library = library;
            _thumbnails = thumbnails;
        }

        private string CurrentUser => User.Identity?.Name ?? string.Empty;
        private bool IsAdmin => User.IsInRole("Admin");

        public class CreateShareRequest
        {
            public string Type { get; set; } = "photo";   // "photo" | "album"
            public string TargetId { get; set; } = "";
            public int? ExpiresInDays { get; set; }
        }

        // POST /api/sharing  (auth) -> create a share link
        [Authorize]
        [HttpPost]
        public IActionResult Create([FromBody] CreateShareRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.TargetId))
                return BadRequest(new { error = "targetId is required" });

            var type = req.Type?.ToLowerInvariant() == "album" ? "album" : "photo";

            // Verify ownership/access before sharing
            if (type == "photo")
            {
                var p = _library.GetById(req.TargetId, CurrentUser);
                if (p == null) return NotFound(new { error = "Photo not found or access denied" });
            }
            else
            {
                var a = _albums.Get(req.TargetId);
                if (a == null) return NotFound(new { error = "Album not found" });
                if (!IsAdmin && !string.Equals(a.Owner, CurrentUser, StringComparison.OrdinalIgnoreCase))
                    return Forbid();
            }

            if (req.ExpiresInDays is < 1 or > 365)
                return BadRequest(new { error = "Expiration must be between 1 and 365 days." });

            DateTime? expires = req.ExpiresInDays.HasValue
                ? DateTime.UtcNow.AddDays(req.ExpiresInDays.Value)
                : null;

            var share = _shares.Create(CurrentUser, type, req.TargetId, expires);
            var url = $"{Request.Scheme}://{Request.Host}/share.html?token={share.Token}";
            return Ok(new { share.Token, share.Type, share.TargetId, share.ExpiresUtc, url });
        }

        // GET /api/sharing/mine  (auth)
        [Authorize]
        [HttpGet("mine")]
        public IActionResult Mine() => Ok(_shares.GetForOwner(CurrentUser).Select(s => new
        {
            s.Token, s.Type, s.TargetId, s.CreatedUtc, s.ExpiresUtc,
            url = $"{Request.Scheme}://{Request.Host}/share.html?token={s.Token}"
        }));

        // DELETE /api/sharing/{token}  (auth)
        [Authorize]
        [HttpDelete("{token}")]
        public IActionResult Revoke(string token) =>
            _shares.Revoke(token, CurrentUser, IsAdmin) ? NoContent() : NotFound();

        // ---------- PUBLIC (no auth) ----------

        // GET /api/sharing/public/{token}  -> manifest of shared content
        [AllowAnonymous]
        [HttpGet("public/{token}")]
        public IActionResult PublicManifest(string token)
        {
            var share = _shares.GetByToken(token);
            if (share == null) return NotFound(new { error = "Share not found or expired" });

            if (share.Type == "photo")
            {
                var p = _library.GetByIds(new[] { share.TargetId }, share.Owner).FirstOrDefault();
                if (p == null) return NotFound(new { error = "Photo no longer available" });
                return Ok(new
                {
                    type = "photo",
                    title = p.FileName,
                    photos = new[] { new { p.Id, p.FileName, p.IsVideo, p.DateTaken, p.Width, p.Height } }
                });
            }
            else
            {
                var album = _albums.Get(share.TargetId);
                if (album == null) return NotFound(new { error = "Album no longer available" });
                var photos = _library.GetByIds(album.PhotoIds, share.Owner)
                    .OrderByDescending(p => p.DateTaken)
                    .Select(p => new { p.Id, p.FileName, p.IsVideo, p.DateTaken, p.Width, p.Height });
                return Ok(new { type = "album", title = album.Name, description = album.Description, photos });
            }
        }

        // GET /api/sharing/public/{token}/photo/{id}/thumbnail
        [AllowAnonymous]
        [HttpGet("public/{token}/photo/{id}/thumbnail")]
        public IActionResult PublicThumbnail(string token, string id)
        {
            if (!TryResolvePublicPhoto(token, id, out var fullPath, out var isVideo))
                return NotFound();

            if (!isVideo)
            {
                var thumbnail = _thumbnails.GetThumbnail(new FileInfo(fullPath!));
                if (thumbnail is not null)
                    return File(thumbnail, "image/jpeg");
            }

            return Content(ThumbnailService.GetMediaPlaceholder(isVideo), "image/svg+xml");
        }

        // GET /api/sharing/public/{token}/photo/{id}/file
        [AllowAnonymous]
        [HttpGet("public/{token}/photo/{id}/file")]
        public IActionResult PublicFile(string token, string id)
        {
            if (!TryResolvePublicPhoto(token, id, out var fullPath, out _) || !System.IO.File.Exists(fullPath))
                return NotFound();

            var stream = new FileStream(fullPath!, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, FileBrowserService.GetContentType(fullPath!), enableRangeProcessing: true);
        }

        private bool TryResolvePublicPhoto(string token, string id, out string? fullPath, out bool isVideo)
        {
            fullPath = null; isVideo = false;
            var share = _shares.GetByToken(token);
            if (share == null) return false;

            // The requested photo id must belong to the shared target
            var allowedIds = new HashSet<string>();
            if (share.Type == "photo") allowedIds.Add(share.TargetId);
            else
            {
                var album = _albums.Get(share.TargetId);
                if (album == null) return false;
                foreach (var pid in album.PhotoIds) allowedIds.Add(pid);
            }
            if (!allowedIds.Contains(id)) return false;

            var p = _library.GetByIds(new[] { id }, share.Owner).FirstOrDefault();
            if (p == null) return false;
            fullPath = p.FullPath;
            isVideo = p.IsVideo;
            return true;
        }
    }
}
