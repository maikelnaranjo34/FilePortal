namespace FilePortal.Models;

/// <summary>
/// A public, token-based share link for a single photo or a whole album.
/// </summary>
public class Share
{
    /// <summary>Opaque, unguessable token used in the public URL.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>"photo" or "album".</summary>
    public string Type { get; set; } = "photo";

    /// <summary>Photo ID or Album ID being shared.</summary>
    public string TargetId { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Optional expiry; null means no expiry.</summary>
    public DateTime? ExpiresUtc { get; set; }

    public bool IsExpired => ExpiresUtc.HasValue && DateTime.UtcNow > ExpiresUtc.Value;
}
