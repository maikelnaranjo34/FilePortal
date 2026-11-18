using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FilePortal.Services;

namespace FilePortal.Controllers
{
    [ApiController]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Route("api/[controller]")]
    [Authorize]
    public class SearchController : ControllerBase
    {
        private readonly PhotoLibraryService _library;
        private readonly FavoriteStoreService _favorites;

        public SearchController(PhotoLibraryService library, FavoriteStoreService favorites)
        {
            _library = library;
            _favorites = favorites;
        }

        private string CurrentUser => User.Identity?.Name ?? string.Empty;

        // GET /api/search?q=&from=&to=&type=&camera=&skip=&take=
        // type: all | photos | videos
        [HttpGet]
        public IActionResult Search(
            [FromQuery] string? q,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? type,
            [FromQuery] string? camera,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 200)
        {
            skip = Math.Max(skip, 0);
            take = Math.Clamp(take, 1, 500);
            var favs = _favorites.GetFavorites(CurrentUser);
            var photos = _library.GetVisiblePhotos(CurrentUser).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                photos = photos.Where(p =>
                    p.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (p.CameraModel?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.CameraMake?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    p.DirectoryPath.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            if (from.HasValue) photos = photos.Where(p => p.DateTaken >= from.Value.Date);
            if (to.HasValue) photos = photos.Where(p => p.DateTaken.Date <= to.Value.Date);

            if (!string.IsNullOrWhiteSpace(type))
            {
                if (type.Equals("photos", StringComparison.OrdinalIgnoreCase))
                    photos = photos.Where(p => !p.IsVideo);
                else if (type.Equals("videos", StringComparison.OrdinalIgnoreCase))
                    photos = photos.Where(p => p.IsVideo);
            }

            if (!string.IsNullOrWhiteSpace(camera))
            {
                photos = photos.Where(p =>
                    (p.CameraModel?.Contains(camera, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.CameraMake?.Contains(camera, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var ordered = photos.OrderByDescending(p => p.DateTaken).ToList();
            var total = ordered.Count;
            var page = ordered.Skip(skip).Take(take).Select(p => new
            {
                p.Id,
                p.FileName,
                p.IsVideo,
                p.DateTaken,
                p.Width,
                p.Height,
                isFavorite = favs.Contains(p.Id)
            });

            return Ok(new { total, skip, take, hasMore = skip + take < total, results = page });
        }

        // GET /api/search/cameras  -> distinct camera list for filters
        [HttpGet("cameras")]
        public IActionResult Cameras()
        {
            var photos = _library.GetVisiblePhotos(CurrentUser);
            var cameras = photos
                .Where(p => !string.IsNullOrWhiteSpace(p.CameraModel))
                .Select(p => p.CameraModel!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
            return Ok(cameras);
        }
    }
}
