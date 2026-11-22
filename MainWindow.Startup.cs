using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            WindowBoundsHelper.Attach(this);
            WindowMaterial.Attach(this);
            Closing += (_, args) =>
            {
                if (_activePlayer is null) return;
                args.Cancel = true;
                _closeAfterPlayer = true;
                _activePlayer.Close();
            };
            InitializeSettingsUi();
            InitializePsiIntro();
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

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyStartupMaximized();
            UpdateMainWindowButtonsState();
            ShowLoadingOverlay(string.Empty, isIndeterminate: true);

            try
            {
                _ = InitializeNetworkClockAsync();
                try { await LoadAndApplySettingsAsync(); }
                catch (Exception ex)
                {
                    _settingsReadFailed = true;
                    DiagnosticsService.LogException("Startup.Settings", ex);
                    MessageBox.Show(T("Настройки повреждены. Используются временные настройки.", "Using temporary settings because settings could not be loaded."));
                }
                PsiIntro.Start(_appSettings.IntroSoundEnabled, AreAnimationsEnabled);
                PsiIntro.SetCatalogState(false, true);
                InitializeLogViewer();
                ShowPreviousUpdateResult();
                DiagnosticsService.LogInfo("App", $"Started, v{GetCurrentAppVersion()}, lang={_appSettings.Language}, scale={_appSettings.UiScale:0.00}");
                var loadVideosTask = LoadVideosAsync();
                _ = _appSettings.AutoCheckUpdates
                    ? CheckForUpdatesDuringStartupAsync()
                    : SkipUpdateCheckAsync();
                await loadVideosTask;
                StartQuestRolloverMonitoring();
                _questUiRefreshTimer.Start();
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException("MainWindow_Loaded", ex);
                var logInfo = DiagnosticsService.IsEnabled()
                    ? $"{T("Лог ошибок:", "Error log:")} {DiagnosticsService.GetLogFilePath()}"
                    : T("Логирование отключено в настройках.", "Logging is disabled in Settings.");
                MessageBox.Show(
                    $"{T("Произошла ошибка инициализации приложения:", "App initialization failed:")}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}" +
                    logInfo,
                    T("Ошибка", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                HideLoadingOverlay();
                if (!_introDismissed)
                {
                    PsiIntro.Start(_appSettings.IntroSoundEnabled, AreAnimationsEnabled);
                    PsiIntro.SetCatalogState(_allVideos.Count > 0, false);
                }
            }
        }

        private void ApplyStartupMaximized()
        {
            Topmost = false;
            WindowState = WindowState.Maximized;
            Dispatcher.BeginInvoke(() =>
            {
                if (WindowState != WindowState.Minimized)
                {
                    WindowState = WindowState.Maximized;
                }
            }, DispatcherPriority.Loaded);
        }
    }
}
