namespace FilePortal.Models;

/// <summary>
/// A user-created album: an ordered collection of photo IDs.
/// </summary>
public class Album
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Username of the album owner.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Photo IDs contained in this album (ordered).</summary>
    public List<string> PhotoIds { get; set; } = new();

    /// <summary>Optional explicit cover; falls back to first photo when null.</summary>
    public string? CoverPhotoId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
