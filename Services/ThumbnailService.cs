using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace FilePortal.Services;

public sealed class ThumbnailService(IConfiguration configuration, ILogger<ThumbnailService> logger) : IDisposable
{
    private const int MaxSide = 240;
    private const long MaxPixels = 50_000_000;
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 32 * 1024 * 1024 });
    private readonly SemaphoreSlim _videoSlots = new(2);
    private readonly string? _ffmpegPath = ResolveFfmpegPath(configuration["VideoThumbnails:FfmpegPath"]);

    public byte[]? GetThumbnail(FileInfo file)
    {
        if (!Formats.Contains(file.Extension))
            return null;

        var key = (file.FullName, file.LastWriteTimeUtc, file.Length);
        if (_cache.TryGetValue<byte[]>(key, out var cached))
            return cached;

        using var input = FileBrowserService.OpenRead(file.FullName);
        using var codec = SKCodec.Create(input);
        if (codec is null || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
            return null;

        var scale = Math.Min(1f, (float)MaxSide / Math.Max(codec.Info.Width, codec.Info.Height));
        var decodeSize = codec.GetScaledDimensions(scale);
        using var decoded = new SKBitmap(new SKImageInfo(decodeSize.Width, decodeSize.Height));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
            return null;

        var width = Math.Max(1, (int)Math.Round(codec.Info.Width * scale));
        var height = Math.Max(1, (int)Math.Round(codec.Info.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(decoded);
        surface.Canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
        using var thumbnail = surface.Snapshot();
        using var jpeg = thumbnail.Encode(SKEncodedImageFormat.Jpeg, 80);
        var result = jpeg.ToArray();
        _cache.Set(key, result, new MemoryCacheEntryOptions { Size = result.Length, SlidingExpiration = TimeSpan.FromHours(1) });
        return result;
    }

    public async Task<byte[]?> GetVideoThumbnailAsync(FileInfo file, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_ffmpegPath) || !Path.IsPathFullyQualified(_ffmpegPath) || !file.Exists)
            return null;

        var key = (file.FullName, file.LastWriteTimeUtc, file.Length);
        if (_cache.TryGetValue<byte[]>(key, out var cached))
            return cached;

        await _videoSlots.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<byte[]>(key, out cached))
                return cached;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            byte[]? frame = null;
            try
            {
                frame = await ReadVideoFrameAsync(_ffmpegPath, file.FullName, "2", timeout.Token)
                    ?? await ReadVideoFrameAsync(_ffmpegPath, file.FullName, "0", timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A slow or damaged file can keep its video icon.
            }
            catch (Win32Exception ex)
            {
                logger.LogWarning("Could not start the configured video thumbnail decoder ({ErrorCode}).", ex.NativeErrorCode);
            }
            catch (IOException)
            {
                // A failed decoder pipe can keep its video icon.
            }

            _cache.Set(key, frame, new MemoryCacheEntryOptions
            {
                Size = frame?.Length ?? 256,
                AbsoluteExpirationRelativeToNow = frame is null ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(1)
            });
            return frame;
        }
        finally
        {
            _videoSlots.Release();
        }
    }

    private static string? ResolveFfmpegPath(string? configured)
    {
        var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var appLocal = Path.Combine(AppContext.BaseDirectory, "tools", executableName);
        if (File.Exists(appLocal))
            return appLocal;

        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured) && File.Exists(configured))
            return configured;

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(
                     Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
                return candidate;
        }

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            try
            {
                var candidates = Directory.EnumerateDirectories(packages, "Gyan.FFmpeg_*", SearchOption.TopDirectoryOnly)
                    .SelectMany(package => Directory.EnumerateFiles(package, "ffmpeg.exe", SearchOption.AllDirectories))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToArray();
                if (candidates.Length > 0)
                    return candidates[0];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static async Task<byte[]?> ReadVideoFrameAsync(
        string executable, string input, string position, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in new[]
        {
            "-nostdin", "-loglevel", "quiet", "-max_alloc", "134217728",
            "-protocol_whitelist", "file,pipe", "-format_whitelist", "mov,matroska,avi,flv,asf,mpeg,mpegts",
            "-threads", "1", "-max_pixels", "50000000", "-ss", position, "-i", input,
            "-map", "0:V:0", "-frames:v", "1", "-filter_threads", "1",
            "-vf", $"scale={MaxSide}:{MaxSide}:force_original_aspect_ratio=decrease:reset_sar=1",
            "-c:v", "mjpeg", "-threads", "1", "-q:v", "4", "-f", "image2pipe", "pipe:1"
        })
            start.ArgumentList.Add(argument);

        cancellationToken.ThrowIfCancellationRequested();
        using var decoder = Process.Start(start);
        if (decoder is null)
            return null;

        try
        {
            var buffer = new byte[128 * 1024];
            // Stop the decoder when a Windows pipe read is still pending at the deadline.
            var length = await decoder.StandardOutput.BaseStream.ReadAtLeastAsync(
                buffer, buffer.Length, false, cancellationToken).AsTask().WaitAsync(cancellationToken);
            if (length == buffer.Length)
                return null;

            await decoder.WaitForExitAsync(cancellationToken);
            return decoder.ExitCode == 0 && length >= 4 && buffer[0] == 0xff && buffer[1] == 0xd8
                && buffer[length - 2] == 0xff && buffer[length - 1] == 0xd9 ? buffer[..length] : null;
        }
        finally
        {
            if (!decoder.HasExited)
            {
                decoder.Kill();
                await decoder.WaitForExitAsync();
            }
        }
    }

    public static string GetMediaPlaceholder(bool isVideo)
    {
        var shape = isVideo
            ? "<path d='M10 8l6 4-6 4V8z' fill='#a78bfa'/>"
            : "<path d='M3 19l6-8 4 5 3-4 5 7z' fill='#9ca3af'/>";
        return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'><rect width='24' height='24' fill='#1f2937'/>"
            + shape + "</svg>";
    }

    public void Dispose()
    {
        _cache.Dispose();
        _videoSlots.Dispose();
    }
}
