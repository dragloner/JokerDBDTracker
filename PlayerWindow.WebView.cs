using System.Diagnostics;
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

                if (_isPlayerClosing) return;
                UiAnimation.SetIsEnabled(this, _appSettings.AnimationsEnabled);
                DiagnosticsService.SetEnabled(_appSettings.LoggingEnabled);
                PositionWindowToOwnerMonitor();
                ApplyStartupMaximizedWindowed();
                AnimatePlayerWindowEntrance();
                VideoTitleText.Text = _video.IsTwitchStream ? "Twitch" : _video.Title;
                InitializePresetsUi();
                if (_video.IsTwitchStream)
                {
                    EffectsWorkspaceTimecodesTab.Visibility = System.Windows.Visibility.Collapsed;
                    EffectsWorkspaceCommunityTab.Visibility = System.Windows.Visibility.Collapsed;
                    if (ToggleChatDockButton is not null)
                    {
                        ToggleChatDockButton.Visibility = System.Windows.Visibility.Collapsed;
                    }
                    if (ToggleCommentsDockButton is not null)
                    {
                        ToggleCommentsDockButton.Visibility = System.Windows.Visibility.Collapsed;
                    }
                }
                else
                {
                    EffectsWorkspaceCommunityTab.Visibility = System.Windows.Visibility.Collapsed;
                    _ = LoadVideoTimecodesAsync();
                }
                if (PlayerSfxSpamToggle is not null)
                {
                    PlayerSfxSpamToggle.IsChecked = _appSettings.SoundSpamMode;
                }
                ApplyPlayerLocalization();
                ApplyCommunityDockLayout();
                UpdateDynamicBindHints();
                RegisterGlobalHotkeys();
                UpdateWindowSizeButtonState();
                UpdateStrengthSlidersEnabledState();
                UpdateEffectDetailsVisibility(animate: false);
                SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
                _isPlayerNavigationInProgress = true;
                _isPlayerRuntimeReady = false;
                SetPlayerInteractionsEnabled(false);
                SetPlayerLoadingOverlay(visible: true, _video.IsTwitchStream
                    ? PT("Загрузка Twitch...", "Loading Twitch...")
                    : PT("Подготовка YouTube...", "Preparing YouTube..."));
                SetPlayerSurfaceVisible(false);

                try
                {
                    var userDataFolder = await Task.Run(ResolveWebViewUserDataFolder);
                    var environment = await CoreWebView2Environment.CreateAsync(
                        browserExecutableFolder: null,
                        userDataFolder: userDataFolder);
                    _webViewEnvironment = environment;
                    if (_isPlayerClosing) return;
                    await Player.EnsureCoreWebView2Async(environment);
                    if (_isPlayerClosing) { Player.Dispose(); return; }

                    Player.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    Player.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    Player.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    Player.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    Player.ZoomFactor = 1.0;
                    if (_video.IsTwitchStream)
                    {
                        await Player.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                            BuildWebInputStateBridgeScript());
                    }
                    else
                    {
                        await Player.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                            BuildEarlyKioskBootstrapScript(_video.VideoId));
                        await Player.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                            BuildSeekBridgeScript());
                        await Player.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                            BuildMediaDiagnosticsScript());
                    }
                    Player.CoreWebView2.ContainsFullScreenElementChanged += CoreWebView2_ContainsFullScreenElementChanged;
                    Player.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                    Player.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    Player.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                    Player.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                    Player.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                    Player.CoreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;
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

        private ulong _activeNavigationId;
        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_isPlayerClosing || !IsAllowedPlayerNavigation(e.Uri)) { e.Cancel = true; return; }
            _activeNavigationId = e.NavigationId;
            _isPlayerNavigationInProgress = true;
            _isPlayerRuntimeReady = false;
            unchecked
            {
                _playerNavigationVersion++;
            }
            _lastXpSampleTimestamp = null;
            _lastMeasuredTime = -1;
            _soundEffectsPrewarmedForCurrentNavigation = false;

            if (_video.IsTwitchStream)
            {
                // Twitch: don't hide the surface during navigations (login redirects, page loads).
                SetPlayerInteractionsEnabled(false);
                return;
            }

            SetPlayerInteractionsEnabled(false);
            SetPlayerLoadingOverlay(visible: true, PT("Загрузка видео...", "Loading video..."));
            SetPlayerSurfaceVisible(false);

        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_isPlayerClosing)
            {
                return;
            }

            if (e.NavigationId != _activeNavigationId) return;
            DiagnosticsService.LogInfo("Player.Navigation", $"Completed: success={e.IsSuccess}, status={e.WebErrorStatus}.");
            var navigationVersion = _playerNavigationVersion;

            // Twitch mode: handle before YouTube error-recovery logic.
            // Login redirects may have e.IsSuccess = false; we never want to recover to a URL here.
            if (_video.IsTwitchStream)
            {
                if (Player.CoreWebView2 is null)
                {
                    return;
                }

                _navigationCompletedFailureCount = 0;
                _isPlayerNavigationInProgress = false;
                _isPlayerRuntimeReady = true;
                SetPlayerInteractionsEnabled(true);
                SetPlayerSurfaceVisible(true);
                SetPlayerLoadingOverlay(visible: false);

                try
                {
                    Player.ZoomFactor = 1.0;
                    _lastAppliedEffectsSignature = string.Empty;
                    await ApplyEffectsSafelyAsync(force: true);
                    FlushPendingEffectsApplyRequests();
                    _ = PrewarmWebViewSoundEffectsAsync();
                }
                catch
                {
                    // Effects apply failures are non-fatal for Twitch.
                }
                return;
            }

            if (!e.IsSuccess || Player.CoreWebView2 is null)
            {
                _navigationCompletedFailureCount++;
                if (_navigationCompletedFailureCount <= 2)
                {
                    SetPlayerLoadingOverlay(visible: true, PT("Повторная попытка загрузки плеера...", "Retrying player load..."));
                    _ = RecoverLockedVideoAsync();
                    // Keep _isPlayerNavigationInProgress = true; RecoverLockedVideoAsync will re-navigate.
                    return;
                }

                // Too many failures — release the navigation lock so the timer can still tick.
                _isPlayerNavigationInProgress = false;
                _isPlayerRuntimeReady = false;
                SetPlayerLoadingOverlay(visible: true, PT(
                    "Не удалось загрузить плеер. Проверьте интернет и попробуйте снова.",
                    "Failed to load player. Check internet connection and try again."));
                SetPlayerInteractionsEnabled(false);
                RetryPlayerButton.Visibility = Visibility.Visible;
                PlayerLoadingProgress.Visibility = Visibility.Collapsed;
                return;
            }

            _navigationCompletedFailureCount = 0;
            try
            {
                Player.ZoomFactor = 1.0;

                // A successful navigation can be a consent/login document, not a video.
                // Let the user interact with it before installing player-only behavior.
                if (!await WaitForPlayerDocumentAsync(navigationVersion)) return;

                SetPlayerLoadingOverlay(visible: true, PT("Загрузка видео...", "Loading video..."));
                var bootstrapResult = await ExecuteWebScriptWithTimeoutAsync(
                    BuildKioskModeScript(
                        _video.VideoId,
                        ReadConfiguredKey(_appSettings.HideEffectsPanelBind, Key.H),
                        [.. BuildPlayerHotkeySet()]),
                    timeoutMs: 10000,
                    operation: "CoreWebView2_NavigationCompleted.Bootstrap");
                if (_isPlayerClosing || navigationVersion != _playerNavigationVersion)
                {
                    return;
                }

                if (bootstrapResult is null)
                {
                    // Script timed out or failed — release the lock so the player isn't frozen forever.
                    // The position timer will clear the loading overlay once video starts ticking.
                    _isPlayerNavigationInProgress = false;
                    _isPlayerRuntimeReady = true;
                    SetPlayerSurfaceVisible(true);
                    SetPlayerInteractionsEnabled(true);
                    SetPlayerLoadingOverlay(visible: true, PT(
                        "Загрузка видео...",
                        "Loading video..."));
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
                UpdateJokerVideoState();
                FlushPendingEffectsApplyRequests();
                _ = PrewarmWebViewSoundEffectsAsync();
            }
            catch
            {
                // Script error during bootstrap — release lock so the player isn't frozen.
                _isPlayerNavigationInProgress = false;
                _isPlayerRuntimeReady = true;
                SetPlayerSurfaceVisible(true);
                SetPlayerInteractionsEnabled(true);
                SetPlayerLoadingOverlay(visible: true, PT(
                    "Ошибка инициализации плеера. Попробуйте открыть видео снова.",
                    "Player initialization error. Try opening this video again."));
            }
        }

        private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (_isPlayerClosing || _video.IsTwitchStream) return;
            UpdateJokerVideoState();
            if (Player.CoreWebView2 is not null && !IsAllowedPlayerNavigation(Player.CoreWebView2.Source))
                _ = RecoverLockedVideoAsync();
        }

        private void UpdateJokerVideoState()
        {
            if (_video.IsTwitchStream)
            {
                _isOnJokerVideo = true;
                SetNonJokerBannerVisible(false);
                return;
            }

            var currentUrl = Player.CoreWebView2?.Source;
            bool isJoker = false;
            if (!string.IsNullOrEmpty(currentUrl) &&
                Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri) &&
                uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                var q = HttpUtility.ParseQueryString(uri.Query);
                isJoker = string.Equals(q.Get("v"), _video.VideoId, StringComparison.Ordinal);
            }

            _isOnJokerVideo = isJoker;
            SetNonJokerBannerVisible(!isJoker);
        }

        private void SetNonJokerBannerVisible(bool visible)
        {
            if (NonJokerBanner is null) return;
            NonJokerBanner.Visibility = visible
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }

        private bool TryReadTrustedWebMessage(CoreWebView2WebMessageReceivedEventArgs e, out string message)
        {
            message = string.Empty;
            if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps) return false;
            var trusted = _video.IsTwitchStream
                ? source.Host == "www.twitch.tv" || source.Host == "twitch.tv"
                : source.Host == "www.youtube.com" && source.AbsolutePath == "/watch" &&
                  string.Equals(HttpUtility.ParseQueryString(source.Query).Get("v"), _video.VideoId, StringComparison.Ordinal);
            if (!trusted) return false;
            try { message = e.TryGetWebMessageAsString(); return !string.IsNullOrEmpty(message) && message.Length <= 512; }
            catch (ArgumentException ex) { DiagnosticsService.LogException("WebMessage.Type", ex); return false; }
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!TryReadTrustedWebMessage(e, out var message)) return;
            if (message.StartsWith("jdbd:media:", StringComparison.Ordinal))
            {
                DiagnosticsService.LogInfo("Player.Media", message["jdbd:media:".Length..]);
                return;
            }
            if (string.Equals(message, "jdbd:web-input-on", StringComparison.Ordinal))
            {
                _isWebViewTextInputActive = true;
                Dispatcher.BeginInvoke(UpdateGlobalHotkeysForTypingFocusState, DispatcherPriority.Input);
                return;
            }

            if (string.Equals(message, "jdbd:web-input-off", StringComparison.Ordinal))
            {
                _isWebViewTextInputActive = false;
                Dispatcher.BeginInvoke(UpdateGlobalHotkeysForTypingFocusState, DispatcherPriority.Input);
                return;
            }

            if (string.Equals(message, "jdbd:add-timecode", StringComparison.Ordinal))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    MarkUserInteraction();
                    RequestTimecodeCapture();
                }, DispatcherPriority.Input);
                return;
            }

            if (message.StartsWith("jdbd:open-community:", StringComparison.Ordinal))
            {
                var kind = message.EndsWith(":chat", StringComparison.Ordinal)
                    ? PlayerCommunityDockKind.Chat
                    : PlayerCommunityDockKind.Comments;
                Dispatcher.BeginInvoke(async () =>
                {
                    MarkUserInteraction();
                    await OpenCommunityDockAsync(kind, forceReload: false);
                }, DispatcherPriority.Input);
                return;
            }

            if (string.Equals(message, "jdbd:seeked", StringComparison.Ordinal))
            {
                Dispatcher.BeginInvoke(OnSeekDetected, DispatcherPriority.Input);
                return;
            }

            if (!CanProcessPlayerCommands())
            {
                return;
            }

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

        private async void RetryPlayerButton_Click(object sender, RoutedEventArgs e)
        {
            _navigationCompletedFailureCount = 0;
            _isRecoveringBlockedNavigation = false;
            await RecoverLockedVideoAsync();
        }

        private void SetPlayerLoadingOverlay(bool visible, string? text = null)
        {
            if (PlayerLoadingOverlay is null)
            {
                return;
            }

            PlayerLoadingOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            RetryPlayerButton.Visibility = Visibility.Collapsed;
            PlayerLoadingProgress.Visibility = Visibility.Visible;
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

            UpdateGlobalHotkeysForTypingFocusState();
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
                    const player = document.getElementById('movie_player');
                    if (video instanceof HTMLVideoElement) {
                        const hasNetworkSource = video.networkState !== HTMLMediaElement.NETWORK_NO_SOURCE;
                        const hasRenderableFrame = Number.isFinite(video.currentTime) &&
                            (video.currentTime > 0.15 || video.readyState >= 2);
                        const isLiveUiReady = !!document.querySelector(
                            '.ytp-live, .ytp-live-badge, .ytp-time-current, .ytp-time-display');
                        const isPlayingOrReady =
                            (!video.paused && hasNetworkSource) ||
                            hasRenderableFrame ||
                            (isLiveUiReady && hasNetworkSource && video.readyState >= 1);
                        if (isPlayingOrReady) {
                            return true;
                        }
                    }

                    if (player instanceof HTMLElement) {
                        const playerClasses = player.className || '';
                        const isPlayerReady =
                            playerClasses.includes('playing-mode') ||
                            playerClasses.includes('paused-mode') ||
                            player.getAttribute('data-player-size');
                        if (isPlayerReady) {
                            return true;
                        }
                    }

                    return false;
                })();
                """;

            for (var i = 0; i < 150; i++)
            {
                if (_isPlayerClosing)
                {
                    return false;
                }

                try
                {
                    var result = await ExecuteWebScriptWithTimeoutAsync(
                        playbackStateScript,
                        timeoutMs: 1500,
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
            if (_video.IsTwitchStream)
            {
                return "https://www.twitch.tv/";
            }

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
            TryMigrateLegacyProfile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView_Profile"), stableFolder);
            TryMigrateLegacyProfile(Path.Combine(AppStoragePaths.GetCurrentLocalAppDataDirectory(), "YouTube_Profile"), stableFolder);
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

        /// <summary>
        /// Validates and returns the video ID if it matches YouTube format, otherwise returns empty string.
        /// YouTube IDs are alphanumeric + underscore + hyphen, typically 11 chars.
        /// </summary>
        private static string SanitizeVideoId(string videoId)
        {
            if (string.IsNullOrEmpty(videoId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(videoId, @"^[a-zA-Z0-9_\-]{1,20}$"))
            {
                return string.Empty;
            }

            return videoId;
        }

        private static string BuildSeekBridgeScript()
        {
            return """
                (() => {
                    // Only the top-level YouTube watch frame should report seeks.
                    if (window.frameElement !== null) return;
                    if (location.hostname !== 'www.youtube.com') return;
                    if (!location.pathname.startsWith('/watch')) return;
                    if (window.__jdbdSeekBridgeInstalled) return;
                    window.__jdbdSeekBridgeInstalled = true;

                    // Track steady-state playback position so we can distinguish
                    // user-initiated seeks (large jumps) from internal YouTube seeks
                    // triggered by quality switches, buffering, etc. (near-zero jumps).
                    let lastSteadyTime = -1;
                    let attachedVideo = null;

                    const onTimeUpdate = () => {
                        if (attachedVideo && !attachedVideo.seeking) {
                            lastSteadyTime = attachedVideo.currentTime;
                        }
                    };

                    const onSeeked = () => {
                        if (!attachedVideo) return;
                        const newTime = attachedVideo.currentTime;
                        if (!Number.isFinite(newTime)) return;
                        // Ignore seeks < 3 s from last known position (quality switches, buffering).
                        if (lastSteadyTime >= 0 && Math.abs(newTime - lastSteadyTime) < 3) return;
                        try { window.chrome?.webview?.postMessage('jdbd:seeked'); } catch {}
                    };

                    const tryAttach = () => {
                        const video = window.__stwfxRuntime?.video
                            || document.querySelector('video.html5-main-video')
                            || document.querySelector('.html5-video-player video');
                        if (!video || video === attachedVideo) return;
                        if (attachedVideo) {
                            attachedVideo.removeEventListener('timeupdate', onTimeUpdate);
                            attachedVideo.removeEventListener('seeked', onSeeked);
                        }
                        attachedVideo = video;
                        video.addEventListener('timeupdate', onTimeUpdate, { passive: true });
                        video.addEventListener('seeked', onSeeked, { passive: true });
                    };

                    const interval = setInterval(tryAttach, 800);
                    setTimeout(() => clearInterval(interval), 60000);
                    tryAttach();
                })();
                """;
        }

        private static string BuildWebInputStateBridgeScript()
        {
            return """
                (() => {
                    if (window.__jdbdWebInputBridgeInstalled) {
                        return;
                    }

                    window.__jdbdWebInputBridgeInstalled = true;
                    let lastState = null;

                    const isEditableElement = (element) => {
                        if (!element || !(element instanceof Element)) {
                            return false;
                        }

                        if (element.matches('input, textarea, select, [contenteditable=""], [contenteditable="true"]')) {
                            return true;
                        }

                        if (element.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"]')) {
                            return true;
                        }

                        const role = (element.getAttribute('role') || '').toLowerCase();
                        return role === 'textbox' || role === 'searchbox' || role === 'combobox';
                    };

                    const reportState = () => {
                        const nextState = isEditableElement(document.activeElement);
                        if (lastState === nextState) {
                            return;
                        }

                        lastState = nextState;
                        try {
                            window.chrome?.webview?.postMessage(nextState ? 'jdbd:web-input-on' : 'jdbd:web-input-off');
                        } catch {
                            // no-op
                        }
                    };

                    const reportSoon = () => setTimeout(reportState, 0);
                    document.addEventListener('focusin', reportSoon, true);
                    document.addEventListener('focusout', reportSoon, true);
                    document.addEventListener('pointerdown', reportSoon, true);
                    document.addEventListener('mousedown', reportSoon, true);
                    document.addEventListener('keydown', reportSoon, true);
                    window.addEventListener('load', reportSoon);
                    reportSoon();
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

        private async Task MirrorPlaybackKeyToWebViewAsync(Key key)
        {
            if (!CanProcessPlayerCommands() || Player.CoreWebView2 is null)
            {
                return;
            }

            var action = key switch
            {
                Key.Space => "togglePlayPause",
                Key.Left => "seekBackward",
                Key.Right => "seekForward",
                Key.Up => "volumeUp",
                Key.Down => "volumeDown",
                _ => string.Empty
            };
            if (string.IsNullOrEmpty(action))
            {
                return;
            }

            var script = $$"""
                (() => {
                    try {
                        const video = document.querySelector('video');
                        if (!video) {
                            return false;
                        }

                        const clamp01 = (v) => Math.max(0, Math.min(1, v));
                        switch ('{{action}}') {
                            case 'togglePlayPause':
                                if (video.paused) {
                                    const playPromise = video.play?.();
                                    if (playPromise && typeof playPromise.catch === 'function') {
                                        playPromise.catch(() => {});
                                    }
                                } else {
                                    video.pause?.();
                                }
                                return true;

                            case 'seekBackward':
                                if (Number.isFinite(video.currentTime)) {
                                    video.currentTime = Math.max(0, video.currentTime - 5);
                                    return true;
                                }
                                return false;

                            case 'seekForward':
                                if (Number.isFinite(video.currentTime)) {
                                    const duration = Number.isFinite(video.duration) ? video.duration : Number.POSITIVE_INFINITY;
                                    video.currentTime = Math.min(duration, video.currentTime + 5);
                                    return true;
                                }
                                return false;

                            case 'volumeUp':
                                video.muted = false;
                                video.volume = clamp01((Number.isFinite(video.volume) ? video.volume : 0) + 0.05);
                                return true;

                            case 'volumeDown':
                                video.muted = false;
                                video.volume = clamp01((Number.isFinite(video.volume) ? video.volume : 0) - 0.05);
                                return true;

                            default:
                                return false;
                        }
                    } catch {
                        return false;
                    }
                })();
                """;

            await ExecuteWebScriptWithTimeoutAsync(
                script,
                timeoutMs: 500,
                operation: $"MirrorPlaybackKeyToWebViewAsync.{key}");

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

            // Allow all Twitch infrastructure when playing a Twitch stream.
            if (_video.IsTwitchStream)
            {
                static bool HostMatches(string value, string expected) =>
                    value == expected || value.EndsWith($".{expected}", StringComparison.Ordinal);

                var isTwitchHost = HostMatches(host, "twitch.tv") || host == "www.amazon.com" || host == "accounts.google.com" || host == "appleid.apple.com";
                return isTwitchHost;
            }

            if (host is "accounts.google.com" or "consent.google.com") return true;

            if (host != "youtube.com" && !host.EndsWith(".youtube.com", StringComparison.Ordinal))
            {
                return false;
            }

            if (host.StartsWith("consent.", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith("accounts.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (uri.AbsolutePath.StartsWith("/signin", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/oauth", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/consent", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/sorry", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/account_advanced", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                var query = HttpUtility.ParseQueryString(uri.Query);
                var videoId = query.Get("v");
                return string.Equals(videoId, _video.VideoId, StringComparison.Ordinal);
            }

            // Allow embed player and YouTube internal API/resource paths.
            if (uri.AbsolutePath.Equals("/embed/" + _video.VideoId, StringComparison.Ordinal) ||
                uri.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/youtubei/", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/s/", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/generate_204", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/ptracking", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/pagead/", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/annotations_invideo", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/get_video_info", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/videoplayback", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Block home page, shorts, channel pages, search, etc. — only video watch pages allowed.
            _ = ShowBlockedNavigationToastAsync();
            return false;
        }

        private async Task ShowBlockedNavigationToastAsync()
        {
            if (BlockedNavToast is null || _isPlayerClosing)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                BlockedNavToast.Visibility = System.Windows.Visibility.Visible;
            });

            await Task.Delay(2000);

            await Dispatcher.InvokeAsync(() =>
            {
                if (BlockedNavToast is not null)
                {
                    BlockedNavToast.Visibility = System.Windows.Visibility.Collapsed;
                }
            });
        }


    }
}
