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

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyStartupMaximized();
            UpdateMainWindowButtonsState();
            ShowLoadingOverlay(string.Empty, isIndeterminate: true);

            try
            {
                await InitializeNetworkClockAsync();
                await LoadAndApplySettingsAsync();
                var loadVideosTask = LoadVideosAsync();
                var updateCheckTask = CheckForUpdatesDuringStartupAsync();
                await Task.WhenAll(loadVideosTask, updateCheckTask);
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
