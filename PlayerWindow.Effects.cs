using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class PlayerWindow : Window
    {
        private void UpdateEffectSessionStats()
        {
            var activeEffects = GetActiveEffectsCount();
            if (activeEffects > 0)
            {
                WatchedWithAnyEffects = true;
                MaxEnabledEffectsCount = Math.Max(MaxEnabledEffectsCount, activeEffects);
            }

            if (Fx8.IsChecked == true && BlurStrengthSlider.Value >= 0.75)
            {
                UsedStrongBlur = true;
            }

            if (Fx9.IsChecked == true && RedGlowStrengthSlider.Value >= 0.75)
            {
                UsedStrongRedGlow = true;
            }

            if (Fx15.IsChecked == true && VioletGlowStrengthSlider.Value >= 0.75)
            {
                UsedStrongVioletGlow = true;
            }

            if (Fx11.IsChecked == true && ShakeStrengthSlider.Value >= 0.75)
            {
                UsedStrongShake = true;
            }
        }

        private void EffectToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: true);
            RequestApplyEffects(immediate: true);
        }

        private void StrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RequestApplyEffects(immediate: false);
        }

        private async void EffectsApplyDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _effectsApplyDebounceTimer.Stop();
            await ApplyEffectsSafelyAsync();
        }

        private void RequestApplyEffects(bool immediate, bool force = false)
        {
            _effectRefreshCounter = 0;
            if (immediate)
            {
                _effectsApplyDebounceTimer.Stop();
                _ = ApplyEffectsSafelyAsync(force);
                return;
            }

            _pendingEffectsApplyForce |= force;
            _effectsApplyDebounceTimer.Stop();
            _effectsApplyDebounceTimer.Start();
        }

        private async Task ApplyEffectsSafelyAsync(bool force = false)
        {
            if (_isApplyingEffects)
            {
                _pendingEffectsApply = true;
                _pendingEffectsApplyForce |= force;
                return;
            }

            var nextForce = force || _pendingEffectsApplyForce;
            _isApplyingEffects = true;
            try
            {
                do
                {
                    _pendingEffectsApply = false;
                    _pendingEffectsApplyForce = false;
                    await ApplyEffectsAsync(nextForce);
                    nextForce = _pendingEffectsApplyForce;
                }
                while (_pendingEffectsApply);
            }
            finally
            {
                _isApplyingEffects = false;
            }
        }

        private int GetActiveEffectsCount()
        {
            return GetEffectsState().Count(v => v);
        }

        private bool[] GetEffectsState()
        {
            return
            [
                Fx1.IsChecked == true,
                Fx2.IsChecked == true,
                Fx3.IsChecked == true,
                Fx4.IsChecked == true,
                Fx5.IsChecked == true,
                Fx6.IsChecked == true,
                Fx7.IsChecked == true,
                Fx8.IsChecked == true,
                Fx9.IsChecked == true,
                Fx10.IsChecked == true,
                Fx11.IsChecked == true,
                Fx12.IsChecked == true,
                Fx13.IsChecked == true,
                Fx14.IsChecked == true,
                Fx15.IsChecked == true
            ];
        }

        private EffectSettings GetEffectSettings()
        {
            static double NormalizeSigned(Slider slider) => Math.Clamp(slider.Value, -1, 1);
            static double NormalizePositive(Slider slider) => Math.Clamp(slider.Value, 0, 1);

            return new EffectSettings
            {
                Flags = GetEffectsState(),
                Contrast = NormalizeSigned(ContrastStrengthSlider),
                Darkness = NormalizeSigned(DarknessStrengthSlider),
                Saturation = NormalizeSigned(SaturationStrengthSlider),
                HueShift = NormalizeSigned(HueShiftStrengthSlider),
                Blur = NormalizePositive(BlurStrengthSlider),
                RedGlow = NormalizePositive(RedGlowStrengthSlider),
                Vhs = NormalizePositive(VhsStrengthSlider),
                Shake = NormalizePositive(ShakeStrengthSlider),
                Pixelation = NormalizePositive(PixelationStrengthSlider),
                ColdTone = NormalizeSigned(ColdToneStrengthSlider),
                VioletGlow = NormalizePositive(VioletGlowStrengthSlider)
            };
        }

        private async Task ApplyEffectsAsync(bool forceApply)
        {
            if (Player.CoreWebView2 is null)
            {
                return;
            }

            var settingsJson = JsonSerializer.Serialize(GetEffectSettings());
            if (!forceApply && string.Equals(_lastAppliedEffectsSignature, settingsJson, StringComparison.Ordinal))
            {
                return;
            }

            var script = $$"""
                (() => {
                    const settings = {{settingsJson}};
                    const flags = settings.Flags;
                    const video = document.querySelector('video');
                    if (!video) return;
                    const activeCount = flags.reduce((acc, v) => acc + (v ? 1 : 0), 0);
                    const clearMirrorGuard = () => {
                        if (!window.__stwfxMirrorGuard) {
                            return;
                        }

                        try {
                            window.__stwfxMirrorGuard.disconnect();
                        } catch {
                            // no-op
                        }
                        window.__stwfxMirrorGuard = null;
                    };

                    const installMirrorGuard = (targetVideo) => {
                        clearMirrorGuard();
                        const enforceMirror = () => {
                            targetVideo.style.setProperty('transform-origin', 'center center', 'important');
                            targetVideo.style.setProperty('transform', 'scaleX(-1)', 'important');
                        };

                        const observer = new MutationObserver(() => {
                            enforceMirror();
                        });
                        observer.observe(targetVideo, {
                            attributes: true,
                            attributeFilter: ['style', 'class']
                        });

                        enforceMirror();
                        window.__stwfxMirrorGuard = {
                            disconnect: () => observer.disconnect()
                        };
                    };

                    if (activeCount === 0) {
                        clearMirrorGuard();
                        video.style.setProperty('filter', 'none', 'important');
                        video.style.setProperty('transform', 'none', 'important');
                        video.style.setProperty('transform-origin', 'center center', 'important');
                        video.style.setProperty('image-rendering', 'auto', 'important');
                        video.style.setProperty('animation', 'none', 'important');

                        const existingOverlay = document.getElementById('stwfx-vhs');
                        if (existingOverlay) {
                            existingOverlay.remove();
                        }
                        return;
                    }

                    const filters = [];
                    if (flags[0]) filters.push('grayscale(1)');
                    if (flags[1]) filters.push('sepia(1)');
                    if (flags[2]) filters.push('invert(1)');
                    if (flags[3]) {
                        const contrast = settings.Contrast >= 0
                            ? 1 + settings.Contrast * 4.0
                            : 1 + settings.Contrast * 0.75;
                        filters.push(`contrast(${Math.max(0.2, contrast).toFixed(2)})`);
                    }
                    if (flags[4]) {
                        const brightness = 1 - settings.Darkness * 0.9;
                        filters.push(`brightness(${Math.max(0.1, brightness).toFixed(2)})`);
                    }
                    if (flags[5]) {
                        const saturation = settings.Saturation >= 0
                            ? 1 + settings.Saturation * 5.0
                            : 1 + settings.Saturation * 0.95;
                        filters.push(`saturate(${Math.max(0.05, saturation).toFixed(2)})`);
                    }
                    if (flags[6]) filters.push(`hue-rotate(${Math.round(settings.HueShift * 260)}deg)`);
                    if (flags[7]) filters.push(`blur(${(0.6 + settings.Blur * 9.4).toFixed(1)}px)`);
                    if (flags[8]) filters.push(`drop-shadow(0 0 ${(10 + settings.RedGlow * 48).toFixed(1)}px rgba(255,35,35,${(0.35 + settings.RedGlow * 0.65).toFixed(2)}))`);
                    if (flags[12]) filters.push(`contrast(${(1.05 + settings.Pixelation * 1.40).toFixed(2)})`);
                    if (flags[13]) {
                        const toneHue = settings.ColdTone >= 0
                            ? 170 + settings.ColdTone * 170
                            : settings.ColdTone * 120;
                        const toneSat = 1 + Math.abs(settings.ColdTone) * 1.6;
                        filters.push(`hue-rotate(${Math.round(toneHue)}deg) saturate(${toneSat.toFixed(2)})`);
                    }
                    if (flags[14]) filters.push(`drop-shadow(0 0 ${(12 + settings.VioletGlow * 52).toFixed(1)}px rgba(186,85,255,${(0.35 + settings.VioletGlow * 0.65).toFixed(2)}))`);

                    video.style.setProperty('filter', filters.length > 0 ? filters.join(' ') : 'none', 'important');
                    const mirrorScaleX = flags[11] ? -1 : 1;
                    const mirrorEnabled = mirrorScaleX === -1;
                    const shakeEnabled = flags[10];
                    video.style.setProperty('image-rendering', flags[12] ? 'pixelated' : 'auto', 'important');
                    video.style.setProperty('transform-origin', 'center center', 'important');

                    const shakeDuration = Math.max(0.03, 0.20 - settings.Shake * 0.17);
                    const shakeAmp = 1 + settings.Shake * 10.0;

                    let style = document.getElementById('stwfx-style');
                    if (!style) {
                        style = document.createElement('style');
                        style.id = 'stwfx-style';
                        document.head.appendChild(style);
                    }
                    const mirrorTransform = mirrorScaleX === -1 ? ' scaleX(-1)' : '';
                    style.textContent = `
                        @keyframes stw_shake {
                            0% { transform: translate(0,0)${mirrorTransform}; }
                            25% { transform: translate(${shakeAmp.toFixed(2)}px, ${(-shakeAmp).toFixed(2)}px)${mirrorTransform}; }
                            50% { transform: translate(${(-shakeAmp).toFixed(2)}px, ${shakeAmp.toFixed(2)}px)${mirrorTransform}; }
                            75% { transform: translate(${shakeAmp.toFixed(2)}px, ${shakeAmp.toFixed(2)}px)${mirrorTransform}; }
                            100% { transform: translate(0,0)${mirrorTransform}; }
                        }
                    `;

                    if (mirrorEnabled && !shakeEnabled) {
                        video.style.setProperty('transform', 'scaleX(-1)', 'important');
                        installMirrorGuard(video);
                    } else {
                        clearMirrorGuard();
                        video.style.setProperty('transform', mirrorEnabled ? 'scaleX(-1)' : 'none', 'important');
                    }

                    video.style.setProperty('animation', 'none', 'important');
                    void video.offsetWidth;
                    video.style.setProperty(
                        'animation',
                        shakeEnabled ? `stw_shake ${shakeDuration.toFixed(2)}s infinite linear` : 'none',
                        'important');

                    let overlay = document.getElementById('stwfx-vhs');
                    if (!overlay) {
                        overlay = document.createElement('div');
                        overlay.id = 'stwfx-vhs';
                        overlay.style.position = 'fixed';
                        overlay.style.left = '0';
                        overlay.style.top = '0';
                        overlay.style.width = '100%';
                        overlay.style.height = '100%';
                        overlay.style.pointerEvents = 'none';
                        overlay.style.mixBlendMode = 'screen';
                        overlay.style.backgroundImage =
                            'repeating-linear-gradient(0deg, rgba(255,255,255,0.26) 0px, rgba(255,255,255,0.26) 1px, rgba(0,0,0,0) 2px, rgba(0,0,0,0) 4px), ' +
                            'repeating-linear-gradient(180deg, rgba(15,15,15,0.18) 0px, rgba(15,15,15,0.18) 2px, rgba(0,0,0,0) 3px, rgba(0,0,0,0) 5px)';
                        overlay.style.zIndex = '2147483647';
                        overlay.style.display = 'none';
                        document.body.appendChild(overlay);
                    }
                    overlay.style.display = flags[9] ? 'block' : 'none';
                    overlay.style.opacity = (0.30 + settings.Vhs * 0.70).toFixed(2);
                })();
                """;

            await Player.CoreWebView2.ExecuteScriptAsync(script);
            _lastAppliedEffectsSignature = settingsJson;
        }

        private async void ResetEffectsButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var effect in new[]
                     {
                         Fx1, Fx2, Fx3, Fx4, Fx5,
                         Fx6, Fx7, Fx8, Fx9, Fx10,
                         Fx11, Fx12, Fx13, Fx14, Fx15
                     })
            {
                effect.IsChecked = false;
            }

            ContrastStrengthSlider.Value = 0;
            DarknessStrengthSlider.Value = 0;
            SaturationStrengthSlider.Value = 0;
            HueShiftStrengthSlider.Value = 0;
            BlurStrengthSlider.Value = 0;
            RedGlowStrengthSlider.Value = 0;
            VhsStrengthSlider.Value = 0;
            ShakeStrengthSlider.Value = 0;
            PixelationStrengthSlider.Value = 0.45;
            ColdToneStrengthSlider.Value = 0;
            VioletGlowStrengthSlider.Value = 0;
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: false);

            await ApplyEffectsSafelyAsync(force: true);
        }

        private void UpdateStrengthSlidersEnabledState()
        {
            ContrastStrengthSlider.IsEnabled = Fx4.IsChecked == true;
            DarknessStrengthSlider.IsEnabled = Fx5.IsChecked == true;
            SaturationStrengthSlider.IsEnabled = Fx6.IsChecked == true;
            HueShiftStrengthSlider.IsEnabled = Fx7.IsChecked == true;
            BlurStrengthSlider.IsEnabled = Fx8.IsChecked == true;
            RedGlowStrengthSlider.IsEnabled = Fx9.IsChecked == true;
            VhsStrengthSlider.IsEnabled = Fx10.IsChecked == true;
            ShakeStrengthSlider.IsEnabled = Fx11.IsChecked == true;
            PixelationStrengthSlider.IsEnabled = Fx13.IsChecked == true;
            ColdToneStrengthSlider.IsEnabled = Fx14.IsChecked == true;
            VioletGlowStrengthSlider.IsEnabled = Fx15.IsChecked == true;
        }

        private void UpdateEffectDetailsVisibility(bool animate)
        {
            foreach (var (toggle, details) in _effectDetails)
            {
                AnimateDetailsPanel(details, toggle.IsChecked == true, animate);
            }
        }

        private static void AnimateDetailsPanel(FrameworkElement details, bool expand, bool animate)
        {
            const double expandedHeight = 44;
            details.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            details.BeginAnimation(UIElement.OpacityProperty, null);

            if (!animate)
            {
                details.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
                details.MaxHeight = expand ? expandedHeight : 0;
                details.Opacity = expand ? 1 : 0;
                return;
            }

            var duration = TimeSpan.FromMilliseconds(180);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            if (expand)
            {
                details.Visibility = Visibility.Visible;
                details.BeginAnimation(
                    FrameworkElement.MaxHeightProperty,
                    new DoubleAnimation(0, expandedHeight, duration) { EasingFunction = easing });
                details.BeginAnimation(
                    UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
                return;
            }

            var collapseAnimation = new DoubleAnimation(details.ActualHeight, 0, duration) { EasingFunction = easing };
            collapseAnimation.Completed += (_, _) =>
            {
                details.Visibility = Visibility.Collapsed;
            };
            details.BeginAnimation(FrameworkElement.MaxHeightProperty, collapseAnimation);
            details.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(details.Opacity, 0, duration) { EasingFunction = easing });
        }

        private void ToggleEffectsPanelButton_Click(object sender, RoutedEventArgs e)
        {
            _effectsPanelExpanded = !_effectsPanelExpanded;
            ApplyEffectsPanelLayout();
            RequestApplyEffects(immediate: true);
            TriggerEffectsReapplyBurst();
        }

        private void ApplyEffectsPanelLayout()
        {
            if (_effectsPanelExpanded)
            {
                EffectsPanel.Visibility = Visibility.Visible;
                EffectsColumn.Width = new GridLength(420);
                ToggleEffectsPanelButton.Content = "Скрыть эффекты";
                return;
            }

            EffectsPanel.Visibility = Visibility.Collapsed;
            EffectsColumn.Width = new GridLength(0);
            ToggleEffectsPanelButton.Content = "Показать эффекты";
        }

    }
}
