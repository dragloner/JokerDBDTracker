using System.Windows;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private async Task LoadVideosAsync()
        {
            try
            {
                var videos = await _streamsService.GetAllStreamsAsync(StreamsUrl);
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
                    $"Не удалось загрузить стримы:{Environment.NewLine}{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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
