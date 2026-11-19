using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FilePortal.Models;
using FilePortal.Services;

namespace FilePortal.Controllers
{
    [ApiController]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Route("api/[controller]")]
    [Authorize]
    public class PhotosController : ControllerBase
    {
        private readonly PhotoLibraryService _library;
        private readonly FavoriteStoreService _favorites;
        private readonly ThumbnailService _thumbnails;
        private readonly FileArchiveService _archives;
        private readonly FileBrowserService _browser;

        public PhotosController(PhotoLibraryService library, FavoriteStoreService favorites, ThumbnailService thumbnails,
            FileArchiveService archives, FileBrowserService browser)
        {
            _library = library;
            _favorites = favorites;
            _thumbnails = thumbnails;
            _archives = archives;
            _browser = browser;
        }

        private string CurrentUser => User.Identity?.Name ?? string.Empty;

        public class PhotoDto
        {
            public string Id { get; set; } = "";
            public string FileName { get; set; } = "";
            public bool IsVideo { get; set; }
            public long Size { get; set; }
            public DateTime DateTaken { get; set; }
            public int? Width { get; set; }
            public int? Height { get; set; }
            public bool IsFavorite { get; set; }
        }

        public class TimelineSectionDto
        {
            public string Title { get; set; } = "";   // e.g. "June 22, 2026"
            public string Key { get; set; } = "";     // e.g. "2026-06-22"
            public List<PhotoDto> Photos { get; set; } = new();
        }

        private PhotoDto ToDto(PhotoMeta m, HashSet<string> favs) => new()
        {
            Id = m.Id,
            FileName = m.FileName,
            IsVideo = m.IsVideo,
            Size = m.Size,
            DateTaken = m.DateTaken,
            Width = m.Width,
            Height = m.Height,
            IsFavorite = favs.Contains(m.Id)
        };

        // GET /api/photos/timeline?skip=0&take=100&favorites=false&videosOnly=false
        [HttpGet("timeline")]
        public IActionResult GetTimeline([FromQuery] int skip = 0, [FromQuery] int take = 120,
            [FromQuery] bool favorites = false, [FromQuery] bool videosOnly = false)
        {
            skip = Math.Max(skip, 0);
            take = Math.Clamp(take, 1, 500);
            var favs = _favorites.GetFavorites(CurrentUser);

            var photos = _library.GetVisiblePhotos(CurrentUser);
            if (videosOnly)
                photos = photos.Where(photo => photo.IsVideo).ToList();
            if (favorites)
                photos = photos.Where(p => favs.Contains(p.Id)).ToList();

            var total = photos.Count;
            var page = photos.Skip(skip).Take(take).ToList();

            // Group page into day sections (already sorted newest first)
            var sections = page
                .GroupBy(p => p.DateTaken.Date)
                .Select(g => new TimelineSectionDto
                {
                    Key = g.Key.ToString("yyyy-MM-dd"),
                    Title = g.Key.ToString("MMMM d, yyyy"),
                    Photos = g.Select(p => ToDto(p, favs)).ToList()
                })
                .ToList();

            return Ok(new
            {
                total,
                skip,
                take,
                returned = page.Count,
                hasMore = skip + page.Count < total,
                sections
            });
        }

        // GET /api/photos/{id}  -> full metadata + EXIF
        [HttpGet("{id}")]
        public IActionResult GetPhoto(string id)
        {
            var m = _library.GetById(id, CurrentUser);
            if (m == null) return NotFound(new { error = "Photo not found or access denied" });

            var favs = _favorites.GetFavorites(CurrentUser);
            return Ok(new
            {
                m.Id,
                m.FileName,
                m.Extension,
                m.IsVideo,
                m.Size,
                m.DateTaken,
                m.DateModified,
                m.Width,
                m.Height,
                m.CameraMake,
                m.CameraModel,
                m.Latitude,
                m.Longitude,
                isFavorite = favs.Contains(m.Id)
            });
        }

        // GET /api/photos/{id}/thumbnail
        [HttpGet("{id}/thumbnail")]
        public async Task<IActionResult> GetThumbnail(string id, CancellationToken cancellationToken)
        {
            var m = _library.GetById(id, CurrentUser);
            if (m == null) return NotFound();

            var file = new FileInfo(m.FullPath);
            var thumbnail = m.IsVideo
                ? await _thumbnails.GetVideoThumbnailAsync(file, cancellationToken)
                : _thumbnails.GetThumbnail(file);
            if (thumbnail is not null)
                return File(thumbnail, "image/jpeg");

            return Content(ThumbnailService.GetMediaPlaceholder(m.IsVideo), "image/svg+xml");
        }

        // GET /api/photos/{id}/file  -> stream original (range-enabled for video)
        [HttpGet("{id}/file")]
        public IActionResult GetFile(string id, [FromQuery] bool download = false)
        {
            var m = _library.GetById(id, CurrentUser);
            if (m == null || !System.IO.File.Exists(m.FullPath))
                return NotFound();

            var stream = FileBrowserService.OpenRead(m.FullPath);
            var contentType = FileBrowserService.GetContentType(m.FullPath);
            // enableRangeProcessing => video seeking works
            return File(stream, contentType, download ? m.FileName : null, enableRangeProcessing: true);
        }

        // GET /api/photos/favorites
        [HttpGet("favorites")]
        public IActionResult GetFavorites()
        {
            var favs = _favorites.GetFavorites(CurrentUser);
            var photos = _library.GetByIds(favs, CurrentUser);
            return Ok(photos.OrderByDescending(p => p.DateTaken).Select(p => ToDto(p, favs)));
        }

        public class FavoriteRequest { public bool Favorite { get; set; } = true; }

        // POST /api/photos/{id}/favorite
        [HttpPost("{id}/favorite")]
        public IActionResult SetFavorite(string id, [FromBody] FavoriteRequest req)
        {
            var m = _library.GetById(id, CurrentUser);
            if (m == null) return NotFound(new { error = "Photo not found or access denied" });
            _favorites.SetFavorite(CurrentUser, id, req?.Favorite ?? true);
            return Ok(new { id, favorite = req?.Favorite ?? true });
        }

        public class IdsRequest { public List<string> Ids { get; set; } = new(); }

        // POST /api/photos/batch/delete  (admin only - destructive)
        [Authorize(Roles = "Admin")]
        [HttpPost("batch/delete")]
        public IActionResult BatchDelete([FromBody] IdsRequest req)
        {
            if (req?.Ids == null || req.Ids.Count == 0)
                return BadRequest(new { error = "No ids provided" });

            var ids = req.Ids.Distinct(StringComparer.Ordinal).ToList();
            var photos = _library.GetByIds(ids, CurrentUser);
            if (photos.Count != ids.Count)
                return NotFound(new { error = "One or more photos are missing or inaccessible." });

            var deletedIds = new List<string>();
            var errors = new List<string>();
            foreach (var p in photos)
            {
                try
                {
                    if (System.IO.File.Exists(p.FullPath))
                    {
                        System.IO.File.Delete(p.FullPath);
                        deletedIds.Add(p.Id);
                    }
                    else
                        errors.Add($"{p.FileName}: File no longer exists.");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{p.FileName}: {FileDeletion.DescribeFailure(p.FullPath, exception)}");
                }
            }
            _library.ForgetMissing(photos);
            return Ok(new { deleted = deletedIds.Count, deletedIds, errors });
        }

        // POST /api/photos/batch/download  -> zip of selected photos
        [HttpPost("batch/download")]
        public Task<IActionResult> BatchDownload([FromBody] IdsRequest request, CancellationToken cancellationToken) =>
            Download(request.Ids, cancellationToken);

        [HttpPost("batch/download-form")]
        [RequestFormLimits(ValueCountLimit = 10_000)]
        public Task<IActionResult> BatchDownloadForm([FromForm] List<string> ids, CancellationToken cancellationToken) =>
            Download(ids, cancellationToken);

        private async Task<IActionResult> Download(List<string>? ids, CancellationToken cancellationToken)
        {
            if (ids is null or { Count: 0 })
                return BadRequest(new { error = "Select at least one photo." });

            var photos = _library.GetByIds(ids, CurrentUser);
            if (photos.Count == 0)
                return NotFound(new { error = "No accessible photos." });

            var roots = _browser.GetRoots((UserRecord)HttpContext.Items[typeof(UserRecord)]!);
            var archive = await _archives.CreateAsync(photos.Select(photo => photo.FullPath), roots, cancellationToken);
            return File(archive, "application/zip", "photos.zip");
        }

        // POST /api/photos/scan  (admin only) -> force library rescan
        [Authorize(Roles = "Admin")]
        [HttpPost("scan")]
        public IActionResult Rescan()
        {
            var count = _library.Scan(force: true);
            return Ok(new { message = "Scan complete", count });
        }
    }
}
