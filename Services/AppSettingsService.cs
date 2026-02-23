using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace JokerDBDTracker.Services
{
    public sealed class AppSettingsData
    {
        public bool AutoStartEnabled { get; set; }
        public string Language { get; set; } = "ru";
        public double UiScale { get; set; } = 1.0;
        public bool AnimationsEnabled { get; set; } = true;
        public bool LoggingEnabled { get; set; } = true;
        public string FullscreenBehavior { get; set; } = "auto";
        public string HideEffectsPanelBind { get; set; } = "H";
        public string AuraFarmSoundBind { get; set; } = "Y";
        public string LaughSoundBind { get; set; } = "U";
        public string PsiSoundBind { get; set; } = "I";
        public string RespectSoundBind { get; set; } = "O";
        public string Effect1Bind { get; set; } = "D1";
        public string Effect2Bind { get; set; } = "D2";
        public string Effect3Bind { get; set; } = "D3";
        public string Effect4Bind { get; set; } = "D4";
        public string Effect5Bind { get; set; } = "D5";
        public string Effect6Bind { get; set; } = "D6";
        public string Effect7Bind { get; set; } = "D7";
        public string Effect8Bind { get; set; } = "D8";
        public string Effect9Bind { get; set; } = "D9";
        public string Effect10Bind { get; set; } = "D0";
        public string Effect11Bind { get; set; } = "Q";
        public string Effect12Bind { get; set; } = "W";
        public string Effect13Bind { get; set; } = "E";
        public string Effect14Bind { get; set; } = "R";
        public string Effect15Bind { get; set; } = "T";
        public List<string> PlayerCustomPresetNames { get; set; } = [];
        public List<string> PlayerCustomPresetPayloads { get; set; } = [];
    }

    public sealed class AppSettingsService
    {
        private static readonly SemaphoreSlim FileAccessLock = new(1, 1);
        private readonly string _settingsPath;
        private readonly string _legacySettingsPath;

        private sealed class EncryptedEnvelope
        {
            public int Version { get; set; } = 1;
            public bool Protected { get; set; } = true;
            public string Payload { get; set; } = string.Empty;
        }

        public AppSettingsService()
        {
            var appFolder = AppStoragePaths.GetCurrentAppDataDirectory();
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, "settings.json");
            var legacyFolder = AppStoragePaths.GetLegacyAppDataDirectory();
            Directory.CreateDirectory(legacyFolder);
            _legacySettingsPath = Path.Combine(legacyFolder, "settings.json");

            if (!File.Exists(_settingsPath) && File.Exists(_legacySettingsPath))
            {
                try
                {
                    File.Copy(_legacySettingsPath, _settingsPath, overwrite: false);
                }
                catch
                {
                    // Best-effort migration; defaults are used on read failure.
                }
            }
        }

        public async Task<AppSettingsData> LoadAsync(CancellationToken cancellationToken = default)
        {
            await FileAccessLock.WaitAsync(cancellationToken);
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    if (File.Exists(_legacySettingsPath))
                    {
                        try
                        {
                            File.Copy(_legacySettingsPath, _settingsPath, overwrite: false);
                        }
                        catch
                        {
                            // Ignore and continue with default settings.
                        }
                    }

                    if (!File.Exists(_settingsPath))
                    {
                        return new AppSettingsData();
                    }
                }

                var payload = await File.ReadAllBytesAsync(_settingsPath, cancellationToken);
                var loaded = DeserializeSettingsPayload(payload);
                return Normalize(loaded ?? new AppSettingsData());
            }
            catch
            {
                return new AppSettingsData();
            }
            finally
            {
                FileAccessLock.Release();
            }
        }

        public async Task SaveAsync(AppSettingsData settings, CancellationToken cancellationToken = default)
        {
            await FileAccessLock.WaitAsync(cancellationToken);
            try
            {
                var normalized = Normalize(settings);
                var tempPath = $"{_settingsPath}.tmp";
                var rawPayload = JsonSerializer.SerializeToUtf8Bytes(normalized);
                var encryptedPayload = ProtectedData.Protect(rawPayload, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var envelope = new EncryptedEnvelope
                {
                    Version = 1,
                    Protected = true,
                    Payload = Convert.ToBase64String(encryptedPayload)
                };

                await using (var stream = new FileStream(
                                 tempPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, envelope, cancellationToken: cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(tempPath, _settingsPath, overwrite: true);
                try
                {
                    File.Copy(_settingsPath, _legacySettingsPath, overwrite: true);
                }
                catch
                {
                    // Keep current settings even if legacy mirror write fails.
                }
            }
            finally
            {
                FileAccessLock.Release();
            }
        }

        private static AppSettingsData? DeserializeSettingsPayload(byte[] payload)
        {
            if (payload.Length == 0)
            {
                return new AppSettingsData();
            }

            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("Protected", out var protectedFlag) &&
                protectedFlag.ValueKind == JsonValueKind.True &&
                document.RootElement.TryGetProperty("Payload", out var encryptedPayloadElement) &&
                encryptedPayloadElement.ValueKind == JsonValueKind.String)
            {
                var encryptedPayloadText = encryptedPayloadElement.GetString();
                if (string.IsNullOrWhiteSpace(encryptedPayloadText))
                {
                    return new AppSettingsData();
                }

                var encryptedPayload = Convert.FromBase64String(encryptedPayloadText);
                var rawPayload = ProtectedData.Unprotect(encryptedPayload, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<AppSettingsData>(rawPayload);
            }

            return document.RootElement.Deserialize<AppSettingsData>();
        }

        private static AppSettingsData Normalize(AppSettingsData settings)
        {
            const int maxCustomPresets = 20;

            settings.Language = string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : "ru";
            settings.UiScale = Math.Clamp(settings.UiScale, 0.85, 1.35);
            settings.FullscreenBehavior = string.Equals(settings.FullscreenBehavior, "windowed", StringComparison.OrdinalIgnoreCase)
                ? "windowed"
                : "auto";
            settings.HideEffectsPanelBind = NormalizeBind(settings.HideEffectsPanelBind, "H");
            settings.AuraFarmSoundBind = NormalizeBind(settings.AuraFarmSoundBind, "Y");
            settings.LaughSoundBind = NormalizeBind(settings.LaughSoundBind, "U");
            settings.PsiSoundBind = NormalizeBind(settings.PsiSoundBind, "I");
            settings.RespectSoundBind = NormalizeBind(settings.RespectSoundBind, "O");
            settings.Effect1Bind = NormalizeBind(settings.Effect1Bind, "D1");
            settings.Effect2Bind = NormalizeBind(settings.Effect2Bind, "D2");
            settings.Effect3Bind = NormalizeBind(settings.Effect3Bind, "D3");
            settings.Effect4Bind = NormalizeBind(settings.Effect4Bind, "D4");
            settings.Effect5Bind = NormalizeBind(settings.Effect5Bind, "D5");
            settings.Effect6Bind = NormalizeBind(settings.Effect6Bind, "D6");
            settings.Effect7Bind = NormalizeBind(settings.Effect7Bind, "D7");
            settings.Effect8Bind = NormalizeBind(settings.Effect8Bind, "D8");
            settings.Effect9Bind = NormalizeBind(settings.Effect9Bind, "D9");
            settings.Effect10Bind = NormalizeBind(settings.Effect10Bind, "D0");
            settings.Effect11Bind = NormalizeBind(settings.Effect11Bind, "Q");
            settings.Effect12Bind = NormalizeBind(settings.Effect12Bind, "W");
            settings.Effect13Bind = NormalizeBind(settings.Effect13Bind, "E");
            settings.Effect14Bind = NormalizeBind(settings.Effect14Bind, "R");
            settings.Effect15Bind = NormalizeBind(settings.Effect15Bind, "T");

            if (string.Equals(settings.AuraFarmSoundBind, "F1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(settings.LaughSoundBind, "F2", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(settings.PsiSoundBind, "F3", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(settings.RespectSoundBind, "F4", StringComparison.OrdinalIgnoreCase))
            {
                settings.AuraFarmSoundBind = "Y";
                settings.LaughSoundBind = "U";
                settings.PsiSoundBind = "I";
                settings.RespectSoundBind = "O";
            }

            settings.PlayerCustomPresetNames ??= [];
            settings.PlayerCustomPresetPayloads ??= [];
            TrimAndPadPresetList(settings.PlayerCustomPresetNames, maxCustomPresets, maxTextLength: 48);
            TrimAndPadPresetList(settings.PlayerCustomPresetPayloads, maxCustomPresets, maxTextLength: 24_000);

            return settings;
        }

        private static void TrimAndPadPresetList(List<string> list, int targetCount, int maxTextLength)
        {
            if (list.Count > targetCount)
            {
                list.RemoveRange(targetCount, list.Count - targetCount);
            }

            for (var i = 0; i < list.Count; i++)
            {
                var text = list[i] ?? string.Empty;
                text = text.Trim();
                if (text.Length > maxTextLength)
                {
                    text = text[..maxTextLength];
                }

                list[i] = text;
            }

            while (list.Count < targetCount)
            {
                list.Add(string.Empty);
            }
        }

        private static string NormalizeBind(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value.Trim().ToUpperInvariant();
        }
    }
}
