using System.IO;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        private readonly List<MediaPlayer> _activeSoundPlayers = [];
        private readonly Dictionary<SoundEffectKind, MediaPlayer> _activeSoundPlayersByKind = [];

        private enum SoundEffectKind
        {
            AuraFarm,
            Laugh,
            PsiRadiation,
            Respect
        }

        private void PlayerWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            MarkUserInteraction();
        }

        private void PlayerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            MarkUserInteraction();
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (ShouldBypassPlayerKeyHandlingBecauseTyping())
            {
                return;
            }

            if (TryMirrorPlaybackKeyFromEffectsPanel(e, key))
            {
                e.Handled = true;
                return;
            }
            if (ShouldSuppressDuplicateAppKeybind(key))
            {
                e.Handled = true;
                return;
            }

            if (TryHandleAppKeybind(key))
            {
                e.Handled = true;
            }
        }

        private void PlayerWindow_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UpdateGlobalHotkeysForTypingFocusState();
        }

        private void PlayerWindow_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(UpdateGlobalHotkeysForTypingFocusState, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void UpdateGlobalHotkeysForTypingFocusState()
        {
            if (!IsActive)
            {
                return;
            }

            if (ShouldBypassPlayerKeyHandlingBecauseTyping())
            {
                UnregisterGlobalHotkeys();
                return;
            }

            RegisterGlobalHotkeys();
        }

        private bool TryMirrorPlaybackKeyFromEffectsPanel(KeyEventArgs e, Key key)
        {
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return false;
            }

            if (e.IsRepeat && key == Key.Space)
            {
                return false;
            }

            if (key is not (Key.Space or Key.Left or Key.Right or Key.Up or Key.Down))
            {
                return false;
            }

            if (!CanProcessPlayerCommands() ||
                EffectsPanel is null ||
                !EffectsPanel.IsVisible ||
                !EffectsPanel.IsKeyboardFocusWithin ||
                Player is null ||
                Player.IsKeyboardFocusWithin)
            {
                return false;
            }

            _ = MirrorPlaybackKeyToWebViewAsync(key);
            return key == Key.Space && IsEffectCheckBoxFocused();
        }

        private bool IsEffectCheckBoxFocused()
        {
            var current = Keyboard.FocusedElement as DependencyObject;
            while (current is not null)
            {
                if (current is CheckBox)
                {
                    return true;
                }

                current = current switch
                {
                    Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                    FrameworkContentElement frameworkContent => frameworkContent.Parent,
                    _ => null
                };
            }

            return false;
        }

        private bool TryHandleAppKeybind(Key key)
        {
            if (!CanProcessPlayerCommands())
            {
                return false;
            }

            if (ShouldBypassPlayerKeyHandlingBecauseTyping())
            {
                return false;
            }

            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return false;
            }

            if (TryHandleEffectsPanelToggleKey(key))
            {
                return true;
            }

            if (TryHandleSoundEffectKey(key))
            {
                return true;
            }

            if (!TryResolveEffectByKey(key, out var checkBox))
            {
                return false;
            }

            var nextState = checkBox.IsChecked != true;
            checkBox.IsChecked = nextState;
            return true;
        }

        private bool ShouldSuppressDuplicateAppKeybind(Key key)
        {
            var now = DateTime.UtcNow;
            var isDuplicate = _lastProcessedAppKeybind == key &&
                              (now - _lastProcessedAppKeybindUtc).TotalMilliseconds <= 130;
            _lastProcessedAppKeybind = key;
            _lastProcessedAppKeybindUtc = now;
            return isDuplicate;
        }

        private void MarkUserInteraction()
        {
            _lastUserInteractionUtc = DateTime.UtcNow;
        }

        private bool TryHandleEffectsPanelToggleKey(Key key)
        {
            var configured = ReadConfiguredKey(_appSettings.HideEffectsPanelBind, Key.H);
            if (key != configured)
            {
                return false;
            }

            ToggleEffectsPanelState();
            return true;
        }

        private bool TryHandleSoundEffectKey(Key key)
        {
            if (key == ReadConfiguredKey(_appSettings.AuraFarmSoundBind, Key.Y))
            {
                PlaySoundEffect(SoundEffectKind.AuraFarm);
                return true;
            }

            if (key == ReadConfiguredKey(_appSettings.LaughSoundBind, Key.U))
            {
                PlaySoundEffect(SoundEffectKind.Laugh);
                return true;
            }

            if (key == ReadConfiguredKey(_appSettings.PsiSoundBind, Key.I))
            {
                PlaySoundEffect(SoundEffectKind.PsiRadiation);
                return true;
            }

            if (key == ReadConfiguredKey(_appSettings.RespectSoundBind, Key.O))
            {
                PlaySoundEffect(SoundEffectKind.Respect);
                return true;
            }

            return false;
        }

        private bool ShouldBypassPlayerKeyHandlingBecauseTyping()
        {
            if (Keyboard.FocusedElement is not DependencyObject focusedElement)
            {
                return false;
            }

            DependencyObject? current = focusedElement;
            while (current is not null)
            {
                if (current is TextBox or PasswordBox or RichTextBox)
                {
                    return true;
                }

                if (current is ComboBox)
                {
                    return true;
                }

                if (current is ComboBoxItem)
                {
                    return true;
                }

                current = current switch
                {
                    Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                    FrameworkContentElement frameworkContent => frameworkContent.Parent,
                    _ => null
                };
            }

            return false;
        }

        private static Key ReadConfiguredKey(string configuredValue, Key fallback)
        {
            if (!string.IsNullOrWhiteSpace(configuredValue) &&
                Enum.TryParse<Key>(configuredValue.Trim(), ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }
        private void PlaySoundEffect(SoundEffectKind kind)
        {
            try
            {
                if (StopSoundEffect(kind))
                {
                    return;
                }

                var audioResourceUri = ResolveSoundEffectResourceUri(kind);
                if (audioResourceUri is null)
                {
                    ReportSoundPlaybackFailure("Sound asset not found.");
                    return;
                }

                PlayAudioFile(kind, audioResourceUri);
            }
            catch (Exception ex)
            {
                ReportSoundPlaybackFailure(ex.Message);
            }
        }

        private Uri? ResolveSoundEffectResourceUri(SoundEffectKind kind)
        {
            var fileName = kind switch
            {
                SoundEffectKind.AuraFarm => "doue-aura.mp3",
                SoundEffectKind.Laugh => "sitcom-laughing-1.mp3",
                SoundEffectKind.PsiRadiation => "zvuk-psi-izlucheniia.mp3",
                SoundEffectKind.Respect => "italian-mafia-music.mp3",
                _ => null
            };

            if (fileName is null)
            {
                return null;
            }

            var externalSoundsPath = Path.Combine(AppContext.BaseDirectory, "sounds", fileName);
            if (File.Exists(externalSoundsPath))
            {
                return new Uri(externalSoundsPath, UriKind.Absolute);
            }

            var externalAssetsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", fileName);
            if (File.Exists(externalAssetsPath))
            {
                return new Uri(externalAssetsPath, UriKind.Absolute);
            }

            var cachedBundledPath = MaterializeBundledSoundToCache(fileName);
            return string.IsNullOrWhiteSpace(cachedBundledPath)
                ? null
                : new Uri(cachedBundledPath, UriKind.Absolute);
        }

        private static string? MaterializeBundledSoundToCache(string fileName)
        {
            try
            {
                var cacheDirectory = Path.Combine(AppStoragePaths.GetCurrentLocalAppDataDirectory(), "SoundCache");
                Directory.CreateDirectory(cacheDirectory);
                var cachedFilePath = Path.Combine(cacheDirectory, fileName);
                if (File.Exists(cachedFilePath) && new FileInfo(cachedFilePath).Length > 0)
                {
                    return cachedFilePath;
                }

                var packUri = new Uri($"pack://application:,,,/Assets/Sounds/{fileName}", UriKind.Absolute);
                var resource = Application.GetResourceStream(packUri);
                if (resource?.Stream is null)
                {
                    return null;
                }

                using var sourceStream = resource.Stream;
                using var targetStream = new FileStream(cachedFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                sourceStream.CopyTo(targetStream);
                return cachedFilePath;
            }
            catch
            {
                return null;
            }
        }

        private bool StopSoundEffect(SoundEffectKind kind)
        {
            if (!_activeSoundPlayersByKind.TryGetValue(kind, out var player))
            {
                return false;
            }

            _activeSoundPlayersByKind.Remove(kind);
            _activeSoundPlayers.Remove(player);

            try
            {
                player.Stop();
                player.Close();
            }
            catch
            {
                // Ignore sound shutdown errors.
            }

            return true;
        }

        private void PlayAudioFile(SoundEffectKind kind, Uri resourceUri)
        {
            var player = new MediaPlayer();
            player.Open(resourceUri);
            player.Volume = 1.0;
            player.MediaEnded += (_, _) =>
            {
                player.Stop();
                player.Close();
                _activeSoundPlayers.Remove(player);
                if (_activeSoundPlayersByKind.TryGetValue(kind, out var activePlayer) &&
                    ReferenceEquals(activePlayer, player))
                {
                    _activeSoundPlayersByKind.Remove(kind);
                }
            };
            player.MediaFailed += (_, _) =>
            {
                player.Close();
                _activeSoundPlayers.Remove(player);
                if (_activeSoundPlayersByKind.TryGetValue(kind, out var activePlayer) &&
                    ReferenceEquals(activePlayer, player))
                {
                    _activeSoundPlayersByKind.Remove(kind);
                }
                ReportSoundPlaybackFailure("MediaPlayer failed to decode sound.");
            };

            _activeSoundPlayers.Add(player);
            _activeSoundPlayersByKind[kind] = player;
            player.Play();
        }

        private void ReportSoundPlaybackFailure(string details)
        {
            DiagnosticsService.LogInfo("SoundPlayback", details);
            if (_hasShownSoundPlaybackWarning)
            {
                return;
            }

            _hasShownSoundPlaybackWarning = true;
            Dispatcher.BeginInvoke(() =>
            {
                MessageBox.Show(
                    PT(
                        "Не удалось воспроизвести звуковые эффекты. Проверьте целостность файлов релиза.",
                        "Failed to play sound effects. Check release files integrity."),
                    PT("Звук", "Sound"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }

        private void StopAllSoundEffects()
        {
            foreach (var player in _activeSoundPlayers.ToArray())
            {
                try
                {
                    player.Stop();
                    player.Close();
                }
                catch
                {
                    // Ignore sound shutdown errors.
                }
            }

            _activeSoundPlayers.Clear();
            _activeSoundPlayersByKind.Clear();
        }

        private void UpdateDynamicBindHints()
        {
            if (BindsHintText is not null)
            {
                var hide = FormatBindLabel(_appSettings.HideEffectsPanelBind);
                var aura = FormatBindLabel(_appSettings.AuraFarmSoundBind);
                var laugh = FormatBindLabel(_appSettings.LaughSoundBind);
                var psi = FormatBindLabel(_appSettings.PsiSoundBind);
                var respect = FormatBindLabel(_appSettings.RespectSoundBind);
                var fxRow1 = string.Join("/", new[]
                {
                    FormatBindLabel(_appSettings.Effect1Bind),
                    FormatBindLabel(_appSettings.Effect2Bind),
                    FormatBindLabel(_appSettings.Effect3Bind),
                    FormatBindLabel(_appSettings.Effect4Bind),
                    FormatBindLabel(_appSettings.Effect5Bind),
                    FormatBindLabel(_appSettings.Effect6Bind),
                    FormatBindLabel(_appSettings.Effect7Bind),
                    FormatBindLabel(_appSettings.Effect8Bind),
                    FormatBindLabel(_appSettings.Effect9Bind),
                    FormatBindLabel(_appSettings.Effect10Bind)
                });
                var fxRow2 = string.Join("/", new[]
                {
                    FormatBindLabel(_appSettings.Effect11Bind),
                    FormatBindLabel(_appSettings.Effect12Bind),
                    FormatBindLabel(_appSettings.Effect13Bind),
                    FormatBindLabel(_appSettings.Effect14Bind),
                    FormatBindLabel(_appSettings.Effect15Bind)
                });
                BindsHintText.Text = PT(
                    $"FX 1-10: {fxRow1}\nFX 11-15: {fxRow2} | H:{hide} | Y:{aura} U:{laugh} I:{psi} O:{respect}",
                    $"FX 1-10: {fxRow1}\nFX 11-15: {fxRow2} | H:{hide} | Y:{aura} U:{laugh} I:{psi} O:{respect}");
            }

            if (AuraFarmSoundButton is not null)
            {
                AuraFarmSoundButton.Content = PT("1. Звук Aura Farm", "1. Aura Farm Sound") +
                                              $" [{FormatBindLabel(_appSettings.AuraFarmSoundBind)}]";
            }

            if (LaughSoundButton is not null)
            {
                LaughSoundButton.Content = PT("2. Звук Смех", "2. Laugh Sound") +
                                           $" [{FormatBindLabel(_appSettings.LaughSoundBind)}]";
            }

            if (PsiSoundButton is not null)
            {
                PsiSoundButton.Content = PT("3. Звук Пси", "3. Psi Radiation") +
                                         $" [{FormatBindLabel(_appSettings.PsiSoundBind)}]";
            }

            if (RespectSoundButton is not null)
            {
                RespectSoundButton.Content = PT("4. +Respect Don Mafia", "4. +Respect Don Mafia") +
                                             $" [{FormatBindLabel(_appSettings.RespectSoundBind)}]";
            }
        }

        private static string FormatBindLabel(string bind)
        {
            if (string.IsNullOrWhiteSpace(bind))
            {
                return "-";
            }

            var value = bind.Trim().ToUpperInvariant();
            return value switch
            {
                "TAB" => "Tab",
                "SPACE" => "Space",
                _ when value.StartsWith("D", StringComparison.Ordinal) &&
                       value.Length == 2 &&
                       char.IsDigit(value[1]) => value[1].ToString(),
                _ => value
            };
        }

        private bool TryResolveEffectByKey(Key key, out CheckBox checkBox)
        {
            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect1Bind, Key.D1))
            {
                checkBox = Fx1;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect2Bind, Key.D2))
            {
                checkBox = Fx2;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect3Bind, Key.D3))
            {
                checkBox = Fx3;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect4Bind, Key.D4))
            {
                checkBox = Fx4;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect5Bind, Key.D5))
            {
                checkBox = Fx5;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect6Bind, Key.D6))
            {
                checkBox = Fx6;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect7Bind, Key.D7))
            {
                checkBox = Fx7;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect8Bind, Key.D8))
            {
                checkBox = Fx8;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect9Bind, Key.D9))
            {
                checkBox = Fx9;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect10Bind, Key.D0))
            {
                checkBox = Fx10;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect11Bind, Key.Q))
            {
                checkBox = Fx11;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect12Bind, Key.W))
            {
                checkBox = Fx12;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect13Bind, Key.E))
            {
                checkBox = Fx13;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect14Bind, Key.R))
            {
                checkBox = Fx14;
                return true;
            }

            if (IsConfiguredEffectKeyMatch(key, _appSettings.Effect15Bind, Key.T))
            {
                checkBox = Fx15;
                return true;
            }

            checkBox = null!;
            return false;
        }

        private string BuildEffectBindHintText()
        {
            var tokens = new[]
            {
                $"1={FormatBindLabel(_appSettings.Effect1Bind)}",
                $"2={FormatBindLabel(_appSettings.Effect2Bind)}",
                $"3={FormatBindLabel(_appSettings.Effect3Bind)}",
                $"4={FormatBindLabel(_appSettings.Effect4Bind)}",
                $"5={FormatBindLabel(_appSettings.Effect5Bind)}",
                $"6={FormatBindLabel(_appSettings.Effect6Bind)}",
                $"7={FormatBindLabel(_appSettings.Effect7Bind)}",
                $"8={FormatBindLabel(_appSettings.Effect8Bind)}",
                $"9={FormatBindLabel(_appSettings.Effect9Bind)}",
                $"10={FormatBindLabel(_appSettings.Effect10Bind)}",
                $"11={FormatBindLabel(_appSettings.Effect11Bind)}",
                $"12={FormatBindLabel(_appSettings.Effect12Bind)}",
                $"13={FormatBindLabel(_appSettings.Effect13Bind)}",
                $"14={FormatBindLabel(_appSettings.Effect14Bind)}",
                $"15={FormatBindLabel(_appSettings.Effect15Bind)}"
            };

            return string.Join(", ", tokens);
        }

        private static bool IsConfiguredEffectKeyMatch(Key pressedKey, string configuredBind, Key fallback)
        {
            var configuredKey = ReadConfiguredKey(configuredBind, fallback);
            if (pressedKey == configuredKey)
            {
                return true;
            }

            return TryGetDigitValue(pressedKey, out var pressedDigit) &&
                   TryGetDigitValue(configuredKey, out var configuredDigit) &&
                   pressedDigit == configuredDigit;
        }

        private static bool TryGetDigitValue(Key key, out int value)
        {
            switch (key)
            {
                case Key.D0:
                case Key.NumPad0:
                    value = 0;
                    return true;
                case Key.D1:
                case Key.NumPad1:
                    value = 1;
                    return true;
                case Key.D2:
                case Key.NumPad2:
                    value = 2;
                    return true;
                case Key.D3:
                case Key.NumPad3:
                    value = 3;
                    return true;
                case Key.D4:
                case Key.NumPad4:
                    value = 4;
                    return true;
                case Key.D5:
                case Key.NumPad5:
                    value = 5;
                    return true;
                case Key.D6:
                case Key.NumPad6:
                    value = 6;
                    return true;
                case Key.D7:
                case Key.NumPad7:
                    value = 7;
                    return true;
                case Key.D8:
                case Key.NumPad8:
                    value = 8;
                    return true;
                case Key.D9:
                case Key.NumPad9:
                    value = 9;
                    return true;
                default:
                    value = -1;
                    return false;
            }
        }

        private void AuraFarmSoundButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            PlaySoundEffect(SoundEffectKind.AuraFarm);
        }

        private void LaughSoundButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            PlaySoundEffect(SoundEffectKind.Laugh);
        }

        private void PsiSoundButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            PlaySoundEffect(SoundEffectKind.PsiRadiation);
        }

        private void RespectSoundButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            PlaySoundEffect(SoundEffectKind.Respect);
        }
    }
}


