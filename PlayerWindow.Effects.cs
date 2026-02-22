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

            if (Fx9.IsChecked == true && Math.Abs(FisheyeStrengthSlider.Value) >= 0.75)
            {
                UsedStrongRedGlow = true;
            }

            if (Fx13.IsChecked == true && JpegDamageStrengthSlider.Value >= 0.75)
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
            if (!CanProcessPlayerCommands())
            {
                _pendingEffectsApply = true;
                _pendingEffectsApplyForce |= force;
                _effectsApplyDebounceTimer.Stop();
                return;
            }

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
            if (!CanProcessPlayerCommands())
            {
                _pendingEffectsApply = true;
                _pendingEffectsApplyForce |= force;
                return;
            }

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
            static double ClampRange(Slider slider, double min, double max) => Math.Clamp(slider.Value, min, max);

            return new EffectSettings
            {
                Flags = GetEffectsState(),
                Contrast = NormalizeSigned(ContrastStrengthSlider),
                Darkness = NormalizeSigned(DarknessStrengthSlider),
                Saturation = NormalizeSigned(SaturationStrengthSlider),
                HueShift = NormalizeSigned(HueShiftStrengthSlider),
                Blur = NormalizePositive(BlurStrengthSlider),
                Fisheye = NormalizeSigned(FisheyeStrengthSlider),
                Vhs = NormalizePositive(VhsStrengthSlider),
                Shake = NormalizePositive(ShakeStrengthSlider),
                JpegDamage = NormalizePositive(JpegDamageStrengthSlider),
                ColdTone = NormalizeSigned(ColdToneStrengthSlider),
                AudioVolumeBoost = NormalizePositive(AudioVolumeBoostSlider),
                AudioPitchSemitones = ClampRange(AudioPitchSemitonesSlider, -8, 8),
                AudioReverb = NormalizePositive(AudioReverbStrengthSlider),
                AudioEcho = NormalizePositive(AudioEchoStrengthSlider),
                AudioDistortion = NormalizePositive(AudioDistortionStrengthSlider),
                AudioEqLowDb = ClampRange(AudioEqLowDbSlider, -18, 18),
                AudioEqMidDb = ClampRange(AudioEqMidDbSlider, -18, 18),
                AudioEqHighDb = ClampRange(AudioEqHighDbSlider, -18, 18)
            };
        }

        private async Task ApplyEffectsAsync(bool forceApply)
        {
            if (!CanProcessPlayerCommands())
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
                    const flags = Array.isArray(settings.Flags) ? settings.Flags : [];
                    settings.Flags = flags;
                    const pickMainVideo = () => {
                        const pinnedRuntimeVideo = window.__stwfxRuntime?.video;
                        if (pinnedRuntimeVideo instanceof HTMLVideoElement && pinnedRuntimeVideo.isConnected) {
                            return pinnedRuntimeVideo;
                        }

                        const nodes = Array.from(document.querySelectorAll('video'))
                            .filter((node) => node instanceof HTMLVideoElement);
                        if (nodes.length === 0) {
                            return null;
                        }

                        const scoreVideo = (node) => {
                            const rect = node.getBoundingClientRect();
                            const style = window.getComputedStyle(node);
                            const area = Math.max(0, rect.width) * Math.max(0, rect.height);
                            if (style.display === 'none' || style.visibility === 'hidden' || area <= 0) {
                                return Number.NEGATIVE_INFINITY;
                            }

                            let score = area;
                            const opacity = Number.parseFloat(style.opacity || '1');
                            if (!Number.isFinite(opacity) || opacity < 0.01) {
                                return Number.NEGATIVE_INFINITY;
                            }

                            if (rect.width < 120 || rect.height < 68) {
                                score -= 1_000_000_000;
                            }

                            if (node.classList.contains('html5-main-video')) {
                                score += 1_000_000_000_000;
                            }
                            if (node.classList.contains('video-stream')) {
                                score += 100_000_000_000;
                            }
                            if (node.closest('.html5-video-player')) {
                                score += 10_000_000_000;
                            }

                            const badAncestor = node.closest(
                                '[id*="cinematic" i], [class*="cinematic" i], [id*="ambient" i], [class*="ambient" i], [id*="blur" i], [class*="blur" i]');
                            if (badAncestor) {
                                score -= 2_000_000_000_000;
                            }

                            const filterText = (style.filter || '').toLowerCase();
                            if (filterText.includes('blur(')) {
                                score -= 500_000_000_000;
                            }

                            if (opacity < 0.95) {
                                score -= 100_000_000_000;
                            }

                            if (node.readyState >= 2) {
                                score += 50_000_000;
                            }

                            return score;
                        };

                        let best = null;
                        let bestScore = Number.NEGATIVE_INFINITY;
                        for (const node of nodes) {
                            const score = scoreVideo(node);
                            if (score > bestScore) {
                                best = node;
                                bestScore = score;
                            }
                        }

                        return best || nodes[0] || null;
                    };

                    const video = pickMainVideo();
                    if (!video) {
                        return;
                    }

                    const clamp01 = (value) => Math.max(0, Math.min(1, Number(value) || 0));
                    const clamp11 = (value) => Math.max(-1, Math.min(1, Number(value) || 0));
                    const removeNode = (id) => {
                        const node = document.getElementById(id);
                        if (node) {
                            node.remove();
                        }
                    };

                    const ensureOverlay = (id, setup) => {
                        let node = document.getElementById(id);
                        if (!node) {
                            node = document.createElement('div');
                            node.id = id;
                            setup(node);
                            (document.body || document.documentElement).appendChild(node);
                        }
                        return node;
                    };

                    const getRuntime = () => {
                        if (window.__stwfxRuntime && window.__stwfxRuntime.video === video) {
                            return window.__stwfxRuntime;
                        }

                        if (window.__stwfxRuntime && typeof window.__stwfxRuntime.destroy === 'function') {
                            try {
                                window.__stwfxRuntime.destroy();
                            } catch {
                                // no-op
                            }
                        }

                        const runtime = {
                            video,
                            settings: null,
                            rafId: 0,
                            running: false,
                            shakeX: 0,
                            shakeY: 0,
                            postFxRafId: 0,
                            postFxRunning: false,
                            fisheyeEnabled: false,
                            jpegDamageEnabled: false,
                            jpegStrength: 0,
                            jpegCanvas: null,
                            jpegCtx: null,
                            jpegBufferCanvas: null,
                            jpegBufferCtx: null,
                            jpegScratchCanvas: null,
                            jpegScratchCtx: null,
                            fisheyeDistortCanvas: null,
                            fisheyeDistortCtx: null,
                            fisheyeMap: null,
                            fisheyeMapWidth: 0,
                            fisheyeMapHeight: 0,
                            fisheyeMapStrength: 999,
                            fisheyeImageData: null,
                            fisheyeStrength: 0,
                            postFxBaseFilter: 'none',
                            audioUnsupported: false,
                            audioCtx: null,
                            audioSource: null,
                            audioInputGain: null,
                            audioEqLow: null,
                            audioEqMid: null,
                            audioEqHigh: null,
                            audioDistortionNode: null,
                            audioDryGain: null,
                            audioDelayNode: null,
                            audioDelayFeedbackGain: null,
                            audioEchoWetGain: null,
                            audioReverbConvolver: null,
                            audioReverbWetGain: null,
                            audioOutputGain: null,
                            audioLastPitchFactor: 1,
                            audioLastReverbKey: '',
                            audioLastDistortionKey: '',
                            stopShake() {
                                if (this.rafId) {
                                    cancelAnimationFrame(this.rafId);
                                    this.rafId = 0;
                                }

                                this.running = false;
                                this.shakeX = 0;
                                this.shakeY = 0;
                            },
                            applyTransform() {
                                if (!this.settings) {
                                    return;
                                }

                                const fxFlags = this.settings.Flags;
                                const shakeEnabled = fxFlags[10] === true;
                                const horizontalMirrorEnabled = fxFlags[11] === true;
                                const verticalMirrorEnabled = fxFlags[14] === true;
                                this.video.style.setProperty('transform-origin', 'center center', 'important');
                                this.video.style.setProperty('image-rendering', 'auto', 'important');
                                this.video.style.setProperty('animation', 'none', 'important');
                                this.video.style.setProperty('will-change', 'transform, filter', 'important');

                                const tx = shakeEnabled ? this.shakeX : 0;
                                const ty = shakeEnabled ? this.shakeY : 0;
                                const scaleX = horizontalMirrorEnabled ? -1 : 1;
                                const scaleY = verticalMirrorEnabled ? -1 : 1;
                                const transform = `translate3d(${tx.toFixed(2)}px, ${ty.toFixed(2)}px, 0) scale(${scaleX}, ${scaleY})`;
                                this.video.style.setProperty('transform', transform, 'important');
                            },
                            setPostFxBaseFilter(value) {
                                this.postFxBaseFilter = typeof value === 'string' && value.length > 0
                                    ? value
                                    : 'none';
                            },
                            applyPitchPlaybackRate(semitones) {
                                const clampedSemitones = Math.max(-12, Math.min(12, Number(semitones) || 0));
                                const targetFactor = Math.pow(2, clampedSemitones / 12);
                                const currentRate = Number.isFinite(this.video.playbackRate) ? this.video.playbackRate : 1;
                                const previousFactor = Number.isFinite(this.audioLastPitchFactor) && this.audioLastPitchFactor > 0.001
                                    ? this.audioLastPitchFactor
                                    : 1;
                                const estimatedBaseRate = Math.max(0.1, Math.min(4.0, currentRate / previousFactor));
                                const targetRate = Math.max(0.1, Math.min(4.0, estimatedBaseRate * targetFactor));
                                if (Math.abs(currentRate - targetRate) > 0.001) {
                                    try {
                                        this.video.playbackRate = targetRate;
                                    } catch {
                                        // Some players may temporarily reject playbackRate updates.
                                    }
                                }

                                const preservePitch = Math.abs(clampedSemitones) < 0.05;
                                try {
                                    if ('preservesPitch' in this.video) {
                                        this.video.preservesPitch = preservePitch;
                                    }
                                    if ('webkitPreservesPitch' in this.video) {
                                        this.video.webkitPreservesPitch = preservePitch;
                                    }
                                    if ('mozPreservesPitch' in this.video) {
                                        this.video.mozPreservesPitch = preservePitch;
                                    }
                                } catch {
                                    // Ignore unsupported pitch-preservation properties.
                                }

                                this.audioLastPitchFactor = targetFactor;
                            },
                            buildDistortionCurve(strength) {
                                const normalized = clamp01(strength);
                                if (normalized <= 0.001) {
                                    return null;
                                }

                                const samples = 2048;
                                const curve = new Float32Array(samples);
                                const amount = 12 + normalized * 420;
                                const deg = Math.PI / 180;
                                for (let i = 0; i < samples; i++) {
                                    const x = (i * 2) / (samples - 1) - 1;
                                    curve[i] = ((3 + amount) * x * 20 * deg) / (Math.PI + amount * Math.abs(x));
                                }

                                return curve;
                            },
                            buildReverbImpulse(strength) {
                                if (!this.audioCtx) {
                                    return null;
                                }

                                const normalized = clamp01(strength);
                                if (normalized <= 0.001) {
                                    return null;
                                }

                                const roundedKey = String(Math.round(normalized * 28));
                                if (this.audioLastReverbKey === roundedKey && this.audioReverbConvolver?.buffer) {
                                    return this.audioReverbConvolver.buffer;
                                }

                                const sampleRate = this.audioCtx.sampleRate || 48000;
                                const seconds = 0.45 + normalized * 2.6;
                                const decay = 1.8 + normalized * 6.4;
                                const length = Math.max(1, Math.floor(sampleRate * seconds));
                                const impulse = this.audioCtx.createBuffer(2, length, sampleRate);

                                for (let channel = 0; channel < impulse.numberOfChannels; channel++) {
                                    const data = impulse.getChannelData(channel);
                                    for (let i = 0; i < length; i++) {
                                        const t = i / (length - 1 || 1);
                                        const noise = Math.random() * 2 - 1;
                                        const damping = Math.pow(1 - t, decay);
                                        const stereoShape = channel === 0
                                            ? 0.92 + (1 - t) * 0.08
                                            : 0.85 + t * 0.15;
                                        data[i] = noise * damping * stereoShape;
                                    }
                                }

                                this.audioLastReverbKey = roundedKey;
                                return impulse;
                            },
                            ensureAudioGraph() {
                                if (this.audioUnsupported) {
                                    return false;
                                }

                                if (this.audioCtx &&
                                    this.audioSource &&
                                    this.audioInputGain &&
                                    this.audioEqLow &&
                                    this.audioEqMid &&
                                    this.audioEqHigh &&
                                    this.audioDistortionNode &&
                                    this.audioDryGain &&
                                    this.audioDelayNode &&
                                    this.audioDelayFeedbackGain &&
                                    this.audioEchoWetGain &&
                                    this.audioReverbConvolver &&
                                    this.audioReverbWetGain &&
                                    this.audioOutputGain)
                                {
                                    return true;
                                }

                                const resetAudioFields = () => {
                                    this.audioCtx = null;
                                    this.audioSource = null;
                                    this.audioInputGain = null;
                                    this.audioEqLow = null;
                                    this.audioEqMid = null;
                                    this.audioEqHigh = null;
                                    this.audioDistortionNode = null;
                                    this.audioDryGain = null;
                                    this.audioDelayNode = null;
                                    this.audioDelayFeedbackGain = null;
                                    this.audioEchoWetGain = null;
                                    this.audioReverbConvolver = null;
                                    this.audioReverbWetGain = null;
                                    this.audioOutputGain = null;
                                };

                                try {
                                    const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
                                    if (!AudioContextCtor) {
                                        this.audioUnsupported = true;
                                        return false;
                                    }

                                    const ctx = new AudioContextCtor();
                                    const source = ctx.createMediaElementSource(this.video);
                                    const inputGain = ctx.createGain();
                                    const eqLow = ctx.createBiquadFilter();
                                    const eqMid = ctx.createBiquadFilter();
                                    const eqHigh = ctx.createBiquadFilter();
                                    const distortion = ctx.createWaveShaper();
                                    const dryGain = ctx.createGain();
                                    const delayNode = ctx.createDelay(1.5);
                                    const delayFeedback = ctx.createGain();
                                    const echoWetGain = ctx.createGain();
                                    const convolver = ctx.createConvolver();
                                    const reverbWetGain = ctx.createGain();
                                    const outputGain = ctx.createGain();

                                    eqLow.type = 'lowshelf';
                                    eqLow.frequency.value = 180;
                                    eqMid.type = 'peaking';
                                    eqMid.frequency.value = 950;
                                    eqMid.Q.value = 0.9;
                                    eqHigh.type = 'highshelf';
                                    eqHigh.frequency.value = 3600;

                                    distortion.curve = null;
                                    distortion.oversample = 'none';
                                    delayNode.delayTime.value = 0.22;
                                    delayFeedback.gain.value = 0;
                                    echoWetGain.gain.value = 0;
                                    reverbWetGain.gain.value = 0;
                                    dryGain.gain.value = 1;
                                    outputGain.gain.value = 1;
                                    inputGain.gain.value = 1;

                                    source.connect(inputGain);
                                    inputGain.connect(eqLow);
                                    eqLow.connect(eqMid);
                                    eqMid.connect(eqHigh);
                                    eqHigh.connect(distortion);

                                    distortion.connect(dryGain);
                                    dryGain.connect(outputGain);

                                    distortion.connect(delayNode);
                                    delayNode.connect(echoWetGain);
                                    echoWetGain.connect(outputGain);
                                    delayNode.connect(delayFeedback);
                                    delayFeedback.connect(delayNode);

                                    distortion.connect(convolver);
                                    convolver.connect(reverbWetGain);
                                    reverbWetGain.connect(outputGain);

                                    outputGain.connect(ctx.destination);

                                    this.audioCtx = ctx;
                                    this.audioSource = source;
                                    this.audioInputGain = inputGain;
                                    this.audioEqLow = eqLow;
                                    this.audioEqMid = eqMid;
                                    this.audioEqHigh = eqHigh;
                                    this.audioDistortionNode = distortion;
                                    this.audioDryGain = dryGain;
                                    this.audioDelayNode = delayNode;
                                    this.audioDelayFeedbackGain = delayFeedback;
                                    this.audioEchoWetGain = echoWetGain;
                                    this.audioReverbConvolver = convolver;
                                    this.audioReverbWetGain = reverbWetGain;
                                    this.audioOutputGain = outputGain;
                                    this.audioLastReverbKey = '';
                                    this.audioLastDistortionKey = '';
                                    return true;
                                } catch {
                                    try {
                                        this.audioCtx?.close?.();
                                    } catch {
                                        // no-op
                                    }

                                    resetAudioFields();
                                    return false;
                                }
                            },
                            resetAudioNodeEffects() {
                                if (this.audioInputGain) {
                                    this.audioInputGain.gain.value = 1;
                                }
                                if (this.audioEqLow) {
                                    this.audioEqLow.gain.value = 0;
                                }
                                if (this.audioEqMid) {
                                    this.audioEqMid.gain.value = 0;
                                }
                                if (this.audioEqHigh) {
                                    this.audioEqHigh.gain.value = 0;
                                }
                                if (this.audioDistortionNode) {
                                    this.audioDistortionNode.curve = null;
                                    this.audioDistortionNode.oversample = 'none';
                                }
                                if (this.audioDelayNode) {
                                    this.audioDelayNode.delayTime.value = 0.22;
                                }
                                if (this.audioDelayFeedbackGain) {
                                    this.audioDelayFeedbackGain.gain.value = 0;
                                }
                                if (this.audioEchoWetGain) {
                                    this.audioEchoWetGain.gain.value = 0;
                                }
                                if (this.audioReverbConvolver) {
                                    this.audioReverbConvolver.buffer = null;
                                }
                                if (this.audioReverbWetGain) {
                                    this.audioReverbWetGain.gain.value = 0;
                                }
                                if (this.audioOutputGain) {
                                    this.audioOutputGain.gain.value = 1;
                                }

                                this.audioLastReverbKey = '';
                                this.audioLastDistortionKey = '';
                            },
                            resetAudioEffects() {
                                this.applyPitchPlaybackRate(0);
                                this.resetAudioNodeEffects();
                            },
                            applyAudioEffects() {
                                if (!this.settings) {
                                    return;
                                }

                                const volumeBoost = clamp01(this.settings.AudioVolumeBoost);
                                const pitchSemitones = Math.max(-12, Math.min(12, Number(this.settings.AudioPitchSemitones) || 0));
                                const reverb = clamp01(this.settings.AudioReverb);
                                const echo = clamp01(this.settings.AudioEcho);
                                const distortion = clamp01(this.settings.AudioDistortion);
                                const eqLowDb = Math.max(-18, Math.min(18, Number(this.settings.AudioEqLowDb) || 0));
                                const eqMidDb = Math.max(-18, Math.min(18, Number(this.settings.AudioEqMidDb) || 0));
                                const eqHighDb = Math.max(-18, Math.min(18, Number(this.settings.AudioEqHighDb) || 0));

                                this.applyPitchPlaybackRate(pitchSemitones);

                                const needsNodeEffects = volumeBoost > 0.001 ||
                                    reverb > 0.001 ||
                                    echo > 0.001 ||
                                    distortion > 0.001 ||
                                    Math.abs(eqLowDb) > 0.01 ||
                                    Math.abs(eqMidDb) > 0.01 ||
                                    Math.abs(eqHighDb) > 0.01;

                                if (!needsNodeEffects) {
                                    this.resetAudioNodeEffects();
                                    return;
                                }

                                if (!this.ensureAudioGraph()) {
                                    return;
                                }

                                try {
                                    this.audioCtx?.resume?.();
                                } catch {
                                    // no-op
                                }

                                if (this.audioInputGain) {
                                    this.audioInputGain.gain.value = 1 + volumeBoost * 2.2;
                                }
                                if (this.audioEqLow) {
                                    this.audioEqLow.gain.value = eqLowDb;
                                }
                                if (this.audioEqMid) {
                                    this.audioEqMid.gain.value = eqMidDb;
                                }
                                if (this.audioEqHigh) {
                                    this.audioEqHigh.gain.value = eqHighDb;
                                }
                                if (this.audioDistortionNode) {
                                    const distortionKey = String(Math.round(distortion * 1000));
                                    if (this.audioLastDistortionKey !== distortionKey) {
                                        this.audioDistortionNode.curve = this.buildDistortionCurve(distortion);
                                        this.audioDistortionNode.oversample = distortion > 0.001 ? '2x' : 'none';
                                        this.audioLastDistortionKey = distortionKey;
                                    }
                                }
                                if (this.audioDelayNode) {
                                    this.audioDelayNode.delayTime.value = 0.12 + echo * 0.34;
                                }
                                if (this.audioDelayFeedbackGain) {
                                    this.audioDelayFeedbackGain.gain.value = echo > 0.001 ? (0.06 + echo * 0.50) : 0;
                                }
                                if (this.audioEchoWetGain) {
                                    this.audioEchoWetGain.gain.value = echo > 0.001 ? (0.04 + echo * 0.30) : 0;
                                }
                                if (this.audioReverbConvolver && this.audioReverbWetGain) {
                                    if (reverb > 0.001) {
                                        this.audioReverbConvolver.buffer = this.buildReverbImpulse(reverb);
                                        this.audioReverbWetGain.gain.value = 0.05 + reverb * 0.46;
                                    } else {
                                        this.audioReverbConvolver.buffer = null;
                                        this.audioReverbWetGain.gain.value = 0;
                                        this.audioLastReverbKey = '';
                                    }
                                }
                                if (this.audioOutputGain) {
                                    this.audioOutputGain.gain.value = 1;
                                }
                            },
                            closeAudioGraph() {
                                const disconnectNode = (node) => {
                                    if (!node || typeof node.disconnect !== 'function') {
                                        return;
                                    }

                                    try {
                                        node.disconnect();
                                    } catch {
                                        // Ignore disconnect races during navigation.
                                    }
                                };

                                disconnectNode(this.audioSource);
                                disconnectNode(this.audioInputGain);
                                disconnectNode(this.audioEqLow);
                                disconnectNode(this.audioEqMid);
                                disconnectNode(this.audioEqHigh);
                                disconnectNode(this.audioDistortionNode);
                                disconnectNode(this.audioDryGain);
                                disconnectNode(this.audioDelayNode);
                                disconnectNode(this.audioDelayFeedbackGain);
                                disconnectNode(this.audioEchoWetGain);
                                disconnectNode(this.audioReverbConvolver);
                                disconnectNode(this.audioReverbWetGain);
                                disconnectNode(this.audioOutputGain);

                                if (this.audioCtx && typeof this.audioCtx.close === 'function') {
                                    try {
                                        this.audioCtx.close();
                                    } catch {
                                        // no-op
                                    }
                                }

                                this.audioCtx = null;
                                this.audioSource = null;
                                this.audioInputGain = null;
                                this.audioEqLow = null;
                                this.audioEqMid = null;
                                this.audioEqHigh = null;
                                this.audioDistortionNode = null;
                                this.audioDryGain = null;
                                this.audioDelayNode = null;
                                this.audioDelayFeedbackGain = null;
                                this.audioEchoWetGain = null;
                                this.audioReverbConvolver = null;
                                this.audioReverbWetGain = null;
                                this.audioOutputGain = null;
                                this.audioLastReverbKey = '';
                                this.audioLastDistortionKey = '';
                                this.audioLastPitchFactor = 1;
                            },
                            applyJpegArtifacts(baseSource, targetWidth, targetHeight, strength) {
                                if (!this.jpegCtx || !this.jpegScratchCtx) {
                                    return;
                                }

                                const s = clamp01(strength);
                                if (s <= 0.0001) {
                                    return;
                                }

                                // Stronger macro-pixelation baseline, with extra push near the top of the range.
                                const pixelBoost = s > 0.70 ? (s - 0.70) * 7.5 : 0;
                                const sampleFactor = 1.7 + s * 7.4 + pixelBoost;
                                const sampleWidth = Math.max(8, Math.round(targetWidth / sampleFactor));
                                const sampleHeight = Math.max(8, Math.round(targetHeight / sampleFactor));
                                if (!this.jpegBufferCanvas || this.jpegBufferCanvas.width !== sampleWidth || this.jpegBufferCanvas.height !== sampleHeight) {
                                    this.jpegBufferCanvas = document.createElement('canvas');
                                    this.jpegBufferCanvas.width = sampleWidth;
                                    this.jpegBufferCanvas.height = sampleHeight;
                                    this.jpegBufferCtx = this.jpegBufferCanvas.getContext('2d', { alpha: true, desynchronized: true });
                                }

                                if (!this.jpegBufferCtx) {
                                    return;
                                }

                                this.jpegBufferCtx.clearRect(0, 0, sampleWidth, sampleHeight);
                                this.jpegBufferCtx.imageSmoothingEnabled = true;
                                this.jpegBufferCtx.drawImage(baseSource, 0, 0, sampleWidth, sampleHeight);
                                this.jpegCtx.clearRect(0, 0, targetWidth, targetHeight);
                                this.jpegCtx.imageSmoothingEnabled = false;
                                this.jpegCtx.drawImage(this.jpegBufferCanvas, 0, 0, targetWidth, targetHeight);

                                // Stable snapshot for glitch passes; do not recursively sample already-mutated output.
                                this.jpegScratchCtx.clearRect(0, 0, targetWidth, targetHeight);
                                this.jpegScratchCtx.imageSmoothingEnabled = false;
                                this.jpegScratchCtx.drawImage(this.jpegCanvas, 0, 0, targetWidth, targetHeight);

                                const maxBlockSize = Math.max(2, Math.round(3 + s * 20));
                                const blockPasses = Math.round(24 + s * 240);
                                for (let i = 0; i < blockPasses; i++) {
                                    const w = 1 + Math.floor(Math.random() * maxBlockSize);
                                    const h = 1 + Math.floor(Math.random() * maxBlockSize);
                                    const x = Math.floor(Math.random() * Math.max(1, targetWidth - w + 1));
                                    const y = Math.floor(Math.random() * Math.max(1, targetHeight - h + 1));
                                    const sx = Math.max(0, Math.min(targetWidth - w, x + Math.round((Math.random() - 0.5) * (6 + s * 70))));
                                    const sy = Math.max(0, Math.min(targetHeight - h, y + Math.round((Math.random() - 0.5) * (4 + s * 40))));
                                    this.jpegCtx.globalAlpha = 0.15 + Math.random() * (0.18 + s * 0.35);
                                    this.jpegCtx.drawImage(this.jpegScratchCanvas, sx, sy, w, h, x, y, w, h);
                                    this.jpegCtx.globalAlpha = 1;
                                }

                                const stripPasses = Math.round(4 + s * 26);
                                for (let i = 0; i < stripPasses; i++) {
                                    const horizontal = Math.random() < 0.8;
                                    if (horizontal) {
                                        const h = Math.max(1, Math.round(1 + Math.random() * (2 + s * 12)));
                                        const y = Math.floor(Math.random() * Math.max(1, targetHeight - h + 1));
                                        const shift = Math.round((Math.random() - 0.5) * (6 + s * 120));
                                        const sx = Math.max(0, shift);
                                        const tx = Math.max(0, -shift);
                                        const sw = targetWidth - Math.abs(shift);
                                        if (sw > 1) {
                                            this.jpegCtx.globalAlpha = 0.22 + Math.random() * (0.24 + s * 0.34);
                                            this.jpegCtx.drawImage(this.jpegScratchCanvas, sx, y, sw, h, tx, y, sw, h);
                                            this.jpegCtx.globalAlpha = 1;
                                        }
                                    }
                                    else
                                    {
                                        const w = Math.max(1, Math.round(1 + Math.random() * (1 + s * 5)));
                                        const x = Math.floor(Math.random() * Math.max(1, targetWidth - w + 1));
                                        const shift = Math.round((Math.random() - 0.5) * (4 + s * 60));
                                        const sy = Math.max(0, shift);
                                        const ty = Math.max(0, -shift);
                                        const sh = targetHeight - Math.abs(shift);
                                        if (sh > 1) {
                                            this.jpegCtx.globalAlpha = 0.10 + Math.random() * (0.14 + s * 0.22);
                                            this.jpegCtx.drawImage(this.jpegScratchCanvas, x, sy, w, sh, x, ty, w, sh);
                                            this.jpegCtx.globalAlpha = 1;
                                        }
                                    }
                                }

                                const dotPasses = Math.round(targetWidth * targetHeight * (0.0008 + s * 0.0075));
                                for (let i = 0; i < dotPasses; i++) {
                                    const x = Math.floor(Math.random() * targetWidth);
                                    const y = Math.floor(Math.random() * targetHeight);
                                    const alpha = (0.05 + Math.random() * (0.10 + s * 0.26)).toFixed(3);
                                    if (Math.random() < 0.15 + s * 0.35) {
                                        this.jpegCtx.fillStyle = `rgba(${Math.floor(Math.random() * 255)},${Math.floor(Math.random() * 255)},${Math.floor(Math.random() * 255)},${alpha})`;
                                    }
                                    else
                                    {
                                        const m = Math.random() < 0.5 ? 255 : 0;
                                        this.jpegCtx.fillStyle = `rgba(${m},${m},${m},${alpha})`;
                                    }

                                    this.jpegCtx.fillRect(x, y, 1, 1);
                                }

                                // Full-frame flicker only at the extreme end so mid/high values stay usable.
                                if (s > 0.95) {
                                    const overdrive = (s - 0.95) / 0.05;
                                    const flickerChance = 0.18 + overdrive * 0.70;
                                    if (Math.random() < flickerChance) {
                                        this.jpegCtx.globalAlpha = 0.10 + overdrive * (0.18 + Math.random() * 0.32);
                                        this.jpegCtx.fillStyle = Math.random() < 0.5 ? 'rgba(0,0,0,1)' : 'rgba(255,255,255,1)';
                                        this.jpegCtx.fillRect(0, 0, targetWidth, targetHeight);
                                    }
                                }

                                this.jpegCtx.globalAlpha = 1;
                                this.jpegCtx.imageSmoothingEnabled = false;
                            },
                            applyFisheyeDistortion(sourceCanvas, targetWidth, targetHeight, strength) {
                                if (!sourceCanvas) {
                                    return sourceCanvas;
                                }

                                const signed = clamp11(strength);
                                const absStrength = Math.abs(signed);
                                if (absStrength <= 0.01) {
                                    return sourceCanvas;
                                }

                                if (!this.fisheyeDistortCanvas ||
                                    this.fisheyeDistortCanvas.width !== targetWidth ||
                                    this.fisheyeDistortCanvas.height !== targetHeight)
                                {
                                    this.fisheyeDistortCanvas = document.createElement('canvas');
                                    this.fisheyeDistortCanvas.width = targetWidth;
                                    this.fisheyeDistortCanvas.height = targetHeight;
                                    this.fisheyeDistortCtx = this.fisheyeDistortCanvas.getContext('2d', { alpha: true, desynchronized: true });
                                    this.fisheyeMap = null;
                                    this.fisheyeMapWidth = 0;
                                    this.fisheyeMapHeight = 0;
                                    this.fisheyeMapStrength = 999;
                                }

                                if (!this.fisheyeDistortCtx) {
                                    return sourceCanvas;
                                }

                                try {
                                    const ctx = this.fisheyeDistortCtx;
                                    const cx = targetWidth * 0.5;
                                    const cy = targetHeight * 0.5;
                                    const maxNormRadius = Math.SQRT2; // corners of normalized rect [-1..1]
                                    const tile = Math.max(6, Math.round(18 - absStrength * 10));

                                    // Full-frame warp. Start from original frame to hide any tiny seams.
                                    ctx.clearRect(0, 0, targetWidth, targetHeight);
                                    ctx.imageSmoothingEnabled = true;
                                    ctx.drawImage(sourceCanvas, 0, 0, targetWidth, targetHeight);

                                    const mapRadius = (rn) => {
                                        const t = Math.max(0, Math.min(1, rn));
                                        if (signed >= 0) {
                                            // Barrel / fisheye lens: magnify center, compress outer area.
                                            const p = 1 + absStrength * 1.25;
                                            return Math.pow(t, p);
                                        }

                                        // Pinch (reverse fisheye).
                                        const p = 1 + absStrength * 1.25;
                                        return 1 - Math.pow(1 - t, p);
                                    };

                                    for (let y = 0; y < targetHeight; y += tile) {
                                        const h = Math.min(tile, targetHeight - y);
                                        for (let x = 0; x < targetWidth; x += tile) {
                                            const w = Math.min(tile, targetWidth - x);
                                            const nx = ((x + w * 0.5) - cx) / Math.max(1, cx);
                                            const ny = ((y + h * 0.5) - cy) / Math.max(1, cy);
                                            const rUnit = Math.hypot(nx, ny);
                                            const rn = Math.max(0, Math.min(1, rUnit / maxNormRadius));
                                            if (rUnit < 1e-6) {
                                                continue;
                                            }

                                            const srcRn = mapRadius(rn);
                                            const scaleNorm = (srcRn * maxNormRadius) / rUnit;
                                            const srcNx = nx * scaleNorm;
                                            const srcNy = ny * scaleNorm;
                                            const sxCenter = cx + srcNx * cx;
                                            const syCenter = cy + srcNy * cy;

                                            // Approximate local derivative for smoother lens look.
                                            const eps = 1 / Math.max(targetWidth, targetHeight);
                                            const rn2 = Math.max(0, Math.min(1, rn + eps));
                                            const srcRn2 = mapRadius(rn2);
                                            const deriv = Math.max(0.25, Math.min(3.2, (srcRn2 - srcRn) / Math.max(eps, rn2 - rn)));
                                            const localScale = Math.max(0.28, Math.min(3.0, deriv));
                                            const srcW = Math.max(1, w * localScale);
                                            const srcH = Math.max(1, h * localScale);
                                            const sx = Math.max(0, Math.min(targetWidth - srcW, sxCenter - srcW * 0.5));
                                            const sy = Math.max(0, Math.min(targetHeight - srcH, syCenter - srcH * 0.5));

                                            // Slight overlap to hide tile seams.
                                            const expand = 0.9;
                                            const dxDraw = Math.max(0, x - expand);
                                            const dyDraw = Math.max(0, y - expand);
                                            const dwDraw = Math.min(targetWidth - dxDraw, w + expand * 2);
                                            const dhDraw = Math.min(targetHeight - dyDraw, h + expand * 2);

                                            ctx.drawImage(
                                                sourceCanvas,
                                                sx,
                                                sy,
                                                srcW,
                                                srcH,
                                                dxDraw,
                                                dyDraw,
                                                dwDraw,
                                                dhDraw);
                                        }
                                    }

                                    return this.fisheyeDistortCanvas;
                                } catch {
                                    return sourceCanvas;
                                }
                            },
                            ensureJpegOverlay() {
                                if (this.jpegCanvas && this.jpegCtx && this.jpegCanvas.isConnected) {
                                    return this.jpegCanvas;
                                }

                                if (this.jpegCanvas && !this.jpegCanvas.isConnected) {
                                    this.jpegCanvas = null;
                                    this.jpegCtx = null;
                                }

                                let canvas = document.getElementById('stwfx-jpeg');
                                if (canvas && !(canvas instanceof HTMLCanvasElement)) {
                                    canvas.remove();
                                    canvas = null;
                                }

                                if (!canvas) {
                                    canvas = document.createElement('canvas');
                                    canvas.id = 'stwfx-jpeg';
                                    canvas.style.position = 'fixed';
                                    canvas.style.left = '0';
                                    canvas.style.top = '0';
                                    canvas.style.width = '1px';
                                    canvas.style.height = '1px';
                                    canvas.style.pointerEvents = 'none';
                                    canvas.style.mixBlendMode = 'normal';
                                    canvas.style.zIndex = '2147483645';
                                    canvas.style.display = 'none';
                                    (document.body || document.documentElement).appendChild(canvas);
                                }

                                if (!(canvas instanceof HTMLCanvasElement)) {
                                    return null;
                                }

                                this.jpegCanvas = canvas;
                                this.jpegCtx = canvas.getContext('2d', { alpha: true, desynchronized: true, willReadFrequently: true });
                                return this.jpegCanvas;
                            },
                            isPostFxActive() {
                                return (this.jpegDamageEnabled && clamp01(this.jpegStrength) > 0.0001) ||
                                    (this.fisheyeEnabled && Math.abs(clamp11(this.fisheyeStrength)) > 0.01);
                            },
                            stopPostFxLoop(clearOverlay) {
                                if (this.postFxRafId) {
                                    cancelAnimationFrame(this.postFxRafId);
                                    this.postFxRafId = 0;
                                }

                                this.postFxRunning = false;
                                if (clearOverlay && this.jpegCanvas) {
                                    this.jpegCanvas.style.removeProperty('transform');
                                    this.jpegCanvas.style.removeProperty('transform-origin');
                                    this.jpegCanvas.style.display = 'none';
                                    if (this.jpegCtx) {
                                        this.jpegCtx.clearRect(0, 0, this.jpegCanvas.width, this.jpegCanvas.height);
                                    }
                                }
                            },
                            ensurePostFxLoop() {
                                if (!this.isPostFxActive()) {
                                    this.stopPostFxLoop(true);
                                    return;
                                }

                                if (!this.ensureJpegOverlay()) {
                                    return;
                                }

                                this.renderJpegDamageFrame();
                                if (this.postFxRunning) {
                                    return;
                                }

                                this.postFxRunning = true;
                                this.postFxRafId = requestAnimationFrame(this.postFxTick);
                            },
                            stopFisheye() {
                                this.fisheyeEnabled = false;
                                this.fisheyeStrength = 0;
                                this.ensurePostFxLoop();
                            },
                            startFisheye(strength) {
                                const fisheyeNormalized = clamp11(strength);
                                this.fisheyeStrength = fisheyeNormalized;
                                this.fisheyeEnabled = Math.abs(fisheyeNormalized) > 0.01;
                                this.ensurePostFxLoop();
                            },
                            stopJpegDamage() {
                                this.jpegDamageEnabled = false;
                                this.jpegStrength = 0;
                                this.ensurePostFxLoop();
                            },
                            renderJpegDamageFrame() {
                                if (!this.jpegCanvas || !this.jpegCtx) {
                                    return;
                                }

                                if (!this.isPostFxActive()) {
                                    this.stopPostFxLoop(true);
                                    return;
                                }

                                const rect = this.video.getBoundingClientRect();
                                if (!Number.isFinite(rect.width) || !Number.isFinite(rect.height) || rect.width < 2 || rect.height < 2) {
                                    this.stopPostFxLoop(true);
                                    return;
                                }

                                const strength = this.jpegDamageEnabled ? clamp01(this.jpegStrength) : 0;
                                const fisheye = this.fisheyeEnabled ? clamp11(this.fisheyeStrength) : 0;
                                const fisheyeAbs = Math.abs(fisheye);

                                try {
                                    this.jpegCanvas.style.display = 'block';
                                    this.jpegCanvas.style.left = `${rect.left.toFixed(3)}px`;
                                    this.jpegCanvas.style.top = `${rect.top.toFixed(3)}px`;
                                    this.jpegCanvas.style.width = `${rect.width.toFixed(3)}px`;
                                    this.jpegCanvas.style.height = `${rect.height.toFixed(3)}px`;
                                    const fxFlags = Array.isArray(this.settings?.Flags) ? this.settings.Flags : [];
                                    const overlayScaleX = fxFlags[11] === true ? -1 : 1;
                                    const overlayScaleY = fxFlags[14] === true ? -1 : 1;
                                    this.jpegCanvas.style.transformOrigin = 'center center';
                                    this.jpegCanvas.style.transform = `scale(${overlayScaleX}, ${overlayScaleY})`;

                                    const maxSide = strength > 0.0001 ? 1280 : 1024;
                                    const renderScale = Math.min(1, maxSide / Math.max(rect.width, rect.height));
                                    const targetWidth = Math.max(2, Math.round(rect.width * renderScale));
                                    const targetHeight = Math.max(2, Math.round(rect.height * renderScale));
                                    if (this.jpegCanvas.width !== targetWidth || this.jpegCanvas.height !== targetHeight) {
                                        this.jpegCanvas.width = targetWidth;
                                        this.jpegCanvas.height = targetHeight;
                                    }

                                    if (!this.jpegScratchCanvas || this.jpegScratchCanvas.width !== targetWidth || this.jpegScratchCanvas.height !== targetHeight) {
                                        this.jpegScratchCanvas = document.createElement('canvas');
                                        this.jpegScratchCanvas.width = targetWidth;
                                        this.jpegScratchCanvas.height = targetHeight;
                                        this.jpegScratchCtx = this.jpegScratchCanvas.getContext('2d', { alpha: true, desynchronized: true, willReadFrequently: true });
                                    }

                                    if (!this.jpegScratchCtx) {
                                        this.stopPostFxLoop(true);
                                        return;
                                    }

                                    // Capture current video frame and emulate CSS filter stack in-canvas.
                                    this.jpegScratchCtx.clearRect(0, 0, targetWidth, targetHeight);
                                    this.jpegScratchCtx.filter = this.postFxBaseFilter;
                                    this.jpegScratchCtx.imageSmoothingEnabled = true;
                                    this.jpegScratchCtx.drawImage(this.video, 0, 0, targetWidth, targetHeight);
                                    this.jpegScratchCtx.filter = 'none';

                                    let sourceForDamage = this.jpegScratchCanvas;
                                    if (fisheyeAbs > 0.01) {
                                        sourceForDamage = this.applyFisheyeDistortion(this.jpegScratchCanvas, targetWidth, targetHeight, fisheye);
                                    }

                                    this.jpegCanvas.style.mixBlendMode = 'normal';
                                    this.jpegCanvas.style.opacity = '1';
                                    this.jpegCtx.clearRect(0, 0, targetWidth, targetHeight);
                                    this.jpegCtx.imageSmoothingEnabled = true;
                                    this.jpegCtx.drawImage(sourceForDamage, 0, 0, targetWidth, targetHeight);

                                    if (strength > 0.0001) {
                                        this.applyJpegArtifacts(sourceForDamage, targetWidth, targetHeight, strength);
                                    }

                                    // Keep native video visible; overlay is fully opaque and this avoids source video reselection bugs.
                                } catch {
                                    this.jpegCanvas.style.display = 'none';
                                }
                            },
                            postFxTick: () => {
                                const state = window.__stwfxRuntime;
                                if (!state || !state.postFxRunning) {
                                    return;
                                }

                                state.renderJpegDamageFrame();
                                if (!state.postFxRunning) {
                                    return;
                                }
                                state.postFxRafId = requestAnimationFrame(state.postFxTick);
                            },
                            startJpegDamage(strength, fisheyeStrength) {
                                const normalized = clamp01(strength);
                                const fisheyeNormalized = clamp11(fisheyeStrength);
                                this.jpegDamageEnabled = normalized > 0.0001;
                                this.jpegStrength = normalized;
                                this.fisheyeStrength = fisheyeNormalized;
                                this.fisheyeEnabled = Math.abs(fisheyeNormalized) > 0.01;
                                this.ensurePostFxLoop();
                            },
                            tick: () => {
                                const runtimeState = window.__stwfxRuntime;
                                if (!runtimeState || !runtimeState.running || !runtimeState.settings) {
                                    return;
                                }

                                const shakeStrength = clamp01(runtimeState.settings.Shake);
                                const amplitude = 1.5 + shakeStrength * 20.0;
                                const smoothing = 0.38 + shakeStrength * 0.32;
                                const targetX = (Math.random() * 2 - 1) * amplitude;
                                const targetY = (Math.random() * 2 - 1) * amplitude;
                                runtimeState.shakeX += (targetX - runtimeState.shakeX) * smoothing;
                                runtimeState.shakeY += (targetY - runtimeState.shakeY) * smoothing;
                                runtimeState.applyTransform();
                                runtimeState.rafId = requestAnimationFrame(runtimeState.tick);
                            },
                            startShake() {
                                if (this.running) {
                                    return;
                                }

                                this.running = true;
                                this.tick();
                            },
                            destroy() {
                                this.resetAudioEffects();
                                this.closeAudioGraph();
                                this.stopShake();
                                this.stopFisheye();
                                this.stopJpegDamage();
                                removeNode('stwfx-vhs');
                                removeNode('stwfx-jpeg');
                            }
                        };

                        window.__stwfxRuntime = runtime;
                        return runtime;
                    };

                    const runtime = getRuntime();
                    runtime.settings = settings;
                    runtime.applyAudioEffects();
                    const activeCount = flags.reduce((acc, value) => acc + (value ? 1 : 0), 0);
                    const audioFxActive =
                        clamp01(settings.AudioVolumeBoost) > 0.001 ||
                        Math.abs(Math.max(-12, Math.min(12, Number(settings.AudioPitchSemitones) || 0))) > 0.05 ||
                        clamp01(settings.AudioReverb) > 0.001 ||
                        clamp01(settings.AudioEcho) > 0.001 ||
                        clamp01(settings.AudioDistortion) > 0.001 ||
                        Math.abs(Math.max(-18, Math.min(18, Number(settings.AudioEqLowDb) || 0))) > 0.01 ||
                        Math.abs(Math.max(-18, Math.min(18, Number(settings.AudioEqMidDb) || 0))) > 0.01 ||
                        Math.abs(Math.max(-18, Math.min(18, Number(settings.AudioEqHighDb) || 0))) > 0.01;

                    if (activeCount === 0 && !audioFxActive) {
                        runtime.stopShake();
                        runtime.resetAudioEffects();
                        runtime.stopFisheye();
                        runtime.stopJpegDamage();
                        video.style.setProperty('filter', 'none', 'important');
                        video.style.setProperty('transform', 'none', 'important');
                        video.style.setProperty('transform-origin', 'center center', 'important');
                        video.style.setProperty('image-rendering', 'auto', 'important');
                        video.style.setProperty('animation', 'none', 'important');
                        video.style.setProperty('will-change', 'auto', 'important');
                        removeNode('stwfx-vhs');
                        return;
                    }

                    if (activeCount === 0) {
                        runtime.stopShake();
                        runtime.stopFisheye();
                        runtime.stopJpegDamage();
                        video.style.setProperty('filter', 'none', 'important');
                        video.style.setProperty('transform', 'none', 'important');
                        video.style.setProperty('transform-origin', 'center center', 'important');
                        video.style.setProperty('image-rendering', 'auto', 'important');
                        video.style.setProperty('animation', 'none', 'important');
                        video.style.setProperty('will-change', 'auto', 'important');
                        removeNode('stwfx-vhs');
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
                    const fisheyeAmount = flags[8] ? clamp11(settings.Fisheye) : 0;
                    const jpegStrength = flags[12] ? clamp01(settings.JpegDamage) : 0;
                    if (flags[13]) {
                        const toneHue = settings.ColdTone >= 0
                            ? 170 + settings.ColdTone * 170
                            : settings.ColdTone * 120;
                        const toneSat = 1 + Math.abs(settings.ColdTone) * 1.6;
                        filters.push(`hue-rotate(${Math.round(toneHue)}deg) saturate(${toneSat.toFixed(2)})`);
                    }

                    runtime.setPostFxBaseFilter(filters.join(' '));

                    video.style.setProperty('filter', filters.length > 0 ? filters.join(' ') : 'none', 'important');
                    runtime.applyTransform();

                    if (flags[10]) {
                        runtime.startShake();
                    } else {
                        runtime.stopShake();
                        runtime.applyTransform();
                    }

                    if (Math.abs(fisheyeAmount) > 0.01 || jpegStrength > 0.0001) {
                        runtime.startJpegDamage(jpegStrength, fisheyeAmount);
                    } else {
                        runtime.stopFisheye();
                        runtime.stopJpegDamage();
                    }

                    const vhsOverlay = ensureOverlay('stwfx-vhs', (overlay) => {
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
                    });
                    vhsOverlay.style.display = flags[9] ? 'block' : 'none';
                    vhsOverlay.style.opacity = (0.30 + clamp01(settings.Vhs) * 0.70).toFixed(2);
                })();
                """;

            var applyResult = await ExecuteWebScriptWithTimeoutAsync(
                script,
                timeoutMs: 1800,
                operation: "ApplyEffectsAsync");
            if (applyResult is null)
            {
                return;
            }

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
            FisheyeStrengthSlider.Value = 0;
            VhsStrengthSlider.Value = 0;
            ShakeStrengthSlider.Value = 0;
            JpegDamageStrengthSlider.Value = 0.4;
            ColdToneStrengthSlider.Value = 0;
            AudioVolumeBoostSlider.Value = 0;
            AudioPitchSemitonesSlider.Value = 0;
            AudioReverbStrengthSlider.Value = 0;
            AudioEchoStrengthSlider.Value = 0;
            AudioDistortionStrengthSlider.Value = 0;
            AudioEqLowDbSlider.Value = 0;
            AudioEqMidDbSlider.Value = 0;
            AudioEqHighDbSlider.Value = 0;
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: false);

            await ApplyEffectsSafelyAsync(force: true);
        }

        private void ResetAudioFxButton_Click(object sender, RoutedEventArgs e)
        {
            AudioVolumeBoostSlider.Value = 0;
            AudioPitchSemitonesSlider.Value = 0;
            AudioReverbStrengthSlider.Value = 0;
            AudioEchoStrengthSlider.Value = 0;
            AudioDistortionStrengthSlider.Value = 0;
            AudioEqLowDbSlider.Value = 0;
            AudioEqMidDbSlider.Value = 0;
            AudioEqHighDbSlider.Value = 0;
            RequestApplyEffects(immediate: true, force: true);
        }

        private void UpdateStrengthSlidersEnabledState()
        {
            ContrastStrengthSlider.IsEnabled = Fx4.IsChecked == true;
            DarknessStrengthSlider.IsEnabled = Fx5.IsChecked == true;
            SaturationStrengthSlider.IsEnabled = Fx6.IsChecked == true;
            HueShiftStrengthSlider.IsEnabled = Fx7.IsChecked == true;
            BlurStrengthSlider.IsEnabled = Fx8.IsChecked == true;
            FisheyeStrengthSlider.IsEnabled = Fx9.IsChecked == true;
            VhsStrengthSlider.IsEnabled = Fx10.IsChecked == true;
            ShakeStrengthSlider.IsEnabled = Fx11.IsChecked == true;
            JpegDamageStrengthSlider.IsEnabled = Fx13.IsChecked == true;
            ColdToneStrengthSlider.IsEnabled = Fx14.IsChecked == true;
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
            ToggleEffectsPanelState();
        }

        private void ToggleEffectsPanelState()
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
                EffectsSplitterColumn.Width = new GridLength(10);
                ToggleEffectsPanelButton.Content = PT("Скрыть эффекты", "Hide effects");
                if (_appSettings.AnimationsEnabled)
                {
                    EffectsPanel.Opacity = 0;
                    EffectsPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(240),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                return;
            }

            if (_appSettings.AnimationsEnabled)
            {
                var fade = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                fade.Completed += (_, _) =>
                {
                    EffectsPanel.Visibility = Visibility.Collapsed;
                    EffectsSplitterColumn.Width = new GridLength(0);
                    EffectsColumn.Width = new GridLength(0);
                };
                EffectsPanel.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                EffectsPanel.Visibility = Visibility.Collapsed;
                EffectsSplitterColumn.Width = new GridLength(0);
                EffectsColumn.Width = new GridLength(0);
            }

            ToggleEffectsPanelButton.Content = PT("Показать эффекты", "Show effects");
        }

    }
}

