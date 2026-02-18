using System.Net;
using System.Net.Http;
using System.Windows;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private async Task LoadVideosAsync()
        {
            List<Models.YouTubeVideo> videos;
            try
            {
                videos = await _streamsService.GetAllStreamsAsync(StreamsUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    BuildStreamsLoadErrorMessage(ex),
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
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
                    _allVideos.Add(video);
                }

                RefreshVisibleVideos();
                RefreshRecommendations();
                RefreshProfile();
                RefreshHomeSummary();
                UpdateStreakText();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось инициализировать данные приложения:{Environment.NewLine}{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string BuildStreamsLoadErrorMessage(Exception ex)
        {
            var baseMessage = $"Не удалось загрузить стримы:{Environment.NewLine}{ex.Message}";
            var errorText = ex.ToString();

            if (ex is HttpRequestException httpEx)
            {
                if (httpEx.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized ||
                    errorText.Contains("403") ||
                    errorText.Contains("451"))
                {
                    return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                           "Возможно, доступ к YouTube ограничен в вашей сети/регионе. " +
                           "Попробуйте включить VPN.";
                }

                if (httpEx.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout ||
                    errorText.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                    errorText.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                           "Похоже на нестабильное интернет-соединение или временную недоступность сервиса. " +
                           "Проверьте интернет и попробуйте снова.";
                }
            }

            if (ex is TaskCanceledException ||
                errorText.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("The remote name could not be resolved", StringComparison.OrdinalIgnoreCase))
            {
                return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                       "Проверьте интернет-соединение. Возможно, сеть недоступна или есть проблемы с DNS.";
            }

            if (errorText.Contains("Failed to read YouTube keys", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("Failed to parse streams page data", StringComparison.OrdinalIgnoreCase))
            {
                return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                       "Возможно, YouTube недоступен без VPN в вашем регионе или временно изменился формат страницы.";
            }

            return $"{baseMessage}{Environment.NewLine}{Environment.NewLine}" +
                   "Проверьте интернет-соединение и попробуйте снова.";
        }

        private async Task MarkAsWatchedAsync(Models.YouTubeVideo video)
        {
            var nowUtc = DateTime.UtcNow;
            video.LastViewedAtUtc = nowUtc;
            _watchHistory[video.VideoId] = nowUtc;
            _watchedDays.Add(DateOnly.FromDateTime(nowUtc.ToLocalTime().Date));
            await SaveHistoryAsync();

            UpdateStreakText();

            if (CurrentSortMode() is "watched_recent" or "watched_oldest")
            {
                RefreshVisibleVideos();
            }
            else
            {
                VideoList.Items.Refresh();
            }
        }

        private async Task SaveHistoryAsync()
        {
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
            await _watchHistoryService.SaveAsync(payload);
        }
    }
}
