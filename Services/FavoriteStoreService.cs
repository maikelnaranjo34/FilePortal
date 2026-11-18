using System.Text.Json;

namespace FilePortal.Services;

/// <summary>
/// Per-user favorite (starred) photos. Stored as username -> set of photo IDs.
/// </summary>
public class FavoriteStoreService
{
    private readonly string _storePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public FavoriteStoreService(UserStoreService users)
    {
        var dataDir = users.DirectoryPath;
        Directory.CreateDirectory(dataDir);
        _storePath = Path.Combine(dataDir, "favorites.json");
    }

    private Dictionary<string, List<string>> LoadAll()
    {
        try
        {
            using var stream = new FileStream(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var records = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(stream) ?? throw new InvalidDataException("The gallery store is invalid.");
            var favorites = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (user, ids) in records)
            {
                if (favorites.TryGetValue(user, out var existing))
                    favorites[user] = existing.Union(ids, StringComparer.Ordinal).ToList();
                else
                    favorites.Add(user, ids);
            }
            return favorites;
        }
        catch (FileNotFoundException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveAll(Dictionary<string, List<string>> map)
    {
        var temporary = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, map, _jsonOptions);

            File.Move(temporary, _storePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public HashSet<string> GetFavorites(string username)
    {
        var map = LoadAll();
        return map.TryGetValue(username, out var list)
            ? new HashSet<string>(list)
            : new HashSet<string>();
    }

    public bool IsFavorite(string username, string photoId) =>
        GetFavorites(username).Contains(photoId);

    public void SetFavorite(string username, string photoId, bool favorite)
    {
        using (FileStateLock.Acquire(_storePath + ".lock"))
        {
            var map = LoadAll();
            if (!map.TryGetValue(username, out var list))
            {
                list = new List<string>();
                map[username] = list;
            }
            if (favorite)
            {
                if (!list.Contains(photoId)) list.Add(photoId);
            }
            else
            {
                list.Remove(photoId);
            }
            SaveAll(map);
        }
    }
}
