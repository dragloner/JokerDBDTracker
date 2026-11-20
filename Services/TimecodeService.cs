using System.IO;
using System.Text.Json;
using JokerDBDTracker.Models;

namespace JokerDBDTracker.Services;

public sealed class TimecodeService
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate;
    public TimecodeService(string? directory = null)
    {
        _path = Path.Combine(directory ?? AppStoragePaths.GetCurrentAppDataDirectory(), "timecodes.json");
        _gate = AtomicFile.Gate(_path);
    }
    public async Task<List<Timecode>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }
    private async Task<List<Timecode>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        foreach (var path in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var list = JsonSerializer.Deserialize<List<Timecode>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? throw new InvalidDataException("Timecodes are empty.");
                if (list.Any(t => t is null || string.IsNullOrWhiteSpace(t.VideoId) || t.Seconds < 0)) throw new InvalidDataException("Invalid timecode entry.");
                return list;
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
            { failure = ex; DiagnosticsService.LogException("Timecodes.Load", ex); }
        }
        if (failure is not null) throw new InvalidDataException("Timecodes could not be recovered. Original files were preserved.", failure);
        return [];
    }
    public async Task UpdateAsync(Action<List<Timecode>> update, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = await LoadCoreAsync(cancellationToken);
            update(list);
            await AtomicFile.WriteAsync(_path, JsonSerializer.SerializeToUtf8Bytes(list), cancellationToken);
        }
        finally { _gate.Release(); }
    }
    public async Task SaveAsync(List<Timecode> timecodes, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await AtomicFile.WriteAsync(_path, JsonSerializer.SerializeToUtf8Bytes(timecodes), cancellationToken); }
        finally { _gate.Release(); }
    }
}
