namespace FilePortal.Models;

public class FileItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;

    // Path relative to the root (we’ll use this in the UI to navigate)
    public string RelativePath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }
    public long? Size { get; set; }
    public DateTime? LastModified { get; set; }
}
