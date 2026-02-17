using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow : Window
    {
        private const string StreamsUrl = "https://www.youtube.com/@JokerDBD/streams";
        private const string AchievementCursed15 = "cursed_15_effects_full_stream";
        private const int MaxRecentStreamsInProfile = 5;
        private const int MaxLevel = 100;
        private const int MaxPrestige = 100;
        private const int XpFirstWatch = 120;
        private const int XpAchievement = 800;

        private readonly ObservableCollection<YouTubeVideo> _videos = [];
        private readonly List<YouTubeVideo> _allVideos = [];
        private readonly Dictionary<string, DateTime> _watchHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _playbackSecondsHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<DateOnly> _watchedDays = [];
        private readonly HashSet<string> _favoriteVideoIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unlockedAchievements = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firstViewRewardedVideoIds = new(StringComparer.OrdinalIgnoreCase);
        private int _totalXp;
        private int _prestige;
        private int _prestigeXp;
        private int _effectSessionsAny;
        private int _effectSessionsFivePlus;
        private int _effectSessionsTenPlus;
        private int _effectSessionsStrongBlur;
        private int _effectSessionsStrongRedGlow;
        private int _effectSessionsStrongVioletGlow;
        private int _effectSessionsStrongShake;

        private readonly YouTubeStreamsService _streamsService = new();
        private readonly WatchHistoryService _watchHistoryService = new();

        private bool _suppressSelectionEvents;
        private string? _selectedVideoId;
        private string _searchText = string.Empty;
        private static readonly Brush TopNavSelectedBackground = BrushFromHex("#E4EEF6");
        private static readonly Brush TopNavSelectedForeground = BrushFromHex("#173041");
        private static readonly Brush TopNavSelectedBorder = BrushFromHex("#8FB4CD");
        private static readonly Brush TopNavDefaultBackground = BrushFromHex("#2C4357");
        private static readonly Brush TopNavDefaultForeground = BrushFromHex("#EAF6FF");
        private static readonly Brush TopNavDefaultBorder = BrushFromHex("#6E91A9");

        public MainWindow()
        {
            InitializeComponent();
            VideoList.ItemsSource = _videos;
            UpdateTopNavButtonsVisualState();
            StateChanged += MainWindow_StateChanged;
            UpdateMainWindowButtonsState();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Maximized;
            UpdateMainWindowButtonsState();
            await LoadVideosAsync();
        }

        private void MainMinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MainWindowSizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            UpdateMainWindowButtonsState();
        }

        private void MainCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateMainWindowButtonsState();
        }

        private void UpdateMainWindowButtonsState()
        {
            if (MainWindowSizeButton is null)
            {
                return;
            }

            MainWindowSizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void TopHeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                UpdateMainWindowButtonsState();
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                var pointerPosition = e.GetPosition(this);
                var screenPosition = PointToScreen(pointerPosition);
                var widthRatio = ActualWidth > 0 ? pointerPosition.X / ActualWidth : 0.5;
                widthRatio = Math.Clamp(widthRatio, 0.0, 1.0);

                var restoreWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Math.Max(1060, Width);
                WindowState = WindowState.Normal;
                Left = screenPosition.X - restoreWidth * widthRatio;
                Top = Math.Max(0, screenPosition.Y - 20);
                UpdateMainWindowButtonsState();
            }

            try
            {
                DragMove();
            }
            catch
            {
                // Ignore drag interruptions caused by rapid pointer transitions.
            }
        }

        private static bool IsInteractiveElement(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is ButtonBase || source is TextBox || source is ComboBox || source is Slider)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

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

        private async Task MarkAsWatchedAsync(YouTubeVideo video)
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

            // Recommendation score:
            // 1) Unwatched first.
            // 2) Favorite boost.
            // 3) Similarity to favorite titles by keyword overlap.
            // 4) More recent watched streams are lower priority than unseen streams.
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

        private async Task OpenVideoAsync(YouTubeVideo video)
        {
            _selectedVideoId = video.VideoId;
            await MarkAsWatchedAsync(video);

            var player = new PlayerWindow(video, video.LastPlaybackSeconds);
            Hide();
            player.ShowDialog();
            Show();
            Activate();

            video.LastPlaybackSeconds = player.LastPlaybackSeconds;
            _playbackSecondsHistory[video.VideoId] = player.LastPlaybackSeconds;

            if (!_firstViewRewardedVideoIds.Contains(video.VideoId))
            {
                _firstViewRewardedVideoIds.Add(video.VideoId);
                AddXp(XpFirstWatch);
            }

            AddXp(player.WatchXpEarned);

            if (player.WatchedWithAnyEffects)
            {
                _effectSessionsAny++;
            }

            if (player.MaxEnabledEffectsCount >= 5)
            {
                _effectSessionsFivePlus++;
            }

            if (player.MaxEnabledEffectsCount >= 10)
            {
                _effectSessionsTenPlus++;
            }

            if (player.UsedStrongBlur)
            {
                _effectSessionsStrongBlur++;
            }

            if (player.UsedStrongRedGlow)
            {
                _effectSessionsStrongRedGlow++;
            }

            if (player.UsedStrongVioletGlow)
            {
                _effectSessionsStrongVioletGlow++;
            }

            if (player.UsedStrongShake)
            {
                _effectSessionsStrongShake++;
            }

            if (player.CursedMasterUnlocked && !_unlockedAchievements.Contains(AchievementCursed15))
            {
                _unlockedAchievements.Add(AchievementCursed15);
                AddXp(XpAchievement);
            }

            await SaveHistoryAsync();
            RefreshRecommendations();
            RefreshProfile();
            RefreshHomeSummary();
            RefreshVisibleVideos();
            ClearAllSelections();
        }

        private async void VideoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents || IsProfileTabSelected())
            {
                return;
            }

            if (VideoList.SelectedItem is not YouTubeVideo video)
            {
                return;
            }

            await OpenVideoAsync(video);
        }

        private async void RecommendationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents || IsProfileTabSelected())
            {
                return;
            }

            if (RecommendationsList.SelectedItem is not YouTubeVideo video)
            {
                return;
            }

            await OpenVideoAsync(video);
        }

        private async void RecentStreamsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents)
            {
                return;
            }

            if (RecentStreamsList.SelectedItem is not YouTubeVideo video)
            {
                return;
            }

            await OpenVideoAsync(video);
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
            if (!IsProfileTabSelected())
            {
                RefreshVisibleVideos();
                RefreshRecommendations();
            }
        }

        private async void ToggleFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string videoId)
            {
                return;
            }

            var video = _allVideos.FirstOrDefault(v => v.VideoId == videoId);
            if (video is null)
            {
                return;
            }

            video.IsFavorite = !video.IsFavorite;
            if (video.IsFavorite)
            {
                _favoriteVideoIds.Add(video.VideoId);
            }
            else
            {
                _favoriteVideoIds.Remove(video.VideoId);
            }

            await SaveHistoryAsync();
            RefreshRecommendations();
            RefreshProfile();
            RefreshHomeSummary();
            RefreshVisibleVideos(video.VideoId);
        }

        private async void PrestigeButton_Click(object sender, RoutedEventArgs e)
        {
            var level = CalculateLevelFromXp(_prestigeXp);
            if (level < MaxLevel || _prestige >= MaxPrestige)
            {
                return;
            }

            _prestige++;
            _prestigeXp = 0;

            await SaveHistoryAsync();
            RefreshProfile();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
