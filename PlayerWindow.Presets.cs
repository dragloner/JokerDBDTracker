using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        private static readonly PlayerPresetDefinition[] BuiltInPlayerPresets =
        [
            new()
            {
                Key = "j_nightmare",
                DisplayNameRu = "JNightmare",
                DisplayNameEn = "JNightmare",
                DescriptionRu = "Макс. громкость + дисторшн + EQ + ч/б + контраст",
                DescriptionEn = "Max volume + distortion + EQ + B&W + contrast",
                Factory = CreateJNightmarePreset
            },
            new()
            {
                Key = "j_fire",
                DisplayNameRu = "JFire",
                DisplayNameEn = "JFire",
                DescriptionRu = "Басс-буст earrape — тяжёлый низ, умеренный дисторшн",
                DescriptionEn = "Bass boost earrape — heavy low end, moderate distortion",
                Factory = CreateJFirePreset
            },
            new()
            {
                Key = "j_relaxing",
                DisplayNameRu = "JRelaxing",
                DisplayNameEn = "JRelaxing",
                DescriptionRu = "Эхо + реверб + тёплый оттенок, расслабляющий",
                DescriptionEn = "Echo + reverb + warm hue shift, relaxing",
                Factory = CreateJRelaxingPreset
            },
            new()
            {
                Key = "j_retro_tape",
                DisplayNameRu = "JRetroTape",
                DisplayNameEn = "JRetroTape",
                DescriptionRu = "VHS, JPEG и лёгкая тряска",
                DescriptionEn = "VHS, JPEG and light shake",
                Factory = CreateRetroVhsPreset
            },
            new()
            {
                Key = "j_dream",
                DisplayNameRu = "JDream",
                DisplayNameEn = "JDream",
                DescriptionRu = "Фишай, холодный тон и мягкое размытие",
                DescriptionEn = "Fisheye, cold tone and soft blur",
                Factory = CreateDreamPreset
            },
            new()
            {
                Key = "j_void",
                DisplayNameRu = "JVoid",
                DisplayNameEn = "JVoid",
                DescriptionRu = "Сильный визуальный хаос",
                DescriptionEn = "Strong visual chaos",
                Factory = CreateChaosPreset
            },
            new()
            {
                Key = "j_ice",
                DisplayNameRu = "JIce",
                DisplayNameEn = "JIce",
                DescriptionRu = "Контраст, холод и немного звука",
                DescriptionEn = "Contrast, cold tone and slight audio FX",
                Factory = CreateCinemaColdPreset
            },
            new()
            {
                Key = "j_haze",
                DisplayNameRu = "JHaze",
                DisplayNameEn = "JHaze",
                DescriptionRu = "Размытие + затемнение",
                DescriptionEn = "Blur + darkness",
                Factory = CreateDeepBlurPreset
            },
            new()
            {
                Key = "j_frostbite",
                DisplayNameRu = "JFrostbite",
                DisplayNameEn = "JFrostbite",
                DescriptionRu = "JPEG + холодный тон + VHS",
                DescriptionEn = "JPEG + cold tone + VHS",
                Factory = CreateColdGlitchPreset
            },
            new()
            {
                Key = "j_tunnel",
                DisplayNameRu = "JTunnel",
                DisplayNameEn = "JTunnel",
                DescriptionRu = "Negative fisheye + chaos + shake",
                DescriptionEn = "Negative fisheye + chaos + shake",
                Factory = CreateDemonTunnelPreset
            },
            new()
            {
                Key = "j_mirror",
                DisplayNameRu = "JMirror",
                DisplayNameEn = "JMirror",
                DescriptionRu = "Double mirrors + blur + JPEG",
                DescriptionEn = "Double mirrors + blur + JPEG",
                Factory = CreateMirrorPrisonPreset
            },
            new()
            {
                Key = "j_poison",
                DisplayNameRu = "JPoison",
                DisplayNameEn = "JPoison",
                DescriptionRu = "Toxic hue VHS/JPEG + dirty audio",
                DescriptionEn = "Toxic hue VHS/JPEG + dirty audio",
                Factory = CreatePoisonTapePreset
            }
        ];

        private void InitializePresetsUi()
        {
            EnsureCustomPresetSlotsInitialized();
            ApplyPresetsLocalizationTexts();
            RefreshCustomPresetSlotComboBox();

            if (CustomPresetSlotComboBox is not null)
            {
                CustomPresetSlotComboBox.SelectedIndex = Math.Clamp(_selectedCustomPresetSlotIndex, 0, MaxCustomPlayerPresets - 1);
            }

            RefreshCustomPresetEditor();
            UpdatePresetStatusText();
            UpdatePresetWorkspaceSwitcherState();
        }

        private void ApplyPresetsLocalizationTexts()
        {
            if (EffectsWorkspaceEffectsTab is not null)
            {
                EffectsWorkspaceEffectsTab.Header = PT("Эффекты", "Effects");
            }

            if (EffectsWorkspacePresetsTab is not null)
            {
                EffectsWorkspacePresetsTab.Header = PT("Пресеты", "Presets");
            }

            if (PresetBuiltInHeaderText is not null)
            {
                PresetBuiltInHeaderText.Text = PT("Готовые пресеты", "Built-in presets");
            }

            if (PresetBuiltInHintText is not null)
            {
                PresetBuiltInHintText.Text = PT(
                    "Быстрые стили для картинки и звука. После применения можно докрутить вручную.",
                    "Quick styles for visuals and audio. You can tweak them after applying.");
            }

            if (CustomPresetHeaderText is not null)
            {
                CustomPresetHeaderText.Text = PT("Кастомные пресеты (до 20)", "Custom presets (up to 20)");
            }

            if (CustomPresetHintText is not null)
            {
                CustomPresetHintText.Text = PT(
                    "Выберите слот, задайте имя, сохраните текущую сборку эффектов и загружайте позже.",
                    "Choose a slot, name it, save current effects setup, and load it later.");
            }

            if (SaveCustomPresetButton is not null)
            {
                SaveCustomPresetButton.Content = PT("Сохранить", "Save");
            }

            if (LoadCustomPresetButton is not null)
            {
                LoadCustomPresetButton.Content = PT("Загрузить слот", "Load selected");
            }

            if (DeleteCustomPresetButton is not null)
            {
                DeleteCustomPresetButton.Content = PT("Очистить слот", "Clear slot");
            }

            if (QuickSavePresetButton is not null)
            {
                QuickSavePresetButton.Content = PT("Сохранить пресет", "Save preset");
            }

            if (QuickLoadPresetButton is not null)
            {
                QuickLoadPresetButton.Content = PT("Загрузить пресет", "Load preset");
            }
            if (ShowEffectsWorkspaceButton is not null)
            {
                ShowEffectsWorkspaceButton.Content = PT("Эффекты", "Effects");
            }

            if (ShowPresetsWorkspaceButton is not null)
            {
                ShowPresetsWorkspaceButton.Content = PT("Пресеты", "Presets");
            }

            if (BackToEffectsFromPresetsButton is not null)
            {
                BackToEffectsFromPresetsButton.Content = PT("Назад к эффектам", "Back to effects");
            }

            if (ApplyAndBackPresetButton is not null)
            {
                ApplyAndBackPresetButton.Content = PT("Назад к эффектам", "Back to effects");
            }

            if (PresetsSaveCurrentButton is not null)
            {
                PresetsSaveCurrentButton.Content = PT("Сохранить текущий в слот", "Save current to slot");
            }
        }

        private void EnsureCustomPresetSlotsInitialized()
        {
            if (_customPresetSlots.Count > 0)
            {
                return;
            }

            _appSettings.PlayerCustomPresetNames ??= [];
            _appSettings.PlayerCustomPresetPayloads ??= [];

            while (_appSettings.PlayerCustomPresetNames.Count < MaxCustomPlayerPresets)
            {
                _appSettings.PlayerCustomPresetNames.Add(string.Empty);
            }

            while (_appSettings.PlayerCustomPresetPayloads.Count < MaxCustomPlayerPresets)
            {
                _appSettings.PlayerCustomPresetPayloads.Add(string.Empty);
            }

            for (var i = 0; i < MaxCustomPlayerPresets; i++)
            {
                _customPresetSlots.Add(new PlayerPresetSlot
                {
                    SlotIndex = i,
                    Name = _appSettings.PlayerCustomPresetNames[i] ?? string.Empty,
                    PayloadJson = _appSettings.PlayerCustomPresetPayloads[i] ?? string.Empty
                });
            }
        }

        private void RefreshCustomPresetSlotComboBox()
        {
            if (CustomPresetSlotComboBox is null)
            {
                return;
            }

            var selectedIndex = CustomPresetSlotComboBox.SelectedIndex;
            CustomPresetSlotComboBox.Items.Clear();
            foreach (var slot in _customPresetSlots)
            {
                var slotNumber = slot.SlotIndex + 1;
                var title = string.IsNullOrWhiteSpace(slot.Name)
                    ? PT($"Слот {slotNumber} (пусто)", $"Slot {slotNumber} (empty)")
                    : PT($"Слот {slotNumber}: {slot.Name}", $"Slot {slotNumber}: {slot.Name}");
                CustomPresetSlotComboBox.Items.Add(title);
            }

            CustomPresetSlotComboBox.SelectedIndex = selectedIndex >= 0
                ? Math.Min(selectedIndex, Math.Max(0, CustomPresetSlotComboBox.Items.Count - 1))
                : Math.Clamp(_selectedCustomPresetSlotIndex, 0, MaxCustomPlayerPresets - 1);
        }

        private void RefreshCustomPresetEditor()
        {
            if (_customPresetSlots.Count == 0)
            {
                return;
            }

            var slot = _customPresetSlots[Math.Clamp(_selectedCustomPresetSlotIndex, 0, _customPresetSlots.Count - 1)];

            if (CustomPresetNameTextBox is not null)
            {
                CustomPresetNameTextBox.Text = slot.Name;
            }

            if (LoadCustomPresetButton is not null)
            {
                LoadCustomPresetButton.IsEnabled = slot.HasPayload;
            }

            if (DeleteCustomPresetButton is not null)
            {
                DeleteCustomPresetButton.IsEnabled = slot.HasPayload;
            }
        }

        private void UpdatePresetStatusText(string? overrideText = null)
        {
            if (PresetStatusText is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(overrideText))
            {
                PresetStatusText.Text = overrideText!;
                return;
            }

            if (_customPresetSlots.Count == 0)
            {
                PresetStatusText.Text = string.Empty;
                return;
            }

            var slot = _customPresetSlots[Math.Clamp(_selectedCustomPresetSlotIndex, 0, _customPresetSlots.Count - 1)];
            PresetStatusText.Text = slot.HasPayload
                ? PT("В слоте сохранён пресет. Можно загрузить или перезаписать.", "Preset saved in this slot. You can load or overwrite it.")
                : PT("Слот пустой. Сохрани текущие настройки эффектов в этот слот.", "This slot is empty. Save current effects into it.");
        }

        private void PresetSlotComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CustomPresetSlotComboBox is null || CustomPresetSlotComboBox.SelectedIndex < 0)
            {
                return;
            }

            _selectedCustomPresetSlotIndex = CustomPresetSlotComboBox.SelectedIndex;
            RefreshCustomPresetEditor();
            UpdatePresetStatusText();
        }

        private async void SaveCustomPresetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveCurrentPresetToSelectedSlotAsync();
            }
            catch (Exception ex)
            {
                UpdatePresetStatusText(PT($"Ошибка сохранения пресета: {ex.Message}", $"Failed to save preset: {ex.Message}"));
            }
        }

        private async void CustomPresetNameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                try
                {
                    await SaveCurrentPresetToSelectedSlotAsync();
                    CustomPresetNameTextBox?.SelectAll();
                }
                catch (Exception ex)
                {
                    UpdatePresetStatusText(PT($"РћС€РёР±РєР° СЃРѕС…СЂР°РЅРµРЅРёСЏ РїСЂРµСЃРµС‚Р°: {ex.Message}", $"Failed to save preset: {ex.Message}"));
                }

                return;
            }

            if (e.Key == System.Windows.Input.Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                SwitchPresetsPage(showPresets: false);
            }
        }

        private void LoadCustomPresetButton_Click(object sender, RoutedEventArgs e)
        {
            EnsureCustomPresetSlotsInitialized();
            var slot = _customPresetSlots[Math.Clamp(_selectedCustomPresetSlotIndex, 0, _customPresetSlots.Count - 1)];
            if (!slot.HasPayload)
            {
                UpdatePresetStatusText(PT("Слот пустой.", "Slot is empty."));
                return;
            }

            if (!TryDeserializePreset(slot.PayloadJson, out var settings))
            {
                UpdatePresetStatusText(PT("Не удалось прочитать пресет в слоте.", "Failed to read preset from slot."));
                return;
            }

            ApplyEffectSettingsToControls(settings!, presetKey: "custom", animateDetails: false);
            UpdatePresetStatusText(PT("Кастомный пресет применён.", "Custom preset applied."));
        }

        private async void DeleteCustomPresetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureCustomPresetSlotsInitialized();
                var slot = _customPresetSlots[Math.Clamp(_selectedCustomPresetSlotIndex, 0, _customPresetSlots.Count - 1)];
                slot.Name = string.Empty;
                slot.PayloadJson = string.Empty;
                SyncCustomPresetSlotsBackToSettings();
                await _settingsService.SaveAsync(_appSettings);
                RefreshCustomPresetSlotComboBox();
                RefreshCustomPresetEditor();
                UpdatePresetStatusText(PT("Слот очищен.", "Slot cleared."));
            }
            catch (Exception ex)
            {
                UpdatePresetStatusText(PT($"Ошибка очистки слота: {ex.Message}", $"Failed to clear slot: {ex.Message}"));
            }
        }

        private void ApplyBuiltInPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string presetKey)
            {
                return;
            }

            var preset = BuiltInPlayerPresets.FirstOrDefault(p => string.Equals(p.Key, presetKey, StringComparison.OrdinalIgnoreCase));
            if (preset is null)
            {
                return;
            }

            ApplyEffectSettingsToControls(preset.Factory(), preset.Key, animateDetails: false);
            var displayName = IsEnglishPlayerLanguage ? preset.DisplayNameEn : preset.DisplayNameRu;
            UpdatePresetStatusText(PT($"Применён пресет: {displayName}", $"Preset applied: {displayName}"));
        }

        private async Task SaveCurrentPresetToSelectedSlotAsync()
        {
            EnsureCustomPresetSlotsInitialized();
            var slot = _customPresetSlots[Math.Clamp(_selectedCustomPresetSlotIndex, 0, _customPresetSlots.Count - 1)];
            slot.Name = SanitizePresetName(CustomPresetNameTextBox?.Text, slot.SlotIndex);
            slot.PayloadJson = JsonSerializer.Serialize(NormalizeEffectSettings(GetEffectSettings()));
            SyncCustomPresetSlotsBackToSettings();
            await _settingsService.SaveAsync(_appSettings);
            RefreshCustomPresetSlotComboBox();
            RefreshCustomPresetEditor();
            UpdatePresetStatusText(PT("Кастомный пресет сохранён.", "Custom preset saved."));
        }

        private void SwitchPresetsPage(bool showPresets)
        {
            if (EffectsWorkspaceTabs is null)
            {
                return;
            }

            EffectsWorkspaceTabs.SelectedItem = showPresets
                ? EffectsWorkspacePresetsTab
                : EffectsWorkspaceEffectsTab;
            UpdatePresetWorkspaceSwitcherState();
        }

        private void UpdatePresetWorkspaceSwitcherState()
        {
            var isEffectsPage = EffectsWorkspaceEffectsTab?.IsSelected == true;
            var isPresetsPage = EffectsWorkspacePresetsTab?.IsSelected == true;

            if (ShowEffectsWorkspaceButton is not null)
            {
                ShowEffectsWorkspaceButton.IsEnabled = !isEffectsPage;
            }

            if (ShowPresetsWorkspaceButton is not null)
            {
                ShowPresetsWorkspaceButton.IsEnabled = !isPresetsPage;
            }

            if (BackToEffectsFromPresetsButton is not null)
            {
                BackToEffectsFromPresetsButton.IsEnabled = isPresetsPage;
            }

            if (ApplyAndBackPresetButton is not null)
            {
                ApplyAndBackPresetButton.IsEnabled = isPresetsPage;
            }
        }

        private void ShowEffectsWorkspaceButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchPresetsPage(showPresets: false);
        }

        private void ShowPresetsWorkspaceButton_Click(object sender, RoutedEventArgs e)
        {
            EnsureCustomPresetSlotsInitialized();
            RefreshCustomPresetEditor();
            UpdatePresetStatusText();
            SwitchPresetsPage(showPresets: true);
            CustomPresetSlotComboBox?.Focus();
        }

        private void BackToEffectsFromPresetsButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchPresetsPage(showPresets: false);
        }

        private void QuickSavePresetButton_Click(object sender, RoutedEventArgs e)
        {
            EnsureCustomPresetSlotsInitialized();
            RefreshCustomPresetEditor();
            UpdatePresetStatusText(PT("Выберите слот, задайте имя и нажмите Сохранить.", "Choose a slot, enter a name, then press Save."));
            SwitchPresetsPage(showPresets: true);
            if (CustomPresetNameTextBox is not null)
            {
                CustomPresetNameTextBox.Focus();
                CustomPresetNameTextBox.SelectAll();
                return;
            }

            if (CustomPresetSlotComboBox is not null)
            {
                CustomPresetSlotComboBox.Focus();
                return;
            }

            try
            {
                return;
            }
            catch (Exception ex)
            {
                UpdatePresetStatusText(PT($"Ошибка сохранения пресета: {ex.Message}", $"Failed to save preset: {ex.Message}"));
            }
        }

        private async void PresetsSaveCurrentButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveCurrentPresetToSelectedSlotAsync();
                CustomPresetNameTextBox?.Focus();
                CustomPresetNameTextBox?.SelectAll();
            }
            catch (Exception ex)
            {
                UpdatePresetStatusText(PT($"РћС€РёР±РєР° СЃРѕС…СЂР°РЅРµРЅРёСЏ РїСЂРµСЃРµС‚Р°: {ex.Message}", $"Failed to save preset: {ex.Message}"));
            }
        }

        private void QuickLoadPresetButton_Click(object sender, RoutedEventArgs e)
        {
            EnsureCustomPresetSlotsInitialized();
            RefreshCustomPresetEditor();
            UpdatePresetStatusText();
            SwitchPresetsPage(showPresets: true);
            CustomPresetSlotComboBox?.Focus();
        }

        private void ApplyEffectSettingsToControls(EffectSettings settings, string presetKey, bool animateDetails)
        {
            if (settings is null)
            {
                return;
            }

            settings = NormalizeEffectSettings(settings);

            var flags = settings.Flags ?? [];
            bool Flag(int index) => index >= 0 && index < flags.Length && flags[index];

            _suppressEffectUiEvents = true;
            try
            {
                Fx1.IsChecked = Flag(0);
                Fx2.IsChecked = Flag(1);
                Fx3.IsChecked = Flag(2);
                Fx4.IsChecked = Flag(3);
                Fx5.IsChecked = Flag(4);
                Fx6.IsChecked = Flag(5);
                Fx7.IsChecked = Flag(6);
                Fx8.IsChecked = Flag(7);
                Fx9.IsChecked = Flag(8);
                Fx10.IsChecked = Flag(9);
                Fx11.IsChecked = Flag(10);
                Fx12.IsChecked = Flag(11);
                Fx13.IsChecked = Flag(12);
                Fx14.IsChecked = Flag(13);
                Fx15.IsChecked = Flag(14);

                ContrastStrengthSlider.Value = Math.Clamp(settings.Contrast, -1, 1);
                DarknessStrengthSlider.Value = Math.Clamp(settings.Darkness, -1, 1);
                SaturationStrengthSlider.Value = Math.Clamp(settings.Saturation, -1, 1);
                HueShiftStrengthSlider.Value = Math.Clamp(settings.HueShift, -1, 1);
                BlurStrengthSlider.Value = Math.Clamp(settings.Blur, 0, 1);
                FisheyeStrengthSlider.Value = Math.Clamp(settings.Fisheye, -1, 1);
                _fisheyeCenterX = Math.Clamp(settings.FisheyeCenterX, 0, 1);
                _fisheyeCenterY = Math.Clamp(settings.FisheyeCenterY, 0, 1);
                VhsStrengthSlider.Value = Math.Clamp(settings.Vhs, 0, 1);
                ShakeStrengthSlider.Value = Math.Clamp(settings.Shake, 0, 1);
                JpegDamageStrengthSlider.Value = Math.Clamp(settings.JpegDamage, 0, 1);
                ColdToneStrengthSlider.Value = Math.Clamp(settings.ColdTone, -1, 1);

                AudioVolumeBoostSlider.Value = Math.Clamp(settings.AudioVolumeBoost, 0, 3);
                AudioPitchSemitonesSlider.Value = Math.Clamp(settings.AudioPitchSemitones, -24, 24);
                AudioReverbStrengthSlider.Value = Math.Clamp(settings.AudioReverb, 0, 2);
                AudioEchoStrengthSlider.Value = Math.Clamp(settings.AudioEcho, 0, 2);
                AudioDistortionStrengthSlider.Value = Math.Clamp(settings.AudioDistortion, 0, 2);
                AudioWobbleStrengthSlider.Value = Math.Clamp(settings.AudioWobble, 0, 2);
                AudioEqLowDbSlider.Value = Math.Clamp(settings.AudioEqLowDb, -30, 30);
                AudioEqMidDbSlider.Value = Math.Clamp(settings.AudioEqMidDb, -30, 30);
                AudioEqHighDbSlider.Value = Math.Clamp(settings.AudioEqHighDb, -30, 30);
            }
            finally
            {
                _suppressEffectUiEvents = false;
            }

            UpdateFisheyeCenterPadVisual();
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: animateDetails);
            MarkUserInteraction();
            if (!string.IsNullOrWhiteSpace(presetKey))
            {
                _presetKeysUsedThisSession.Add(presetKey);
            }

            RequestApplyEffects(immediate: true, force: true);
            SendWtEffectsSync();
        }

        private void SyncCustomPresetSlotsBackToSettings()
        {
            _appSettings.PlayerCustomPresetNames ??= [];
            _appSettings.PlayerCustomPresetPayloads ??= [];
            _appSettings.PlayerCustomPresetNames.Clear();
            _appSettings.PlayerCustomPresetPayloads.Clear();
            foreach (var slot in _customPresetSlots.OrderBy(s => s.SlotIndex))
            {
                _appSettings.PlayerCustomPresetNames.Add(slot.Name.Trim());
                _appSettings.PlayerCustomPresetPayloads.Add(slot.PayloadJson.Trim());
            }
        }

        private static string SanitizePresetName(string? rawName, int slotIndex)
        {
            var name = (rawName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Preset {slotIndex + 1}";
            }

            if (name.Length > 48)
            {
                name = name[..48];
            }

            return name;
        }

        private static bool TryDeserializePreset(string json, out EffectSettings? settings)
        {
            settings = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                settings = NormalizeEffectSettings(JsonSerializer.Deserialize<EffectSettings>(json));
                return settings is not null;
            }
            catch
            {
                return false;
            }
        }

        private static EffectSettings NormalizeEffectSettings(EffectSettings? settings)
        {
            var normalized = CreateDefaultPreset();
            if (settings is null)
            {
                return normalized;
            }

            var flags = settings.Flags ?? [];
            for (var i = 0; i < Math.Min(normalized.Flags.Length, flags.Length); i++)
            {
                normalized.Flags[i] = flags[i];
            }

            normalized.Contrast = Math.Clamp(settings.Contrast, -1, 1);
            normalized.Darkness = Math.Clamp(settings.Darkness, -1, 1);
            normalized.Saturation = Math.Clamp(settings.Saturation, -1, 1);
            normalized.HueShift = Math.Clamp(settings.HueShift, -1, 1);
            normalized.Blur = Math.Clamp(settings.Blur, 0, 1);
            normalized.Fisheye = Math.Clamp(settings.Fisheye, -1, 1);
            normalized.FisheyeCenterX = Math.Clamp(settings.FisheyeCenterX, 0, 1);
            normalized.FisheyeCenterY = Math.Clamp(settings.FisheyeCenterY, 0, 1);
            normalized.Vhs = Math.Clamp(settings.Vhs, 0, 1);
            normalized.Shake = Math.Clamp(settings.Shake, 0, 1);
            normalized.JpegDamage = Math.Clamp(settings.JpegDamage, 0, 1);
            normalized.ColdTone = Math.Clamp(settings.ColdTone, -1, 1);
            normalized.AudioVolumeBoost = Math.Clamp(settings.AudioVolumeBoost, 0, 3);
            normalized.AudioPitchSemitones = Math.Clamp(settings.AudioPitchSemitones, -24, 24);
            normalized.AudioReverb = Math.Clamp(settings.AudioReverb, 0, 2);
            normalized.AudioEcho = Math.Clamp(settings.AudioEcho, 0, 2);
            normalized.AudioDistortion = Math.Clamp(settings.AudioDistortion, 0, 2);
            normalized.AudioWobble = Math.Clamp(settings.AudioWobble, 0, 2);
            normalized.AudioEqLowDb = Math.Clamp(settings.AudioEqLowDb, -30, 30);
            normalized.AudioEqMidDb = Math.Clamp(settings.AudioEqMidDb, -30, 30);
            normalized.AudioEqHighDb = Math.Clamp(settings.AudioEqHighDb, -30, 30);
            return normalized;
        }

        private static EffectSettings CreateDefaultPreset()
        {
            return new EffectSettings
            {
                Flags = new bool[15],
                Contrast = 0,
                Darkness = 0,
                Saturation = 0,
                HueShift = 0,
                Blur = 0,
                Fisheye = 0,
                FisheyeCenterX = 0.5,
                FisheyeCenterY = 0.5,
                Vhs = 0,
                Shake = 0,
                JpegDamage = 0.4,
                ColdTone = 0,
                AudioVolumeBoost = 0,
                AudioPitchSemitones = 0,
                AudioReverb = 0,
                AudioEcho = 0,
                AudioDistortion = 0,
                AudioWobble = 0,
                AudioEqLowDb = 0,
                AudioEqMidDb = 0,
                AudioEqHighDb = 0
            };
        }

        private static EffectSettings CreateJNightmarePreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[0] = true;  // Grayscale (B&W)
            p.Flags[3] = true;  // High contrast
            p.AudioVolumeBoost = 3.0;       // Max volume
            p.AudioDistortion = 2.0;        // Max distortion
            p.AudioEqLowDb = 30.0;          // Max low boost
            p.AudioEqMidDb = 24.0;          // Strong mid boost
            p.AudioEqHighDb = 30.0;         // Strong high boost
            p.Contrast = 1.0;               // Very high contrast
            p.AudioPitchSemitones = -4.0;   // Slowdown -4 semitones
            return p;
        }

        private static EffectSettings CreateJFirePreset()
        {
            var p = CreateDefaultPreset();
            // Bass boost earrape — heavy low end, moderate distortion, no visuals
            p.AudioVolumeBoost = 2.2;
            p.AudioDistortion = 1.0;
            p.AudioEqLowDb = 28.0;
            p.AudioEqMidDb = 8.0;
            p.AudioEqHighDb = -4.0;
            p.AudioPitchSemitones = -2.0;   // Slight slowdown -2 semitones
            return p;
        }

        private static EffectSettings CreateJRelaxingPreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[5] = true;  // Saturation (warm colors)
            p.Flags[6] = true;  // Hue shift
            p.AudioReverb = 1.4;
            p.AudioEcho = 1.0;
            p.AudioEqLowDb = 6.0;           // Warm bass boost
            p.AudioEqMidDb = -3.0;          // Cut harsh mids
            p.AudioEqHighDb = -5.0;         // Roll off highs
            p.HueShift = 0.18;              // Warm orange shift
            p.Saturation = 0.35;            // Slightly richer colors
            return p;
        }

        private static EffectSettings CreateRetroVhsPreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[9] = true;  // VHS
            p.Flags[12] = true; // JPEG
            p.Flags[10] = true; // Shake
            p.Flags[4] = true;  // Darkness
            p.Flags[3] = true;  // Contrast
            p.Vhs = 0.82;
            p.JpegDamage = 0.54;
            p.Shake = 0.12;
            p.Darkness = 0.12;
            p.Contrast = 0.28;
            p.AudioDistortion = 0.12;
            p.AudioEqLowDb = 1.5;
            p.AudioEqMidDb = -2.8;
            p.AudioEqHighDb = -6.0;
            p.AudioReverb = 0.05;
            return p;
        }

        private static EffectSettings CreateDreamPreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[8] = true;  // Fisheye
            p.Flags[13] = true; // Cold tone
            p.Flags[7] = true;  // Blur
            p.Flags[5] = true;  // Saturation
            p.Flags[6] = true;  // Hue shift
            p.Fisheye = 0.19;
            p.Blur = 0.30;
            p.ColdTone = 0.34;
            p.Saturation = 0.22;
            p.HueShift = 0.08;
            p.FisheyeCenterX = 0.44;
            p.FisheyeCenterY = 0.40;
            p.AudioReverb = 0.28;
            p.AudioEcho = 0.07;
            p.AudioPitchSemitones = 1.0;
            return p;
        }

        private static EffectSettings CreateChaosPreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[2] = true;  // Invert
            p.Flags[6] = true;  // Hue shift
            p.Flags[8] = true;  // Fisheye
            p.Flags[10] = true; // Shake
            p.Flags[12] = true; // JPEG
            p.Flags[9] = true;  // VHS
            p.Flags[3] = true;  // Contrast
            p.HueShift = 0.90;
            p.Fisheye = -0.78;
            p.Shake = 0.67;
            p.JpegDamage = 0.83;
            p.Vhs = 0.52;
            p.Contrast = 0.42;
            p.AudioEcho = 0.32;
            p.AudioDistortion = 0.18;
            p.AudioPitchSemitones = -2.4;
            p.AudioEqHighDb = -5.5;
            return p;
        }

        private static EffectSettings CreateCinemaColdPreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[3] = true;  // Contrast
            p.Flags[4] = true;  // Darkness
            p.Flags[13] = true; // Cold tone
            p.Flags[6] = true;  // Hue shift
            p.Contrast = 0.30;
            p.Darkness = 0.18;
            p.ColdTone = 0.24;
            p.HueShift = -0.05;
            p.AudioVolumeBoost = 0.20;
            p.AudioEqLowDb = 2.8;
            p.AudioEqMidDb = 0.8;
            p.AudioEqHighDb = 2.2;
            p.AudioReverb = 0.10;
            return p;
        }

        private static EffectSettings CreateDeepBlurPreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[7] = true;  // Blur
            p.Flags[4] = true;  // Darkness
            p.Flags[5] = true;  // Saturation
            p.Flags[1] = true;  // Sepia
            p.Blur = 0.72;
            p.Darkness = 0.22;
            p.Saturation = -0.38;
            p.AudioReverb = 0.12;
            p.AudioEqHighDb = -3.5;
            return p;
        }

        private static EffectSettings CreateColdGlitchPreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[12] = true; // JPEG
            p.Flags[13] = true; // Cold
            p.Flags[9] = true;  // VHS
            p.Flags[6] = true;  // Hue
            p.Flags[10] = true; // Shake
            p.Flags[8] = true;  // Fisheye
            p.JpegDamage = 0.66;
            p.ColdTone = 0.74;
            p.Vhs = 0.36;
            p.HueShift = 0.18;
            p.Shake = 0.16;
            p.Fisheye = -0.22;
            p.FisheyeCenterX = 0.55;
            p.FisheyeCenterY = 0.52;
            p.AudioEqHighDb = -4.0;
            p.AudioDistortion = 0.10;
            return p;
        }

        private static EffectSettings CreateDemonTunnelPreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[2] = true;  // Invert
            p.Flags[8] = true;  // Fisheye
            p.Flags[10] = true; // Shake
            p.Flags[4] = true;  // Darkness
            p.Flags[3] = true;  // Contrast
            p.Flags[12] = true; // JPEG
            p.Fisheye = -0.92;
            p.FisheyeCenterX = 0.50;
            p.FisheyeCenterY = 0.62;
            p.Shake = 0.74;
            p.Darkness = 0.42;
            p.Contrast = 0.62;
            p.JpegDamage = 0.48;
            p.AudioDistortion = 0.30;
            p.AudioEcho = 0.22;
            p.AudioReverb = 0.12;
            p.AudioPitchSemitones = -2.8;
            p.AudioEqLowDb = 2.0;
            return p;
        }

        private static EffectSettings CreateMirrorPrisonPreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[11] = true; // H mirror
            p.Flags[14] = true; // V mirror
            p.Flags[7] = true;  // Blur
            p.Flags[12] = true; // JPEG
            p.Flags[3] = true;  // Contrast
            p.Flags[0] = true;  // Grayscale
            p.Blur = 0.34;
            p.JpegDamage = 0.58;
            p.Contrast = 0.44;
            p.AudioReverb = 0.24;
            p.AudioEcho = 0.16;
            p.AudioEqMidDb = -1.2;
            return p;
        }

        private static EffectSettings CreatePoisonTapePreset()
        {
            var p = CreateDefaultPreset();
            p.Flags[9] = true;  // VHS
            p.Flags[12] = true; // JPEG
            p.Flags[6] = true;  // Hue shift
            p.Flags[5] = true;  // Saturation
            p.Flags[10] = true; // Shake
            p.Flags[13] = true; // Cold tone
            p.Vhs = 0.70;
            p.JpegDamage = 0.74;
            p.HueShift = 0.63;
            p.Saturation = 0.42;
            p.Shake = 0.22;
            p.ColdTone = 0.22;
            p.AudioDistortion = 0.28;
            p.AudioEcho = 0.18;
            p.AudioEqLowDb = -2.0;
            p.AudioEqMidDb = -1.0;
            p.AudioEqHighDb = -5.2;
            return p;
        }

    }
}
