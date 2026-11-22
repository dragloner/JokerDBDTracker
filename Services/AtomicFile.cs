using System.Collections.Concurrent;
using System.IO;

namespace JokerDBDTracker.Services;

internal static class AtomicFile
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    public static SemaphoreSlim Gate(string path) => Gates.GetOrAdd(Path.GetFullPath(path), _ => new(1, 1));

    public static async Task RestoreAsync(string path, byte[] payload, CancellationToken cancellationToken)
    {
        if (File.Exists(path)) File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
        await WriteAsync(path, payload, cancellationToken);
    }

    public static async Task WriteAsync(string path, byte[] data, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            {
                await stream.WriteAsync(data, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporaryPath, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
