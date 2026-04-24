using System.Windows;
using System.Windows.Controls;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private async Task RefreshFavoritesClipsAsync()
        {
            try
            {
                _timecodes = await _timecodeService.LoadAsync();
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException("RefreshFavoritesClipsAsync", ex);
                _timecodes = [];
            }

            SyncTimecodeCountsToVideos();
            RefreshFavoritesClipsView();
            RefreshFavoritesSummary();
        }

        private void RefreshFavoritesClipsView()
        {
            var clips = GetVisibleFavoriteClips();

            if (FavoritesClipsList is null)
            {
                return;
            }

            FavoritesClipsList.ItemsSource = null;
            FavoritesClipsList.ItemsSource = clips;

            if (FavoritesClipsHeaderText is not null)
            {
                FavoritesClipsHeaderText.Text = T("Сохранённые моменты", "Saved moments");
            }

            if (FavoritesClipsSubtitleText is not null)
            {
                FavoritesClipsSubtitleText.Text = BuildFavoritesClipsSubtitle(clips.Count);
            }

            if (FavoritesClipsCountText is not null)
            {
                FavoritesClipsCountText.Text = BuildFavoritesClipsCountText(clips.Count);
            }

            if (FavoritesClipsEmptyText is not null)
            {
                FavoritesClipsEmptyText.Text = BuildFavoritesEmptyText();
                FavoritesClipsEmptyText.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void FavoritesClipOpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var id = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(id)) return;

            var timecode = _timecodes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            if (timecode is null) return;

            OpenFavoriteClip(timecode);
        }

        private void RandomFavoriteMomentButton_Click(object sender, RoutedEventArgs e)
        {
            var clips = GetVisibleFavoriteClips();
            if (clips.Count == 0)
            {
                return;
            }

            var clip = clips[Random.Shared.Next(clips.Count)];
            OpenFavoriteClip(clip);
        }

        private void ResumeFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            var nextVideo = _videos.FirstOrDefault(v => v.HasResumePosition)
                            ?? _videos.FirstOrDefault();
            if (nextVideo is null)
            {
                return;
            }

            _ = OpenVideoAsync(nextVideo);
        }

        private void OpenFavoriteClip(Timecode timecode)
        {
            var video = _allVideos.FirstOrDefault(v => string.Equals(v.VideoId, timecode.VideoId, StringComparison.OrdinalIgnoreCase));
            if (video is null)
            {
                return;
            }

            _ = OpenVideoAtTimecodeAsync(video, timecode.Seconds);
        }

        private async Task OpenVideoAtTimecodeAsync(YouTubeVideo video, int seconds)
        {
            if (_isOpeningVideo) return;

            video.LastPlaybackSeconds = seconds;
            await OpenVideoAsync(video);
        }

        private void FavoritesClipDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var id = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(id)) return;
            _ = DeleteFavoritesClipAsync(id);
        }

        private async Task DeleteFavoritesClipAsync(string id)
        {
            try
            {
                var all = await _timecodeService.LoadAsync();
                all.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
                await _timecodeService.SaveAsync(all);
                _timecodes = all;
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException("DeleteFavoritesClipAsync", ex);
            }

            SyncTimecodeCountsToVideos();
            RefreshVisibleVideos();
            RefreshFavoritesClipsView();
            RefreshFavoritesSummary();
        }

        private IEnumerable<Timecode> GetFavoriteClipsSource()
        {
            return _timecodes.Where(t => _favoriteVideoIds.Contains(t.VideoId));
        }

        private List<Timecode> GetVisibleFavoriteClips()
        {
            var source = GetFavoriteClipsSource();
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                source = source.Where(t => MatchesTimecodeSearch(t, _searchText));
            }

            return [.. source
                .OrderByDescending(t => t.SavedAtUtc)
                .ThenBy(t => t.VideoTitle, StringComparer.CurrentCultureIgnoreCase)];
        }

        private static bool MatchesTimecodeSearch(Timecode timecode, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            var comparison = StringComparison.CurrentCultureIgnoreCase;
            return (!string.IsNullOrWhiteSpace(timecode.Label) && timecode.Label.Contains(query, comparison)) ||
                   (!string.IsNullOrWhiteSpace(timecode.VideoTitle) && timecode.VideoTitle.Contains(query, comparison)) ||
                   timecode.TimeFormatted.Contains(query, comparison) ||
                   timecode.SavedAtText.Contains(query, comparison) ||
                   timecode.Seconds.ToString().Contains(query, comparison);
        }

        private void SyncTimecodeCountsToVideos()
        {
            var countsByVideoId = _timecodes
                .GroupBy(t => t.VideoId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var video in _allVideos)
            {
                video.TimecodeCount = countsByVideoId.TryGetValue(video.VideoId, out var count) ? count : 0;
            }

            VideoList?.Items.Refresh();
            ContinueWatchingList?.Items.Refresh();
            RecommendationsList?.Items.Refresh();
            RecentStreamsList?.Items.Refresh();
        }

        private void RefreshFavoritesSummary()
        {
            if (FavoritesSummaryPanel is null)
            {
                return;
            }

            var favoriteVideos = _allVideos.Where(v => v.IsFavorite).ToList();
            var favoriteClips = GetFavoriteClipsSource().ToList();
            var visibleClips = GetVisibleFavoriteClips();
            var resumeCount = favoriteVideos.Count(v => v.HasResumePosition);
            var streamsWithMoments = favoriteVideos.Count(v => v.HasTimecodes);

            FavoritesOverviewHeaderText.Text = T("Избранное без шума", "Favorites without noise");
            FavoritesOverviewSubText.Text = string.IsNullOrWhiteSpace(_searchText)
                ? T(
                    "Быстрый доступ к любимым стримам, сохранённым моментам и просмотру с продолжением.",
                    "Quick access to favorite streams, saved moments, and continue-watching picks.")
                : T(
                    $"Поиск нашёл {_videos.Count} стримов и {visibleClips.Count} таймкодов по запросу «{_searchText}».",
                    $"Search found {_videos.Count} streams and {visibleClips.Count} timecodes for \"{_searchText}\".");

            FavoritesFavoriteCountLabelText.Text = T("Избранные стримы", "Favorite streams");
            FavoritesFavoriteCountValueText.Text = favoriteVideos.Count.ToString();

            FavoritesMomentCountLabelText.Text = T("Сохранённые моменты", "Saved moments");
            FavoritesMomentCountValueText.Text = favoriteClips.Count.ToString();

            FavoritesResumeCountLabelText.Text = T("Готовы к продолжению", "Ready to resume");
            FavoritesResumeCountValueText.Text = resumeCount.ToString();

            FavoritesStreamsWithMomentsLabelText.Text = T("Стримы с таймкодами", "Streams with timecodes");
            FavoritesStreamsWithMomentsValueText.Text = streamsWithMoments.ToString();

            FavoritesSearchHintText.Text = favoriteClips.Count == 0
                ? T("Добавь таймкод в плеере клавишей M, чтобы он появился здесь.", "Press M in the player to save a moment here.")
                : T("Поиск сверху теперь ищет и по названиям таймкодов, и по времени вроде 01:52:39.", "Top search now works for timecode labels and for times like 01:52:39.");

            if (FilterTimecodesButton is not null)
            {
                FilterTimecodesButton.Content = T("# Таймкоды", "# Timecodes");
            }

            ResumeFavoriteButton.Content = T("Продолжить лучшее", "Resume best match");
            RandomFavoriteMomentButton.Content = T("Случайный момент", "Random moment");
            RandomFavoriteMomentButton.IsEnabled = favoriteClips.Count > 0;
            ResumeFavoriteButton.IsEnabled = _videos.Count > 0;
        }

        private string BuildFavoritesClipsCountText(int visibleCount)
        {
            var totalCount = GetFavoriteClipsSource().Count();
            if (totalCount == 0)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_searchText) || visibleCount == totalCount)
            {
                return T($"{totalCount} моментов", $"{totalCount} moments");
            }

            return T($"{visibleCount} из {totalCount} моментов", $"{visibleCount} of {totalCount} moments");
        }

        private string BuildFavoritesClipsSubtitle(int visibleCount)
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                return T("Недавние таймкоды из избранных стримов.", "Recent timecodes from favorite streams.");
            }

            return T(
                $"Найдено {visibleCount} совпадений по названию, подписи таймкода или времени.",
                $"{visibleCount} matches by title, label, or timestamp.");
        }

        private string BuildFavoritesEmptyText()
        {
            if (GetFavoriteClipsSource().Any())
            {
                return T("По этому запросу не найдено таймкодов в избранном.", "No favorite timecodes found for this search.");
            }

            return T(
                "Пока нет сохранённых моментов в избранных стримах. Открой видео и нажми M в нужный момент.",
                "No saved moments for favorite streams yet. Open a video and press M at the right moment.");
        }
    }
}
