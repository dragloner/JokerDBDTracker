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

        private bool TryHandleAppKeybind(Key key)
        {
            if (!CanProcessPlayerCommands())
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
                var audioResourceUri = ResolveSoundEffectResourceUri(kind);
                if (audioResourceUri is null)
                {
                    ReportSoundPlaybackFailure("Sound asset not found.");
                    return;
                }

                PlayAudioFile(audioResourceUri);
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
                SoundEffectKind.AuraFarm => "aura-ego.mp3",
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

        private void PlayAudioFile(Uri resourceUri)
        {
            var player = new MediaPlayer();
            player.Open(resourceUri);
            player.Volume = 1.0;
            player.MediaEnded += (_, _) =>
            {
                player.Stop();
                player.Close();
                _activeSoundPlayers.Remove(player);
            };
            player.MediaFailed += (_, _) =>
            {
                player.Close();
                _activeSoundPlayers.Remove(player);
                ReportSoundPlaybackFailure("MediaPlayer failed to decode sound.");
            };

            _activeSoundPlayers.Add(player);
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
                BindsHintText.Text = PT(
                    $"Бинды: 1-0 / F1-F10 = эффекты 1-10, Q-T / F11-F12 + ZXC = 11-15. Панель: {hide}. Звуки: Aura {aura}, Смех {laugh}, Пси {psi}, +Respect {respect}.",
                    $"Binds: 1-0 / F1-F10 = effects 1-10, Q-T / F11-F12 + ZXC = 11-15. Panel: {hide}. Sounds: Aura {aura}, Laugh {laugh}, Psi {psi}, +Respect {respect}.");
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
            checkBox = key switch
            {
                Key.D1 or Key.NumPad1 or Key.F1 => Fx1,
                Key.D2 or Key.NumPad2 or Key.F2 => Fx2,
                Key.D3 or Key.NumPad3 or Key.F3 => Fx3,
                Key.D4 or Key.NumPad4 or Key.F4 => Fx4,
                Key.D5 or Key.NumPad5 or Key.F5 => Fx5,
                Key.D6 or Key.NumPad6 or Key.F6 => Fx6,
                Key.D7 or Key.NumPad7 or Key.F7 => Fx7,
                Key.D8 or Key.NumPad8 or Key.F8 => Fx8,
                Key.D9 or Key.NumPad9 or Key.F9 => Fx9,
                Key.D0 or Key.NumPad0 or Key.F10 => Fx10,
                Key.Q or Key.F11 => Fx11,
                Key.W or Key.F12 => Fx12,
                Key.E or Key.Z => Fx13,
                Key.R or Key.X => Fx14,
                Key.T or Key.C => Fx15,
                _ => null!
            };

            return checkBox is not null;
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


