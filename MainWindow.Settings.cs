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
            EnsureAutoStartState(_appSettings.AutoStartEnabled);
        }

        private void ApplySettingsToControls()
        {
            if (AutoStartCheckBox is null ||
                LanguageComboBox is null ||
                UiScaleSlider is null ||
                AnimationsEnabledCheckBox is null ||
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

