using System.IO.Compression;
using FilePortal.Models;

namespace FilePortal.Services;

public sealed class FileArchiveService(FileBrowserService browser)
{
    public async Task<FileStream> CreateAsync(IEnumerable<string> sources, IReadOnlyList<RootItem> roots,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(browser.WorkingDirectory);
        var temporaryPath = Path.Combine(browser.WorkingDirectory, Guid.NewGuid().ToString("N") + ".zip");
        var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var source in sources)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(source);
                    if (string.IsNullOrEmpty(name))
                        name = "Drive";

                    var uniqueName = name;
                    for (var suffix = 1; !entries.Add(uniqueName); suffix++)
                        uniqueName = Path.GetFileNameWithoutExtension(name) + "_" + suffix + Path.GetExtension(name);

                    await AddToZip(archive, source, uniqueName, roots, cancellationToken);
                }
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private async Task AddToZip(ZipArchive archive, string source, string name, IReadOnlyList<RootItem> roots, CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Path, string EntryName)>();
        pending.Push((source, name));
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!browser.TryResolve(item.Path, roots, out var path))
                continue;

            if (Directory.Exists(path))
            {
                archive.CreateEntry(item.EntryName + "/");
                foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos().Where(browser.CanList))
                    pending.Push((entry.FullName, item.EntryName + "/" + entry.Name));
                continue;
            }

            var entryStream = archive.CreateEntry(item.EntryName, CompressionLevel.Fastest).Open();
            await using (entryStream)
            await using (var input = System.IO.File.OpenRead(path))
                await input.CopyToAsync(entryStream, cancellationToken);
        }
    }

}
