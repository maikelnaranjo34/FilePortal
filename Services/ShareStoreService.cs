using System.Security.Cryptography;
using System.Text.Json;
using FilePortal.Models;

namespace FilePortal.Services;

/// <summary>
/// JSON-file backed store for public share links.
/// </summary>
public class ShareStoreService
{
    private readonly string _storePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ShareStoreService(UserStoreService users)
    {
        var dataDir = users.DirectoryPath;
        Directory.CreateDirectory(dataDir);
        _storePath = Path.Combine(dataDir, "shares.json");
    }

    private List<Share> LoadAll()
    {
        try
        {
            using var stream = new FileStream(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var records = JsonSerializer.Deserialize<List<Share>>(stream) ?? throw new InvalidDataException("The gallery store is invalid.");
            return records;
        }
        catch (FileNotFoundException)
        {
            return [];
        }
    }

    private void SaveAll(List<Share> list)
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

    private static string NewToken()
    {
        // 32 url-safe chars, unguessable
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public Share Create(string owner, string type, string targetId, DateTime? expiresUtc)
    {
        using (FileStateLock.Acquire(_storePath + ".lock"))
        {
            var list = LoadAll();
            var share = new Share
            {
                Token = NewToken(),
                Type = type,
                TargetId = targetId,
                Owner = owner,
                ExpiresUtc = expiresUtc
            };
            list.Add(share);
            SaveAll(list);
            return share;
        }
    }

    public Share? GetByToken(string token)
    {
        var share = LoadAll().FirstOrDefault(s => s.Token == token);
        if (share == null || share.IsExpired) return null;
        return share;
    }

    public List<Share> GetForOwner(string owner) =>
        LoadAll().Where(s => string.Equals(s.Owner, owner, StringComparison.OrdinalIgnoreCase)).ToList();

    public bool Revoke(string token, string owner, bool isAdmin)
    {
        using (FileStateLock.Acquire(_storePath + ".lock"))
        {
            var list = LoadAll();
            var share = list.FirstOrDefault(s => s.Token == token);
            if (share == null) return false;
            if (!isAdmin && !string.Equals(share.Owner, owner, StringComparison.OrdinalIgnoreCase)) return false;
            list.Remove(share);
            SaveAll(list);
            return true;
        }
    }
}
