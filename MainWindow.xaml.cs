using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.IO.Compression;
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
        private const int XpFirstWatch = 900;
        private const int XpAchievement = 3500;
        private const double WatchSessionXpMultiplier = 1.30;
        private const double QuestRewardXpMultiplier = 1.40;

        private readonly ObservableCollection<YouTubeVideo> _videos = [];
        private readonly List<YouTubeVideo> _allVideos = [];
        private readonly Dictionary<string, DateTime> _watchHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _playbackSecondsHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<DateOnly> _watchedDays = [];
        private readonly HashSet<string> _favoriteVideoIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unlockedAchievements = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firstViewRewardedVideoIds = new(StringComparer.OrdinalIgnoreCase);
        private bool _isHistoryLoadedSuccessfully;
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
        private bool _updateButtonOpensReleasePage;

        private bool _suppressSelectionEvents;
        private bool _isOpeningVideo;
        private string? _selectedVideoId;
        private string _searchText = string.Empty;
        private bool _pendingDragRestoreFromMaximized;
        private Point _pendingDragStartPoint;
        private bool _isMainMinimizeAnimating;
        private readonly NetworkTimeService _networkTimeService = new();
        private DateTime _internetUtcAtSync;
        private DateTime _localUtcAtSync;
        private bool _hasInternetTime;
        private readonly DispatcherTimer _networkTimeSyncTimer = new();
        private readonly DispatcherTimer _questRolloverTimer = new();
        private readonly DispatcherTimer _questUiRefreshTimer = new();
        private DateOnly _lastQuestRefreshDay;
        private string _lastQuestRefreshWeekKey = string.Empty;
        private DateOnly? _activeDailyQuestDate;
        private readonly List<string> _activeDailyQuestIds = [];
        private string _activeWeeklyQuestWeekKey = string.Empty;
        private readonly List<string> _activeWeeklyQuestIds = [];
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
            InitializeSettingsUi();
            VideoList.ItemsSource = _videos;
            UpdateTopNavButtonsVisualState();
            StateChanged += MainWindow_StateChanged;
            Closed += MainWindow_Closed;
            UpdateMainWindowButtonsState();
            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            PreviewMouseMove += MainWindow_PreviewMouseMove;
            PreviewMouseLeftButtonUp += MainWindow_PreviewMouseLeftButtonUp;
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(180);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
            _networkTimeSyncTimer.Interval = TimeSpan.FromMinutes(5);
            _networkTimeSyncTimer.Tick += NetworkTimeSyncTimer_Tick;
            _questRolloverTimer.Interval = TimeSpan.FromSeconds(15);
            _questRolloverTimer.Tick += QuestRolloverTimer_Tick;
            _questUiRefreshTimer.Interval = TimeSpan.FromSeconds(1);
            _questUiRefreshTimer.Tick += QuestUiRefreshTimer_Tick;
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
                UpdateStatusText.Text = T("● Не удалось проверить обновления", "● Failed to check updates");
                UpdateStatusText.Foreground = BrushFromHex("#E4C487");
                _updateButtonOpensReleasePage = false;
                UpdateProgramButton.Visibility = Visibility.Collapsed;
                RestartProgramButton.Visibility = Visibility.Collapsed;
                return;
            }

            _latestReleaseUrl = string.IsNullOrWhiteSpace(updateInfo.ReleaseUrl)
                ? _latestReleaseUrl
                : updateInfo.ReleaseUrl;

            if (updateInfo.IsUpdateAvailable)
            {
                UpdateStatusText.Text = T($"● Доступно обновление {updateInfo.LatestVersionText}", $"● Update available {updateInfo.LatestVersionText}");
                UpdateStatusText.Foreground = BrushFromHex("#F0B56E");
                var hasDirectInstaller = !string.IsNullOrWhiteSpace(updateInfo.DownloadAssetUrl) &&
                                         !string.IsNullOrWhiteSpace(updateInfo.DownloadAssetName);
                _updateButtonOpensReleasePage = !hasDirectInstaller;
                UpdateProgramButton.Content = hasDirectInstaller
                    ? T("Обновить программу", "Update app")
                    : T("Открыть релиз", "Open release");
                UpdateProgramButton.Visibility = Visibility.Visible;
                RestartProgramButton.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateStatusText.Text = T("● Установлено последнее обновление", "● You are up to date");
            UpdateStatusText.Foreground = BrushFromHex("#82D7AA");
            _updateButtonOpensReleasePage = false;
            UpdateProgramButton.Visibility = Visibility.Collapsed;
            RestartProgramButton.Visibility = Visibility.Collapsed;
        }

        private async void UpdateProgramButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloadingUpdate)
            {
                return;
            }

            if (_updateButtonOpensReleasePage)
            {
                OpenReleasePageInBrowser();
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
                ShowLoadingOverlay(T("Скачивание обновления...", "Downloading update..."), isIndeterminate: false);

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
                    LoadingProgressText.Text = T($"Загрузка обновления: {clamped * 100:0}%", $"Update download: {clamped * 100:0}%");
                });

                _updateDownloadCts = new CancellationTokenSource();
                await _updateService.DownloadAssetAsync(
                    _lastUpdateInfo.DownloadAssetUrl,
                    destinationPath,
                    _lastUpdateInfo.DownloadAssetSizeBytes,
                    _lastUpdateInfo.DownloadAssetSha256,
                    progress,
                    _updateDownloadCts.Token);
                if (!string.Equals(Path.GetExtension(destinationPath), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        T(
                            $"Ожидается portable zip-архив для обновления, но получен файл: {Path.GetFileName(destinationPath)}",
                            $"Expected portable ZIP update archive, but got: {Path.GetFileName(destinationPath)}"));
                }

                ShowLoadingOverlay(T("Подготовка обновления...", "Preparing update..."), isIndeterminate: true);
                StartPortableSelfUpdate(destinationPath);

                HideLoadingOverlay();
                Application.Current.Shutdown();
                return;
            }
            catch (OperationCanceledException)
            {
                HideLoadingOverlay();
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                MessageBox.Show(
                    $"{T("Не удалось скачать или запустить обновление:", "Failed to download or apply update:")}{Environment.NewLine}{ex.Message}",
                    T("Обновление", "Update"),
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
                    $"{T("Не удалось перезапустить программу:", "Failed to restart app:")}{Environment.NewLine}{ex.Message}",
                    T("Перезапуск", "Restart"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void StartPortableSelfUpdate(string zipPath)
        {
            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExePath))
            {
                throw new InvalidOperationException(T(
                    "Не удалось определить путь к текущему исполняемому файлу.",
                    "Unable to resolve current executable path."));
            }

            var installDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
            {
                throw new InvalidOperationException(T(
                    "Не удалось определить папку установки приложения.",
                    "Unable to resolve application install directory."));
            }

            var updatesFolder = Path.GetDirectoryName(zipPath) ?? Path.GetTempPath();
            var extractRoot = Path.Combine(updatesFolder, $"update_extract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractRoot);
            ExtractZipSafely(zipPath, extractRoot);

            var exeName = Path.GetFileName(currentExePath);
            var stagedRoot = ResolveStagedRoot(extractRoot, exeName);
            if (string.IsNullOrWhiteSpace(stagedRoot))
            {
                throw new InvalidOperationException(
                    T($"В архиве обновления не найден исполняемый файл {exeName}.", $"Executable {exeName} was not found in update archive."));
            }

            var scriptPath = Path.Combine(updatesFolder, $"apply_update_{Guid.NewGuid():N}.ps1");
            var scriptContent = BuildApplyUpdateScript(
                parentProcessId: Environment.ProcessId,
                sourceDirectory: stagedRoot,
                targetDirectory: installDirectory,
                executableName: exeName);
            File.WriteAllText(scriptPath, scriptContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private static string ResolveStagedRoot(string extractedDirectory, string executableName)
        {
            if (File.Exists(Path.Combine(extractedDirectory, executableName)))
            {
                return extractedDirectory;
            }

            var directChildMatch = Directory.EnumerateDirectories(extractedDirectory)
                .FirstOrDefault(dir => File.Exists(Path.Combine(dir, executableName)));
            if (!string.IsNullOrWhiteSpace(directChildMatch))
            {
                return directChildMatch;
            }

            var deepMatch = Directory.EnumerateFiles(extractedDirectory, executableName, SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(dir => !string.IsNullOrWhiteSpace(dir));
            return deepMatch ?? string.Empty;
        }

        private static void ExtractZipSafely(string zipPath, string destinationDirectory)
        {
            var fullDestination = Path.GetFullPath(destinationDirectory);
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName))
                {
                    continue;
                }

                var targetPath = Path.GetFullPath(Path.Combine(fullDestination, entry.FullName));
                if (!targetPath.StartsWith(fullDestination, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsafe path inside update archive: {entry.FullName}");
                }

                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                    entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }

        private static string BuildApplyUpdateScript(int parentProcessId, string sourceDirectory, string targetDirectory, string executableName)
        {
            static string Esc(string value) => value.Replace("'", "''");
            return $@"$ErrorActionPreference = 'Stop'
$parentPid = {parentProcessId}
$source = '{Esc(sourceDirectory)}'
$target = '{Esc(targetDirectory)}'
$exeName = '{Esc(executableName)}'

for ($i = 0; $i -lt 240; $i++) {{
    try {{
        Get-Process -Id $parentPid -ErrorAction Stop | Out-Null
        Start-Sleep -Milliseconds 250
    }} catch {{
        break
    }}
}}

if (!(Test-Path $source) -or !(Test-Path $target)) {{
    exit 1
}}

$robocopyResult = & robocopy $source $target /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
$exitCode = $LASTEXITCODE
if ($exitCode -ge 8) {{
    exit $exitCode
}}

$updatedExe = Join-Path $target $exeName
if (Test-Path $updatedExe) {{
    Start-Process -FilePath $updatedExe
}}";
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
                LoadingProgressText.Text = isIndeterminate ? string.Empty : T("Загрузка обновления: 0%", "Update download: 0%");
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
                _networkTimeSyncTimer.Stop();
                _questRolloverTimer.Stop();
                _questUiRefreshTimer.Stop();
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
