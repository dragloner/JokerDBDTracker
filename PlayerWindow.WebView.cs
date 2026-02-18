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
        private async void PlayerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyStartupMaximizedWindowed();
            VideoTitleText.Text = _video.Title;
            UpdateWindowSizeButtonState();
            UpdateStrengthSlidersEnabledState();
            UpdateEffectDetailsVisibility(animate: false);
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            SetPlayerLoadingOverlay(visible: true, "Подготовка YouTube...");
            SetPlayerSurfaceVisible(false);

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
                await Player.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    BuildEarlyKioskBootstrapScript(_video.VideoId));
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
            SetPlayerLoadingOverlay(visible: true, "Загрузка видео...");
            SetPlayerSurfaceVisible(false);
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
                SetPlayerLoadingOverlay(visible: true, "Не удалось загрузить плеер. Проверьте интернет и попробуйте снова.");
                SetPlayerSurfaceVisible(false);
                return;
            }

            try
            {
                SetPlayerLoadingOverlay(visible: true, "Загрузка видео...");
                await Player.CoreWebView2.ExecuteScriptAsync(BuildKioskModeScript(_video.VideoId));
                SetPlayerSurfaceVisible(true);

                _lastAppliedEffectsSignature = string.Empty;
                await ApplyEffectsSafelyAsync(force: true);
            }
            catch
            {
                // Ignore transient script failures during YouTube page bootstrap.
                SetPlayerLoadingOverlay(visible: true, "Ошибка инициализации плеера. Попробуйте открыть видео снова.");
                SetPlayerSurfaceVisible(false);
            }
        }

        private void SetPlayerLoadingOverlay(bool visible, string? text = null)
        {
            if (PlayerLoadingOverlay is null)
            {
                return;
            }

            PlayerLoadingOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!string.IsNullOrWhiteSpace(text) && PlayerLoadingText is not null)
            {
                PlayerLoadingText.Text = text;
            }
        }

        private void SetPlayerSurfaceVisible(bool visible)
        {
            if (PlayerSurfaceHost is null)
            {
                return;
            }

            PlayerSurfaceHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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

        private static string BuildEarlyKioskBootstrapScript(string videoId)
        {
            var safeVideoId = videoId.Replace("\\", "\\\\").Replace("'", "\\'");
            return $$"""
                (() => {
                    const expectedVideoId = '{{safeVideoId}}';
                    const hiddenSelectors = [
                        '#secondary',
                        '#secondary-inner',
                        '#related',
                        '#below',
                        '#comments',
                        'ytd-comments',
                        '#chat',
                        '#chat-container',
                        'ytd-watch-metadata',
                        'ytd-watch-info-text',
                        '#description',
                        '#description-inline-expander',
                        '#description-inner',
                        'ytd-engagement-panel-section-list-renderer',
                        '#panels'
                    ];

                    const ensureEarlyStyle = () => {
                        if (document.getElementById('jdbd-early-kiosk-style')) {
                            return;
                        }

                        const style = document.createElement('style');
                        style.id = 'jdbd-early-kiosk-style';
                        style.textContent = `
                            html, body { overflow: hidden !important; background: #000 !important; margin: 0 !important; padding: 0 !important; }
                            ${hiddenSelectors.join(', ')} { display: none !important; visibility: hidden !important; pointer-events: none !important; }
                            #columns, #primary { max-height: 100vh !important; overflow: hidden !important; }
                        `;
                        (document.head || document.documentElement).appendChild(style);
                    };

                    const blockForeignWatchClick = (event) => {
                        const target = event.target;
                        if (!target || !target.closest) {
                            return;
                        }

                        const link = target.closest('a[href]');
                        if (!link) {
                            return;
                        }

                        let url;
                        try {
                            url = new URL(link.href, location.origin);
                        } catch {
                            return;
                        }

                        if (!url.hostname.endsWith('youtube.com') || url.pathname !== '/watch') {
                            return;
                        }

                        const clickedVideoId = url.searchParams.get('v');
                        if (clickedVideoId && clickedVideoId !== expectedVideoId) {
                            event.preventDefault();
                            event.stopImmediatePropagation();
                            event.stopPropagation();
                        }
                    };

                    ensureEarlyStyle();
                    document.addEventListener('click', blockForeignWatchClick, true);
                    document.addEventListener('auxclick', blockForeignWatchClick, true);
                    document.addEventListener('mousedown', blockForeignWatchClick, true);
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


    }
}
