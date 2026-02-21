using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JokerDBDTracker.Services;
using Microsoft.Win32;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private readonly AppSettingsService _settingsService = new();
        private AppSettingsData _appSettings = new();
        private bool _isApplyingSettingsUi;
        private readonly ScaleTransform _uiScaleTransform = new(1.0, 1.0);
        private bool _isCapturingBind;
        private string _bindCaptureTarget = string.Empty;

        private const string AutoStartRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartValueName = "JokerDBDTracker";
        private const string BindTargetHideEffects = "hide_effects";
        private const string BindTargetAuraFarm = "aura_farm";
        private const string BindTargetLaugh = "laugh";
        private const string BindTargetPsi = "psi";
        private const string BindTargetRespect = "respect";
        private const int EffectBindCount = 15;

        private void InitializeSettingsUi()
        {
            if (MainRootBorder is null)
            {
                return;
            }

            MainRootBorder.LayoutTransform = _uiScaleTransform;
            MainRootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        private async Task LoadAndApplySettingsAsync()
        {
            _appSettings = await _settingsService.LoadAsync();
            ApplySettingsToRuntime();
            ApplySettingsToControls();
            ApplyLocalization();
        }

        private void ApplySettingsToRuntime()
        {
            ApplyUiScale(_appSettings.UiScale);
            UiAnimation.SetIsEnabled(this, _appSettings.AnimationsEnabled);
            DiagnosticsService.SetEnabled(_appSettings.LoggingEnabled);
            EnsureAutoStartState(_appSettings.AutoStartEnabled);
        }

        private void ApplySettingsToControls()
        {
            if (AutoStartCheckBox is null ||
                LanguageComboBox is null ||
                UiScaleSlider is null ||
                AnimationsEnabledCheckBox is null ||
                LoggingEnabledCheckBox is null ||
                FullscreenBehaviorComboBox is null)
            {
                return;
            }

            _isApplyingSettingsUi = true;
            try
            {
                AutoStartCheckBox.IsChecked = _appSettings.AutoStartEnabled;
                SetComboSelectionByTag(LanguageComboBox, _appSettings.Language);
                UiScaleSlider.Value = _appSettings.UiScale;
                AnimationsEnabledCheckBox.IsChecked = _appSettings.AnimationsEnabled;
                LoggingEnabledCheckBox.IsChecked = _appSettings.LoggingEnabled;
                SetComboSelectionByTag(FullscreenBehaviorComboBox, _appSettings.FullscreenBehavior);
                UpdateUiScaleText(_appSettings.UiScale);
                UpdateBindDisplayTexts();
                SetBindCaptureStatus(string.Empty);
                UpdateBindCaptureButtons();
            }
            finally
            {
                _isApplyingSettingsUi = false;
            }
        }

        private void SetComboSelectionByTag(ComboBox combo, string tag)
        {
            foreach (var item in combo.Items)
            {
                if (item is not ComboBoxItem comboItem)
                {
                    continue;
                }

                if (string.Equals(comboItem.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = comboItem;
                    return;
                }
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                await _settingsService.SaveAsync(_appSettings);
            }
            catch
            {
                // Best-effort persistence.
            }
        }

        private void ApplyUiScale(double scale)
        {
            var normalizedScale = Math.Clamp(scale, 0.85, 1.35);
            _uiScaleTransform.ScaleX = normalizedScale;
            _uiScaleTransform.ScaleY = normalizedScale;
        }

        private static void EnsureAutoStartState(bool enabled)
        {
            try
            {
                using var runKey = Registry.CurrentUser.CreateSubKey(AutoStartRunKey, writable: true);
                if (runKey is null)
                {
                    return;
                }

                if (!enabled)
                {
                    runKey.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
                    return;
                }

                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    return;
                }

                runKey.SetValue(AutoStartValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            catch
            {
                // Ignore environments where startup registration is unavailable.
            }
        }

        private static string ReadComboTag(ComboBox combo, string fallback)
        {
            return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
        }

        private void UpdateUiScaleText(double scale)
        {
            if (UiScaleValueText is not null)
            {
                UiScaleValueText.Text = $"{scale:0.00}x";
            }
        }

        private async void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSettingsUi)
            {
                return;
            }

            _appSettings.AutoStartEnabled = AutoStartCheckBox.IsChecked == true;
            EnsureAutoStartState(_appSettings.AutoStartEnabled);
            await SaveSettingsAsync();
        }

        private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingSettingsUi || LanguageComboBox.SelectedItem is not ComboBoxItem)
            {
                return;
            }

            _appSettings.Language = ReadComboTag(LanguageComboBox, "ru");
            ApplyLocalization();
            await SaveSettingsAsync();
        }

        private async void UiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
            {
                return;
            }

            var scale = Math.Round(UiScaleSlider.Value, 2);
            UpdateUiScaleText(scale);
            if (_isApplyingSettingsUi)
            {
                return;
            }

            _appSettings.UiScale = scale;
            ApplyUiScale(scale);
            await SaveSettingsAsync();
        }

        private async void AnimationsEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSettingsUi)
            {
                return;
            }

            _appSettings.AnimationsEnabled = AnimationsEnabledCheckBox.IsChecked == true;
            UiAnimation.SetIsEnabled(this, _appSettings.AnimationsEnabled);
            await SaveSettingsAsync();
        }

        private async void LoggingEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSettingsUi)
            {
                return;
            }

            _appSettings.LoggingEnabled = LoggingEnabledCheckBox.IsChecked == true;
            DiagnosticsService.SetEnabled(_appSettings.LoggingEnabled);
            await SaveSettingsAsync();
        }

        private async void FullscreenBehaviorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingSettingsUi || FullscreenBehaviorComboBox.SelectedItem is not ComboBoxItem)
            {
                return;
            }

            _appSettings.FullscreenBehavior = ReadComboTag(FullscreenBehaviorComboBox, "auto");
            await SaveSettingsAsync();
        }

        private void AssignBindButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string target || string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            _isCapturingBind = true;
            _bindCaptureTarget = target;
            SetBindCaptureStatus(T("Нажмите любую клавишу (Esc - отмена).", "Press any key (Esc to cancel)."));
            UpdateBindCaptureButtons();
        }

        private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isCapturingBind)
            {
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                _isCapturingBind = false;
                _bindCaptureTarget = string.Empty;
                SetBindCaptureStatus(T("Назначение отменено.", "Binding canceled."));
                UpdateBindCaptureButtons();
                e.Handled = true;
                return;
            }

            if (key == Key.None)
            {
                return;
            }

            var bind = key.ToString().ToUpperInvariant();
            ApplyBindToTarget(_bindCaptureTarget, bind);
            _isCapturingBind = false;
            _bindCaptureTarget = string.Empty;
            UpdateBindDisplayTexts();
            SetBindCaptureStatus(T($"Назначено: {FormatBindForUi(bind)}", $"Assigned: {FormatBindForUi(bind)}"));
            UpdateBindCaptureButtons();
            await SaveSettingsAsync();
            e.Handled = true;
        }

        private void ApplyBindToTarget(string target, string bind)
        {
            switch (target)
            {
                case BindTargetHideEffects:
                    _appSettings.HideEffectsPanelBind = bind;
                    break;
                case BindTargetAuraFarm:
                    _appSettings.AuraFarmSoundBind = bind;
                    break;
                case BindTargetLaugh:
                    _appSettings.LaughSoundBind = bind;
                    break;
                case BindTargetPsi:
                    _appSettings.PsiSoundBind = bind;
                    break;
                case BindTargetRespect:
                    _appSettings.RespectSoundBind = bind;
                    break;
                default:
                    if (TryParseEffectBindTarget(target, out var effectIndex))
                    {
                        SetEffectBindByIndex(effectIndex, bind);
                    }

                    break;
            }
        }

        private void UpdateBindDisplayTexts()
        {
            if (HideEffectsPanelBindValueText is null ||
                AuraFarmBindValueText is null ||
                LaughBindValueText is null ||
                PsiBindValueText is null ||
                RespectBindValueText is null)
            {
                return;
            }

            HideEffectsPanelBindValueText.Text = FormatBindForUi(_appSettings.HideEffectsPanelBind);
            AuraFarmBindValueText.Text = FormatBindForUi(_appSettings.AuraFarmSoundBind);
            LaughBindValueText.Text = FormatBindForUi(_appSettings.LaughSoundBind);
            PsiBindValueText.Text = FormatBindForUi(_appSettings.PsiSoundBind);
            RespectBindValueText.Text = FormatBindForUi(_appSettings.RespectSoundBind);
            UpdateEffectBindButtons();
        }

        private static string FormatBindForUi(string bind)
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

        private void SetBindCaptureStatus(string text)
        {
            if (BindCaptureStatusText is not null)
            {
                BindCaptureStatusText.Text = text;
            }
        }

        private void UpdateBindCaptureButtons()
        {
            if (AssignHideEffectsBindButton is null ||
                AssignAuraFarmBindButton is null ||
                AssignLaughBindButton is null ||
                AssignPsiBindButton is null ||
                AssignRespectBindButton is null)
            {
                return;
            }

            var buttons = new[]
            {
                AssignHideEffectsBindButton,
                AssignAuraFarmBindButton,
                AssignLaughBindButton,
                AssignPsiBindButton,
                AssignRespectBindButton
            };

            foreach (var button in buttons)
            {
                var isCurrent = _isCapturingBind &&
                                string.Equals(button.Tag?.ToString(), _bindCaptureTarget, StringComparison.Ordinal);
                button.Content = isCurrent ? T("Нажмите...", "Press key...") : T("Назначить", "Assign");
            }

            UpdateEffectBindButtons();
        }

        private void UpdateEffectBindButtons()
        {
            for (var effectIndex = 1; effectIndex <= EffectBindCount; effectIndex++)
            {
                var button = GetEffectBindButton(effectIndex);
                if (button is null)
                {
                    continue;
                }

                var target = $"fx{effectIndex}";
                var isCurrent = _isCapturingBind &&
                                string.Equals(target, _bindCaptureTarget, StringComparison.OrdinalIgnoreCase);
                if (isCurrent)
                {
                    button.Content = T("Нажмите...", "Press key...");
                    continue;
                }

                button.Content = $"{effectIndex} [{FormatBindForUi(GetEffectBindByIndex(effectIndex))}]";
            }
        }

        private Button? GetEffectBindButton(int effectIndex)
        {
            return effectIndex switch
            {
                1 => AssignFx1BindButton,
                2 => AssignFx2BindButton,
                3 => AssignFx3BindButton,
                4 => AssignFx4BindButton,
                5 => AssignFx5BindButton,
                6 => AssignFx6BindButton,
                7 => AssignFx7BindButton,
                8 => AssignFx8BindButton,
                9 => AssignFx9BindButton,
                10 => AssignFx10BindButton,
                11 => AssignFx11BindButton,
                12 => AssignFx12BindButton,
                13 => AssignFx13BindButton,
                14 => AssignFx14BindButton,
                15 => AssignFx15BindButton,
                _ => null
            };
        }

        private static bool TryParseEffectBindTarget(string target, out int effectIndex)
        {
            effectIndex = 0;
            if (string.IsNullOrWhiteSpace(target) || !target.StartsWith("fx", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(target.AsSpan(2), out var parsed))
            {
                return false;
            }

            if (parsed < 1 || parsed > EffectBindCount)
            {
                return false;
            }

            effectIndex = parsed;
            return true;
        }

        private string GetEffectBindByIndex(int effectIndex)
        {
            return effectIndex switch
            {
                1 => _appSettings.Effect1Bind,
                2 => _appSettings.Effect2Bind,
                3 => _appSettings.Effect3Bind,
                4 => _appSettings.Effect4Bind,
                5 => _appSettings.Effect5Bind,
                6 => _appSettings.Effect6Bind,
                7 => _appSettings.Effect7Bind,
                8 => _appSettings.Effect8Bind,
                9 => _appSettings.Effect9Bind,
                10 => _appSettings.Effect10Bind,
                11 => _appSettings.Effect11Bind,
                12 => _appSettings.Effect12Bind,
                13 => _appSettings.Effect13Bind,
                14 => _appSettings.Effect14Bind,
                15 => _appSettings.Effect15Bind,
                _ => string.Empty
            };
        }

        private void SetEffectBindByIndex(int effectIndex, string bind)
        {
            switch (effectIndex)
            {
                case 1:
                    _appSettings.Effect1Bind = bind;
                    break;
                case 2:
                    _appSettings.Effect2Bind = bind;
                    break;
                case 3:
                    _appSettings.Effect3Bind = bind;
                    break;
                case 4:
                    _appSettings.Effect4Bind = bind;
                    break;
                case 5:
                    _appSettings.Effect5Bind = bind;
                    break;
                case 6:
                    _appSettings.Effect6Bind = bind;
                    break;
                case 7:
                    _appSettings.Effect7Bind = bind;
                    break;
                case 8:
                    _appSettings.Effect8Bind = bind;
                    break;
                case 9:
                    _appSettings.Effect9Bind = bind;
                    break;
                case 10:
                    _appSettings.Effect10Bind = bind;
                    break;
                case 11:
                    _appSettings.Effect11Bind = bind;
                    break;
                case 12:
                    _appSettings.Effect12Bind = bind;
                    break;
                case 13:
                    _appSettings.Effect13Bind = bind;
                    break;
                case 14:
                    _appSettings.Effect14Bind = bind;
                    break;
                case 15:
                    _appSettings.Effect15Bind = bind;
                    break;
            }
        }

        private static string GetDefaultEffectBindByIndex(int effectIndex)
        {
            return effectIndex switch
            {
                1 => "D1",
                2 => "D2",
                3 => "D3",
                4 => "D4",
                5 => "D5",
                6 => "D6",
                7 => "D7",
                8 => "D8",
                9 => "D9",
                10 => "D0",
                11 => "Q",
                12 => "W",
                13 => "E",
                14 => "R",
                15 => "T",
                _ => string.Empty
            };
        }

        private void ResetAllBindsToDefaults()
        {
            _appSettings.HideEffectsPanelBind = "H";
            _appSettings.AuraFarmSoundBind = "Y";
            _appSettings.LaughSoundBind = "U";
            _appSettings.PsiSoundBind = "I";
            _appSettings.RespectSoundBind = "O";
            for (var effectIndex = 1; effectIndex <= EffectBindCount; effectIndex++)
            {
                SetEffectBindByIndex(effectIndex, GetDefaultEffectBindByIndex(effectIndex));
            }
        }

        private async void ResetBindsButton_Click(object sender, RoutedEventArgs e)
        {
            var confirmResult = MessageBox.Show(
                T(
                    "Точно сбросить все бинды к значениям по умолчанию? Это перетрёт твои текущие назначения.",
                    "Are you sure you want to reset all binds to defaults? This will overwrite current assignments."),
                T("Подтверждение", "Confirmation"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (confirmResult != MessageBoxResult.Yes)
            {
                SetBindCaptureStatus(T("Сброс биндов отменён.", "Binds reset canceled."));
                return;
            }

            _isCapturingBind = false;
            _bindCaptureTarget = string.Empty;
            ResetAllBindsToDefaults();
            UpdateBindDisplayTexts();
            SetBindCaptureStatus(T("Бинды сброшены к значениям по умолчанию.", "Binds were reset to defaults."));
            UpdateBindCaptureButtons();
            await SaveSettingsAsync();
        }

        private void ResetCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var profilePath = AppStoragePaths.GetWebViewProfileDirectory();
            var localAppDataPath = AppStoragePaths.GetCurrentLocalAppDataDirectory();
            var updatesPath = Path.Combine(localAppDataPath, "Updates");

            try
            {
                if (Directory.Exists(profilePath))
                {
                    Directory.Delete(profilePath, recursive: true);
                }

                if (Directory.Exists(updatesPath))
                {
                    Directory.Delete(updatesPath, recursive: true);
                }

                Directory.CreateDirectory(profilePath);
                MessageBox.Show(
                    T(
                        "Кеш очищен. Новые данные WebView2 будут созданы автоматически.",
                        "Cache has been cleared. New WebView2 data will be created automatically."),
                    T("Настройки", "Settings"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{T("Не удалось очистить кеш:", "Failed to clear cache:")}{Environment.NewLine}{ex.Message}",
                    T("Настройки", "Settings"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}

