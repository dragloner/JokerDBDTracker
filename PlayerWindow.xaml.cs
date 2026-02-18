using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class PlayerWindow : Window
    {
        private readonly YouTubeVideo _video;
        private readonly int _startSeconds;
        private readonly DispatcherTimer _positionTimer = new();
        private readonly (CheckBox toggle, FrameworkElement details)[] _effectDetails;
        private bool _isRecoveringBlockedNavigation;
        private const string DesktopChromeUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private bool _effectsPanelExpanded = true;
        private bool _isApplyingEffects;
        private bool _pendingEffectsApply;
        private bool _isPlayerElementFullScreen;
        private WindowState _windowStateBeforePlayerFullscreen = WindowState.Maximized;
        private ResizeMode _resizeModeBeforePlayerFullscreen = ResizeMode.CanResize;
        private double _lastMeasuredTime = -1;
        private double _watchXpBuffer;
        private double _eligibleWatchSeconds;
        private DateTime? _lastXpSampleUtc;
        private bool _halfHourBonusGranted;
        private bool _hourBonusGranted;
        private readonly DispatcherTimer _effectsApplyDebounceTimer = new();
        private readonly DispatcherTimer _resizeSettleDebounceTimer = new();
        private readonly WatchHistoryService _watchHistoryPersistService = new();
        private int _lastPersistedPlaybackSeconds;
        private DateTime _lastPlaybackPersistUtc = DateTime.MinValue;
        private bool _isPersistingPlayback;
        private int _effectRefreshCounter;
        private bool _isEffectsBurstReapplyRunning;
        private static readonly SemaphoreSlim PlaybackPersistLock = new(1, 1);
        private const int PlaybackPersistIntervalSeconds = 10;
        private const int EffectRefreshTickInterval = 3;
        private const int ResizeSettleDelayMilliseconds = 180;
        private const int HalfHourWatchBonusXp = 25;
        private const int OneHourWatchBonusXp = 60;
        private bool _isResizeInteractionInProgress;

        private sealed class EffectSettings
        {
            public bool[] Flags { get; init; } = [];
            public double Contrast { get; init; }
            public double Darkness { get; init; }
            public double Saturation { get; init; }
            public double HueShift { get; init; }
            public double Blur { get; init; }
            public double RedGlow { get; init; }
            public double Vhs { get; init; }
            public double Shake { get; init; }
            public double Pixelation { get; init; }
            public double ColdTone { get; init; }
            public double VioletGlow { get; init; }
        }

        public int LastPlaybackSeconds { get; private set; }
        public bool CursedMasterUnlocked { get; private set; }
        public int WatchXpEarned { get; private set; }
        public bool WatchedWithAnyEffects { get; private set; }
        public int MaxEnabledEffectsCount { get; private set; }
        public bool UsedStrongBlur { get; private set; }
        public bool UsedStrongRedGlow { get; private set; }
        public bool UsedStrongVioletGlow { get; private set; }
        public bool UsedStrongShake { get; private set; }

        public PlayerWindow(YouTubeVideo video, int startSeconds)
        {
            InitializeComponent();
            WindowBoundsHelper.Attach(this);
            _video = video;
            _startSeconds = Math.Max(startSeconds, 0);
            LastPlaybackSeconds = _startSeconds;
            _lastPersistedPlaybackSeconds = _startSeconds;
            _effectDetails =
            [
                (Fx4, Fx4Details),
                (Fx5, Fx5Details),
                (Fx6, Fx6Details),
                (Fx7, Fx7Details),
                (Fx8, Fx8Details),
                (Fx9, Fx9Details),
                (Fx10, Fx10Details),
                (Fx11, Fx11Details),
                (Fx13, Fx13Details),
                (Fx14, Fx14Details),
                (Fx15, Fx15Details)
            ];

            Loaded += PlayerWindow_Loaded;
            Closing += PlayerWindow_Closing;
            Closed += PlayerWindow_Closed;
            StateChanged += PlayerWindow_StateChanged;
            SizeChanged += PlayerWindow_SizeChanged;
            LocationChanged += PlayerWindow_LocationChanged;

            _positionTimer.Interval = TimeSpan.FromSeconds(2);
            _positionTimer.Tick += PositionTimer_Tick;
            _effectsApplyDebounceTimer.Interval = TimeSpan.FromMilliseconds(70);
            _effectsApplyDebounceTimer.Tick += EffectsApplyDebounceTimer_Tick;
            _resizeSettleDebounceTimer.Interval = TimeSpan.FromMilliseconds(ResizeSettleDelayMilliseconds);
            _resizeSettleDebounceTimer.Tick += ResizeSettleDebounceTimer_Tick;
        }

        private async void PlayerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyStartupMaximizedWindowed();
            VideoTitleText.Text = _video.Title;
            UpdateWindowSizeButtonState();
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: false);
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

            try
            {
                var userDataFolder = await Task.Run(ResolveWebViewUserDataFolder);
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
                await Player.EnsureCoreWebView2Async(environment);

                Player.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                Player.CoreWebView2.Settings.IsStatusBarEnabled = false;
                Player.CoreWebView2.Settings.AreDevToolsEnabled = false;
                Player.CoreWebView2.Settings.IsZoomControlEnabled = true;
                Player.CoreWebView2.Settings.UserAgent = DesktopChromeUserAgent;
                Player.CoreWebView2.ContainsFullScreenElementChanged += CoreWebView2_ContainsFullScreenElementChanged;
                Player.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                Player.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                Player.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                Player.CoreWebView2.Navigate(BuildLockedWatchUrl(_startSeconds));
                _positionTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось инициализировать плеер:{Environment.NewLine}{ex.Message}",
                    "Ошибка плеера",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // Block opening any external windows/tabs from the embedded player.
            e.Handled = true;
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (IsAllowedPlayerNavigation(e.Uri))
            {
                return;
            }

            e.Cancel = true;
            _ = RecoverLockedVideoAsync();
        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess || Player.CoreWebView2 is null)
            {
                return;
            }

            try
            {
                await Player.CoreWebView2.ExecuteScriptAsync(BuildKioskModeScript(_video.VideoId));
                await ApplyEffectsSafelyAsync();
            }
            catch
            {
                // Ignore transient script failures during YouTube page bootstrap.
            }
        }

        private async Task RecoverLockedVideoAsync()
        {
            if (Player.CoreWebView2 is null || _isRecoveringBlockedNavigation)
            {
                return;
            }

            _isRecoveringBlockedNavigation = true;
            try
            {
                var resumeSeconds = Math.Max(0, LastPlaybackSeconds);
                Player.CoreWebView2.Navigate(BuildLockedWatchUrl(resumeSeconds));
            }
            finally
            {
                await Task.Delay(120);
                _isRecoveringBlockedNavigation = false;
            }
        }

        private string BuildLockedWatchUrl(int startSeconds)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["v"] = _video.VideoId;
            query["autoplay"] = "1";
            if (startSeconds > 0)
            {
                query["t"] = $"{startSeconds}s";
            }

            return $"https://www.youtube.com/watch?{query}";
        }

        private static string ResolveWebViewUserDataFolder()
        {
            var stableFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppStoragePaths.CurrentFolderName,
                "YouTube_Profile");
            Directory.CreateDirectory(stableFolder);

            var legacyFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "YouTube_Profile");
            TryMigrateLegacyProfile(legacyFolder, stableFolder);
            return stableFolder;
        }

        private static void TryMigrateLegacyProfile(string sourceFolder, string targetFolder)
        {
            try
            {
                if (!Directory.Exists(sourceFolder))
                {
                    return;
                }

                if (string.Equals(sourceFolder, targetFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (Directory.EnumerateFileSystemEntries(targetFolder).Any())
                {
                    return;
                }

                foreach (var directory in Directory.EnumerateDirectories(sourceFolder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(sourceFolder, directory);
                    Directory.CreateDirectory(Path.Combine(targetFolder, relative));
                }

                foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(sourceFolder, file);
                    var destination = Path.Combine(targetFolder, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, overwrite: false);
                }
            }
            catch
            {
                // Migration failures should not break player startup.
            }
        }

        private static string BuildKioskModeScript(string videoId)
        {
            var safeVideoId = videoId.Replace("\\", "\\\\").Replace("'", "\\'");
            return $$"""
                (() => {
                    const expectedVideoId = '{{safeVideoId}}';
                    const installScrollLock = () => {
                        if (window.__jdbdScrollLockInstalled) {
                            return;
                        }

                        window.__jdbdScrollLockInstalled = true;
                        const preventScroll = (event) => {
                            event.preventDefault();
                        };

                        const blockedKeys = new Set([
                            'ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' '
                        ]);

                        document.addEventListener('wheel', preventScroll, { passive: false, capture: true });
                        document.addEventListener('touchmove', preventScroll, { passive: false, capture: true });
                        document.addEventListener('keydown', (event) => {
                            if (blockedKeys.has(event.key)) {
                                event.preventDefault();
                            }
                        }, { capture: true });

                        document.addEventListener('click', (event) => {
                            const link = event.target && event.target.closest ? event.target.closest('a[href*="/watch"]') : null;
                            if (!link) {
                                return;
                            }

                            try {
                                const url = new URL(link.href, location.origin);
                                const video = url.searchParams.get('v');
                                if (!video || video !== expectedVideoId) {
                                    event.preventDefault();
                                    event.stopPropagation();
                                }
                            } catch {
                                event.preventDefault();
                                event.stopPropagation();
                            }
                        }, { capture: true });
                    };

                    const applyKioskMode = () => {
                        const current = new URL(location.href);
                        if (!current.hostname.endsWith('youtube.com')) {
                            return;
                        }

                        if (current.pathname === '/watch') {
                            const currentVideo = current.searchParams.get('v');
                            if (currentVideo && currentVideo !== expectedVideoId) {
                                current.searchParams.set('v', expectedVideoId);
                                location.replace(current.toString());
                                return;
                            }
                        }

                        const player = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
                        if (!player) {
                            return;
                        }

                        document.documentElement.style.setProperty('overflow', 'hidden', 'important');
                        document.documentElement.style.setProperty('background', '#000', 'important');
                        document.documentElement.style.setProperty('margin', '0', 'important');
                        document.documentElement.style.setProperty('padding', '0', 'important');

                        document.body.style.setProperty('margin', '0', 'important');
                        document.body.style.setProperty('padding', '0', 'important');
                        document.body.style.setProperty('overflow', 'hidden', 'important');
                        document.body.style.setProperty('background', '#000', 'important');

                        for (const scrollRoot of ['ytd-app', '#content', '#page-manager', 'ytd-watch-flexy', '#columns']) {
                            const node = document.querySelector(scrollRoot);
                            if (!node) {
                                continue;
                            }

                            node.style.setProperty('overflow', 'hidden', 'important');
                            node.scrollTop = 0;
                        }
                        window.scrollTo(0, 0);

                        const hideSelectors = [
                            'ytd-masthead',
                            '#masthead-container',
                            '#secondary',
                            '#secondary-inner',
                            '#related',
                            '#below',
                            '#comments',
                            'ytd-comments',
                            'ytd-watch-metadata',
                            'ytd-watch-info-text',
                            '#description',
                            '#description-inline-expander',
                            '#description-inner',
                            'ytd-text-inline-expander',
                            'ytd-engagement-panel-section-list-renderer',
                            '#panels',
                            'ytd-watch-flexy[engagement-panel-visible]',
                            '#chat',
                            '#chat-container',
                            '#guide',
                            'ytd-mini-guide-renderer',
                            'tp-yt-app-drawer',
                            'ytd-reel-shelf-renderer',
                            'ytd-watch-next-secondary-results-renderer',
                            'ytd-watch-next-feed-renderer',
                            '#end',
                            '#meta',
                            '#columns > :not(#primary)'
                        ];
                        for (const selector of hideSelectors) {
                            for (const element of document.querySelectorAll(selector)) {
                                element.style.setProperty('display', 'none', 'important');
                            }
                        }

                        const flexy = document.querySelector('ytd-watch-flexy');
                        if (flexy) {
                            flexy.removeAttribute('is-two-columns_');
                            flexy.setAttribute('theater', '');
                            flexy.style.setProperty('height', '100vh', 'important');
                            flexy.style.setProperty('max-height', '100vh', 'important');
                            flexy.style.setProperty('overflow', 'hidden', 'important');
                        }

                        const primary = document.querySelector('#primary');
                        if (primary) {
                            primary.style.setProperty('max-height', '100vh', 'important');
                            primary.style.setProperty('overflow', 'hidden', 'important');
                        }
                    };

                    installScrollLock();
                    applyKioskMode();
                    const observer = new MutationObserver(applyKioskMode);
                    observer.observe(document.documentElement, { childList: true, subtree: true });
                    setTimeout(applyKioskMode, 250);
                    setTimeout(applyKioskMode, 1000);
                })();
                """;
        }

        private bool IsAllowedPlayerNavigation(string? uriText)
        {
            if (string.IsNullOrWhiteSpace(uriText))
            {
                return false;
            }

            if (string.Equals(uriText, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var host = uri.Host.ToLowerInvariant();
            var isGoogleHost = host == "google.com" ||
                               host.EndsWith(".google.com", StringComparison.Ordinal) ||
                               host.EndsWith(".gstatic.com", StringComparison.Ordinal) ||
                               host.EndsWith(".googleusercontent.com", StringComparison.Ordinal);
            if (isGoogleHost)
            {
                return true;
            }

            if (host != "youtube.com" && !host.EndsWith(".youtube.com", StringComparison.Ordinal))
            {
                return false;
            }

            if (uri.AbsolutePath.StartsWith("/signin", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/oauth", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                var query = HttpUtility.ParseQueryString(uri.Query);
                var videoId = query.Get("v");
                return string.Equals(videoId, _video.VideoId, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }


        private void ApplyStartupMaximizedWindowed()
        {
            Topmost = false;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Maximized;
            Dispatcher.BeginInvoke(() =>
            {
                if (WindowState != WindowState.Minimized)
                {
                    WindowState = WindowState.Maximized;
                }
            }, DispatcherPriority.Loaded);
        }

        private void CoreWebView2_ContainsFullScreenElementChanged(object? sender, object e)
        {
            if (Player.CoreWebView2 is null)
            {
                return;
            }

            var shouldBeFullscreen = Player.CoreWebView2.ContainsFullScreenElement;
            if (shouldBeFullscreen == _isPlayerElementFullScreen)
            {
                return;
            }

            _isPlayerElementFullScreen = shouldBeFullscreen;
            if (shouldBeFullscreen)
            {
                _windowStateBeforePlayerFullscreen = WindowState;
                _resizeModeBeforePlayerFullscreen = ResizeMode;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                ApplyFullMonitorBounds();
                Activate();
                PulseTopmost();
                RequestApplyEffects(immediate: true);
                TriggerEffectsReapplyBurst();
                return;
            }

            RestoreWindowAfterPlayerFullscreen();
        }

        private void RestoreWindowAfterPlayerFullscreen()
        {
            ResizeMode = _resizeModeBeforePlayerFullscreen == ResizeMode.NoResize
                ? ResizeMode.CanResize
                : _resizeModeBeforePlayerFullscreen;
            WindowState = _windowStateBeforePlayerFullscreen == WindowState.Minimized
                ? WindowState.Maximized
                : _windowStateBeforePlayerFullscreen;
            RequestApplyEffects(immediate: true);
            TriggerEffectsReapplyBurst();
        }

        private async Task ExitEmbeddedPlayerFullscreenAsync()
        {
            if (!_isPlayerElementFullScreen || Player.CoreWebView2 is null)
            {
                return;
            }

            try
            {
                const string exitFullscreenScript = """
                    (() => {
                        try {
                            const doc = document;
                            if (doc.fullscreenElement && doc.exitFullscreen) {
                                doc.exitFullscreen();
                            }
                        } catch {
                            // no-op
                        }

                        try {
                            const button = document.querySelector('.ytp-fullscreen-button');
                            const player = document.getElementById('movie_player');
                            if (button && player && typeof player.isFullscreen === 'function' && player.isFullscreen()) {
                                button.click();
                            }
                        } catch {
                            // no-op
                        }
                    })();
                    """;
                await Player.CoreWebView2.ExecuteScriptAsync(exitFullscreenScript);
                await Task.Delay(60);
            }
            catch
            {
                // If web script fails, fallback to local window restore.
            }

            if (_isPlayerElementFullScreen)
            {
                _isPlayerElementFullScreen = false;
                RestoreWindowAfterPlayerFullscreen();
            }
        }

        private void PulseTopmost()
        {
            Topmost = true;
            Dispatcher.BeginInvoke(() =>
            {
                Topmost = false;
            }, DispatcherPriority.ApplicationIdle);
        }

        private void ApplyFullMonitorBounds()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                Left = 0;
                Top = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
                return;
            }

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                Left = 0;
                Top = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
                return;
            }

            var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                Left = 0;
                Top = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
                return;
            }

            var leftPx = monitorInfo.rcMonitor.left;
            var topPx = monitorInfo.rcMonitor.top;
            var rightPx = monitorInfo.rcMonitor.right;
            var bottomPx = monitorInfo.rcMonitor.bottom;

            if (PresentationSource.FromVisual(this) is HwndSource source &&
                source.CompositionTarget is not null)
            {
                var transform = source.CompositionTarget.TransformFromDevice;
                var topLeftDip = transform.Transform(new System.Windows.Point(leftPx, topPx));
                var bottomRightDip = transform.Transform(new System.Windows.Point(rightPx, bottomPx));
                Left = topLeftDip.X;
                Top = topLeftDip.Y;
                Width = Math.Max(1, bottomRightDip.X - topLeftDip.X);
                Height = Math.Max(1, bottomRightDip.Y - topLeftDip.Y);
                return;
            }

            Left = monitorInfo.rcMonitor.left;
            Top = monitorInfo.rcMonitor.top;
            Width = Math.Max(1, monitorInfo.rcMonitor.right - monitorInfo.rcMonitor.left);
            Height = Math.Max(1, monitorInfo.rcMonitor.bottom - monitorInfo.rcMonitor.top);
        }

        private async void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (Player.CoreWebView2 is null)
            {
                return;
            }

            try
            {
                const string script = """
                    (() => {
                        const video = document.querySelector('video');
                        if (!video || !Number.isFinite(video.currentTime) || !Number.isFinite(video.duration)) {
                            return { current: -1, duration: 0, paused: true, seeking: false, playbackRate: 1 };
                        }
                        return {
                            current: video.currentTime,
                            duration: video.duration,
                            paused: !!video.paused,
                            seeking: !!video.seeking,
                            playbackRate: Number.isFinite(video.playbackRate) ? video.playbackRate : 1
                        };
                    })();
                    """;

                var raw = await Player.CoreWebView2.ExecuteScriptAsync(script);
                using var document = JsonDocument.Parse(raw);
                var current = document.RootElement.GetProperty("current").GetDouble();
                var duration = document.RootElement.GetProperty("duration").GetDouble();
                var paused = document.RootElement.GetProperty("paused").GetBoolean();
                var seeking = document.RootElement.GetProperty("seeking").GetBoolean();
                var playbackRate = document.RootElement.GetProperty("playbackRate").GetDouble();
                if (current < 0 || duration <= 0)
                {
                    _lastXpSampleUtc = DateTime.UtcNow;
                    return;
                }

                LastPlaybackSeconds = (int)Math.Floor(current);
                var nowUtc = DateTime.UtcNow;
                if (!_lastXpSampleUtc.HasValue)
                {
                    _lastXpSampleUtc = nowUtc;
                    _lastMeasuredTime = current;
                    if (_isResizeInteractionInProgress)
                    {
                        RequestApplyEffects(immediate: false);
                    }
                    else
                    {
                        await ApplyEffectsSafelyAsync();
                    }
                    UpdateCursedAchievementState(current, duration);
                    return;
                }

                var elapsedWallSeconds = (nowUtc - _lastXpSampleUtc.Value).TotalSeconds;
                _lastXpSampleUtc = nowUtc;

                var playbackDelta = _lastMeasuredTime >= 0 ? current - _lastMeasuredTime : 0;
                var positivePlaybackRate = Math.Clamp(playbackRate, 0.1, 4.0);
                var maxExpectedDelta = Math.Max(0.7, elapsedWallSeconds * positivePlaybackRate * 1.75 + 0.7);
                var progressionLooksNatural = playbackDelta >= -0.35 && playbackDelta <= maxExpectedDelta;
                var canCreditXp = !paused &&
                                  !seeking &&
                                  elapsedWallSeconds > 0 &&
                                  elapsedWallSeconds <= 8 &&
                                  playbackDelta > 0.05 &&
                                  progressionLooksNatural;

                if (canCreditXp)
                {
                    var creditedSeconds = Math.Min(elapsedWallSeconds, _positionTimer.Interval.TotalSeconds + 1.0);
                    var multiplier = 1.0 + GetActiveEffectsCount() * 0.08;
                    _watchXpBuffer += (creditedSeconds / 10.0) * multiplier;
                    _eligibleWatchSeconds += creditedSeconds;
                    UpdateEffectSessionStats();
                    TryApplyLongWatchBonuses();
                }

                _lastMeasuredTime = current;
                WatchXpEarned = (int)Math.Floor(_watchXpBuffer);

                var activeEffects = GetActiveEffectsCount();
                var shakeEnabled = Fx11.IsChecked == true;
                if (!_isResizeInteractionInProgress && activeEffects > 0 && !shakeEnabled)
                {
                    _effectRefreshCounter++;
                    if (_effectRefreshCounter >= EffectRefreshTickInterval)
                    {
                        _effectRefreshCounter = 0;
                        await ApplyEffectsSafelyAsync();
                    }
                }
                else
                {
                    _effectRefreshCounter = 0;
                }

                _ = PersistPlaybackPositionAsync(force: false);
                UpdateCursedAchievementState(current, duration);
            }
            catch
            {
                // Ignore transient script errors.
            }
        }

        private void UpdateCursedAchievementState(double current, double duration)
        {
            if (CursedMasterUnlocked || duration <= 0)
            {
                return;
            }

            var fullyWatched = current >= duration * 0.98;
            if (fullyWatched && GetActiveEffectsCount() == 15)
            {
                CursedMasterUnlocked = true;
            }
        }

        private void UpdateEffectSessionStats()
        {
            var activeEffects = GetActiveEffectsCount();
            if (activeEffects > 0)
            {
                WatchedWithAnyEffects = true;
                MaxEnabledEffectsCount = Math.Max(MaxEnabledEffectsCount, activeEffects);
            }

            if (Fx8.IsChecked == true && BlurStrengthSlider.Value >= 0.75)
            {
                UsedStrongBlur = true;
            }

            if (Fx9.IsChecked == true && RedGlowStrengthSlider.Value >= 0.75)
            {
                UsedStrongRedGlow = true;
            }

            if (Fx15.IsChecked == true && VioletGlowStrengthSlider.Value >= 0.75)
            {
                UsedStrongVioletGlow = true;
            }

            if (Fx11.IsChecked == true && ShakeStrengthSlider.Value >= 0.75)
            {
                UsedStrongShake = true;
            }
        }

        private void EffectToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: true);
            RequestApplyEffects(immediate: true);
        }

        private void StrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RequestApplyEffects(immediate: false);
        }

        private async void EffectsApplyDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _effectsApplyDebounceTimer.Stop();
            await ApplyEffectsSafelyAsync();
        }

        private void RequestApplyEffects(bool immediate)
        {
            _effectRefreshCounter = 0;
            if (immediate)
            {
                _effectsApplyDebounceTimer.Stop();
                _ = ApplyEffectsSafelyAsync();
                return;
            }

            _effectsApplyDebounceTimer.Stop();
            _effectsApplyDebounceTimer.Start();
        }

        private async Task ApplyEffectsSafelyAsync()
        {
            if (_isApplyingEffects)
            {
                _pendingEffectsApply = true;
                return;
            }

            _isApplyingEffects = true;
            try
            {
                do
                {
                    _pendingEffectsApply = false;
                    await ApplyEffectsAsync();
                }
                while (_pendingEffectsApply);
            }
            finally
            {
                _isApplyingEffects = false;
            }
        }

        private int GetActiveEffectsCount()
        {
            return GetEffectsState().Count(v => v);
        }

        private bool[] GetEffectsState()
        {
            return
            [
                Fx1.IsChecked == true,
                Fx2.IsChecked == true,
                Fx3.IsChecked == true,
                Fx4.IsChecked == true,
                Fx5.IsChecked == true,
                Fx6.IsChecked == true,
                Fx7.IsChecked == true,
                Fx8.IsChecked == true,
                Fx9.IsChecked == true,
                Fx10.IsChecked == true,
                Fx11.IsChecked == true,
                Fx12.IsChecked == true,
                Fx13.IsChecked == true,
                Fx14.IsChecked == true,
                Fx15.IsChecked == true
            ];
        }

        private EffectSettings GetEffectSettings()
        {
            static double NormalizeSigned(Slider slider) => Math.Clamp(slider.Value, -1, 1);
            static double NormalizePositive(Slider slider) => Math.Clamp(slider.Value, 0, 1);

            return new EffectSettings
            {
                Flags = GetEffectsState(),
                Contrast = NormalizeSigned(ContrastStrengthSlider),
                Darkness = NormalizeSigned(DarknessStrengthSlider),
                Saturation = NormalizeSigned(SaturationStrengthSlider),
                HueShift = NormalizeSigned(HueShiftStrengthSlider),
                Blur = NormalizePositive(BlurStrengthSlider),
                RedGlow = NormalizePositive(RedGlowStrengthSlider),
                Vhs = NormalizePositive(VhsStrengthSlider),
                Shake = NormalizePositive(ShakeStrengthSlider),
                Pixelation = NormalizePositive(PixelationStrengthSlider),
                ColdTone = NormalizeSigned(ColdToneStrengthSlider),
                VioletGlow = NormalizePositive(VioletGlowStrengthSlider)
            };
        }

        private async Task ApplyEffectsAsync()
        {
            if (Player.CoreWebView2 is null)
            {
                return;
            }

            var settingsJson = JsonSerializer.Serialize(GetEffectSettings());

            var script = $$"""
                (() => {
                    const settings = {{settingsJson}};
                    const flags = settings.Flags;
                    const video = document.querySelector('video');
                    if (!video) return;
                    const activeCount = flags.reduce((acc, v) => acc + (v ? 1 : 0), 0);
                    const clearMirrorGuard = () => {
                        if (!window.__stwfxMirrorGuard) {
                            return;
                        }

                        try {
                            window.__stwfxMirrorGuard.disconnect();
                        } catch {
                            // no-op
                        }
                        window.__stwfxMirrorGuard = null;
                    };

                    const installMirrorGuard = (targetVideo) => {
                        clearMirrorGuard();
                        const enforceMirror = () => {
                            targetVideo.style.setProperty('transform-origin', 'center center', 'important');
                            targetVideo.style.setProperty('transform', 'scaleX(-1)', 'important');
                        };

                        const observer = new MutationObserver(() => {
                            enforceMirror();
                        });
                        observer.observe(targetVideo, {
                            attributes: true,
                            attributeFilter: ['style', 'class']
                        });

                        enforceMirror();
                        window.__stwfxMirrorGuard = {
                            disconnect: () => observer.disconnect()
                        };
                    };

                    if (activeCount === 0) {
                        clearMirrorGuard();
                        video.style.setProperty('filter', 'none', 'important');
                        video.style.setProperty('transform', 'none', 'important');
                        video.style.setProperty('transform-origin', 'center center', 'important');
                        video.style.setProperty('image-rendering', 'auto', 'important');
                        video.style.setProperty('animation', 'none', 'important');

                        const existingOverlay = document.getElementById('stwfx-vhs');
                        if (existingOverlay) {
                            existingOverlay.remove();
                        }
                        return;
                    }

                    const filters = [];
                    if (flags[0]) filters.push('grayscale(1)');
                    if (flags[1]) filters.push('sepia(1)');
                    if (flags[2]) filters.push('invert(1)');
                    if (flags[3]) {
                        const contrast = settings.Contrast >= 0
                            ? 1 + settings.Contrast * 4.0
                            : 1 + settings.Contrast * 0.75;
                        filters.push(`contrast(${Math.max(0.2, contrast).toFixed(2)})`);
                    }
                    if (flags[4]) {
                        const brightness = 1 - settings.Darkness * 0.9;
                        filters.push(`brightness(${Math.max(0.1, brightness).toFixed(2)})`);
                    }
                    if (flags[5]) {
                        const saturation = settings.Saturation >= 0
                            ? 1 + settings.Saturation * 5.0
                            : 1 + settings.Saturation * 0.95;
                        filters.push(`saturate(${Math.max(0.05, saturation).toFixed(2)})`);
                    }
                    if (flags[6]) filters.push(`hue-rotate(${Math.round(settings.HueShift * 260)}deg)`);
                    if (flags[7]) filters.push(`blur(${(0.6 + settings.Blur * 9.4).toFixed(1)}px)`);
                    if (flags[8]) filters.push(`drop-shadow(0 0 ${(10 + settings.RedGlow * 48).toFixed(1)}px rgba(255,35,35,${(0.35 + settings.RedGlow * 0.65).toFixed(2)}))`);
                    if (flags[12]) filters.push(`contrast(${(1.05 + settings.Pixelation * 1.40).toFixed(2)})`);
                    if (flags[13]) {
                        const toneHue = settings.ColdTone >= 0
                            ? 170 + settings.ColdTone * 170
                            : settings.ColdTone * 120;
                        const toneSat = 1 + Math.abs(settings.ColdTone) * 1.6;
                        filters.push(`hue-rotate(${Math.round(toneHue)}deg) saturate(${toneSat.toFixed(2)})`);
                    }
                    if (flags[14]) filters.push(`drop-shadow(0 0 ${(12 + settings.VioletGlow * 52).toFixed(1)}px rgba(186,85,255,${(0.35 + settings.VioletGlow * 0.65).toFixed(2)}))`);

                    video.style.setProperty('filter', filters.length > 0 ? filters.join(' ') : 'none', 'important');
                    const mirrorScaleX = flags[11] ? -1 : 1;
                    const mirrorEnabled = mirrorScaleX === -1;
                    const shakeEnabled = flags[10];
                    video.style.setProperty('image-rendering', flags[12] ? 'pixelated' : 'auto', 'important');
                    video.style.setProperty('transform-origin', 'center center', 'important');

                    const shakeDuration = Math.max(0.03, 0.20 - settings.Shake * 0.17);
                    const shakeAmp = 1 + settings.Shake * 10.0;

                    let style = document.getElementById('stwfx-style');
                    if (!style) {
                        style = document.createElement('style');
                        style.id = 'stwfx-style';
                        document.head.appendChild(style);
                    }
                    const mirrorTransform = mirrorScaleX === -1 ? ' scaleX(-1)' : '';
                    style.textContent = `
                        @keyframes stw_shake {
                            0% { transform: translate(0,0)${mirrorTransform}; }
                            25% { transform: translate(${shakeAmp.toFixed(2)}px, ${(-shakeAmp).toFixed(2)}px)${mirrorTransform}; }
                            50% { transform: translate(${(-shakeAmp).toFixed(2)}px, ${shakeAmp.toFixed(2)}px)${mirrorTransform}; }
                            75% { transform: translate(${shakeAmp.toFixed(2)}px, ${shakeAmp.toFixed(2)}px)${mirrorTransform}; }
                            100% { transform: translate(0,0)${mirrorTransform}; }
                        }
                    `;

                    if (mirrorEnabled && !shakeEnabled) {
                        video.style.setProperty('transform', 'scaleX(-1)', 'important');
                        installMirrorGuard(video);
                    } else {
                        clearMirrorGuard();
                        video.style.setProperty('transform', mirrorEnabled ? 'scaleX(-1)' : 'none', 'important');
                    }

                    video.style.setProperty('animation', 'none', 'important');
                    void video.offsetWidth;
                    video.style.setProperty(
                        'animation',
                        shakeEnabled ? `stw_shake ${shakeDuration.toFixed(2)}s infinite linear` : 'none',
                        'important');

                    let overlay = document.getElementById('stwfx-vhs');
                    if (!overlay) {
                        overlay = document.createElement('div');
                        overlay.id = 'stwfx-vhs';
                        overlay.style.position = 'fixed';
                        overlay.style.left = '0';
                        overlay.style.top = '0';
                        overlay.style.width = '100%';
                        overlay.style.height = '100%';
                        overlay.style.pointerEvents = 'none';
                        overlay.style.mixBlendMode = 'screen';
                        overlay.style.backgroundImage =
                            'repeating-linear-gradient(0deg, rgba(255,255,255,0.26) 0px, rgba(255,255,255,0.26) 1px, rgba(0,0,0,0) 2px, rgba(0,0,0,0) 4px), ' +
                            'repeating-linear-gradient(180deg, rgba(15,15,15,0.18) 0px, rgba(15,15,15,0.18) 2px, rgba(0,0,0,0) 3px, rgba(0,0,0,0) 5px)';
                        overlay.style.zIndex = '2147483647';
                        overlay.style.display = 'none';
                        document.body.appendChild(overlay);
                    }
                    overlay.style.display = flags[9] ? 'block' : 'none';
                    overlay.style.opacity = (0.30 + settings.Vhs * 0.70).toFixed(2);
                })();
                """;

            await Player.CoreWebView2.ExecuteScriptAsync(script);
        }

        private async void ResetEffectsButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var effect in new[]
                     {
                         Fx1, Fx2, Fx3, Fx4, Fx5,
                         Fx6, Fx7, Fx8, Fx9, Fx10,
                         Fx11, Fx12, Fx13, Fx14, Fx15
                     })
            {
                effect.IsChecked = false;
            }

            ContrastStrengthSlider.Value = 0;
            DarknessStrengthSlider.Value = 0;
            SaturationStrengthSlider.Value = 0;
            HueShiftStrengthSlider.Value = 0;
            BlurStrengthSlider.Value = 0;
            RedGlowStrengthSlider.Value = 0;
            VhsStrengthSlider.Value = 0;
            ShakeStrengthSlider.Value = 0;
            PixelationStrengthSlider.Value = 0.45;
            ColdToneStrengthSlider.Value = 0;
            VioletGlowStrengthSlider.Value = 0;
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: false);

            await ApplyEffectsSafelyAsync();
        }

        private void UpdateStrengthSlidersEnabledState()
        {
            ContrastStrengthSlider.IsEnabled = Fx4.IsChecked == true;
            DarknessStrengthSlider.IsEnabled = Fx5.IsChecked == true;
            SaturationStrengthSlider.IsEnabled = Fx6.IsChecked == true;
            HueShiftStrengthSlider.IsEnabled = Fx7.IsChecked == true;
            BlurStrengthSlider.IsEnabled = Fx8.IsChecked == true;
            RedGlowStrengthSlider.IsEnabled = Fx9.IsChecked == true;
            VhsStrengthSlider.IsEnabled = Fx10.IsChecked == true;
            ShakeStrengthSlider.IsEnabled = Fx11.IsChecked == true;
            PixelationStrengthSlider.IsEnabled = Fx13.IsChecked == true;
            ColdToneStrengthSlider.IsEnabled = Fx14.IsChecked == true;
            VioletGlowStrengthSlider.IsEnabled = Fx15.IsChecked == true;
        }

        private void UpdateEffectDetailsVisibility(bool animate)
        {
            foreach (var (toggle, details) in _effectDetails)
            {
                AnimateDetailsPanel(details, toggle.IsChecked == true, animate);
            }
        }

        private static void AnimateDetailsPanel(FrameworkElement details, bool expand, bool animate)
        {
            const double expandedHeight = 44;
            details.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            details.BeginAnimation(UIElement.OpacityProperty, null);

            if (!animate)
            {
                details.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
                details.MaxHeight = expand ? expandedHeight : 0;
                details.Opacity = expand ? 1 : 0;
                return;
            }

            var duration = TimeSpan.FromMilliseconds(180);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            if (expand)
            {
                details.Visibility = Visibility.Visible;
                details.BeginAnimation(
                    FrameworkElement.MaxHeightProperty,
                    new DoubleAnimation(0, expandedHeight, duration) { EasingFunction = easing });
                details.BeginAnimation(
                    UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
                return;
            }

            var collapseAnimation = new DoubleAnimation(details.ActualHeight, 0, duration) { EasingFunction = easing };
            collapseAnimation.Completed += (_, _) =>
            {
                details.Visibility = Visibility.Collapsed;
            };
            details.BeginAnimation(FrameworkElement.MaxHeightProperty, collapseAnimation);
            details.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(details.Opacity, 0, duration) { EasingFunction = easing });
        }

        private void ToggleEffectsPanelButton_Click(object sender, RoutedEventArgs e)
        {
            _effectsPanelExpanded = !_effectsPanelExpanded;
            ApplyEffectsPanelLayout();
            RequestApplyEffects(immediate: true);
            TriggerEffectsReapplyBurst();
        }

        private void ApplyEffectsPanelLayout()
        {
            if (_effectsPanelExpanded)
            {
                EffectsPanel.Visibility = Visibility.Visible;
                EffectsColumn.Width = new GridLength(420);
                ToggleEffectsPanelButton.Content = "Скрыть эффекты";
                return;
            }

            EffectsPanel.Visibility = Visibility.Collapsed;
            EffectsColumn.Width = new GridLength(0);
            ToggleEffectsPanelButton.Content = "Показать эффекты";
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private async void ToggleWindowSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlayerElementFullScreen)
            {
                await ExitEmbeddedPlayerFullscreenAsync();
                return;
            }

            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void PlayerWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateWindowSizeButtonState();
            RequestApplyEffects(immediate: true);
            TriggerEffectsReapplyBurst();
        }

        private void PlayerWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (_isPlayerElementFullScreen)
            {
                ApplyFullMonitorBounds();
            }
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (!_isPlayerElementFullScreen)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (_isPlayerElementFullScreen)
                {
                    ApplyFullMonitorBounds();
                    RequestApplyEffects(immediate: false);
                }
            }, DispatcherPriority.Background);
        }

        private void PlayerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || _isPlayerElementFullScreen)
            {
                return;
            }

            _isResizeInteractionInProgress = true;
            _resizeSettleDebounceTimer.Stop();
            _resizeSettleDebounceTimer.Start();
        }

        private void ResizeSettleDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _resizeSettleDebounceTimer.Stop();
            _isResizeInteractionInProgress = false;
            RequestApplyEffects(immediate: false);
        }

        private void TriggerEffectsReapplyBurst()
        {
            _ = ApplyEffectsSafelyAsync();
            if (_isEffectsBurstReapplyRunning)
            {
                return;
            }

            _ = ReapplyEffectsBurstAsync();
        }

        private async Task ReapplyEffectsBurstAsync()
        {
            _isEffectsBurstReapplyRunning = true;
            try
            {
                await Task.Delay(120);
                await ApplyEffectsSafelyAsync();
                await Task.Delay(280);
                await ApplyEffectsSafelyAsync();
                await Task.Delay(500);
                await ApplyEffectsSafelyAsync();
            }
            finally
            {
                _isEffectsBurstReapplyRunning = false;
            }
        }

        private void UpdateWindowSizeButtonState()
        {
            if (WindowSizeButton is null)
            {
                return;
            }

            WindowSizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TopBarPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TryBeginWindowDrag(e, 860);
        }

        private void MainRootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TryBeginWindowDrag(e, 860);
        }

        private void TryBeginWindowDrag(MouseButtonEventArgs e, double minimumRestoreWidth)
        {
            if (e.ChangedButton != MouseButton.Left || IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (_isPlayerElementFullScreen)
            {
                var pointerPosition = e.GetPosition(this);
                _ = ExitPlayerFullscreenAndStartDragAsync(pointerPosition, minimumRestoreWidth);
                e.Handled = true;
                return;
            }

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

                e.Handled = true;
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                var pointerPosition = e.GetPosition(this);
                var screenPosition = PointToScreen(pointerPosition);
                var widthRatio = ActualWidth > 0 ? pointerPosition.X / ActualWidth : 0.5;
                widthRatio = Math.Clamp(widthRatio, 0.0, 1.0);

                var restoreWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Math.Max(minimumRestoreWidth, Width);
                WindowState = WindowState.Normal;
                Left = screenPosition.X - restoreWidth * widthRatio;
                Top = screenPosition.Y - 20;
            }

            try
            {
                DragMove();
                e.Handled = true;
            }
            catch
            {
                // Ignore drag interruptions caused by rapid pointer transitions.
            }
        }

        private void TryApplyLongWatchBonuses()
        {
            if (!_halfHourBonusGranted && _eligibleWatchSeconds >= 30 * 60)
            {
                _watchXpBuffer += HalfHourWatchBonusXp;
                _halfHourBonusGranted = true;
            }

            if (!_hourBonusGranted && _eligibleWatchSeconds >= 60 * 60)
            {
                _watchXpBuffer += OneHourWatchBonusXp;
                _hourBonusGranted = true;
            }
        }

        private static bool IsInteractiveElement(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is ButtonBase ||
                    source is TextBox ||
                    source is ComboBox ||
                    source is Slider ||
                    source is ScrollBar ||
                    source is ScrollViewer ||
                    source is ListBoxItem)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void PlayerWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _ = PersistPlaybackPositionAsync(force: true);
                Topmost = false;
                _positionTimer.Stop();
                _effectsApplyDebounceTimer.Stop();
                _resizeSettleDebounceTimer.Stop();
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
                if (Player.CoreWebView2 is not null)
                {
                    Player.CoreWebView2.ContainsFullScreenElementChanged -= CoreWebView2_ContainsFullScreenElementChanged;
                    Player.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                    Player.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    Player.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    Player.CoreWebView2.Stop();
                    Player.CoreWebView2.Navigate("about:blank");
                }
            }
            catch
            {
                // Ignore teardown errors.
            }
        }

        private async Task ExitPlayerFullscreenAndStartDragAsync(System.Windows.Point pointerPosition, double minimumRestoreWidth)
        {
            await ExitEmbeddedPlayerFullscreenAsync();

            await Dispatcher.InvokeAsync(() =>
            {
                if (WindowState == WindowState.Maximized)
                {
                    var screenPosition = PointToScreen(pointerPosition);
                    var widthRatio = ActualWidth > 0 ? pointerPosition.X / ActualWidth : 0.5;
                    widthRatio = Math.Clamp(widthRatio, 0.0, 1.0);

                    var restoreWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Math.Max(minimumRestoreWidth, Width);
                    WindowState = WindowState.Normal;
                    Left = screenPosition.X - restoreWidth * widthRatio;
                    Top = screenPosition.Y - 20;
                }

                try
                {
                    DragMove();
                }
                catch
                {
                    // Ignore drag interruptions caused by rapid pointer transitions.
                }
            }, DispatcherPriority.Input);
        }

        private void PlayerWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                Player.Dispose();
            }
            catch
            {
                // Ignore disposal errors.
            }
        }

        private async Task PersistPlaybackPositionAsync(bool force)
        {
            if (_isPersistingPlayback)
            {
                return;
            }

            var playbackSeconds = Math.Max(0, LastPlaybackSeconds);
            if (!force &&
                playbackSeconds == _lastPersistedPlaybackSeconds &&
                (DateTime.UtcNow - _lastPlaybackPersistUtc).TotalSeconds < PlaybackPersistIntervalSeconds)
            {
                return;
            }

            _isPersistingPlayback = true;
            try
            {
                await PlaybackPersistLock.WaitAsync();
                try
                {
                    var history = await _watchHistoryPersistService.LoadAsync();
                    history.LastPlaybackSecondsByVideoId[_video.VideoId] = playbackSeconds;
                    await _watchHistoryPersistService.SaveAsync(history);
                    _lastPersistedPlaybackSeconds = playbackSeconds;
                    _lastPlaybackPersistUtc = DateTime.UtcNow;
                }
                finally
                {
                    PlaybackPersistLock.Release();
                }
            }
            catch
            {
                // Best-effort persistence.
            }
            finally
            {
                _isPersistingPlayback = false;
            }
        }

        private const uint MonitorDefaultToNearest = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }
    }
}




