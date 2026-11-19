using System.Text.Json;
using FilePortal.Models;
using Microsoft.AspNetCore.DataProtection;

namespace FilePortal.Services;

public sealed class AiSettingsService
{
    private readonly string _storePath;
    private readonly string _lockPath;
    private readonly IDataProtector _protector;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AiSettingsService(UserStoreService users, IDataProtectionProvider protection)
    {
        _storePath = Path.Combine(users.DirectoryPath, "ai-settings.json");
        _lockPath = Path.Combine(users.DirectoryPath, "ai-settings.lock");
        _protector = protection.CreateProtector("FilePortal.AiSettings.v1");
    }

    public AiSettings Get()
    {
        try
        {
            using var stream = new FileStream(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return JsonSerializer.Deserialize<AiSettings>(stream, JsonOptions)
                ?? throw new InvalidDataException("The AI settings store is invalid.");
        }
        catch (FileNotFoundException)
        {
            return new AiSettings();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The AI settings store is invalid.", exception);
        }
    }

    public void Update(Action<AiSettings> change)
    {
        using var writeLock = FileStateLock.Acquire(_lockPath);
        var settings = Get();
        change(settings);
        Save(settings);
    }

    public string? GetOpenAiApiKey() => Unprotect(Get().ProtectedOpenAiApiKey);
    public string? GetAssemblyAiApiKey() => Unprotect(Get().ProtectedAssemblyAiApiKey);
    public string Protect(string value) => _protector.Protect(value);

    private string? Unprotect(string? value) => string.IsNullOrWhiteSpace(value) ? null : _protector.Unprotect(value);

    private void Save(AiSettings settings)
    {
        var temporaryPath = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
