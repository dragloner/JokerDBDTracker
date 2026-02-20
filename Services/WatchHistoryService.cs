using System.IO;
using System.Security.Cryptography;
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
        public Dictionary<string, int> WatchedSecondsByDay { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> DailyGoalRewardedDays { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> StreakRewardedDays { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> WeeklyWatchDaysRewardedWeeks { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> WeeklyWatchHoursRewardedWeeks { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> BestSessionSecondsByDay { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> EffectSessionsByDay { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> RewardedQuestKeys { get; set; } = new(StringComparer.Ordinal);
        public string ActiveDailyQuestDate { get; set; } = string.Empty;
        public List<string> ActiveDailyQuestIds { get; set; } = [];
        public string ActiveWeeklyQuestWeekKey { get; set; } = string.Empty;
        public List<string> ActiveWeeklyQuestIds { get; set; } = [];
    }

    public class WatchHistoryService
    {
        private static readonly SemaphoreSlim FileAccessLock = new(1, 1);
        private readonly string _historyPath;
        private readonly string _backupPath;
        private readonly string _legacyHistoryPath;

        private sealed class EncryptedEnvelope
        {
            public int Version { get; set; } = 1;
            public bool Protected { get; set; } = true;
            public string Payload { get; set; } = string.Empty;
        }

        public WatchHistoryService()
        {
            var appFolder = AppStoragePaths.GetCurrentAppDataDirectory();
            Directory.CreateDirectory(appFolder);
            _historyPath = Path.Combine(appFolder, "watch-history.json");
            _backupPath = $"{_historyPath}.bak";

            var legacyFolder = AppStoragePaths.GetLegacyAppDataDirectory();
            Directory.CreateDirectory(legacyFolder);
            _legacyHistoryPath = Path.Combine(legacyFolder, "watch-history.json");
            MigrateLegacyHistoryIfNeeded();
        }

        private void MigrateLegacyHistoryIfNeeded()
        {
            try
            {
                if (File.Exists(_historyPath))
                {
                    return;
                }

                if (!File.Exists(_legacyHistoryPath))
                {
                    return;
                }

                File.Copy(_legacyHistoryPath, _historyPath, overwrite: false);
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException("MigrateLegacyHistoryIfNeeded", ex);
            }
        }

        public async Task<WatchHistoryData> LoadAsync(CancellationToken cancellationToken = default)
        {
            await FileAccessLock.WaitAsync(cancellationToken);
            try
            {
                var current = await TryLoadPrimaryCurrentAsync(cancellationToken);
                var legacy = await TryLoadPathOrDefaultAsync(_legacyHistoryPath, cancellationToken);

                if (current is null && legacy is null)
                {
                    return new WatchHistoryData();
                }

                if (current is null)
                {
                    TryCopyFile(_legacyHistoryPath, _historyPath);
                    return legacy!;
                }

                if (legacy is null)
                {
                    return current;
                }

                if (IsSecondHistoryBetter(current, legacy))
                {
                    TryCopyFile(_legacyHistoryPath, _historyPath);
                    return legacy;
                }

                if (IsSecondHistoryBetter(legacy, current))
                {
                    TryCopyFile(_historyPath, _legacyHistoryPath);
                    return current;
                }

                if (TryGetFileWriteTimeUtc(_legacyHistoryPath, out var legacyWrite) &&
                    TryGetFileWriteTimeUtc(_historyPath, out var currentWrite) &&
                    legacyWrite > currentWrite)
                {
                    TryCopyFile(_legacyHistoryPath, _historyPath);
                    return legacy;
                }

                TryCopyFile(_historyPath, _legacyHistoryPath);
                return current;
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
                    var rawPayload = JsonSerializer.SerializeToUtf8Bytes(data);
                    var encryptedPayload = ProtectedData.Protect(rawPayload, optionalEntropy: null, DataProtectionScope.CurrentUser);
                    var envelope = new EncryptedEnvelope
                    {
                        Version = 1,
                        Protected = true,
                        Payload = Convert.ToBase64String(encryptedPayload)
                    };
                    await JsonSerializer.SerializeAsync(stream, envelope, cancellationToken: cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                if (File.Exists(_historyPath))
                {
                    File.Copy(_historyPath, _backupPath, overwrite: true);
                }

                File.Move(tempPath, _historyPath, overwrite: true);
                TryCopyFile(_historyPath, _legacyHistoryPath);
            }
            finally
            {
                FileAccessLock.Release();
            }
        }

        private async Task<WatchHistoryData?> TryLoadPrimaryCurrentAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_historyPath))
            {
                if (!File.Exists(_backupPath))
                {
                    return null;
                }

                TryCopyFile(_backupPath, _historyPath);
            }

            try
            {
                return await LoadFromPathAsync(_historyPath, cancellationToken);
            }
            catch (JsonException)
            {
                if (!File.Exists(_backupPath))
                {
                    return null;
                }

                var recovered = await LoadFromPathAsync(_backupPath, cancellationToken);
                TryCopyFile(_backupPath, _historyPath);
                return recovered;
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (IOException)
            {
                if (!File.Exists(_backupPath))
                {
                    return null;
                }

                var recovered = await LoadFromPathAsync(_backupPath, cancellationToken);
                TryCopyFile(_backupPath, _historyPath);
                return recovered;
            }
        }

        private static async Task<WatchHistoryData?> TryLoadPathOrDefaultAsync(string path, CancellationToken cancellationToken)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return await LoadFromPathAsync(path, cancellationToken);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSecondHistoryBetter(WatchHistoryData first, WatchHistoryData second)
        {
            if (second.Prestige != first.Prestige)
            {
                return second.Prestige > first.Prestige;
            }

            if (second.TotalXp != first.TotalXp)
            {
                return second.TotalXp > first.TotalXp;
            }

            if (second.PrestigeXp != first.PrestigeXp)
            {
                return second.PrestigeXp > first.PrestigeXp;
            }

            if (second.LastWatchedByVideoId.Count != first.LastWatchedByVideoId.Count)
            {
                return second.LastWatchedByVideoId.Count > first.LastWatchedByVideoId.Count;
            }

            if (second.FirstViewRewardedVideoIds.Count != first.FirstViewRewardedVideoIds.Count)
            {
                return second.FirstViewRewardedVideoIds.Count > first.FirstViewRewardedVideoIds.Count;
            }

            return false;
        }

        private static bool TryGetFileWriteTimeUtc(string path, out DateTime writeTimeUtc)
        {
            writeTimeUtc = DateTime.MinValue;
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                writeTimeUtc = File.GetLastWriteTimeUtc(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryCopyFile(string sourcePath, string targetPath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return;
                }

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(sourcePath, targetPath, overwrite: true);
            }
            catch
            {
                // Best-effort sync between legacy/current storage locations.
            }
        }

        private static async Task<WatchHistoryData> LoadFromPathAsync(string path, CancellationToken cancellationToken)
        {
            var payloadBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (payloadBytes.Length == 0)
            {
                return new WatchHistoryData();
            }

            using var document = JsonDocument.Parse(payloadBytes);
            if (TryParseEncryptedEnvelope(document.RootElement, out var unprotectedJson))
            {
                using var unprotectedDoc = JsonDocument.Parse(unprotectedJson);
                return ParseHistoryRoot(unprotectedDoc.RootElement);
            }

            return ParseHistoryRoot(document.RootElement);
        }

        private static bool TryParseEncryptedEnvelope(JsonElement root, out byte[] unprotectedJson)
        {
            unprotectedJson = [];
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("Protected", out var protectedFlag) ||
                protectedFlag.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("Payload", out var payloadElement) ||
                payloadElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var payload = payloadElement.GetString();
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            var encryptedBytes = Convert.FromBase64String(payload);
            unprotectedJson = ProtectedData.Unprotect(encryptedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return true;
        }

        private static WatchHistoryData ParseHistoryRoot(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("LastWatchedByVideoId", out _))
            {
                var typed = root.Deserialize<WatchHistoryData>();
                return typed ?? new WatchHistoryData();
            }

            var oldFormat = root.Deserialize<Dictionary<string, DateTime>>() ??
                            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            var migrated = new WatchHistoryData();
            foreach (var item in oldFormat)
            {
                migrated.LastWatchedByVideoId[item.Key] = item.Value;
                migrated.WatchedDays.Add(item.Value.ToLocalTime().Date.ToString("yyyy-MM-dd"));
            }

            return migrated;
        }
    }
}
