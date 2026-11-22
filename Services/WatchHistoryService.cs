using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace JokerDBDTracker.Services
{
    public class WatchHistoryData
    {
        public Dictionary<string, DateTime> LastWatchedByVideoId { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> LastPlaybackSecondsByVideoId { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> WatchedDays { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> FavoriteVideoIds { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> Achievements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FirstViewRewardedVideoIds { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> WatchedVideoIdsByDay { get; set; } = new(StringComparer.Ordinal);
        public int ClaimedQuestCount { get; set; }
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
        public int EffectPresetSessionsAny { get; set; }
        public int EffectPresetSessionsCustom { get; set; }
        public int EffectPresetSessionsRetro { get; set; }
        public int EffectPresetSessionsChaos { get; set; }
        public int EffectPresetSessionsDream { get; set; }
        public Dictionary<string, int> WatchedSecondsByDay { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> DailyGoalRewardedDays { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> StreakRewardedDays { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> WeeklyWatchDaysRewardedWeeks { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> WeeklyWatchHoursRewardedWeeks { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> BestSessionSecondsByDay { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> EffectSessionsByDay { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> PresetSessionsByDay { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> RewardedQuestKeys { get; set; } = new(StringComparer.Ordinal);
        public string ActiveDailyQuestDate { get; set; } = string.Empty;
        public List<string> ActiveDailyQuestIds { get; set; } = [];
        public string ActiveWeeklyQuestWeekKey { get; set; } = string.Empty;
        public List<string> ActiveWeeklyQuestIds { get; set; } = [];
    }

    public sealed class WatchHistoryService
    {
        private static readonly byte[] DpapiEntropy = [0x4A, 0x6F, 0x6B, 0x65, 0x72, 0x44, 0x42, 0x44, 0x54, 0x72, 0x61, 0x63, 0x6B, 0x65, 0x72, 0x48];
        private readonly string _historyPath;
        private readonly string _legacyHistoryPath;
        private readonly SemaphoreSlim _gate;
        private sealed record Envelope(int Version, bool Protected, string Payload);

        public WatchHistoryService(string? directory = null)
        {
            _historyPath = Path.Combine(directory ?? AppStoragePaths.GetCurrentAppDataDirectory(), "watch-history.json");
            _legacyHistoryPath = Path.Combine(AppStoragePaths.GetLegacyAppDataDirectory(), "watch-history.json");
            _gate = AtomicFile.Gate(_historyPath);
        }

        public async Task<WatchHistoryData> LoadAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                // The current file is authoritative. Legacy storage is only a migration/recovery source.
                var candidates = new[] { _historyPath, _historyPath + ".bak", _legacyHistoryPath };
                Exception? failure = null;
                foreach (var path in candidates)
                {
                    if (!File.Exists(path)) continue;
                    try
                    {
                        var data = await LoadFromPathAsync(path, cancellationToken);
                        if (path != _historyPath)
                        {
                            // Keep a damaged primary for diagnosis and never replace the good backup with it.
                            if (File.Exists(_historyPath)) File.Move(_historyPath, _historyPath + ".corrupt-" + DateTime.UtcNow.Ticks);
                            await WriteCoreAsync(data, cancellationToken);
                            DiagnosticsService.LogInfo("History", "Profile recovered from a backup or migrated from legacy storage.");
                        }
                        return data;
                    }
                    catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException or IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        failure = ex;
                        DiagnosticsService.LogException("History.Load", ex);
                    }
                }
                if (failure is not null) throw new InvalidDataException("Profile and its recovery copies could not be read. Original files were preserved.", failure);
                return new WatchHistoryData();
            }
            finally { _gate.Release(); }
        }

        public async Task SaveAsync(WatchHistoryData data, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try { await WriteCoreAsync(Normalize(data), cancellationToken); }
            finally { _gate.Release(); }
        }

        private Task WriteCoreAsync(WatchHistoryData data, CancellationToken cancellationToken)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data);
            var encrypted = ProtectedData.Protect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return AtomicFile.WriteAsync(_historyPath, JsonSerializer.SerializeToUtf8Bytes(new Envelope(2, true, Convert.ToBase64String(encrypted))), cancellationToken);
        }

        private static async Task<WatchHistoryData> LoadFromPathAsync(string path, CancellationToken cancellationToken)
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Profile root must be an object.");
            if (root.TryGetProperty("Protected", out _))
            {
                var envelope = root.Deserialize<Envelope>() ?? throw new InvalidDataException("Invalid profile envelope.");
                if (!envelope.Protected || envelope.Version is < 1 or > 2 || string.IsNullOrWhiteSpace(envelope.Payload)) throw new InvalidDataException("Unsupported profile envelope.");
                var encrypted = Convert.FromBase64String(envelope.Payload);
                byte[] plain;
                try { plain = ProtectedData.Unprotect(encrypted, DpapiEntropy, DataProtectionScope.CurrentUser); }
                catch (CryptographicException) when (envelope.Version == 1)
                { plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser); }
                return Normalize(JsonSerializer.Deserialize<WatchHistoryData>(plain) ?? throw new InvalidDataException("Profile is empty."));
            }
            // One-time compatibility with profiles written before encryption was introduced.
            if (root.TryGetProperty("LastWatchedByVideoId", out _)) return Normalize(root.Deserialize<WatchHistoryData>()!);
            var old = root.Deserialize<Dictionary<string, DateTime>>() ?? throw new InvalidDataException("Invalid legacy profile.");
            var migrated = new WatchHistoryData();
            foreach (var pair in old) migrated.LastWatchedByVideoId[pair.Key] = pair.Value;
            return Normalize(migrated);
        }

        private static WatchHistoryData Normalize(WatchHistoryData data)
        {
            if (data is null || data.LastWatchedByVideoId is null) throw new InvalidDataException("Profile watch history is missing.");
            // Repair optional collections introduced in later versions, including explicit JSON nulls.
            foreach (var property in typeof(WatchHistoryData).GetProperties())
            {
                if (property.GetValue(data) is null && property.PropertyType != typeof(string) && !property.PropertyType.IsValueType)
                    property.SetValue(data, Activator.CreateInstance(property.PropertyType));
            }
            data.ClaimedQuestCount = Math.Max(data.ClaimedQuestCount, data.RewardedQuestKeys.Count);
            data.TotalXp = Math.Max(0, data.TotalXp);
            data.Prestige = Math.Clamp(data.Prestige, 0, 100);
            data.PrestigeXp = Math.Max(0, data.PrestigeXp);
            // Preserve the best information available when upgrading the old last-view-only schema.
            foreach (var pair in data.LastWatchedByVideoId)
            {
                var day = ProfileClock.Day(pair.Value).ToString("yyyy-MM-dd");
                if (!data.WatchedVideoIdsByDay.TryGetValue(day, out var ids) || ids is null) data.WatchedVideoIdsByDay[day] = ids = new(StringComparer.Ordinal);
                ids.Add(pair.Key);
            }
            return data;
        }
    }
}
