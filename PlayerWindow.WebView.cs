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
            try
            {
                try
                {
                    _appSettings = await _settingsService.LoadAsync();
                }
                catch
                {
                    _appSettings = new AppSettingsData();
                }

                UiAnimation.SetIsEnabled(this, _appSettings.AnimationsEnabled);
                DiagnosticsService.SetEnabled(_appSettings.LoggingEnabled);
                PositionWindowToOwnerMonitor();
                ApplyStartupMaximizedWindowed();
                AnimatePlayerWindowEntrance();
                VideoTitleText.Text = _video.Title;
                ApplyPlayerLocalization();
                UpdateDynamicBindHints();
                RegisterGlobalHotkeys();
                UpdateWindowSizeButtonState();
                UpdateStrengthSlidersEnabledState();
                UpdateEffectDetailsVisibility(animate: false);
                SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
                _isPlayerNavigationInProgress = true;
                _isPlayerRuntimeReady = false;
                SetPlayerInteractionsEnabled(false);
                SetPlayerLoadingOverlay(visible: true, PT("Подготовка YouTube...", "Preparing YouTube..."));
                SetPlayerSurfaceVisible(false);

                try
                {
                    var userDataFolder = await Task.Run(ResolveWebViewUserDataFolder);
                    var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
                    await Player.EnsureCoreWebView2Async(environment);

                    Player.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    Player.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    Player.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    Player.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    Player.CoreWebView2.Settings.UserAgent = DesktopChromeUserAgent;
                    Player.ZoomFactor = 1.0;
                    await Player.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                        BuildEarlyKioskBootstrapScript(_video.VideoId));
                    Player.CoreWebView2.ContainsFullScreenElementChanged += CoreWebView2_ContainsFullScreenElementChanged;
                    Player.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                    Player.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    Player.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                    Player.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                    Player.CoreWebView2.Navigate(BuildLockedWatchUrl(_startSeconds));
                    _positionTimer.Start();
                }
                catch (Exception ex)
                {
                    DiagnosticsService.LogException("PlayerWindow_Loaded.WebViewInit", ex);
                    _isPlayerNavigationInProgress = false;
                    _isPlayerRuntimeReady = false;
                    SetPlayerInteractionsEnabled(false);
                    MessageBox.Show(
                        $"{PT("Не удалось инициализировать плеер:", "Failed to initialize player:")}{Environment.NewLine}{ex.Message}",
                        PT("Ошибка плеера", "Player error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException("PlayerWindow_Loaded", ex);
                var logInfo = DiagnosticsService.IsEnabled()
                    ? $"{PT("Лог ошибок:", "Error log:")} {DiagnosticsService.GetLogFilePath()}"
                    : PT("Логирование отключено в настройках.", "Logging is disabled in Settings.");
                MessageBox.Show(
                    $"{PT("Произошла ошибка при запуске плеера:", "Player startup failed:")}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}" +
                    logInfo,
                    PT("Ошибка плеера", "Player error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Close();
            }
        }

        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // Block opening any external windows/tabs from the embedded player.
            e.Handled = true;
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _isPlayerNavigationInProgress = true;
            _isPlayerRuntimeReady = false;
            unchecked
            {
                _playerNavigationVersion++;
            }
            _lastXpSampleUtc = null;
            _lastMeasuredTime = -1;
            SetPlayerInteractionsEnabled(false);
            SetPlayerLoadingOverlay(visible: true, PT("Загрузка видео...", "Loading video..."));
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
            if (_isPlayerClosing)
            {
                return;
            }

            var navigationVersion = _playerNavigationVersion;
            if (!e.IsSuccess || Player.CoreWebView2 is null)
            {
                _navigationCompletedFailureCount++;
                if (_navigationCompletedFailureCount <= 2)
                {
                    SetPlayerLoadingOverlay(visible: true, PT("Повторная попытка загрузки плеера...", "Retrying player load..."));
                    _ = RecoverLockedVideoAsync();
                    return;
                }

                SetPlayerLoadingOverlay(visible: true, PT(
                    "Не удалось загрузить плеер. Проверьте интернет и попробуйте снова.",
                    "Failed to load player. Check internet connection and try again."));
                SetPlayerInteractionsEnabled(false);
                return;
            }

            _navigationCompletedFailureCount = 0;
            try
            {
                Player.ZoomFactor = 1.0;
                SetPlayerLoadingOverlay(visible: true, PT("Загрузка видео...", "Loading video..."));
                var bootstrapResult = await ExecuteWebScriptWithTimeoutAsync(
                    BuildKioskModeScript(
                        _video.VideoId,
                        ReadConfiguredKey(_appSettings.HideEffectsPanelBind, Key.H)),
                    timeoutMs: 2500,
                    operation: "CoreWebView2_NavigationCompleted.Bootstrap");
                if (_isPlayerClosing || navigationVersion != _playerNavigationVersion)
                {
                    return;
                }

                if (bootstrapResult is null)
                {
                    SetPlayerLoadingOverlay(visible: true, PT(
                        "Плеер не ответил во время запуска. Попробуйте открыть видео снова.",
                        "Player did not respond during startup. Try opening this video again."));
                    SetPlayerSurfaceVisible(false);
                    SetPlayerInteractionsEnabled(false);
                    return;
                }

                SetPlayerSurfaceVisible(true);

                _lastAppliedEffectsSignature = string.Empty;
                await ApplyEffectsSafelyAsync(force: true);
                if (_isPlayerClosing || navigationVersion != _playerNavigationVersion)
                {
                    return;
                }

                var playbackStarted = await WaitForPlaybackStartAsync();
                if (_isPlayerClosing || navigationVersion != _playerNavigationVersion)
                {
                    return;
                }

                if (!playbackStarted)
                {
                    DiagnosticsService.LogInfo(
                        "PlayerStartup",
                        "Playback readiness probe timed out; releasing controls to avoid startup deadlock.");
                }

                _isPlayerNavigationInProgress = false;
                _isPlayerRuntimeReady = true;
                SetPlayerInteractionsEnabled(true);
                SetPlayerLoadingOverlay(visible: false);
                FlushPendingEffectsApplyRequests();
            }
            catch
            {
                // Ignore transient script failures during YouTube page bootstrap.
                SetPlayerLoadingOverlay(visible: true, PT(
                    "Ошибка инициализации плеера. Попробуйте открыть видео снова.",
                    "Player initialization error. Try opening this video again."));
                SetPlayerSurfaceVisible(false);
                SetPlayerInteractionsEnabled(false);
            }
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!CanProcessPlayerCommands())
            {
                return;
            }

            var message = e.TryGetWebMessageAsString();
            if (!string.Equals(message, "jdbd:toggle-effects-panel", StringComparison.Ordinal))
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                MarkUserInteraction();
                var panelToggleKey = ReadConfiguredKey(_appSettings.HideEffectsPanelBind, Key.H);
                TryHandleEffectsPanelToggleKey(panelToggleKey);
            }, DispatcherPriority.Input);
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

        private void SetPlayerInteractionsEnabled(bool enabled)
        {
            var canEnable = enabled && !_isPlayerClosing;
            if (ToggleEffectsPanelButton is not null)
            {
                ToggleEffectsPanelButton.IsEnabled = canEnable;
            }

            if (EffectsPanel is not null)
            {
                EffectsPanel.IsEnabled = canEnable;
            }

            if (WindowSizeButton is not null && !_isPlayerElementFullScreen)
            {
                WindowSizeButton.IsEnabled = canEnable;
            }
        }

        private bool CanProcessPlayerCommands()
        {
            return !_isPlayerClosing &&
                   !_isPlayerNavigationInProgress &&
                   _isPlayerRuntimeReady &&
                   Player.CoreWebView2 is not null;
        }

        private void FlushPendingEffectsApplyRequests()
        {
            if (!_pendingEffectsApply && !_pendingEffectsApplyForce)
            {
                return;
            }

            var force = _pendingEffectsApplyForce;
            _pendingEffectsApply = false;
            _pendingEffectsApplyForce = false;
            RequestApplyEffects(immediate: true, force: force);
        }

        private async Task<string?> ExecuteWebScriptWithTimeoutAsync(string script, int timeoutMs, string operation)
        {
            if (_isPlayerClosing || Player.CoreWebView2 is null)
            {
                return null;
            }

            try
            {
                var scriptTask = Player.CoreWebView2.ExecuteScriptAsync(script);
                var completedTask = await Task.WhenAny(scriptTask, Task.Delay(timeoutMs));
                if (_isPlayerClosing)
                {
                    return null;
                }

                if (completedTask != scriptTask)
                {
                    if ((DateTime.UtcNow - _lastWebScriptTimeoutLogUtc).TotalSeconds >= 5)
                    {
                        _lastWebScriptTimeoutLogUtc = DateTime.UtcNow;
                        DiagnosticsService.LogInfo("WebViewTimeout", $"{operation} exceeded {timeoutMs} ms.");
                    }

                    return null;
                }

                return await scriptTask;
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException(operation, ex);
                return null;
            }
        }

        private async Task<bool> WaitForPlaybackStartAsync()
        {
            if (Player.CoreWebView2 is null)
            {
                return false;
            }

            const string playbackStateScript = """
                (() => {
                    const video = document.querySelector('video');
                    if (!video || !Number.isFinite(video.currentTime)) {
                        return false;
                    }

                    return video.currentTime > 0.15 || (video.readyState >= 2 && !video.paused);
                })();
                """;

            for (var i = 0; i < 60; i++)
            {
                if (_isPlayerClosing)
                {
                    return false;
                }

                try
                {
                    var result = await ExecuteWebScriptWithTimeoutAsync(
                        playbackStateScript,
                        timeoutMs: 800,
                        operation: "WaitForPlaybackStartAsync");
                    if (result is null)
                    {
                        await Task.Delay(100);
                        if (_isPlayerClosing)
                        {
                            return false;
                        }

                        continue;
                    }

                    if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore transient WebView2 script errors during page bootstrap.
                }

                await Task.Delay(100);
                if (_isPlayerClosing)
                {
                    return false;
                }
            }

            return false;
        }

        private async Task RecoverLockedVideoAsync()
        {
            if (Player.CoreWebView2 is null || _isRecoveringBlockedNavigation || _isPlayerClosing)
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
            var stableFolder = AppStoragePaths.GetWebViewProfileDirectory();
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

        private static string BuildKioskModeScript(string videoId, Key panelToggleKey)
        {
            var safeVideoId = videoId.Replace("\\", "\\\\").Replace("'", "\\'");
            var (panelToggleKeyVariants, panelToggleCodeVariants) = BuildKeyboardEventVariants(panelToggleKey);
            var panelToggleKeyJson = JsonSerializer.Serialize(panelToggleKeyVariants);
            var panelToggleCodeJson = JsonSerializer.Serialize(panelToggleCodeVariants);
            return $$"""
                (() => {
                    const expectedVideoId = '{{safeVideoId}}';
                    const panelToggleKeys = new Set({{panelToggleKeyJson}});
                    const panelToggleCodes = new Set({{panelToggleCodeJson}});
                    const isPanelToggleKey = (event) => {
                        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
                            return false;
                        }

                        const key = (event.key || '').toUpperCase();
                        const code = event.code || '';
                        return panelToggleKeys.has(key) || panelToggleCodes.has(code);
                    };

                    const installPanelToggleBridge = () => {
                        if (window.__jdbdPanelToggleBridgeInstalled) {
                            return;
                        }

                        window.__jdbdPanelToggleBridgeInstalled = true;
                        document.addEventListener('keydown', (event) => {
                            if (isPanelToggleKey(event)) {
                                event.preventDefault();
                                event.stopImmediatePropagation();
                                event.stopPropagation();
                                if (!event.repeat) {
                                    try {
                                        window.chrome?.webview?.postMessage('jdbd:toggle-effects-panel');
                                    } catch {
                                        // no-op
                                    }
                                }
                            }
                        }, { capture: true });
                    };

                    const installForeignWatchGuard = () => {
                        if (window.__jdbdForeignWatchGuardInstalled) {
                            return;
                        }

                        window.__jdbdForeignWatchGuardInstalled = true;
                        const blockForeignWatchClick = (event) => {
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
                        };

                        document.addEventListener('click', blockForeignWatchClick, { capture: true });
                        document.addEventListener('auxclick', blockForeignWatchClick, { capture: true });
                        document.addEventListener('mousedown', blockForeignWatchClick, { capture: true });
                    };

                    const installYouTubeSidePanelGuard = () => {
                        if (window.__jdbdSidePanelGuardInstalled) {
                            return;
                        }

                        window.__jdbdSidePanelGuardInstalled = true;

                        const isCommentOrChatControl = (target) => {
                            if (!target || !target.closest) {
                                return false;
                            }

                            const button = target.closest('.ytp-button, button, a[role="button"]');
                            if (!button) {
                                return false;
                            }

                            if (!button.closest('.ytp-right-controls, .ytp-chrome-controls')) {
                                return false;
                            }

                            const text = `${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''}`.toLowerCase();
                            if (!text) {
                                return false;
                            }

                            return text.includes('comment') ||
                                   text.includes('comments') ||
                                   text.includes('комментар') ||
                                   text.includes('chat') ||
                                   text.includes('чат');
                        };

                        const blockPanelToggle = (event) => {
                            if (!isCommentOrChatControl(event.target)) {
                                return;
                            }

                            event.preventDefault();
                            event.stopImmediatePropagation();
                            event.stopPropagation();
                        };

                        document.addEventListener('click', blockPanelToggle, { capture: true });
                        document.addEventListener('mousedown', blockPanelToggle, { capture: true });
                        document.addEventListener('pointerdown', blockPanelToggle, { capture: true });
                    };

                    const applyImmersiveVideoLayout = () => {
                        if (!location.hostname.endsWith('youtube.com')) {
                            return;
                        }

                        const ensureLayoutStyle = () => {
                            if (document.getElementById('jdbd-immersive-style')) {
                                return;
                            }

                            const style = document.createElement('style');
                            style.id = 'jdbd-immersive-style';
                            style.textContent = `
                                html, body, ytd-app, #content, #page-manager, ytd-watch-flexy, #columns, #primary, #primary-inner {
                                    margin: 0 !important;
                                    padding: 0 !important;
                                    background: #000 !important;
                                    overflow: hidden !important;
                                    height: 100% !important;
                                }

                                #columns {
                                    display: grid !important;
                                    grid-template-columns: minmax(0, 1fr) !important;
                                    grid-template-areas: "primary" !important;
                                    column-gap: 0 !important;
                                    row-gap: 0 !important;
                                    max-width: none !important;
                                    width: 100% !important;
                                    height: 100% !important;
                                }

                                #primary {
                                    grid-area: primary !important;
                                    grid-column: 1 / -1 !important;
                                    margin: 0 !important;
                                    width: 100% !important;
                                    max-width: none !important;
                                    height: 100% !important;
                                }

                                #primary-inner {
                                    width: 100% !important;
                                    height: 100vh !important;
                                }

                                #player, #ytd-player, #player-container, #player-container-outer, #player-container-inner,
                                #player-theater-container, #full-bleed-container, #movie_player,
                                .html5-video-player, .html5-video-container {
                                    width: 100% !important;
                                    max-width: none !important;
                                    height: 100% !important;
                                    max-height: none !important;
                                }

                                #player, #ytd-player, #player-container, #player-container-outer, #player-container-inner,
                                #player-theater-container, #full-bleed-container {
                                    min-height: 100vh !important;
                                }

                                .html5-main-video, video {
                                    width: 100% !important;
                                    height: 100% !important;
                                    max-height: none !important;
                                    object-fit: contain !important;
                                    left: 0 !important;
                                    top: 0 !important;
                                }

                                #secondary, #secondary-inner, #related,
                                #below, #comments, ytd-comments,
                                ytd-watch-metadata, ytd-watch-info-text,
                                #description, #description-inline-expander, #description-inner,
                                ytd-text-inline-expander, ytd-engagement-panel-section-list-renderer, #panels,
                                #chat, #chat-container, ytd-live-chat-frame,
                                ytd-merch-shelf-renderer, ytd-reel-shelf-renderer,
                                ytd-watch-next-secondary-results-renderer, ytd-watch-next-feed-renderer,
                                #end, #meta, #guide, ytd-mini-guide-renderer, tp-yt-app-drawer,
                                ytd-masthead, #masthead-container, #header,
                                #columns > :not(#primary) {
                                    display: none !important;
                                    visibility: hidden !important;
                                    pointer-events: none !important;
                                }

                                .ytp-chrome-top,
                                .ytp-title,
                                .ytp-title-link,
                                .ytp-title-channel,
                                .ytp-title-text,
                                .ytp-title-expanded-overlay,
                                .ytp-title-expanded-heading,
                                .ytp-title-expanded-content {
                                    pointer-events: none !important;
                                }

                                .ytp-right-controls .ytp-button[aria-label*="comment" i],
                                .ytp-right-controls .ytp-button[title*="comment" i],
                                .ytp-right-controls .ytp-button[aria-label*="комментар" i],
                                .ytp-right-controls .ytp-button[title*="комментар" i],
                                .ytp-right-controls .ytp-button[aria-label*="chat" i],
                                .ytp-right-controls .ytp-button[title*="chat" i],
                                .ytp-right-controls .ytp-button[aria-label*="чат" i],
                                .ytp-right-controls .ytp-button[title*="чат" i] {
                                    display: none !important;
                                }
                            `;
                            (document.head || document.documentElement).appendChild(style);
                        };

                        ensureLayoutStyle();

                        const flexy = document.querySelector('ytd-watch-flexy');
                        if (flexy) {
                            flexy.setAttribute('theater', '');
                            for (const attr of Array.from(flexy.attributes)) {
                                const name = (attr.name || '').toLowerCase();
                                if (name.includes('two-columns') ||
                                    name.includes('engagement') ||
                                    name.includes('chat') ||
                                    name.includes('comment') ||
                                    name.includes('panel'))
                                {
                                    try {
                                        flexy.removeAttribute(attr.name);
                                    } catch {
                                        // no-op
                                    }
                                }
                            }
                        }

                        const columns = document.getElementById('columns');
                        if (columns) {
                            columns.style.setProperty('grid-template-columns', 'minmax(0, 1fr)', 'important');
                            columns.style.setProperty('grid-template-areas', '"primary"', 'important');
                            columns.style.setProperty('column-gap', '0', 'important');
                            columns.style.setProperty('row-gap', '0', 'important');
                        }

                        const primary = document.getElementById('primary');
                        if (primary) {
                            primary.style.setProperty('grid-column', '1 / -1', 'important');
                            primary.style.setProperty('width', '100%', 'important');
                        }
                    };

                    const enforceExpectedVideoId = () => {
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
                    };

                    installPanelToggleBridge();
                    installForeignWatchGuard();
                    installYouTubeSidePanelGuard();
                    enforceExpectedVideoId();
                    applyImmersiveVideoLayout();
                    const observer = new MutationObserver(() => {
                        applyImmersiveVideoLayout();
                    });
                    observer.observe(document.documentElement, { childList: true, subtree: true });
                })();
                """;
        }

        private static string BuildEarlyKioskBootstrapScript(string videoId)
        {
            var safeVideoId = videoId.Replace("\\", "\\\\").Replace("'", "\\'");
            return $$"""
                (() => {
                    const expectedVideoId = '{{safeVideoId}}';
                    if (window.__jdbdEarlyWatchGuardInstalled) {
                        return;
                    }

                    window.__jdbdEarlyWatchGuardInstalled = true;

                    const ensureEarlyImmersiveStyle = () => {
                        if (document.getElementById('jdbd-early-immersive-style')) {
                            return;
                        }

                        const style = document.createElement('style');
                        style.id = 'jdbd-early-immersive-style';
                        style.textContent = `
                            html, body, ytd-app, #content, #page-manager, ytd-watch-flexy, #columns, #primary, #primary-inner {
                                overflow: hidden !important;
                                height: 100% !important;
                            }

                            #columns {
                                display: grid !important;
                                grid-template-columns: minmax(0, 1fr) !important;
                                grid-template-areas: "primary" !important;
                                column-gap: 0 !important;
                                row-gap: 0 !important;
                            }

                            #primary {
                                grid-area: primary !important;
                                grid-column: 1 / -1 !important;
                                width: 100% !important;
                                max-width: none !important;
                                margin: 0 !important;
                            }

                            #primary-inner {
                                width: 100% !important;
                                height: 100vh !important;
                            }

                            #secondary, #secondary-inner, #related,
                            #below, #comments, ytd-comments,
                            ytd-watch-metadata, ytd-watch-info-text,
                            #description, #description-inline-expander, #description-inner,
                            ytd-engagement-panel-section-list-renderer, #panels,
                            #chat, #chat-container, ytd-live-chat-frame,
                            ytd-masthead, #masthead-container, #header,
                            #columns > :not(#primary) {
                                display: none !important;
                                visibility: hidden !important;
                                pointer-events: none !important;
                            }

                            .ytp-chrome-top,
                            .ytp-title,
                            .ytp-title-link,
                            .ytp-title-channel,
                            .ytp-title-text,
                            .ytp-title-expanded-overlay,
                            .ytp-title-expanded-heading,
                            .ytp-title-expanded-content {
                                pointer-events: none !important;
                            }

                            .ytp-right-controls .ytp-button[aria-label*="comment" i],
                            .ytp-right-controls .ytp-button[title*="comment" i],
                            .ytp-right-controls .ytp-button[aria-label*="комментар" i],
                            .ytp-right-controls .ytp-button[title*="комментар" i],
                            .ytp-right-controls .ytp-button[aria-label*="chat" i],
                            .ytp-right-controls .ytp-button[title*="chat" i],
                            .ytp-right-controls .ytp-button[aria-label*="чат" i],
                            .ytp-right-controls .ytp-button[title*="чат" i] {
                                display: none !important;
                            }
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

                    ensureEarlyImmersiveStyle();
                    document.addEventListener('click', blockForeignWatchClick, true);
                    document.addEventListener('auxclick', blockForeignWatchClick, true);
                    document.addEventListener('mousedown', blockForeignWatchClick, true);
                })();
                """;
        }

        private static (string[] Keys, string[] Codes) BuildKeyboardEventVariants(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                var letter = key.ToString().ToUpperInvariant();
                return ([letter], [$"Key{letter}"]);
            }

            if (key >= Key.D0 && key <= Key.D9)
            {
                var digit = ((int)(key - Key.D0)).ToString();
                return ([digit], [$"Digit{digit}"]);
            }

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                var digit = ((int)(key - Key.NumPad0)).ToString();
                return ([digit], [$"Numpad{digit}"]);
            }

            if (key >= Key.F1 && key <= Key.F24)
            {
                var functionKey = key.ToString().ToUpperInvariant();
                return ([functionKey], [functionKey]);
            }

            return key switch
            {
                Key.Space => ([" "], ["Space"]),
                Key.Tab => (["TAB"], ["Tab"]),
                Key.Enter => (["ENTER"], ["Enter", "NumpadEnter"]),
                Key.Escape => (["ESCAPE", "ESC"], ["Escape"]),
                Key.Left => (["ARROWLEFT"], ["ArrowLeft"]),
                Key.Right => (["ARROWRIGHT"], ["ArrowRight"]),
                Key.Up => (["ARROWUP"], ["ArrowUp"]),
                Key.Down => (["ARROWDOWN"], ["ArrowDown"]),
                _ => ([key.ToString().ToUpperInvariant()], [key.ToString()])
            };
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
