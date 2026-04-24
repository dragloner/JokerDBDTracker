using System.Globalization;
using System.IO;
using System.Text.Json;
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
            Respect,
            Sad
        }

        private void PlayerWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            MarkUserInteraction();
        }

        private void PlayerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            MarkUserInteraction();
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (ShouldBypassPlayerKeyHandling())
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

            if (ShouldBypassPlayerKeyHandling())
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

            if (ShouldBypassPlayerKeyHandling())
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

            if (key == Key.M)
            {
                RequestTimecodeCapture();
                return true;
            }

            if (TryHandleCommunityDockToggleKey(key))
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

        private bool TryHandleCommunityDockToggleKey(Key key)
        {
            if (key == Key.None)
            {
                return false;
            }

            if (key == ReadConfiguredKey(_appSettings.ToggleChatBind, Key.C))
            {
                _ = ToggleCommunityDockAsync(PlayerCommunityDockKind.Chat);
                return true;
            }

            if (key == ReadConfiguredKey(_appSettings.ToggleCommentsBind, Key.V))
            {
                _ = ToggleCommunityDockAsync(PlayerCommunityDockKind.Comments);
                return true;
            }

            return false;
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

            if (key == ReadConfiguredKey(_appSettings.SadSoundBind, Key.P))
            {
                PlaySoundEffect(SoundEffectKind.Sad);
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

        private bool ShouldBypassPlayerKeyHandling()
        {
            if (_timecodePopupVisible)
            {
                return true;
            }

            if (ShouldBypassPlayerKeyHandlingBecauseTyping())
            {
                return true;
            }

            if (_video.IsTwitchStream && _isWebViewTextInputActive)
            {
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
                var spamMode = _appSettings.SoundSpamMode;

                if (_appSettings.ApplyEqToSoundEffects && Player?.CoreWebView2 is not null)
                {
                    var audioResourceUri = ResolveSoundEffectResourceUri(kind);
                    if (audioResourceUri is null)
                    {
                        ReportSoundPlaybackFailure("Sound asset not found.");
                        return;
                    }

                    var sfxParams = BuildCurrentSoundEffectAudioParams();
                    _ = PlaySoundEffectViaWebViewAsync(kind, audioResourceUri, spamMode, sfxParams);
                    NotifyWtSoundEffect(kind.ToString());
                    return;
                }

                // Fallback: MediaPlayer path (no EQ).
                if (!spamMode)
                {
                    if (StopSoundEffect(kind))
                    {
                        return;
                    }
                }
                else
                {
                    // Spam mode still should not stack the SAME effect endlessly.
                    StopSoundEffect(kind);
                    if (_activeSoundPlayers.Count >= 8)
                    {
                        return;
                    }
                }

                var uri = ResolveSoundEffectResourceUri(kind);
                if (uri is null)
                {
                    ReportSoundPlaybackFailure("Sound asset not found.");
                    return;
                }

                PlayAudioFile(kind, uri, spamMode);
                NotifyWtSoundEffect(kind.ToString());
            }
            catch (Exception ex)
            {
                ReportSoundPlaybackFailure(ex.Message);
            }
        }

        private sealed class SoundEffectAudioParams
        {
            public double EqLowDb { get; init; }
            public double EqMidDb { get; init; }
            public double EqHighDb { get; init; }
            public double VolumeBoost { get; init; }
            public double PitchSemitones { get; init; }
            public double Reverb { get; init; }
            public double Echo { get; init; }
            public double Distortion { get; init; }
            public double Wobble { get; init; }
        }

        private SoundEffectAudioParams BuildCurrentSoundEffectAudioParams()
        {
            return new SoundEffectAudioParams
            {
                EqLowDb = AudioEqLowDbSlider.Value,
                EqMidDb = AudioEqMidDbSlider.Value,
                EqHighDb = AudioEqHighDbSlider.Value,
                VolumeBoost = AudioVolumeBoostSlider.Value,
                PitchSemitones = AudioPitchSemitonesSlider.Value,
                Reverb = AudioReverbStrengthSlider.Value,
                Echo = AudioEchoStrengthSlider.Value,
                Distortion = AudioDistortionStrengthSlider.Value,
                Wobble = AudioWobbleStrengthSlider.Value
            };
        }

        private async Task SyncSoundEffectsAudioParamsAsync(SoundEffectAudioParams p)
        {
            try
            {
                if (Player?.CoreWebView2 is null || _isPlayerClosing)
                {
                    return;
                }

                string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
                var script = $$"""
                    (() => {
                        try {
                            window.__jdbdSfx = window.__jdbdSfx || { playing: {}, count: 0, currentParams: null };
                            const sfx = window.__jdbdSfx;
                            sfx.currentParams = {
                                EqLowDb: {{F(p.EqLowDb)}},
                                EqMidDb: {{F(p.EqMidDb)}},
                                EqHighDb: {{F(p.EqHighDb)}},
                                VolumeBoost: {{F(p.VolumeBoost)}},
                                PitchSemitones: {{F(p.PitchSemitones)}},
                                Reverb: {{F(p.Reverb)}},
                                Echo: {{F(p.Echo)}},
                                Distortion: {{F(p.Distortion)}},
                                Wobble: {{F(p.Wobble)}}
                            };

                            for (const entry of Object.values(sfx.playing || {})) {
                                try {
                                    entry?.applyParams?.(sfx.currentParams);
                                } catch {
                                    // no-op
                                }
                            }
                        } catch {
                            // no-op
                        }
                    })();
                    """;

                await Player.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Best-effort realtime sync.
            }
        }

        private async Task PlaySoundEffectViaWebViewAsync(
            SoundEffectKind kind,
            Uri resourceUri,
            bool spamMode,
            SoundEffectAudioParams p)
        {
            try
            {
                if (Player?.CoreWebView2 is null || _isPlayerClosing)
                {
                    return;
                }

                var filePath = resourceUri.LocalPath;
                if (!_soundEffectBase64Cache.TryGetValue(filePath, out var b64))
                {
                    var bytes = await File.ReadAllBytesAsync(filePath);
                    b64 = Convert.ToBase64String(bytes);
                    _soundEffectBase64Cache[filePath] = b64;
                }

                var kindStr = kind.ToString();
                var toggleJs = spamMode ? "false" : "true";

                // All numeric parameters as InvariantCulture strings for JS injection.
                string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

                var script = $$"""
                    (async () => {
                        try {
                            window.__jdbdSfx = window.__jdbdSfx || {
                                playing: {},
                                count: 0,
                                currentParams: null,
                                bufferCache: {},
                                bufferPromises: {},
                                startGen: {},
                                decoding: {},
                                decodeCtx: null
                            };
                            const sfx = window.__jdbdSfx;
                            sfx.bufferCache = sfx.bufferCache || {};
                            sfx.bufferPromises = sfx.bufferPromises || {};
                            sfx.startGen = sfx.startGen || {};
                            sfx.decoding = sfx.decoding || {};
                            const kind = '{{kindStr}}';
                            const toggleMode = {{toggleJs}};
                            const stopEntriesForKind = () => {
                                let stoppedAny = false;
                                for (const [entryKey, entry] of Object.entries(sfx.playing || {})) {
                                    if (entry?.kind !== kind) {
                                        continue;
                                    }

                                    try { entry.source?.stop?.(); } catch {}
                                    try { entry.ctx?.close?.(); } catch {}
                                    delete sfx.playing[entryKey];
                                    sfx.count = Math.max(0, sfx.count - 1);
                                    stoppedAny = true;
                                }

                                return stoppedAny;
                            };

                            const nextGen = (sfx.startGen[kind] || 0) + 1;
                            sfx.startGen[kind] = nextGen;
                            const gen = nextGen;
                            const hadActivePlayback = stopEntriesForKind();
                            const hadDecodeInFlight = sfx.decoding[kind] === true;

                            // Toggle mode: 1st press starts, 2nd press cancels even if the sound is
                            // still decoding and hasn't started playing yet.
                            if (toggleMode && (hadActivePlayback || hadDecodeInFlight)) {
                                return;
                            }

                            if (!toggleMode && sfx.count >= 8) { return; }

                            const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
                            if (!AudioContextCtor) {
                                return;
                            }

                            try {
                            sfx.decoding[kind] = true;

                            const ensureDecodedBuffer = async () => {
                                if (sfx.bufferCache[kind]) {
                                    return sfx.bufferCache[kind];
                                }

                                if (!sfx.bufferPromises[kind]) {
                                    sfx.bufferPromises[kind] = (async () => {
                                        const decodeCtx = sfx.decodeCtx && sfx.decodeCtx.state !== 'closed'
                                            ? sfx.decodeCtx
                                            : new AudioContextCtor();
                                        sfx.decodeCtx = decodeCtx;
                                        if (decodeCtx.state === 'suspended') {
                                            await decodeCtx.resume();
                                        }

                                        const binary = atob('{{b64}}');
                                        const pcm = new Uint8Array(binary.length);
                                        for (let i = 0; i < binary.length; i++) {
                                            pcm[i] = binary.charCodeAt(i);
                                        }

                                        const copy = pcm.buffer.slice(0);
                                        const decoded = await decodeCtx.decodeAudioData(copy);
                                        sfx.bufferCache[kind] = decoded;
                                        return decoded;
                                    })().finally(() => {
                                        delete sfx.bufferPromises[kind];
                                    });
                                }

                                return await sfx.bufferPromises[kind];
                            };

                            const buf = await ensureDecodedBuffer();
                            const ctx = new AudioContextCtor();
                            if (ctx.state === 'suspended') { await ctx.resume(); }

                            if (sfx.startGen[kind] !== gen) {
                                try { await ctx.close(); } catch {}
                                return;
                            }

                            const src = ctx.createBufferSource();
                            src.buffer = buf;

                            const buildDistortionCurve = (strength) => {
                                const clamped = Math.max(0, Math.min(2, Number(strength) || 0));
                                if (clamped <= 0.001) {
                                    return null;
                                }

                                const samples = 2048;
                                const curve = new Float32Array(samples);
                                const amount = 12 + clamped * 700;
                                const deg = Math.PI / 180;
                                for (let i = 0; i < samples; i++) {
                                    const x = (i * 2) / (samples - 1) - 1;
                                    curve[i] = ((3 + amount) * x * 20 * deg) / (Math.PI + amount * Math.abs(x));
                                }

                                return curve;
                            };

                            const buildReverbImpulse = (strength) => {
                                const clamped = Math.max(0, Math.min(2, Number(strength) || 0));
                                if (clamped <= 0.001) {
                                    return null;
                                }

                                const sr = ctx.sampleRate || 48000;
                                const seconds = 0.45 + clamped * 4.0;
                                const decay = 1.8 + clamped * 5.0;
                                const len = Math.max(1, Math.floor(sr * seconds));
                                const impulse = ctx.createBuffer(2, len, sr);
                                for (let ch = 0; ch < impulse.numberOfChannels; ch++) {
                                    const data = impulse.getChannelData(ch);
                                    for (let i = 0; i < len; i++) {
                                        const t = i / (len - 1 || 1);
                                        const stereoShape = ch === 0 ? 0.92 + (1 - t) * 0.08 : 0.85 + t * 0.15;
                                        data[i] = (Math.random() * 2 - 1) * Math.pow(1 - t, decay) * stereoShape;
                                    }
                                }

                                return impulse;
                            };

                            // Build full audio graph matching the video player chain.
                            const inputGain  = ctx.createGain();
                            const eqLow      = ctx.createBiquadFilter();
                            const eqMid      = ctx.createBiquadFilter();
                            const eqHigh     = ctx.createBiquadFilter();
                            const distNode   = ctx.createWaveShaper();
                            const dryGain    = ctx.createGain();
                            const delayNode  = ctx.createDelay(1.5);
                            const delayFb    = ctx.createGain();
                            const echoWet    = ctx.createGain();
                            const convolver  = ctx.createConvolver();
                            const reverbWet  = ctx.createGain();
                            const wobbleDelay = ctx.createDelay(0.05);
                            const wobbleLfoOsc = ctx.createOscillator();
                            const wobbleLfoGain = ctx.createGain();
                            const wobbleWet  = ctx.createGain();
                            const outGain    = ctx.createGain();

                            eqLow.type = 'lowshelf';
                            eqLow.frequency.value = 180;
                            eqMid.type = 'peaking';
                            eqMid.frequency.value = 950;
                            eqMid.Q.value = 0.9;
                            eqHigh.type = 'highshelf';
                            eqHigh.frequency.value = 3600;

                            wobbleDelay.delayTime.value = 0.008;
                            wobbleWet.gain.value = 0;
                            wobbleLfoOsc.type = 'sine';
                            wobbleLfoOsc.frequency.value = 1.8;
                            wobbleLfoGain.gain.value = 0;
                            wobbleLfoOsc.connect(wobbleLfoGain);
                            wobbleLfoGain.connect(wobbleDelay.delayTime);
                            wobbleLfoOsc.start();

                            dryGain.gain.value = 1;
                            outGain.gain.value = 1;

                            // Wire the graph: src → inputGain → EQ chain → distortion
                            src.connect(inputGain);
                            inputGain.connect(eqLow);
                            eqLow.connect(eqMid);
                            eqMid.connect(eqHigh);
                            eqHigh.connect(distNode);
                            // dry path
                            distNode.connect(dryGain);
                            dryGain.connect(outGain);
                            // echo path
                            distNode.connect(delayNode);
                            delayNode.connect(echoWet);
                            echoWet.connect(outGain);
                            delayNode.connect(delayFb);
                            delayFb.connect(delayNode);
                            // reverb path
                            distNode.connect(convolver);
                            convolver.connect(reverbWet);
                            reverbWet.connect(outGain);
                            // wobble path
                            distNode.connect(wobbleDelay);
                            wobbleDelay.connect(wobbleWet);
                            wobbleWet.connect(outGain);
                            // output
                            outGain.connect(ctx.destination);

                            const applyParams = (params) => {
                                const current = params || sfx.currentParams || {
                                    EqLowDb: {{F(p.EqLowDb)}},
                                    EqMidDb: {{F(p.EqMidDb)}},
                                    EqHighDb: {{F(p.EqHighDb)}},
                                    VolumeBoost: {{F(p.VolumeBoost)}},
                                    PitchSemitones: {{F(p.PitchSemitones)}},
                                    Reverb: {{F(p.Reverb)}},
                                    Echo: {{F(p.Echo)}},
                                    Distortion: {{F(p.Distortion)}},
                                    Wobble: {{F(p.Wobble)}}
                                };

                                const pitchSemitones = Math.max(-24, Math.min(24, Number(current.PitchSemitones) || 0));
                                src.playbackRate.value = Math.abs(pitchSemitones) > 0.05
                                    ? Math.pow(2, pitchSemitones / 12)
                                    : 1;

                                const volBoost = Math.max(0, Math.min(3, Number(current.VolumeBoost) || 0));
                                inputGain.gain.value = 1 + volBoost * 2.5;
                                eqLow.gain.value = Math.max(-30, Math.min(30, Number(current.EqLowDb) || 0));
                                eqMid.gain.value = Math.max(-30, Math.min(30, Number(current.EqMidDb) || 0));
                                eqHigh.gain.value = Math.max(-30, Math.min(30, Number(current.EqHighDb) || 0));

                                const distStrength = Math.max(0, Math.min(2, Number(current.Distortion) || 0));
                                distNode.curve = buildDistortionCurve(distStrength);
                                distNode.oversample = distStrength > 0.001 ? '2x' : 'none';

                                const echoStrength = Math.max(0, Math.min(2, Number(current.Echo) || 0));
                                delayNode.delayTime.value = 0.12 + echoStrength * 0.44;
                                delayFb.gain.value = echoStrength > 0.001 ? Math.min(0.97, 0.06 + echoStrength * 0.43) : 0;
                                echoWet.gain.value = echoStrength > 0.001 ? (0.04 + echoStrength * 0.38) : 0;

                                const reverbStrength = Math.max(0, Math.min(2, Number(current.Reverb) || 0));
                                convolver.buffer = buildReverbImpulse(reverbStrength);
                                reverbWet.gain.value = reverbStrength > 0.001 ? (0.05 + reverbStrength * 0.44) : 0;

                                const wobbleStrength = Math.max(0, Math.min(2, Number(current.Wobble) || 0));
                                const wobbleBaseDelay = wobbleStrength > 0.001 ? (0.006 + wobbleStrength * 0.003) : 0.008;
                                wobbleDelay.delayTime.value = wobbleBaseDelay;
                                wobbleWet.gain.value = wobbleStrength > 0.001 ? (0.08 + wobbleStrength * 0.18) : 0;
                                wobbleLfoOsc.frequency.value = wobbleStrength > 0.001 ? (1.2 + wobbleStrength * 2.4) : 1.8;
                                wobbleLfoGain.gain.value = wobbleStrength > 0.001 ? (0.0008 + wobbleStrength * 0.0016) : 0;
                            };

                            sfx.currentParams = sfx.currentParams || {
                                EqLowDb: {{F(p.EqLowDb)}},
                                EqMidDb: {{F(p.EqMidDb)}},
                                EqHighDb: {{F(p.EqHighDb)}},
                                VolumeBoost: {{F(p.VolumeBoost)}},
                                PitchSemitones: {{F(p.PitchSemitones)}},
                                Reverb: {{F(p.Reverb)}},
                                Echo: {{F(p.Echo)}},
                                Distortion: {{F(p.Distortion)}},
                                Wobble: {{F(p.Wobble)}}
                            };
                            applyParams(sfx.currentParams);

                            const entryKey = toggleMode
                                ? kind
                                : `${kind}:${Date.now()}:${Math.random().toString(16).slice(2)}`;
                            sfx.playing[entryKey] = { kind, ctx, source: src, applyParams };
                            sfx.count++;

                            src.start();
                            src.onended = () => {
                                try { ctx.close(); } catch {}
                                if (sfx.playing[entryKey] && sfx.playing[entryKey].ctx === ctx) {
                                    delete sfx.playing[entryKey];
                                }
                                sfx.count = Math.max(0, sfx.count - 1);
                            };
                            } finally {
                                sfx.decoding[kind] = false;
                            }
                        } catch {}
                    })();
                    """;

                await Player.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // No-op: sound playback via WebView is best-effort.
            }
        }

        private async Task PrewarmWebViewSoundEffectsAsync()
        {
            if (_soundEffectsPrewarmedForCurrentNavigation ||
                !_appSettings.ApplyEqToSoundEffects ||
                Player?.CoreWebView2 is null ||
                _isPlayerClosing)
            {
                return;
            }

            _soundEffectsPrewarmedForCurrentNavigation = true;

            try
            {
                var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kind in Enum.GetValues<SoundEffectKind>())
                {
                    var uri = ResolveSoundEffectResourceUri(kind);
                    if (uri is null)
                    {
                        continue;
                    }

                    var filePath = uri.LocalPath;
                    if (!_soundEffectBase64Cache.TryGetValue(filePath, out var b64))
                    {
                        var bytes = await File.ReadAllBytesAsync(filePath);
                        b64 = Convert.ToBase64String(bytes);
                        _soundEffectBase64Cache[filePath] = b64;
                    }

                    payload[kind.ToString()] = b64;
                }

                if (payload.Count == 0 || Player?.CoreWebView2 is null || _isPlayerClosing)
                {
                    return;
                }

                var payloadJson = JsonSerializer.Serialize(payload);
                var script = $$"""
                    (async () => {
                        try {
                            const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
                            if (!AudioContextCtor) {
                                return;
                            }

                            window.__jdbdSfx = window.__jdbdSfx || {
                                playing: {},
                                count: 0,
                                currentParams: null,
                                bufferCache: {},
                                bufferPromises: {},
                                startGen: {},
                                decoding: {},
                                decodeCtx: null
                            };

                            const sfx = window.__jdbdSfx;
                            sfx.bufferCache = sfx.bufferCache || {};
                            sfx.bufferPromises = sfx.bufferPromises || {};

                            const payload = {{payloadJson}};
                            const ensureDecodedBuffer = async (kind, b64) => {
                                if (!b64 || sfx.bufferCache[kind]) {
                                    return;
                                }

                                if (!sfx.bufferPromises[kind]) {
                                    sfx.bufferPromises[kind] = (async () => {
                                        const decodeCtx = sfx.decodeCtx && sfx.decodeCtx.state !== 'closed'
                                            ? sfx.decodeCtx
                                            : new AudioContextCtor();
                                        sfx.decodeCtx = decodeCtx;
                                        if (decodeCtx.state === 'suspended') {
                                            await decodeCtx.resume();
                                        }

                                        const binary = atob(b64);
                                        const pcm = new Uint8Array(binary.length);
                                        for (let i = 0; i < binary.length; i++) {
                                            pcm[i] = binary.charCodeAt(i);
                                        }

                                        const copy = pcm.buffer.slice(0);
                                        const decoded = await decodeCtx.decodeAudioData(copy);
                                        sfx.bufferCache[kind] = decoded;
                                        return decoded;
                                    })().finally(() => {
                                        delete sfx.bufferPromises[kind];
                                    });
                                }

                                await sfx.bufferPromises[kind];
                            };

                            for (const [kind, b64] of Object.entries(payload)) {
                                await ensureDecodedBuffer(kind, b64);
                            }
                        } catch {
                            // no-op
                        }
                    })();
                    """;

                await Player.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogInfo("SoundPlayback", $"Prewarm failed: {ex.Message}");
                _soundEffectsPrewarmedForCurrentNavigation = false;
            }
        }

        private Uri? ResolveSoundEffectResourceUri(SoundEffectKind kind)
        {
            var fileName = kind switch
            {
                SoundEffectKind.AuraFarm => "aura.mp3",
                SoundEffectKind.Laugh => "laugh.mp3",
                SoundEffectKind.PsiRadiation => "psi.mp3",
                SoundEffectKind.Respect => "donmafia.mp3",
                SoundEffectKind.Sad => "sadness.mp3",
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

        private void PlayAudioFile(SoundEffectKind kind, Uri resourceUri, bool spamMode = false)
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

        private async void PlayerSfxSpamToggle_Changed(object sender, System.Windows.RoutedEventArgs e)
        {
            _appSettings.SoundSpamMode = PlayerSfxSpamToggle?.IsChecked == true;
            try
            {
                await _settingsService.SaveAsync(_appSettings);
            }
            catch
            {
                // Best-effort save.
            }
        }

        private void StopAllSoundEffects()
        {
            StopAllWebViewInjectedSoundEffects();

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

        private void StopAllWebViewInjectedSoundEffects()
        {
            try
            {
                if (Player?.CoreWebView2 is null)
                {
                    return;
                }

                _ = Player.CoreWebView2.ExecuteScriptAsync(
                    """
                    (() => {
                        try {
                            const sfx = window.__jdbdSfx;
                            if (!sfx) {
                                return;
                            }
                            sfx.startGen = sfx.startGen || {};
                            sfx.decoding = sfx.decoding || {};
                            const kinds = new Set(['AuraFarm', 'Laugh', 'PsiRadiation', 'Respect', 'Sad']);
                            for (const k of Object.keys(sfx.decoding)) {
                                kinds.add(k);
                            }
                            for (const entry of Object.values(sfx.playing || {})) {
                                if (entry?.kind) {
                                    kinds.add(entry.kind);
                                }
                            }
                            for (const k of kinds) {
                                sfx.startGen[k] = (sfx.startGen[k] || 0) + 1;
                                sfx.decoding[k] = false;
                            }
                            for (const entry of Object.values(sfx.playing || {})) {
                                try { entry?.source?.stop?.(); } catch {}
                                try { entry?.ctx?.close?.(); } catch {}
                            }
                            sfx.playing = {};
                            sfx.count = 0;
                        } catch {
                            // no-op
                        }
                    })();
                    """);
            }
            catch
            {
                // WebView may already be torn down.
            }
        }

        private void UpdateDynamicBindHints()
        {
            if (BindsHintText is not null)
            {
                var hide = FormatBindLabel(_appSettings.HideEffectsPanelBind);
                var chat = FormatBindLabel(_appSettings.ToggleChatBind);
                var comments = FormatBindLabel(_appSettings.ToggleCommentsBind);
                var aura = FormatBindLabel(_appSettings.AuraFarmSoundBind);
                var laugh = FormatBindLabel(_appSettings.LaughSoundBind);
                var psi = FormatBindLabel(_appSettings.PsiSoundBind);
                var respect = FormatBindLabel(_appSettings.RespectSoundBind);
                var sad = FormatBindLabel(_appSettings.SadSoundBind);
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
                    $"FX 1-10: {fxRow1}\nFX 11-15: {fxRow2} | H:{hide} | Y:{aura} U:{laugh} I:{psi} O:{respect} P:{sad}",
                    $"FX 1-10: {fxRow1}\nFX 11-15: {fxRow2} | H:{hide} | Y:{aura} U:{laugh} I:{psi} O:{respect} P:{sad}");
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

            if (SadSoundButton is not null)
            {
                SadSoundButton.Content = PT("5. Звук Грусть", "5. Sadness Sound") +
                                         $" [{FormatBindLabel(_appSettings.SadSoundBind)}]";
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

        private void SadSoundButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            PlaySoundEffect(SoundEffectKind.Sad);
        }
    }
}
