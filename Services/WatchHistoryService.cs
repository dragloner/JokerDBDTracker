using System.IO;
using System.Text.Json;

namespace JokerDBDTracker.Services
{
    public class WatchHistoryData
    {
        public Dictionary<string, DateTime> LastWatchedByVideoId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> LastPlaybackSecondsByVideoId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> WatchedDays { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> FavoriteVideoIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Achievements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FirstViewRewardedVideoIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int TotalXp { get; set; }
        public int Prestige { get; set; }
        public int PrestigeXp { get; set; }
        public int EffectSessionsAny { get; set; }
        public int EffectSessionsFivePlus { get; set; }
        public int EffectSessionsTenPlus { get; set; }
        public int EffectSessionsStrongBlur { get; set; }
        public int EffectSessionsStrongRedGlow { get; set; }
        public int EffectSessionsStrongVioletGlow { get; set; }
        public int EffectSessionsStrongShake { get; set; }
    }

    public class WatchHistoryService
    {
        private static readonly SemaphoreSlim FileAccessLock = new(1, 1);
        private readonly string _historyPath;

        public WatchHistoryService()
        {
            var appFolder = AppStoragePaths.GetCurrentAppDataDirectory();
            Directory.CreateDirectory(appFolder);
            _historyPath = Path.Combine(appFolder, "watch-history.json");
            MigrateLegacyHistoryIfNeeded();
        }

        private void MigrateLegacyHistoryIfNeeded()
        {
            if (File.Exists(_historyPath))
            {
                return;
            }

            var legacyFolder = AppStoragePaths.GetLegacyAppDataDirectory();
            var legacyHistoryPath = Path.Combine(legacyFolder, "watch-history.json");
            if (!File.Exists(legacyHistoryPath))
            {
                return;
            }

            File.Copy(legacyHistoryPath, _historyPath, overwrite: false);
        }

        public async Task<WatchHistoryData> LoadAsync(CancellationToken cancellationToken = default)
        {
            await FileAccessLock.WaitAsync(cancellationToken);
            try
            {
                if (!File.Exists(_historyPath))
                {
                    return new WatchHistoryData();
                }

                await using var stream = new FileStream(
                    _historyPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 8192,
                    useAsync: true);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("LastWatchedByVideoId", out _))
                {
                    var typed = document.RootElement.Deserialize<WatchHistoryData>();
                    return typed ?? new WatchHistoryData();
                }

                // Backward compatibility: old format was Dictionary<string, DateTime>.
                var oldFormat = document.RootElement.Deserialize<Dictionary<string, DateTime>>() ??
                                new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

                var migrated = new WatchHistoryData();
                foreach (var item in oldFormat)
                {
                    migrated.LastWatchedByVideoId[item.Key] = item.Value;
                    migrated.WatchedDays.Add(item.Value.ToLocalTime().Date.ToString("yyyy-MM-dd"));
                }

                return migrated;
            }
            finally
            {
                FileAccessLock.Release();
            }
        }

        public async Task SaveAsync(WatchHistoryData data, CancellationToken cancellationToken = default)
        {
            await FileAccessLock.WaitAsync(cancellationToken);
            try
            {
                var tempPath = $"{_historyPath}.tmp";
                await using (var stream = new FileStream(
                                 tempPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 8192,
                                 useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, data, cancellationToken: cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(tempPath, _historyPath, overwrite: true);
            }
            finally
            {
                FileAccessLock.Release();
            }
        }
    }
}

