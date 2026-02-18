using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.Reflection;
using System.IO;
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
        private readonly GitHubUpdateService _updateService = new();
        private readonly DispatcherTimer _searchDebounceTimer = new();
        private const string GitHubRepoOwner = "dragloner";
        private const string GitHubRepoName = "JokerDBDTracker";
        private string _latestReleaseUrl = $"https://github.com/{GitHubRepoOwner}/{GitHubRepoName}/releases/latest";
        private GitHubUpdateInfo? _lastUpdateInfo;
        private bool _isDownloadingUpdate;
        private CancellationTokenSource? _updateDownloadCts;

        private bool _suppressSelectionEvents;
        private bool _isOpeningVideo;
        private string? _selectedVideoId;
        private string _searchText = string.Empty;
        private bool _pendingDragRestoreFromMaximized;
        private Point _pendingDragStartPoint;
        private static readonly Brush TopNavSelectedBackground = BrushFromHex("#E4EEF6");
        private static readonly Brush TopNavSelectedForeground = BrushFromHex("#173041");
        private static readonly Brush TopNavSelectedBorder = BrushFromHex("#8FB4CD");
        private static readonly Brush TopNavDefaultBackground = BrushFromHex("#2C4357");
        private static readonly Brush TopNavDefaultForeground = BrushFromHex("#EAF6FF");
        private static readonly Brush TopNavDefaultBorder = BrushFromHex("#6E91A9");

        public MainWindow()
        {
            InitializeComponent();
            WindowBoundsHelper.Attach(this);
            VideoList.ItemsSource = _videos;
            UpdateTopNavButtonsVisualState();
            StateChanged += MainWindow_StateChanged;
            Closed += MainWindow_Closed;
            UpdateMainWindowButtonsState();
            Loaded += MainWindow_Loaded;
            PreviewMouseMove += MainWindow_PreviewMouseMove;
            PreviewMouseLeftButtonUp += MainWindow_PreviewMouseLeftButtonUp;
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(180);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
            UpdateVersionText();
            InitializeLoadingBackgroundImage();
        }

        private void UpdateVersionText()
        {
            if (AppVersionText is null)
            {
                return;
            }

            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            var cleanVersion = informational?.Split('+')[0];
            if (string.IsNullOrWhiteSpace(cleanVersion))
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
                cleanVersion = $"{version.Major}.{version.Minor}.{version.Build}";
            }

            AppVersionText.Text = $"v{cleanVersion}";
        }

        private Version GetCurrentAppVersion()
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            var clean = informational?.Split('+')[0];
            if (!string.IsNullOrWhiteSpace(clean) && Version.TryParse(clean, out var infoVersion))
            {
                return infoVersion;
            }

            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        }

        private async Task CheckForUpdatesDuringStartupAsync()
        {
            var updateInfo = await _updateService.CheckForUpdateAsync(
                GitHubRepoOwner,
                GitHubRepoName,
                GetCurrentAppVersion());
            _lastUpdateInfo = updateInfo;

            if (UpdateStatusText is null || UpdateProgramButton is null)
            {
                return;
            }

            if (!updateInfo.IsCheckSuccessful)
            {
                UpdateStatusText.Text = "● Не удалось проверить обновления";
                UpdateStatusText.Foreground = BrushFromHex("#E4C487");
                UpdateProgramButton.Visibility = Visibility.Collapsed;
                RestartProgramButton.Visibility = Visibility.Collapsed;
                return;
            }

            _latestReleaseUrl = string.IsNullOrWhiteSpace(updateInfo.ReleaseUrl)
                ? _latestReleaseUrl
                : updateInfo.ReleaseUrl;

            if (updateInfo.IsUpdateAvailable)
            {
                UpdateStatusText.Text = $"● Доступно обновление {updateInfo.LatestVersionText}";
                UpdateStatusText.Foreground = BrushFromHex("#F0B56E");
                UpdateProgramButton.Visibility = Visibility.Visible;
                RestartProgramButton.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateStatusText.Text = "● Установлено последнее обновление";
            UpdateStatusText.Foreground = BrushFromHex("#82D7AA");
            UpdateProgramButton.Visibility = Visibility.Collapsed;
            RestartProgramButton.Visibility = Visibility.Collapsed;
        }

        private async void UpdateProgramButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloadingUpdate)
            {
                return;
            }

            if (_lastUpdateInfo is null || !_lastUpdateInfo.IsUpdateAvailable)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_lastUpdateInfo.DownloadAssetUrl) ||
                string.IsNullOrWhiteSpace(_lastUpdateInfo.DownloadAssetName))
            {
                OpenReleasePageInBrowser();
                return;
            }

            _isDownloadingUpdate = true;
            try
            {
                UpdateProgramButton.IsEnabled = false;
                ShowLoadingOverlay("Скачивание обновления...", isIndeterminate: false);

                var updatesFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppStoragePaths.CurrentFolderName,
                    "Updates");
                Directory.CreateDirectory(updatesFolder);
                var destinationPath = Path.Combine(updatesFolder, _lastUpdateInfo.DownloadAssetName);

                var progress = new Progress<double>(value =>
                {
                    if (LoadingProgressBar is null || LoadingProgressText is null)
                    {
                        return;
                    }

                    var clamped = Math.Clamp(value, 0, 1);
                    LoadingProgressBar.Value = clamped * 100;
                    LoadingProgressText.Text = $"Загрузка обновления: {clamped * 100:0}%";
                });

                _updateDownloadCts = new CancellationTokenSource();
                await _updateService.DownloadAssetAsync(
                    _lastUpdateInfo.DownloadAssetUrl,
                    destinationPath,
                    progress,
                    _updateDownloadCts.Token);
                LaunchDownloadedUpdate(destinationPath);

                HideLoadingOverlay();
                UpdateStatusText.Text = "● Обновление загружено. Завершите установку и перезапустите программу";
                UpdateStatusText.Foreground = BrushFromHex("#E2C17A");
                RestartProgramButton.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException)
            {
                HideLoadingOverlay();
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                MessageBox.Show(
                    $"Не удалось скачать или запустить обновление:{Environment.NewLine}{ex.Message}",
                    "Обновление",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _updateDownloadCts?.Dispose();
                _updateDownloadCts = null;
                _isDownloadingUpdate = false;
                UpdateProgramButton.IsEnabled = true;
            }
        }

        private void RestartProgramButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentExePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(currentExePath))
                {
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = currentExePath,
                    UseShellExecute = true
                });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось перезапустить программу:{Environment.NewLine}{ex.Message}",
                    "Перезапуск",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void LaunchDownloadedUpdate(string updatePath)
        {
            var extension = Path.GetExtension(updatePath).ToLowerInvariant();
            var startInfo = extension switch
            {
                ".msi" => new ProcessStartInfo
                {
                    FileName = "msiexec",
                    Arguments = $"/i \"{updatePath}\"",
                    UseShellExecute = true
                },
                ".exe" => new ProcessStartInfo
                {
                    FileName = updatePath,
                    UseShellExecute = true
                },
                _ => new ProcessStartInfo
                {
                    FileName = _latestReleaseUrl,
                    UseShellExecute = true
                }
            };

            Process.Start(startInfo);
        }

        private void OpenReleasePageInBrowser()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _latestReleaseUrl,
                UseShellExecute = true
            });
        }

        private void ShowLoadingOverlay(string statusText, bool isIndeterminate)
        {
            if (LoadingStatusText is not null)
            {
                LoadingStatusText.Text = statusText;
            }

            if (LoadingProgressBar is not null)
            {
                LoadingProgressBar.IsIndeterminate = isIndeterminate;
                LoadingProgressBar.Value = 0;
            }

            if (LoadingProgressText is not null)
            {
                LoadingProgressText.Text = isIndeterminate ? string.Empty : "Загрузка обновления: 0%";
            }

            if (LoadingOverlay is not null)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
            }
        }

        private void HideLoadingOverlay()
        {
            if (LoadingOverlay is not null)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _searchDebounceTimer.Stop();
                _updateDownloadCts?.Cancel();
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private void InitializeLoadingBackgroundImage()
        {
            if (LoadingBackgroundImage is null)
            {
                return;
            }

            try
            {
                var resourceUri = new Uri("Assets/loading-screen.jpg", UriKind.Relative);
                var streamInfo = Application.GetResourceStream(resourceUri);
                if (streamInfo?.Stream is null)
                {
                    return;
                }

                using var resourceStream = streamInfo.Stream;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = resourceStream;
                image.EndInit();
                image.Freeze();
                LoadingBackgroundImage.Source = image;
            }
            catch
            {
                // Keep default dark background if custom image fails to load.
            }
        }
    }
}
