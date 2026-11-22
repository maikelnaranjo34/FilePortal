using System.Text.Json;
using FilePortal.Models;

namespace FilePortal.Services;

/// <summary>
/// JSON-file backed store for albums. Uses a shared lock for updates.
/// </summary>
public class AlbumStoreService
{
    private readonly string _storePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public AlbumStoreService(UserStoreService users)
    {
        var dataDir = users.DirectoryPath;
        Directory.CreateDirectory(dataDir);
        _storePath = Path.Combine(dataDir, "albums.json");
    }

    private List<Album> LoadAll()
    {
        try
        {
            using var stream = new FileStream(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var records = JsonSerializer.Deserialize<List<Album>>(stream) ?? throw new InvalidDataException("The gallery store is invalid.");
            return records;
        }
        catch (FileNotFoundException)
        {
            return [];
        }
    }

    private void SaveAll(List<Album> list)
    {
        var temporary = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, list, _jsonOptions);

            File.Move(temporary, _storePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public List<Album> GetAlbumsForUser(string username, bool isAdmin)
    {
        var all = LoadAll();
        if (isAdmin) return all.OrderByDescending(a => a.UpdatedUtc).ToList();
        return all.Where(a => string.Equals(a.Owner, username, StringComparison.OrdinalIgnoreCase))
                  .OrderByDescending(a => a.UpdatedUtc).ToList();
    }

    public Album? Get(string id) => LoadAll().FirstOrDefault(a => a.Id == id);

    public Album Create(string owner, string name, string description, IEnumerable<string> photoIds)
    {
        using (FileStateLock.Acquire(_storePath + ".lock"))
        {
            var list = LoadAll();
            var album = new Album
            {
                Owner = owner,
                Name = string.IsNullOrWhiteSpace(name) ? "Untitled album" : name.Trim(),
                Description = description.Trim(),
                PhotoIds = photoIds.Distinct(StringComparer.Ordinal).ToList()
            };
            album.CoverPhotoId = album.PhotoIds.FirstOrDefault();
            list.Add(album);
            SaveAll(list);
            return album;
        }
    }

    public bool Update(string id, Action<Album> mutate)
    {
        using (FileStateLock.Acquire(_storePath + ".lock"))
        {
            var list = LoadAll();
            var album = list.FirstOrDefault(a => a.Id == id);
            if (album == null) return false;
            mutate(album);
            album.UpdatedUtc = DateTime.UtcNow;
            SaveAll(list);
            return true;
        }
    }

    public bool Delete(string id)
    {
        using (FileStateLock.Acquire(_storePath + ".lock"))
        {
            var list = LoadAll();
            var album = list.FirstOrDefault(a => a.Id == id);
            if (album == null) return false;
            list.Remove(album);
            SaveAll(list);
            return true;
        }
    }

    public bool AddPhotos(string id, IEnumerable<string> photoIds) =>
        Update(id, album =>
        {
            album.PhotoIds = album.PhotoIds.Union(photoIds, StringComparer.Ordinal).ToList();
            album.CoverPhotoId ??= album.PhotoIds.FirstOrDefault();
        });

    public bool RemovePhotos(string id, IEnumerable<string> photoIds)
    {
        var ids = photoIds.ToHashSet(StringComparer.Ordinal);
        return Update(id, album =>
        {
            album.PhotoIds.RemoveAll(ids.Contains);
            if (album.CoverPhotoId is not null && ids.Contains(album.CoverPhotoId))
                album.CoverPhotoId = album.PhotoIds.FirstOrDefault();
        });
    }
}
