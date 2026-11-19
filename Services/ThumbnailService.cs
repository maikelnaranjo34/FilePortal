using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace FilePortal.Services;

public sealed class ThumbnailService : IDisposable
{
    private const int MaxSide = 240;
    private const long MaxPixels = 50_000_000;
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 32 * 1024 * 1024 });

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

    public static string GetMediaPlaceholder(bool isVideo)
    {
        var shape = isVideo
            ? "<path d='M10 8l6 4-6 4V8z' fill='#a78bfa'/>"
            : "<path d='M3 19l6-8 4 5 3-4 5 7z' fill='#9ca3af'/>";
        return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'><rect width='24' height='24' fill='#1f2937'/>"
            + shape + "</svg>";
    }

    public void Dispose() => _cache.Dispose();
}
