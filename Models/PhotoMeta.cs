namespace FilePortal.Models;

/// <summary>
/// Cached metadata for a single photo or video in the library.
/// </summary>
public class PhotoMeta
{
    /// <summary>Stable identifier (hash of the full path). Used in all photo URLs.</summary>
    public string Id { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;

    public bool IsVideo { get; set; }
    public long Size { get; set; }

    /// <summary>Best-effort "date taken": EXIF DateTimeOriginal, else file last-write.</summary>
    public DateTime DateTaken { get; set; }
    public DateTime DateModified { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Cache invalidation key (file last-write ticks at extraction time).</summary>
    public long CacheKey { get; set; }
}
