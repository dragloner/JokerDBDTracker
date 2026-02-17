using System.Windows.Controls;
using JokerDBDTracker.Models;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            if (IsProfileTabSelected())
            {
                return;
            }

            RefreshVisibleVideos();
            RefreshRecommendations();
        }

        private void RefreshVisibleVideos(string? preferredVideoId = null)
        {
            var selectedId = preferredVideoId ?? _selectedVideoId;
            var forFavoritesTab = IsFavoritesTabSelected();

            IEnumerable<YouTubeVideo> visible = forFavoritesTab
                ? _allVideos.Where(v => v.IsFavorite)
                : _allVideos;

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                visible = visible.Where(v => v.Title.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase));
            }

            visible = SortVideos(visible, CurrentSortMode());

            _suppressSelectionEvents = true;
            _videos.Clear();
            foreach (var video in visible)
            {
                _videos.Add(video);
            }
            _suppressSelectionEvents = false;

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                SyncSelection(selectedId);
            }
        }

        private void RefreshRecommendations()
        {
            var source = _allVideos.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                source = source.Where(v => v.Title.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase));
            }

            var favoriteWords = _allVideos
                .Where(v => v.IsFavorite)
                .SelectMany(v => v.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(w => w.ToLowerInvariant())
                .ToHashSet();

            var scored = source
                .Select(v => new
                {
                    Video = v,
                    Score =
                        (v.LastViewedAtUtc.HasValue ? 0 : 1000) +
                        (v.IsFavorite ? 200 : 0) +
                        CountKeywordMatches(v.Title, favoriteWords) * 20 -
                        (v.LastViewedAtUtc.HasValue ? 1 : 0) * DaysSince(v.LastViewedAtUtc)
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Video.OriginalOrder)
                .Take(6)
                .Select(x => x.Video)
                .ToList();

            RecommendationsList.ItemsSource = scored;
        }

        private static int CountKeywordMatches(string title, HashSet<string> favoriteWords)
        {
            if (favoriteWords.Count == 0)
            {
                return 0;
            }

            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return words.Count(w => favoriteWords.Contains(w.ToLowerInvariant()));
        }

        private static int DaysSince(DateTime? date)
        {
            if (!date.HasValue)
            {
                return 0;
            }

            return (DateTime.Now.Date - date.Value.ToLocalTime().Date).Days;
        }

        private static IEnumerable<YouTubeVideo> SortVideos(IEnumerable<YouTubeVideo> videos, string sortMode)
        {
            return sortMode switch
            {
                "watched_recent" => videos
                    .OrderByDescending(v => v.LastViewedAtUtc.HasValue)
                    .ThenByDescending(v => v.LastViewedAtUtc)
                    .ThenBy(v => v.OriginalOrder),
                "watched_oldest" => videos
                    .OrderBy(v => v.LastViewedAtUtc.HasValue)
                    .ThenBy(v => v.LastViewedAtUtc)
                    .ThenBy(v => v.OriginalOrder),
                _ => videos.OrderBy(v => v.OriginalOrder)
            };
        }

        private string CurrentSortMode()
        {
            return (SortModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "watched_recent";
        }

        private void SyncSelection(string? videoId)
        {
            if (string.IsNullOrWhiteSpace(videoId))
            {
                return;
            }

            _suppressSelectionEvents = true;
            VideoList.SelectedItem = _videos.FirstOrDefault(v => v.VideoId == videoId);
            _suppressSelectionEvents = false;
        }

        private void ClearAllSelections()
        {
            _selectedVideoId = null;
            _suppressSelectionEvents = true;
            VideoList.SelectedItem = null;
            RecommendationsList.SelectedItem = null;
            RecentStreamsList.SelectedItem = null;
            _suppressSelectionEvents = false;
        }

        private void SortModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RefreshVisibleVideos();
            RefreshRecommendations();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text.Trim();
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
    }
}
