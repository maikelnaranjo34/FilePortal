using System.Security.Cryptography;
using System.Text.Json;
using FilePortal.Models;

namespace FilePortal.Services;

public enum UserChange
{
    Updated,
    NotFound,
    Conflict,
    LastAdmin,
    InvalidPassword
}

public class UserStoreService
{
    public const string SessionStampClaim = "fileportal:session-stamp";

    private readonly string _storePath;
    private readonly string _lockPath;
    public string DirectoryPath { get; }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public UserStoreService(IConfiguration configuration)
    {
        DirectoryPath = GetDirectoryPath(configuration);
        Directory.CreateDirectory(DirectoryPath);
        _storePath = Path.Combine(DirectoryPath, "users.json");
        _lockPath = Path.Combine(DirectoryPath, "users.lock");
    }

    public static string GetDirectoryPath(IConfiguration configuration)
    {
        var directory = configuration["FilePortal:DataDirectory"];
        return string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.GetFullPath(directory);
    }

    private List<UserRecord> LoadAll()
    {
        try
        {
            using var stream = new FileStream(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var users = JsonSerializer.Deserialize<List<UserRecord>>(stream, JsonOptions);
            if (users is null || users.Any(user => user is null || user.AllowedFolders is null))
                throw new InvalidDataException("The FilePortal user store is invalid.");
            return users;
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The FilePortal user store is invalid.", exception);
        }
    }

    private void SaveAll(List<UserRecord> users)
    {
        var temporaryPath = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, users, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private UserChange Update(Func<List<UserRecord>, UserChange> change)
    {
        using var writeLock = FileStateLock.Acquire(_lockPath);
        var users = LoadAll();
        var result = change(users);
        if (result == UserChange.Updated)
            SaveAll(users);
        return result;
    }

    private static UserRecord? FindUser(List<UserRecord> users, string username) =>
        users.Find(user => string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<UserRecord> GetUsers() =>
        LoadAll().OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase);

    public UserRecord? GetUser(string username) => FindUser(LoadAll(), username);

    public UserRecord? Authenticate(string username, string password)
    {
        var user = GetUser(username.Trim());
        return user is not null && CheckPassword(user, password) ? user : null;
    }

    public UserChange AddUser(string username, string password, bool isAdmin, string firstName, string lastName) =>
        AddUser(username, password, isAdmin, firstName, lastName, provisioning: false);

    public UserChange ProvisionAdmin(string username, string password) =>
        AddUser(username, password, true, "", "", provisioning: true);

    private UserChange AddUser(string username, string password, bool isAdmin, string firstName, string lastName, bool provisioning)
    {
        username = username.Trim();
        var user = new UserRecord
        {
            Username = username,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            IsAdmin = isAdmin,
            CreatedUtc = DateTime.UtcNow
        };
        SetPassword(user, password);

        return Update(users =>
        {
            if (FindUser(users, username) is not null || (provisioning && users.Any(account => account.IsAdmin)))
                return UserChange.Conflict;

            users.Add(user);
            return UserChange.Updated;
        });
    }

    public UserChange RemoveUser(string username) => Update(users =>
    {
        var user = FindUser(users, username);
        if (user is null) return UserChange.NotFound;
        if (user.IsAdmin && users.Count(account => account.IsAdmin) == 1) return UserChange.LastAdmin;

        users.Remove(user);
        return UserChange.Updated;
    });

    public UserChange SetAdmin(string username, bool isAdmin) => Update(users =>
    {
        var user = FindUser(users, username);
        if (user is null) return UserChange.NotFound;
        if (user.IsAdmin == isAdmin) return UserChange.Conflict;
        if (!isAdmin && users.Count(account => account.IsAdmin) == 1) return UserChange.LastAdmin;

        user.IsAdmin = isAdmin;
        user.SecurityStamp = Guid.NewGuid();
        return UserChange.Updated;
    });

    public UserChange SetAllowedFolders(string username, List<string> folders) => Update(users =>
    {
        var user = FindUser(users, username);
        if (user is null) return UserChange.NotFound;

        user.AllowedFolders = folders;
        return UserChange.Updated;
    });

    public UserChange UpdatePassword(string username, string newPassword, string? currentPassword) => Update(users =>
    {
        var user = FindUser(users, username);
        if (user is null) return UserChange.NotFound;
        if (currentPassword is not null && !CheckPassword(user, currentPassword)) return UserChange.InvalidPassword;

        SetPassword(user, newPassword);
        return UserChange.Updated;
    });

    private static bool CheckPassword(UserRecord user, string password)
    {
        var salt = Convert.FromBase64String(user.Salt);
        var expected = Convert.FromBase64String(user.Hash);
        var actual = HashPassword(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static void SetPassword(UserRecord user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        user.Salt = Convert.ToBase64String(salt);
        user.Hash = Convert.ToBase64String(HashPassword(password, salt));
        user.SecurityStamp = Guid.NewGuid();
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
}
