using System.Windows.Input;
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
        public bool IntroSoundEnabled { get; set; } = true;
        public bool LoggingEnabled { get; set; } = true;
        public string FullscreenBehavior { get; set; } = "auto";
        public string HideEffectsPanelBind { get; set; } = "H";
        public string ToggleChatBind { get; set; } = "C";
        public string ToggleCommentsBind { get; set; } = "V";
        public string AuraFarmSoundBind { get; set; } = "Y";
        public string LaughSoundBind { get; set; } = "U";
        public string PsiSoundBind { get; set; } = "I";
        public string RespectSoundBind { get; set; } = "O";
        public string SadSoundBind { get; set; } = "P";
        public string DaUzhTochnoSoundBind { get; set; } = "J";
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
        public bool AutoCheckUpdates { get; set; } = true;
        public bool SoundSpamMode { get; set; }
        public bool ApplyEqToSoundEffects { get; set; } = true;
        public List<string> PlayerCustomPresetNames { get; set; } = [];
        public List<string> PlayerCustomPresetPayloads { get; set; } = [];
    }

    public sealed class AppSettingsService
    {
        private static readonly SemaphoreSlim FileAccessLock = new(1, 1);
        // DPAPI protects at the Windows account boundary; entropy identifies this application format.
        private static readonly byte[] DpapiEntropy = [0x4A, 0x6F, 0x6B, 0x65, 0x72, 0x44, 0x42, 0x44, 0x54, 0x72, 0x61, 0x63, 0x6B, 0x65, 0x72, 0x53];
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
            _legacySettingsPath = Path.Combine(legacyFolder, "settings.json");

        }

        public async Task<AppSettingsData> LoadAsync(CancellationToken cancellationToken = default)
        {
            await FileAccessLock.WaitAsync(cancellationToken);
            try
            {
                Exception? failure = null;
                foreach (var path in new[] { _settingsPath, _settingsPath + ".bak", _legacySettingsPath })
                {
                    if (!File.Exists(path)) continue;
                    byte[] payload;
                    AppSettingsData settings;
                    try
                    {
                        payload = await File.ReadAllBytesAsync(path, cancellationToken);
                        settings = Normalize(DeserializeSettingsPayload(payload) ?? throw new InvalidDataException("Missing settings."));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failure = ex;
                        DiagnosticsService.LogException("Settings.Load", ex);
                        continue;
                    }
                    if (path != _settingsPath)
                        await AtomicFile.RestoreAsync(_settingsPath, payload, cancellationToken);
                    return settings;
                }
                if (failure is not null) throw new InvalidDataException("Settings recovery failed.", failure);
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
                var rawPayload = JsonSerializer.SerializeToUtf8Bytes(normalized);
                var encrypted = ProtectedData.Protect(rawPayload, DpapiEntropy, DataProtectionScope.CurrentUser);
                await AtomicFile.WriteAsync(_settingsPath, JsonSerializer.SerializeToUtf8Bytes(new EncryptedEnvelope { Version = 2, Protected = true, Payload = Convert.ToBase64String(encrypted) }), cancellationToken);
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
                throw new InvalidDataException("Settings file is empty.");
            }

            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Settings must be an object.");
            if (document.RootElement.TryGetProperty("Protected", out var flag) && flag.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("Invalid settings envelope.");
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("Protected", out var protectedFlag) &&
                protectedFlag.ValueKind == JsonValueKind.True &&
                document.RootElement.TryGetProperty("Payload", out var encryptedPayloadElement) &&
                encryptedPayloadElement.ValueKind == JsonValueKind.String)
            {
                if (document.RootElement.TryGetProperty("Version", out var version) && (!version.TryGetInt32(out var number) || number is < 1 or > 2))
                    throw new InvalidDataException("Unsupported settings version.");
                var encryptedPayloadText = encryptedPayloadElement.GetString();
                if (string.IsNullOrWhiteSpace(encryptedPayloadText))
                {
                    throw new InvalidDataException("Settings payload is empty.");
                }

                var encryptedPayload = Convert.FromBase64String(encryptedPayloadText);
                // Try with entropy first (new format), fall back to without (legacy files).
                byte[] rawPayload;
                try
                {
                    rawPayload = ProtectedData.Unprotect(encryptedPayload, DpapiEntropy, DataProtectionScope.CurrentUser);
                }
                catch (CryptographicException)
                {
                    rawPayload = ProtectedData.Unprotect(encryptedPayload, optionalEntropy: null, DataProtectionScope.CurrentUser);
                }
                return JsonSerializer.Deserialize<AppSettingsData>(rawPayload);
            }

            if (document.RootElement.TryGetProperty("Protected", out _)) throw new InvalidDataException("Missing settings payload.");
            return document.RootElement.Deserialize<AppSettingsData>();
        }

        private static AppSettingsData Normalize(AppSettingsData settings)
        {
            const int maxCustomPresets = 20;

            settings.Language = string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : "ru";
            settings.UiScale = double.IsFinite(settings.UiScale) ? Math.Clamp(settings.UiScale, 0.85, 1.35) : 1.0;
            settings.FullscreenBehavior = string.Equals(settings.FullscreenBehavior, "windowed", StringComparison.OrdinalIgnoreCase)
                ? "windowed"
                : "auto";
            settings.HideEffectsPanelBind = NormalizeBind(settings.HideEffectsPanelBind, "H");
            settings.ToggleChatBind = NormalizeBind(settings.ToggleChatBind, "C");
            settings.ToggleCommentsBind = NormalizeBind(settings.ToggleCommentsBind, "V");
            settings.AuraFarmSoundBind = NormalizeBind(settings.AuraFarmSoundBind, "Y");
            settings.LaughSoundBind = NormalizeBind(settings.LaughSoundBind, "U");
            settings.PsiSoundBind = NormalizeBind(settings.PsiSoundBind, "I");
            settings.RespectSoundBind = NormalizeBind(settings.RespectSoundBind, "O");
            settings.SadSoundBind = NormalizeBind(settings.SadSoundBind, "P");
            settings.DaUzhTochnoSoundBind = NormalizeBind(settings.DaUzhTochnoSoundBind, "J");
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


            var defaults = new AppSettingsData();
            var used = new HashSet<Key>();
            foreach (var property in typeof(AppSettingsData).GetProperties().Where(property => property.Name.EndsWith("Bind", StringComparison.Ordinal)))
            {
                Enum.TryParse<Key>(property.GetValue(settings)?.ToString(), true, out var key);
                if (!KeyBindingRules.IsAllowed(key) || !used.Add(KeyBindingRules.Canonical(key)))
                {
                    Enum.TryParse<Key>(property.GetValue(defaults)?.ToString(), true, out var fallback);
                    if (!KeyBindingRules.IsAllowed(fallback) || used.Contains(KeyBindingRules.Canonical(fallback)))
                        fallback = Enumerable.Range((int)Key.F1, (int)Key.F24 - (int)Key.F1 + 1).Select(value => (Key)value).First(candidate => KeyBindingRules.IsAllowed(candidate) && !used.Contains(candidate));
                    used.Add(KeyBindingRules.Canonical(fallback));
                    property.SetValue(settings, fallback.ToString().ToUpperInvariant());
                }
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

            return Enum.TryParse<Key>(value.Trim(), true, out var key) && KeyBindingRules.IsAllowed(key) ? key.ToString().ToUpperInvariant() : fallback;
        }
    }
}
