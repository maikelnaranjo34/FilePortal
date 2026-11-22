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
    public class AlbumsController : ControllerBase
    {
        private readonly AlbumStoreService _albums;
        private readonly PhotoLibraryService _library;

        public AlbumsController(AlbumStoreService albums, PhotoLibraryService library)
        {
            _albums = albums;
            _library = library;
        }

        private string CurrentUser => User.Identity?.Name ?? string.Empty;
        private bool IsAdmin => User.IsInRole("Admin");

        private bool CanEdit(Album a) =>
            IsAdmin || string.Equals(a.Owner, CurrentUser, StringComparison.OrdinalIgnoreCase);

        private object Summarize(Album a) => new
        {
            a.Id,
            a.Name,
            a.Description,
            a.Owner,
            count = a.PhotoIds.Count,
            coverPhotoId = a.CoverPhotoId ?? a.PhotoIds.FirstOrDefault(),
            a.CreatedUtc,
            a.UpdatedUtc
        };

        // GET /api/albums
        [HttpGet]
        public IActionResult List() =>
            Ok(_albums.GetAlbumsForUser(CurrentUser, IsAdmin).Select(Summarize));

        public class CreateAlbumRequest
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public List<string>? PhotoIds { get; set; }
        }

        // POST /api/albums
        [HttpPost]
        public IActionResult Create([FromBody] CreateAlbumRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new { error = "Album name is required" });

            var valid = _library.GetByIds(req.PhotoIds ?? [], CurrentUser).Select(photo => photo.Id);
            var album = _albums.Create(CurrentUser, req.Name, req.Description ?? "", valid);
            return Ok(Summarize(album));
        }

        // GET /api/albums/{id}  -> album with photo details
        [HttpGet("{id}")]
        public IActionResult Get(string id)
        {
            var album = _albums.Get(id);
            if (album == null) return NotFound();
            if (!CanEdit(album)) return Forbid();

            var photos = _library.GetByIds(album.PhotoIds, CurrentUser)
                .OrderByDescending(p => p.DateTaken)
                .Select(p => new { p.Id, p.FileName, p.IsVideo, p.DateTaken, p.Width, p.Height });

            return Ok(new
            {
                album.Id,
                album.Name,
                album.Description,
                album.Owner,
                coverPhotoId = album.CoverPhotoId ?? album.PhotoIds.FirstOrDefault(),
                album.CreatedUtc,
                album.UpdatedUtc,
                photos
            });
        }

        public class UpdateAlbumRequest
        {
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? CoverPhotoId { get; set; }
        }

        // PUT /api/albums/{id}
        [HttpPut("{id}")]
        public IActionResult Update(string id, [FromBody] UpdateAlbumRequest req)
        {
            var album = _albums.Get(id);
            if (album == null) return NotFound();
            if (!CanEdit(album)) return Forbid();

            var ok = _albums.Update(id, a =>
            {
                if (!string.IsNullOrWhiteSpace(req.Name)) a.Name = req.Name.Trim();
                if (req.Description != null) a.Description = req.Description.Trim();
                if (req.CoverPhotoId != null && a.PhotoIds.Contains(req.CoverPhotoId))
                    a.CoverPhotoId = req.CoverPhotoId;
            });
            return ok ? Ok(Summarize(_albums.Get(id)!)) : StatusCode(500);
        }

        // DELETE /api/albums/{id}
        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            var album = _albums.Get(id);
            if (album == null) return NotFound();
            if (!CanEdit(album)) return Forbid();
            return _albums.Delete(id) ? NoContent() : StatusCode(500);
        }

        public class PhotoIdsRequest { public List<string> PhotoIds { get; set; } = new(); }

        // POST /api/albums/{id}/photos  -> add photos
        [HttpPost("{id}/photos")]
        public IActionResult AddPhotos(string id, [FromBody] PhotoIdsRequest req)
        {
            var album = _albums.Get(id);
            if (album == null) return NotFound();
            if (!CanEdit(album)) return Forbid();
            if (req?.PhotoIds == null || req.PhotoIds.Count == 0)
                return BadRequest(new { error = "No photoIds provided" });

            var valid = _library.GetByIds(req.PhotoIds, CurrentUser).Select(p => p.Id);
            _albums.AddPhotos(id, valid);
            return Ok(Summarize(_albums.Get(id)!));
        }

        // DELETE /api/albums/{id}/photos  -> remove photos
        [HttpDelete("{id}/photos")]
        public IActionResult RemovePhotos(string id, [FromBody] PhotoIdsRequest req)
        {
            var album = _albums.Get(id);
            if (album == null) return NotFound();
            if (!CanEdit(album)) return Forbid();
            if (req?.PhotoIds == null || req.PhotoIds.Count == 0)
                return BadRequest(new { error = "No photoIds provided" });

            _albums.RemovePhotos(id, req.PhotoIds);
            return Ok(Summarize(_albums.Get(id)!));
        }
    }
}
