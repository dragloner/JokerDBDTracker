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
            if (IsFavoritesTabSelected())
            {
                RefreshFavoritesClipsView();
                RefreshFavoritesSummary();
                return;
            }

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
                visible = visible.Where(v => MatchesVideoSearch(v, _searchText, forFavoritesTab));
            }

            // Category filter
            visible = _activeCategory switch
            {
                "favorites" => visible.Where(v => v.IsFavorite),
                "with_timecodes" => visible.Where(v => v.HasTimecodes),
                _ => visible
            };

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

            UpdateStreamsCountLabel();
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

        private void RefreshContinueWatching()
        {
            var inProgress = _allVideos
                .Where(v => v.HasResumePosition)
                .OrderByDescending(v => v.LastViewedAtUtc)
                .Take(5)
                .ToList();

            ContinueWatchingList.ItemsSource = inProgress;
            ContinueWatchingSection.Visibility = inProgress.Count > 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }

        private void UpdateStreamsCountLabel()
        {
            if (StreamsCountLabel is null) return;
            var total = _videos.Count;
            var all = IsFavoritesTabSelected()
                ? _allVideos.Count(v => v.IsFavorite)
                : _allVideos.Count;

            if (IsFavoritesTabSelected())
            {
                StreamsCountLabel.Text = string.IsNullOrWhiteSpace(_searchText) && _activeCategory == "all"
                    ? T($"{all} избранных стримов", $"{all} favorite streams")
                    : T($"{total} из {all} избранных стримов", $"{total} of {all} favorite streams");
                return;
            }

            StreamsCountLabel.Text = _activeCategory == "all" && string.IsNullOrEmpty(_searchText)
                ? T($"{all} стримов в каталоге", $"{all} streams in catalog")
                : T($"{total} из {all} стримов", $"{total} of {all} streams");
        }

        private void CategoryFilterButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            _activeCategory = btn.Tag?.ToString() ?? "all";
            UpdateCategoryFilterButtonsVisual();
            RefreshVisibleVideos();
            if (IsFavoritesTabSelected())
            {
                RefreshFavoritesSummary();
            }
            RefreshRecommendations();
            UpdateStreamsCountLabel();
        }

        private void UpdateCategoryFilterButtonsVisual()
        {
            foreach (var btn in new[]
                     {
                         FilterAllButton, FilterFavoritesButton, FilterTimecodesButton
                     })
            {
                if (btn is null) continue;
                btn.Background = System.Windows.Media.Brushes.Transparent;
                btn.BorderBrush = BrushFromHex("#2A4A62");
                btn.Foreground = BrushFromHex("#8BB8D4");
            }

            var activeBtn = _activeCategory switch
            {
                "with_timecodes" => FilterTimecodesButton,
                "favorites" => FilterFavoritesButton,
                _           => FilterAllButton
            };

            if (activeBtn is not null)
            {
                activeBtn.Background    = BrushFromHex("#1E5060");
                activeBtn.BorderBrush   = BrushFromHex("#4a8aaa");
                activeBtn.Foreground    = BrushFromHex("#D4EEFF");
            }
        }

        private void RandomStreamButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_videos.Count == 0) return;
            var rnd = new Random();
            var video = _videos[rnd.Next(_videos.Count)];
            _ = OpenVideoAsync(video);
        }

        private void AddToQueueButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            var videoId = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(videoId)) return;
            var video = _allVideos.FirstOrDefault(v => v.VideoId == videoId);
            if (video is null) return;

            if (_watchQueue.Any(v => v.VideoId == videoId))
            {
                var existing = _watchQueue.First(v => v.VideoId == videoId);
                _watchQueue.Remove(existing);
            }
            else
            {
                _watchQueue.Add(video);
            }

            UpdateWatchQueueUI();
        }

        private bool MatchesVideoSearch(YouTubeVideo video, string query, bool includeTimecodes)
        {
            if (video.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            if (!includeTimecodes)
            {
                return false;
            }

            return GetFavoriteClipsSource().Any(timecode =>
                string.Equals(timecode.VideoId, video.VideoId, StringComparison.OrdinalIgnoreCase) &&
                MatchesTimecodeSearch(timecode, query));
        }

        private void RemoveFromQueueButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            var videoId = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(videoId)) return;
            var existing = _watchQueue.FirstOrDefault(v => v.VideoId == videoId);
            if (existing is not null) _watchQueue.Remove(existing);
            UpdateWatchQueueUI();
        }

        private async void PlayQueueButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await PlayNextInQueueAsync();
        }

        private async Task PlayNextInQueueAsync()
        {
            if (_watchQueue.Count == 0) return;
            var video = _watchQueue[0];
            _watchQueue.RemoveAt(0);
            UpdateWatchQueueUI();
            await OpenVideoAsync(video);
            // After player closes, continue with next if queue still has items.
            if (_watchQueue.Count > 0)
            {
                await PlayNextInQueueAsync();
            }
        }

        private void UpdateWatchQueueUI()
        {
            WatchQueueList.ItemsSource = null;
            WatchQueueList.ItemsSource = _watchQueue;
            WatchQueueCountText.Text = _watchQueue.Count.ToString();
            WatchQueueSection.Visibility = _watchQueue.Count > 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }

        private void ContinueWatchingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            if (ContinueWatchingList.SelectedItem is YouTubeVideo video)
            {
                _suppressSelectionEvents = true;
                ContinueWatchingList.SelectedItem = null;
                _suppressSelectionEvents = false;
                _ = OpenVideoAsync(video);
            }
        }

    }
}
