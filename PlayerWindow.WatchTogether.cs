using System.Text.Json;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        private readonly WatchTogetherService? _watchTogetherService;
        private bool _isSyncingFromRemote;
        private DateTime _lastSyncSendUtc = DateTime.MinValue;
        private DateTime _lastEffectsSyncSendUtc = DateTime.MinValue;
        private string _lastSentEffectsJson = string.Empty;
        private const double SyncSendIntervalSeconds = 1.0;
        private const double EffectsSyncSendIntervalSeconds = 0.3;
        private const double SyncSeekThresholdSeconds = 2.0;

        private void InitializeWatchTogetherSync()
        {
            if (_watchTogetherService is null)
            {
                return;
            }

            _watchTogetherService.MessageReceived += WtSync_MessageReceived;
        }

        private void CleanupWatchTogetherSync()
        {
            if (_watchTogetherService is null)
            {
                return;
            }

            _watchTogetherService.MessageReceived -= WtSync_MessageReceived;
        }

        private void WtSync_MessageReceived(WatchTogetherMessage message)
        {
            if (_isPlayerClosing)
            {
                return;
            }

            switch (message.Type)
            {
                case "play":
                    _ = Dispatcher.InvokeAsync(() => HandleRemotePlay(message.Position));
                    break;
                case "pause":
                    _ = Dispatcher.InvokeAsync(() => HandleRemotePause(message.Position));
                    break;
                case "seek":
                    _ = Dispatcher.InvokeAsync(() => HandleRemoteSeek(message.Position));
                    break;
                case "sync_state":
                    _ = Dispatcher.InvokeAsync(() => HandleRemoteSyncState(message));
                    break;
                case "effects":
                    _ = Dispatcher.InvokeAsync(() => HandleRemoteEffects(message));
                    break;
                case "sound_effect":
                    _ = Dispatcher.InvokeAsync(() => HandleRemoteSoundEffect(message));
                    break;
            }
        }

        private async void HandleRemotePlay(double? position)
        {
            if (!CanProcessPlayerCommands())
            {
                return;
            }

            _isSyncingFromRemote = true;
            try
            {
                var seekPart = position.HasValue
                    ? $"video.currentTime = {position.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};"
                    : string.Empty;

                var script = $$"""
                    (() => {
                        try {
                            const video = document.querySelector('video');
                            if (!video) return false;
                            {{seekPart}}
                            const p = video.play?.();
                            if (p && typeof p.catch === 'function') p.catch(() => {});
                            return true;
                        } catch { return false; }
                    })();
                    """;
                await ExecuteWebScriptWithTimeoutAsync(script, timeoutMs: 800, operation: "WtSync.Play");
            }
            finally
            {
                _isSyncingFromRemote = false;
            }
        }

        private async void HandleRemotePause(double? position)
        {
            if (!CanProcessPlayerCommands())
            {
                return;
            }

            _isSyncingFromRemote = true;
            try
            {
                var seekPart = position.HasValue
                    ? $"video.currentTime = {position.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};"
                    : string.Empty;

                var script = $$"""
                    (() => {
                        try {
                            const video = document.querySelector('video');
                            if (!video) return false;
                            {{seekPart}}
                            video.pause?.();
                            return true;
                        } catch { return false; }
                    })();
                    """;
                await ExecuteWebScriptWithTimeoutAsync(script, timeoutMs: 800, operation: "WtSync.Pause");
            }
            finally
            {
                _isSyncingFromRemote = false;
            }
        }

        private async void HandleRemoteSeek(double? position)
        {
            if (!CanProcessPlayerCommands() || !position.HasValue)
            {
                return;
            }

            _isSyncingFromRemote = true;
            try
            {
                var script = $$"""
                    (() => {
                        try {
                            const video = document.querySelector('video');
                            if (!video) return false;
                            video.currentTime = {{position.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}};
                            return true;
                        } catch { return false; }
                    })();
                    """;
                await ExecuteWebScriptWithTimeoutAsync(script, timeoutMs: 800, operation: "WtSync.Seek");
            }
            finally
            {
                _isSyncingFromRemote = false;
            }
        }

        private async void HandleRemoteSyncState(WatchTogetherMessage message)
        {
            if (!CanProcessPlayerCommands() || !message.Position.HasValue)
            {
                return;
            }

            var currentPos = await GetCurrentPlaybackPositionAsync();
            if (currentPos < 0)
            {
                return;
            }

            var diff = Math.Abs(currentPos - message.Position.Value);
            var remotePaused = string.Equals(message.Text, "paused", StringComparison.Ordinal);
            var posStr = message.Position.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

            // Sync both position (if drifted) and play/pause state.
            _isSyncingFromRemote = true;
            try
            {
                var seekPart = diff > SyncSeekThresholdSeconds
                    ? $"video.currentTime = {posStr};"
                    : string.Empty;

                var statePart = remotePaused
                    ? "if (!video.paused) video.pause();"
                    : "if (video.paused) { const p = video.play?.(); if (p && typeof p.catch === 'function') p.catch(() => {}); }";

                if (string.IsNullOrEmpty(seekPart) && string.IsNullOrEmpty(statePart))
                {
                    return;
                }

                var script = $$"""
                    (() => {
                        try {
                            const video = document.querySelector('video');
                            if (!video) return false;
                            {{seekPart}}
                            {{statePart}}
                            return true;
                        } catch { return false; }
                    })();
                    """;
                await ExecuteWebScriptWithTimeoutAsync(script, timeoutMs: 800, operation: "WtSync.SyncState");
            }
            finally
            {
                _isSyncingFromRemote = false;
            }
        }

        private async Task<double> GetCurrentPlaybackPositionAsync()
        {
            if (!CanProcessPlayerCommands())
            {
                return -1;
            }

            try
            {
                var result = await ExecuteWebScriptWithTimeoutAsync(
                    "(() => { const v = document.querySelector('video'); return v && Number.isFinite(v.currentTime) ? v.currentTime : -1; })()",
                    timeoutMs: 500,
                    operation: "WtSync.GetPosition");
                if (result is not null && double.TryParse(result, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pos))
                {
                    return pos;
                }
            }
            catch
            {
                // Ignore.
            }

            return -1;
        }

        /// <summary>
        /// Called from MirrorPlaybackKeyToWebViewAsync after a play/pause/seek action.
        /// Broadcasts the action to Watch Together peers.
        /// </summary>
        private void NotifyWtPlaybackAction(string action, double? position)
        {
            if (_watchTogetherService is null || !_watchTogetherService.IsConnected || _isSyncingFromRemote)
            {
                return;
            }

            var type = action switch
            {
                "play" => "play",
                "pause" => "pause",
                "seekBackward" or "seekForward" or "seek" => "seek",
                _ => (string?)null
            };

            if (type is not null)
            {
                _watchTogetherService.Send(new WatchTogetherMessage
                {
                    Type = type,
                    Position = position
                });
            }
        }

        /// <summary>
        /// Sends periodic sync state to keep peers in sync.
        /// Called from PositionTimer_Tick. Only the host sends these.
        /// </summary>
        private void SendWtPeriodicSync(double currentPosition, bool isPaused)
        {
            if (_watchTogetherService is null || !_watchTogetherService.IsConnected || _isSyncingFromRemote)
            {
                return;
            }

            if (!_watchTogetherService.IsHost)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastSyncSendUtc).TotalSeconds < SyncSendIntervalSeconds)
            {
                return;
            }

            _lastSyncSendUtc = now;
            _watchTogetherService.Send(new WatchTogetherMessage
            {
                Type = "sync_state",
                Position = currentPosition,
                Text = isPaused ? "paused" : "playing"
            });
        }

        // ── Effects synchronization ──

        /// <summary>
        /// Called when any member changes effects (toggle or slider).
        /// Broadcasts current effects state to all peers.
        /// </summary>
        private void SendWtEffectsSync()
        {
            if (_watchTogetherService is null || !_watchTogetherService.IsConnected || _isSyncingFromRemote)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastEffectsSyncSendUtc).TotalSeconds < EffectsSyncSendIntervalSeconds)
            {
                return;
            }

            var settings = GetEffectSettings();
            var json = JsonSerializer.Serialize(settings);

            // Don't send if nothing changed.
            if (string.Equals(json, _lastSentEffectsJson, StringComparison.Ordinal))
            {
                return;
            }

            _lastEffectsSyncSendUtc = now;
            _lastSentEffectsJson = json;
            _watchTogetherService.Send(new WatchTogetherMessage
            {
                Type = "effects",
                Text = json
            });
        }

        private void HandleRemoteEffects(WatchTogetherMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Text) || !CanProcessPlayerCommands())
            {
                return;
            }

            try
            {
                var settings = JsonSerializer.Deserialize<EffectSettings>(message.Text);
                if (settings is null)
                {
                    return;
                }

                _isSyncingFromRemote = true;
                try
                {
                    ApplyEffectSettingsToControls(settings, presetKey: string.Empty, animateDetails: false);
                }
                finally
                {
                    _isSyncingFromRemote = false;
                }
            }
            catch
            {
                // Ignore malformed effects data.
            }
        }

        // ── Sound effect synchronization ──

        /// <summary>
        /// Called when any member plays a sound effect.
        /// Broadcasts the sound to all peers.
        /// </summary>
        private void NotifyWtSoundEffect(string soundKind)
        {
            if (_watchTogetherService is null || !_watchTogetherService.IsConnected || _isSyncingFromRemote)
            {
                return;
            }

            _watchTogetherService.Send(new WatchTogetherMessage
            {
                Type = "sound_effect",
                Text = soundKind
            });
        }

        private void HandleRemoteSoundEffect(WatchTogetherMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Text) || !CanProcessPlayerCommands())
            {
                return;
            }

            _isSyncingFromRemote = true;
            try
            {
                if (Enum.TryParse<SoundEffectKind>(message.Text, ignoreCase: true, out var kind))
                {
                    PlaySoundEffect(kind);
                }
            }
            finally
            {
                _isSyncingFromRemote = false;
            }
        }
    }
}
