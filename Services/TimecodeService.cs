using System.IO;
using System.Text.Json;
using JokerDBDTracker.Models;

namespace JokerDBDTracker.Services
{
    public sealed class TimecodeService
    {
        private static readonly SemaphoreSlim FileAccessLock = new(1, 1);
        private readonly string _timecodesPath;

        public TimecodeService()
        {
            var appFolder = AppStoragePaths.GetCurrentAppDataDirectory();
            Directory.CreateDirectory(appFolder);
            _timecodesPath = Path.Combine(appFolder, "timecodes.json");
        }

        public async Task<List<Timecode>> LoadAsync(CancellationToken cancellationToken = default)
        {
            await FileAccessLock.WaitAsync(cancellationToken);
            try
            {
                if (!File.Exists(_timecodesPath))
                {
                    return [];
                }

                var json = await File.ReadAllTextAsync(_timecodesPath, cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return [];
                }

                return JsonSerializer.Deserialize<List<Timecode>>(json) ?? [];
            }
            catch
            {
                return [];
            }
            finally
            {
                FileAccessLock.Release();
            }
        }

        public async Task SaveAsync(List<Timecode> timecodes, CancellationToken cancellationToken = default)
        {
            await FileAccessLock.WaitAsync(cancellationToken);
            try
            {
                var tempPath = $"{_timecodesPath}.tmp";
                var json = JsonSerializer.Serialize(timecodes, new JsonSerializerOptions { WriteIndented = false });
                await File.WriteAllTextAsync(tempPath, json, cancellationToken);
                File.Move(tempPath, _timecodesPath, overwrite: true);
            }
            catch
            {
                // Best-effort persistence.
            }
            finally
            {
                FileAccessLock.Release();
            }
        }
    }
}
