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
        private async void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (Player.CoreWebView2 is null)
            {
                return;
            }

            try
            {
                const string script = """
                    (() => {
                        const video = document.querySelector('video');
                        if (!video || !Number.isFinite(video.currentTime) || !Number.isFinite(video.duration)) {
                            return { current: -1, duration: 0, paused: true, seeking: false, playbackRate: 1 };
                        }
                        return {
                            current: video.currentTime,
                            duration: video.duration,
                            paused: !!video.paused,
                            seeking: !!video.seeking,
                            playbackRate: Number.isFinite(video.playbackRate) ? video.playbackRate : 1
                        };
                    })();
                    """;

                var raw = await Player.CoreWebView2.ExecuteScriptAsync(script);
                using var document = JsonDocument.Parse(raw);
                var current = document.RootElement.GetProperty("current").GetDouble();
                var duration = document.RootElement.GetProperty("duration").GetDouble();
                var paused = document.RootElement.GetProperty("paused").GetBoolean();
                var seeking = document.RootElement.GetProperty("seeking").GetBoolean();
                var playbackRate = document.RootElement.GetProperty("playbackRate").GetDouble();
                if (current < 0 || duration <= 0)
                {
                    _lastXpSampleUtc = DateTime.UtcNow;
                    return;
                }

                LastPlaybackSeconds = (int)Math.Floor(current);
                if (current > 0.15)
                {
                    SetPlayerSurfaceVisible(true);
                    SetPlayerLoadingOverlay(visible: false);
                }

                var nowUtc = DateTime.UtcNow;
                if (!_lastXpSampleUtc.HasValue)
                {
                    _lastXpSampleUtc = nowUtc;
                    _lastMeasuredTime = current;
                    if (_isResizeInteractionInProgress)
                    {
                        RequestApplyEffects(immediate: false);
                    }
                    else
                    {
                        await ApplyEffectsSafelyAsync();
                    }
                    UpdateCursedAchievementState(current, duration);
                    return;
                }

                var elapsedWallSeconds = (nowUtc - _lastXpSampleUtc.Value).TotalSeconds;
                _lastXpSampleUtc = nowUtc;

                var playbackDelta = _lastMeasuredTime >= 0 ? current - _lastMeasuredTime : 0;
                var positivePlaybackRate = Math.Clamp(playbackRate, 0.1, 4.0);
                var maxExpectedDelta = Math.Max(0.7, elapsedWallSeconds * positivePlaybackRate * 1.75 + 0.7);
                var progressionLooksNatural = playbackDelta >= -0.35 && playbackDelta <= maxExpectedDelta;
                var canCreditXp = !paused &&
                                  !seeking &&
                                  elapsedWallSeconds > 0 &&
                                  elapsedWallSeconds <= 8 &&
                                  playbackDelta > 0.05 &&
                                  progressionLooksNatural;

                if (canCreditXp)
                {
                    var creditedSeconds = Math.Min(elapsedWallSeconds, _positionTimer.Interval.TotalSeconds + 1.0);
                    var effectsMultiplier = 1.0 + GetActiveEffectsCount() * 0.05;
                    var activeViewerMultiplier = IsActiveViewer(nowUtc) ? 1.08 : 1.0;
                    _watchXpBuffer += creditedSeconds * 1.05 * effectsMultiplier * activeViewerMultiplier;
                    _eligibleWatchSeconds += creditedSeconds;
                    EligibleWatchSeconds = (int)Math.Floor(_eligibleWatchSeconds);
                    UpdateEffectSessionStats();
                    TryApplyLongWatchBonuses();
                }

                _lastMeasuredTime = current;
                WatchXpEarned = (int)Math.Floor(_watchXpBuffer);

                var activeEffects = GetActiveEffectsCount();
                var shakeEnabled = Fx11.IsChecked == true;
                if (!_isResizeInteractionInProgress && activeEffects > 0 && !shakeEnabled)
                {
                    _effectRefreshCounter++;
                    if (_effectRefreshCounter >= EffectRefreshTickInterval)
                    {
                        _effectRefreshCounter = 0;
                        await ApplyEffectsSafelyAsync();
                    }
                }
                else
                {
                    _effectRefreshCounter = 0;
                }

                _ = PersistPlaybackPositionAsync(force: false);
                UpdateCursedAchievementState(current, duration);
            }
            catch
            {
                // Ignore transient script errors.
            }
        }

        private void UpdateCursedAchievementState(double current, double duration)
        {
            if (CursedMasterUnlocked || duration <= 0)
            {
                return;
            }

            var fullyWatched = current >= duration * 0.98;
            if (fullyWatched && GetActiveEffectsCount() == 15)
            {
                CursedMasterUnlocked = true;
            }
        }


        private void TryApplyLongWatchBonuses()
        {
            if (!_halfHourBonusGranted && _eligibleWatchSeconds >= 30 * 60)
            {
                _watchXpBuffer += HalfHourWatchBonusXp;
                _halfHourBonusGranted = true;
            }

            if (!_hourBonusGranted && _eligibleWatchSeconds >= 60 * 60)
            {
                _watchXpBuffer += OneHourWatchBonusXp;
                _hourBonusGranted = true;
            }

            if (!_ninetyMinuteBonusGranted && _eligibleWatchSeconds >= 90 * 60)
            {
                _watchXpBuffer += NinetyMinutesWatchBonusXp;
                _ninetyMinuteBonusGranted = true;
            }
        }

        private bool IsActiveViewer(DateTime nowUtc)
        {
            return IsActive && (nowUtc - _lastUserInteractionUtc).TotalSeconds <= 20;
        }

        private async Task PersistPlaybackPositionAsync(bool force)
        {
            if (_isPersistingPlayback)
            {
                return;
            }

            var playbackSeconds = Math.Max(0, LastPlaybackSeconds);
            if (!force &&
                playbackSeconds == _lastPersistedPlaybackSeconds &&
                (DateTime.UtcNow - _lastPlaybackPersistUtc).TotalSeconds < PlaybackPersistIntervalSeconds)
            {
                return;
            }

            _isPersistingPlayback = true;
            try
            {
                await PlaybackPersistLock.WaitAsync();
                try
                {
                    var history = await _watchHistoryPersistService.LoadAsync();
                    history.LastPlaybackSecondsByVideoId[_video.VideoId] = playbackSeconds;
                    await _watchHistoryPersistService.SaveAsync(history);
                    _lastPersistedPlaybackSeconds = playbackSeconds;
                    _lastPlaybackPersistUtc = DateTime.UtcNow;
                }
                finally
                {
                    PlaybackPersistLock.Release();
                }
            }
            catch
            {
                // Best-effort persistence.
            }
            finally
            {
                _isPersistingPlayback = false;
            }
        }


    }
}
