using System.Net;
using System.Net.Http;
using System.Windows;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private bool _isCatalogLoading;
        private async Task LoadVideosAsync()
        {
            if (_isCatalogLoading || _isOpeningVideo) return;
            _isCatalogLoading = true;
            ReloadCatalogButton.IsEnabled = false;
            try { await LoadVideosCoreAsync(); }
            finally { _isCatalogLoading = false; ReloadCatalogButton.IsEnabled = true; }
        }

        private async Task LoadVideosCoreAsync()
        {
            // Load timecodes alongside history — both are lightweight local files.
            try
            {
                _timecodes = await _timecodeService.LoadAsync();
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException("Timecodes.Load", ex);
                MessageBox.Show(T("Не удалось прочитать сохранённые моменты.", "Saved moments could not be read."));
            }

            try
            {
                var history = await _watchHistoryService.LoadAsync();

                _watchHistory.Clear();
                foreach (var pair in history.LastWatchedByVideoId)
                {
                    _watchHistory[pair.Key] = pair.Value;
                }

                _playbackSecondsHistory.Clear();
                foreach (var pair in history.LastPlaybackSecondsByVideoId)
                {
                    _playbackSecondsHistory[pair.Key] = pair.Value;
                }

                _watchedDays.Clear();
                foreach (var dayText in history.WatchedDays)
                {
                    if (DateOnly.TryParse(dayText, out var day))
                    {
                        _watchedDays.Add(day);
                    }
                }

                _favoriteVideoIds.Clear();
                foreach (var favoriteVideoId in history.FavoriteVideoIds)
                {
                    _favoriteVideoIds.Add(favoriteVideoId);
                }

                _unlockedAchievements.Clear();
                foreach (var achievement in history.Achievements)
                {
                    _unlockedAchievements.Add(achievement);
                }

                _firstViewRewardedVideoIds.Clear();
                foreach (var videoId in history.FirstViewRewardedVideoIds)
                {
                    _firstViewRewardedVideoIds.Add(videoId);
                }

                _watchedVideoIdsByDay.Clear();
                foreach (var pair in history.WatchedVideoIdsByDay) _watchedVideoIdsByDay[pair.Key] = pair.Value;
                _claimedQuestCount = history.ClaimedQuestCount;
                _totalXp = history.TotalXp;
                _prestige = Math.Clamp(history.Prestige, 0, MaxPrestige);
                _prestigeXp = Math.Max(0, history.PrestigeXp);
                _effectSessionsAny = Math.Max(0, history.EffectSessionsAny);
                _effectSessionsFivePlus = Math.Max(0, history.EffectSessionsFivePlus);
                _effectSessionsTenPlus = Math.Max(0, history.EffectSessionsTenPlus);
                _effectSessionsStrongBlur = Math.Max(0, history.EffectSessionsStrongBlur);
                _effectSessionsStrongRedGlow = Math.Max(0, history.EffectSessionsStrongRedGlow);
                _effectSessionsStrongVioletGlow = Math.Max(0, history.EffectSessionsStrongVioletGlow);
                _effectSessionsStrongShake = Math.Max(0, history.EffectSessionsStrongShake);
                _effectPresetSessionsAny = Math.Max(0, history.EffectPresetSessionsAny);
                _effectPresetSessionsCustom = Math.Max(0, history.EffectPresetSessionsCustom);
                _effectPresetSessionsRetro = Math.Max(0, history.EffectPresetSessionsRetro);
                _effectPresetSessionsChaos = Math.Max(0, history.EffectPresetSessionsChaos);
                _effectPresetSessionsDream = Math.Max(0, history.EffectPresetSessionsDream);

                _watchedSecondsByDay.Clear();
                foreach (var pair in history.WatchedSecondsByDay)
                {
                    if (DateOnly.TryParse(pair.Key, out var day))
                    {
                        _watchedSecondsByDay[day] = Math.Max(0, pair.Value);
                    }
                }

                _bestSessionSecondsByDay.Clear();
                foreach (var pair in history.BestSessionSecondsByDay)
                {
                    if (DateOnly.TryParse(pair.Key, out var day))
                    {
                        _bestSessionSecondsByDay[day] = Math.Max(0, pair.Value);
                    }
                }

                _effectSessionsByDay.Clear();
                foreach (var pair in history.EffectSessionsByDay)
                {
                    if (DateOnly.TryParse(pair.Key, out var day))
                    {
                        _effectSessionsByDay[day] = Math.Max(0, pair.Value);
                    }
                }

                _presetSessionsByDay.Clear();
                foreach (var pair in history.PresetSessionsByDay)
                {
                    if (DateOnly.TryParse(pair.Key, out var day))
                    {
                        _presetSessionsByDay[day] = Math.Max(0, pair.Value);
                    }
                }

                _rewardedQuestKeys.Clear();
                foreach (var key in history.RewardedQuestKeys)
                {
                    _rewardedQuestKeys.Add(key);
                }

                _activeDailyQuestDate = DateOnly.TryParse(history.ActiveDailyQuestDate, out var parsedDailyDate)
                    ? parsedDailyDate
                    : null;
                _activeDailyQuestIds.Clear();
                foreach (var questId in history.ActiveDailyQuestIds)
                {
                    if (!string.IsNullOrWhiteSpace(questId))
                    {
                        _activeDailyQuestIds.Add(questId);
                    }
                }

                _activeWeeklyQuestWeekKey = history.ActiveWeeklyQuestWeekKey ?? string.Empty;
                _activeWeeklyQuestIds.Clear();
                foreach (var questId in history.ActiveWeeklyQuestIds)
                {
                    if (!string.IsNullOrWhiteSpace(questId))
                    {
                        _activeWeeklyQuestIds.Add(questId);
                    }
                }

                PurgeExpiredRewardedQuestKeys(GetTrustedToday());
                EnsureQuestRotationSchedule(GetTrustedToday());
                _isHistoryLoadedSuccessfully = true;
                HideLoadingOverlay();
            }
            catch (Exception ex)
            {
                _isHistoryLoadedSuccessfully = false;
                MessageBox.Show(
                    $"{T("Не удалось инициализировать данные профиля:", "Failed to initialize profile data:")}{Environment.NewLine}{ex.Message}",
                    T("Ошибка", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            var cached = await _streamsService.ReadCachedCatalogAsync();
            if (_allVideos.Count == 0 && cached is not null)
                ApplyCatalog(cached.Videos);
            try
            {
                using var streamsLoadCts = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                var result = await _streamsService.GetAllStreamsAsync(StreamsUrl, streamsLoadCts.Token);
                if (result.IsComplete || _allVideos.Count == 0)
                    ApplyCatalog(result.Videos);
                StatusText.Text = T("YouTube вернул только часть каталога.", "YouTube returned a partial catalog.");
                StatusText.Visibility = result.IsComplete ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException("Catalog", ex);
                StatusText.Text = T("Каталог недоступен. Проверьте подключение и нажмите «Обновить каталог».", "Catalog unavailable. Check your connection and refresh the catalog.");
                StatusText.Visibility = Visibility.Visible;
                if (cached?.CachedAtUtc is DateTime cachedAt)
                    StatusText.Text = T($"Сохранённый каталог от {cachedAt.ToLocalTime():g}", $"Cached catalog from {cachedAt.ToLocalTime():g}");
            }

        }

        private void ApplyCatalog(List<Models.YouTubeVideo> videos)
        {
            try
            {
                // Build a per-video timecode count lookup.
                var timecodeCountsByVideoId = _timecodes
                    .GroupBy(t => t.VideoId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

                if (videos.Count > 0) StatusText.Visibility = Visibility.Collapsed;
                _allVideos.Clear();
                foreach (var video in videos)
                {
                    if (_watchHistory.TryGetValue(video.VideoId, out var viewedAt))
                    {
                        video.LastViewedAtUtc = viewedAt;
                    }

                    if (_playbackSecondsHistory.TryGetValue(video.VideoId, out var playbackSeconds))
                    {
                        video.LastPlaybackSeconds = playbackSeconds;
                    }

                    video.IsFavorite = _favoriteVideoIds.Contains(video.VideoId);
                    if (timecodeCountsByVideoId.TryGetValue(video.VideoId, out var timecodeCount))
                    {
                        video.TimecodeCount = timecodeCount;
                    }

                    _allVideos.Add(video);
                }

                RefreshVisibleVideos();
                RefreshRecommendations();
                RefreshProfile();
                RefreshHomeSummary();
                UpdateStreakText();
                if (!_introDismissed && _allVideos.Count > 0) PsiIntro.SetCatalogState(true, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{T("Не удалось инициализировать данные приложения:", "Failed to initialize app data:")}{Environment.NewLine}{ex.Message}",
                    T("Ошибка", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string BuildStreamsLoadErrorMessage(Exception ex)
        {
            var baseMessage = $"{T("Не удалось загрузить стримы:", "Failed to load streams:")}{Environment.NewLine}{ex.Message}";
            var errorText = ex.ToString();

            if (ex is HttpRequestException httpEx)
            {
                if (httpEx.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized ||
                    errorText.Contains("403") ||
                    errorText.Contains("451"))
                {
                    return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                           T(
                               "Возможно, доступ к YouTube ограничен в вашей сети/регионе. Попробуйте включить VPN.",
                               "YouTube access may be restricted in your network/region. Try enabling VPN.");
                }

                if (httpEx.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout ||
                    errorText.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                    errorText.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                           T(
                               "Похоже на нестабильное интернет-соединение или временную недоступность сервиса. Проверьте интернет и попробуйте снова.",
                               "Looks like unstable internet or temporary service downtime. Check your internet and try again.");
                }
            }

            if (ex is TaskCanceledException ||
                errorText.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("The remote name could not be resolved", StringComparison.OrdinalIgnoreCase))
            {
                return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                       T(
                           "Проверьте интернет-соединение. Возможно, сеть недоступна или есть проблемы с DNS.",
                           "Check internet connection. Network may be unavailable or DNS may be failing.");
            }

            if (errorText.Contains("Failed to read YouTube keys", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("Failed to parse streams page data", StringComparison.OrdinalIgnoreCase))
            {
                return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                       T(
                           "Возможно, YouTube недоступен без VPN в вашем регионе или временно изменился формат страницы.",
                           "YouTube may be unavailable without VPN in your region, or page format changed temporarily.");
            }

            return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                   T("Проверьте интернет-соединение и попробуйте снова.", "Check internet connection and try again.");
        }




        private async Task SaveHistoryAsync()
        {
            if (!_isHistoryLoadedSuccessfully)
            {
                return;
            }

            var payload = new WatchHistoryData();

            foreach (var pair in _watchHistory)
            {
                payload.LastWatchedByVideoId[pair.Key] = pair.Value;
            }

            foreach (var pair in _playbackSecondsHistory)
            {
                payload.LastPlaybackSecondsByVideoId[pair.Key] = pair.Value;
            }

            foreach (var day in _watchedDays)
            {
                payload.WatchedDays.Add(day.ToString("yyyy-MM-dd"));
            }

            foreach (var favoriteId in _favoriteVideoIds)
            {
                payload.FavoriteVideoIds.Add(favoriteId);
            }

            foreach (var achievement in _unlockedAchievements)
            {
                payload.Achievements.Add(achievement);
            }

            foreach (var videoId in _firstViewRewardedVideoIds)
            {
                payload.FirstViewRewardedVideoIds.Add(videoId);
            }

            foreach (var pair in _watchedVideoIdsByDay) payload.WatchedVideoIdsByDay[pair.Key] = new HashSet<string>(pair.Value, StringComparer.Ordinal);
            payload.ClaimedQuestCount = _claimedQuestCount;
            payload.TotalXp = _totalXp;
            payload.Prestige = _prestige;
            payload.PrestigeXp = _prestigeXp;
            payload.EffectSessionsAny = _effectSessionsAny;
            payload.EffectSessionsFivePlus = _effectSessionsFivePlus;
            payload.EffectSessionsTenPlus = _effectSessionsTenPlus;
            payload.EffectSessionsStrongBlur = _effectSessionsStrongBlur;
            payload.EffectSessionsStrongRedGlow = _effectSessionsStrongRedGlow;
            payload.EffectSessionsStrongVioletGlow = _effectSessionsStrongVioletGlow;
            payload.EffectSessionsStrongShake = _effectSessionsStrongShake;
            payload.EffectPresetSessionsAny = _effectPresetSessionsAny;
            payload.EffectPresetSessionsCustom = _effectPresetSessionsCustom;
            payload.EffectPresetSessionsRetro = _effectPresetSessionsRetro;
            payload.EffectPresetSessionsChaos = _effectPresetSessionsChaos;
            payload.EffectPresetSessionsDream = _effectPresetSessionsDream;
            foreach (var pair in _watchedSecondsByDay)
            {
                payload.WatchedSecondsByDay[pair.Key.ToString("yyyy-MM-dd")] = pair.Value;
            }

            foreach (var pair in _bestSessionSecondsByDay)
            {
                payload.BestSessionSecondsByDay[pair.Key.ToString("yyyy-MM-dd")] = pair.Value;
            }

            foreach (var pair in _effectSessionsByDay)
            {
                payload.EffectSessionsByDay[pair.Key.ToString("yyyy-MM-dd")] = pair.Value;
            }

            foreach (var pair in _presetSessionsByDay)
            {
                payload.PresetSessionsByDay[pair.Key.ToString("yyyy-MM-dd")] = pair.Value;
            }

            foreach (var key in _rewardedQuestKeys)
            {
                payload.RewardedQuestKeys.Add(key);
            }

            payload.ActiveDailyQuestDate = _activeDailyQuestDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            payload.ActiveDailyQuestIds.Clear();
            payload.ActiveDailyQuestIds.AddRange(_activeDailyQuestIds);
            payload.ActiveWeeklyQuestWeekKey = _activeWeeklyQuestWeekKey;
            payload.ActiveWeeklyQuestIds.Clear();
            payload.ActiveWeeklyQuestIds.AddRange(_activeWeeklyQuestIds);
            await _watchHistoryService.SaveAsync(payload);
        }
    }
}
