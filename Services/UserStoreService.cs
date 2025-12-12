using System.Security.Cryptography;
using System.Text;
using FilePortal.Models;
using System.Text.Json;

namespace FilePortal.Services;

public class UserStoreService
{
    private readonly string _storePath;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public UserStoreService()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _storePath = Path.Combine(dataDir, "users.json");
    }

    private List<UserRecord> LoadAll()
    {
        lock (_lock)
        {
            if (!File.Exists(_storePath)) return new List<UserRecord>();
            var json = File.ReadAllText(_storePath);
            try
            {
                return JsonSerializer.Deserialize<List<UserRecord>>(json) ?? new List<UserRecord>();
            }
            catch
            {
                return new List<UserRecord>();
            }
        }
    }

    private void SaveAll(List<UserRecord> list)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(list, _jsonOptions);
            File.WriteAllText(_storePath, json);
        }
    }

    public IEnumerable<UserRecord> GetUsers()
    {
        return LoadAll().OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase);
    }

    public UserRecord? GetUser(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        return LoadAll().FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
    }

    public int CountAdmins()
    {
        return LoadAll().Count(u => u.IsAdmin);
    }

    public bool IsEmpty()
    {
        return LoadAll().Count == 0;
    }

    public bool SetAdmin(string username, bool isAdmin)
    {
        var list = LoadAll();
        var rec = list.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (rec == null) return false;
        rec.IsAdmin = isAdmin;
        SaveAll(list);
        return true;
    }

    public bool AddUser(string username, string password, bool isAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return false;
        username = username.Trim();

        var list = LoadAll();
        if (list.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))) return false;

        // generate salt
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt);

        var rec = new UserRecord
        {
            Username = username,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash),
            CreatedUtc = DateTime.UtcNow
            ,IsAdmin = isAdmin
        };

        list.Add(rec);
        SaveAll(list);
        return true;
    }

    public bool RemoveUser(string username)
    {
        var list = LoadAll();
        var found = list.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (found == null) return false;
        list.Remove(found);
        SaveAll(list);
        return true;
    }

    public bool ValidateCredentials(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return false;
        var list = LoadAll();
        var rec = list.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (rec == null) return false;

        var salt = Convert.FromBase64String(rec.Salt);
        var expected = Convert.FromBase64String(rec.Hash);
        var actual = HashPassword(password, salt);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public UserRecord? Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;
        var list = LoadAll();
        var rec = list.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (rec == null) return null;

        var salt = Convert.FromBase64String(rec.Salt);
        var expected = Convert.FromBase64String(rec.Hash);
        var actual = HashPassword(password, salt);
        if (CryptographicOperations.FixedTimeEquals(actual, expected)) return rec;
        return null;
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        // PBKDF2
        using var kdf = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32);
    }

    public bool UpdatePassword(string username, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(newPassword)) return false;
        var list = LoadAll();
        var rec = list.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (rec == null) return false;

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(newPassword, salt);

        rec.Salt = Convert.ToBase64String(salt);
        rec.Hash = Convert.ToBase64String(hash);
        rec.CreatedUtc = DateTime.UtcNow;
        SaveAll(list);
        return true;
    }
}
