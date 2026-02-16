using System.IO;
using System.Text.Json;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
        private double _lastMeasuredTime = -1;
        private double _watchXpBuffer;

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
            _video = video;
            _startSeconds = Math.Max(startSeconds, 0);
            LastPlaybackSeconds = _startSeconds;
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
                (Fx14, Fx14Details),
                (Fx15, Fx15Details)
            ];

            Loaded += PlayerWindow_Loaded;
            Closing += PlayerWindow_Closing;

            _positionTimer.Interval = TimeSpan.FromSeconds(2);
            _positionTimer.Tick += PositionTimer_Tick;
        }

        private async void PlayerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            VideoTitleText.Text = _video.Title;
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: false);

            try
            {
                var userDataFolder = ResolveWebViewUserDataFolder();
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

                        const hideSelectors = [
                            'ytd-masthead',
                            '#masthead-container',
                            '#secondary',
                            '#related',
                            '#below',
                            '#comments',
                            'ytd-comments',
                            'ytd-watch-metadata',
                            '#chat',
                            '#chat-container',
                            '#guide',
                            'ytd-mini-guide-renderer',
                            'tp-yt-app-drawer',
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
                        }
                    };

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


        private void CoreWebView2_ContainsFullScreenElementChanged(object? sender, object e)
        {
            if (Player.CoreWebView2 is null)
            {
                return;
            }

            var isFullScreen = Player.CoreWebView2.ContainsFullScreenElement;
            TopBarPanel.Visibility = isFullScreen ? Visibility.Collapsed : Visibility.Visible;

            if (isFullScreen)
            {
                EffectsPanel.Visibility = Visibility.Collapsed;
                EffectsColumn.Width = new GridLength(0);
            }
            else
            {
                ApplyEffectsPanelLayout();
            }
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
                            return { current: -1, duration: 0 };
                        }
                        return { current: video.currentTime, duration: video.duration };
                    })();
                    """;

                var raw = await Player.CoreWebView2.ExecuteScriptAsync(script);
                using var document = JsonDocument.Parse(raw);
                var current = document.RootElement.GetProperty("current").GetDouble();
                var duration = document.RootElement.GetProperty("duration").GetDouble();
                if (current < 0 || duration <= 0)
                {
                    return;
                }

                LastPlaybackSeconds = (int)Math.Floor(current);

                if (_lastMeasuredTime >= 0 && current > _lastMeasuredTime)
                {
                    var deltaSeconds = current - _lastMeasuredTime;
                    var multiplier = 1.0 + GetActiveEffectsCount() * 0.08;
                    _watchXpBuffer += (deltaSeconds / 10.0) * multiplier;
                    UpdateEffectSessionStats();
                }

                _lastMeasuredTime = current;
                WatchXpEarned = (int)Math.Floor(_watchXpBuffer);

                await ApplyEffectsAsync();
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

            if (Fx8.IsChecked == true && BlurStrengthSlider.Value >= 75)
            {
                UsedStrongBlur = true;
            }

            if (Fx9.IsChecked == true && RedGlowStrengthSlider.Value >= 75)
            {
                UsedStrongRedGlow = true;
            }

            if (Fx15.IsChecked == true && VioletGlowStrengthSlider.Value >= 75)
            {
                UsedStrongVioletGlow = true;
            }

            if (Fx11.IsChecked == true && ShakeStrengthSlider.Value >= 75)
            {
                UsedStrongShake = true;
            }
        }

        private async void EffectToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: true);
            await ApplyEffectsAsync();
        }

        private async void StrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
            {
                return;
            }

            await ApplyEffectsAsync();
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
            static double Normalize(Slider slider) => Math.Clamp(slider.Value / 100.0, 0, 1);

            return new EffectSettings
            {
                Flags = GetEffectsState(),
                Contrast = Normalize(ContrastStrengthSlider),
                Darkness = Normalize(DarknessStrengthSlider),
                Saturation = Normalize(SaturationStrengthSlider),
                HueShift = Normalize(HueShiftStrengthSlider),
                Blur = Normalize(BlurStrengthSlider),
                RedGlow = Normalize(RedGlowStrengthSlider),
                Vhs = Normalize(VhsStrengthSlider),
                Shake = Normalize(ShakeStrengthSlider),
                ColdTone = Normalize(ColdToneStrengthSlider),
                VioletGlow = Normalize(VioletGlowStrengthSlider)
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

                    if (activeCount === 0) {
                        video.style.filter = 'none';
                        video.style.transform = 'none';
                        video.style.imageRendering = 'auto';
                        video.style.animation = 'none';

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
                    if (flags[3]) filters.push(`contrast(${(1.2 + settings.Contrast * 2.2).toFixed(2)})`);
                    if (flags[4]) filters.push(`brightness(${(1 - settings.Darkness * 0.7).toFixed(2)})`);
                    if (flags[5]) filters.push(`saturate(${(1.1 + settings.Saturation * 2.8).toFixed(2)})`);
                    if (flags[6]) filters.push(`hue-rotate(${Math.round(settings.HueShift * 180)}deg)`);
                    if (flags[7]) filters.push(`blur(${(0.6 + settings.Blur * 4.4).toFixed(1)}px)`);
                    if (flags[8]) filters.push(`drop-shadow(0 0 ${(8 + settings.RedGlow * 24).toFixed(1)}px rgba(255,51,51,${(0.4 + settings.RedGlow * 0.6).toFixed(2)}))`);
                    if (flags[13]) filters.push(`hue-rotate(${Math.round(170 + settings.ColdTone * 120)}deg) saturate(${(1.1 + settings.ColdTone * 1.1).toFixed(2)})`);
                    if (flags[14]) filters.push(`drop-shadow(0 0 ${(10 + settings.VioletGlow * 24).toFixed(1)}px rgba(186,85,255,${(0.45 + settings.VioletGlow * 0.55).toFixed(2)}))`);

                    video.style.filter = filters.length > 0 ? filters.join(' ') : 'none';
                    video.style.transform = flags[11] ? 'scaleX(-1)' : 'none';
                    video.style.imageRendering = flags[12] ? 'pixelated' : 'auto';

                    const shakeDuration = Math.max(0.05, 0.22 - settings.Shake * 0.16);
                    const shakeAmp = 1 + settings.Shake * 4.0;
                    video.style.animation = flags[10] ? `stw_shake ${shakeDuration.toFixed(2)}s infinite linear` : 'none';

                    let style = document.getElementById('stwfx-style');
                    if (!style) {
                        style = document.createElement('style');
                        style.id = 'stwfx-style';
                        document.head.appendChild(style);
                    }
                    style.textContent = `
                        @keyframes stw_shake {
                            0% { transform: translate(0,0); }
                            25% { transform: translate(${shakeAmp.toFixed(2)}px, ${(-shakeAmp).toFixed(2)}px); }
                            50% { transform: translate(${(-shakeAmp).toFixed(2)}px, ${shakeAmp.toFixed(2)}px); }
                            75% { transform: translate(${shakeAmp.toFixed(2)}px, ${shakeAmp.toFixed(2)}px); }
                            100% { transform: translate(0,0); }
                        }
                    `;

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
                        overlay.style.mixBlendMode = 'overlay';
                        overlay.style.backgroundImage = 'repeating-linear-gradient(0deg, rgba(255,255,255,0.08) 0px, rgba(255,255,255,0.08) 1px, rgba(0,0,0,0) 2px, rgba(0,0,0,0) 4px)';
                        overlay.style.zIndex = '2147483647';
                        overlay.style.display = 'none';
                        document.body.appendChild(overlay);
                    }
                    overlay.style.display = flags[9] ? 'block' : 'none';
                    overlay.style.opacity = (0.18 + settings.Vhs * 0.55).toFixed(2);
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

            ContrastStrengthSlider.Value = 55;
            DarknessStrengthSlider.Value = 45;
            SaturationStrengthSlider.Value = 55;
            HueShiftStrengthSlider.Value = 50;
            BlurStrengthSlider.Value = 45;
            RedGlowStrengthSlider.Value = 50;
            VhsStrengthSlider.Value = 45;
            ShakeStrengthSlider.Value = 35;
            ColdToneStrengthSlider.Value = 40;
            VioletGlowStrengthSlider.Value = 50;
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: false);

            await ApplyEffectsAsync();
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

        private async void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (Player.CoreWebView2 is null)
            {
                return;
            }

            const string script = """
                (() => {
                    const btn = document.querySelector('.ytp-fullscreen-button');
                    if (btn) {
                        btn.click();
                        return true;
                    }

                    const video = document.querySelector('video');
                    if (!video) {
                        return false;
                    }

                    if (document.fullscreenElement) {
                        document.exitFullscreen();
                        return true;
                    }

                    if (video.requestFullscreen) {
                        video.requestFullscreen();
                        return true;
                    }

                    return false;
                })();
                """;

            try
            {
                await Player.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Ignore temporary script errors during navigation.
            }
        }

        private void ToggleEffectsPanelButton_Click(object sender, RoutedEventArgs e)
        {
            _effectsPanelExpanded = !_effectsPanelExpanded;
            ApplyEffectsPanelLayout();
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

        private void PlayerWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _positionTimer.Stop();
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
    }
}

