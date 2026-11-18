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

        using var input = file.OpenRead();
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

    public void Dispose() => _cache.Dispose();
}
