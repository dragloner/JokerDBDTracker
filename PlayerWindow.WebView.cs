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
                VideoTitleText.Text = _video.IsTwitchStream ? "Twitch" : _video.Title;
                InitializePresetsUi();
                if (_video.IsTwitchStream)
                {
                    EffectsWorkspaceTimecodesTab.Visibility = System.Windows.Visibility.Collapsed;
                }
                else
                {
                    _ = LoadVideoTimecodesAsync();
                }
                if (PlayerSfxSpamToggle is not null)
                {
                    PlayerSfxSpamToggle.IsChecked = _appSettings.SoundSpamMode;
                }
                ApplyPlayerLocalization();
                UpdateDynamicBindHints();
                RegisterGlobalHotkeys();
                UpdateWindowSizeButtonState();
                UpdateStrengthSlidersEnabledState();
                UpdateEffectDetailsVisibility(animate: false);
                SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
                InitializeWatchTogetherSync();
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
                    await Player.EnsureCoreWebView2Async(environment);

                    Player.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    Player.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    Player.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    Player.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    Player.CoreWebView2.Settings.UserAgent = DesktopChromeUserAgent;
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
                    }
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

            if (_video.IsTwitchStream)
            {
                // Twitch: don't hide the surface during navigations (login redirects, page loads).
                // Just check if the URL is allowed and cancel if not.
                SetPlayerInteractionsEnabled(false);
                if (!IsAllowedPlayerNavigation(e.Uri))
                {
                    e.Cancel = true;
                }
                return;
            }

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
                        ReadConfiguredKey(_appSettings.HideEffectsPanelBind, Key.H),
                        [.. BuildPlayerHotkeySet()]),
                    timeoutMs: 3500,
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
                isJoker = string.Equals(q.Get("v"), _video.VideoId, StringComparison.OrdinalIgnoreCase);
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

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.TryGetWebMessageAsString();
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

        private static string BuildKioskModeScript(string videoId, Key panelToggleKey, IReadOnlyCollection<Key> reservedAppKeys)
        {
            var safeVideoId = SanitizeVideoId(videoId);
            if (string.IsNullOrEmpty(safeVideoId))
            {
                return string.Empty;
            }

            var (panelToggleKeyVariants, panelToggleCodeVariants) = BuildKeyboardEventVariants(panelToggleKey);
            var reservedKeyVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reservedCodeVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in reservedAppKeys)
            {
                var (keys, codes) = BuildKeyboardEventVariants(key);
                foreach (var value in keys)
                {
                    reservedKeyVariants.Add(value);
                }

                foreach (var value in codes)
                {
                    reservedCodeVariants.Add(value);
                }
            }

            var panelToggleKeyJson = JsonSerializer.Serialize(panelToggleKeyVariants);
            var panelToggleCodeJson = JsonSerializer.Serialize(panelToggleCodeVariants);
            var reservedKeyJson = JsonSerializer.Serialize(reservedKeyVariants);
            var reservedCodeJson = JsonSerializer.Serialize(reservedCodeVariants);
            return $$"""
                (() => {
                    const expectedVideoId = '{{safeVideoId}}';
                    const panelToggleKeys = new Set({{panelToggleKeyJson}});
                    const panelToggleCodes = new Set({{panelToggleCodeJson}});
                    const reservedAppKeys = new Set({{reservedKeyJson}});
                    const reservedAppCodes = new Set({{reservedCodeJson}});
                    const isPanelToggleKey = (event) => {
                        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
                            return false;
                        }

                        const key = (event.key || '').toUpperCase();
                        const code = event.code || '';
                        return panelToggleKeys.has(key) || panelToggleCodes.has(code);
                    };
                    const isEditableTarget = (target) => {
                        if (!target || !(target instanceof Element)) {
                            return false;
                        }

                        return !!target.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"], [role="textbox"], [role="searchbox"], [role="combobox"]');
                    };
                    const isReservedAppShortcut = (event) => {
                        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
                            return false;
                        }

                        const key = (event.key || '').toUpperCase();
                        const code = event.code || '';
                        return reservedAppKeys.has(key) || reservedAppCodes.has(code);
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

                    const installTimecodeBridge = () => {
                        if (window.__jdbdTimecodeBridgeInstalled) {
                            return;
                        }

                        window.__jdbdTimecodeBridgeInstalled = true;

                        document.addEventListener('keydown', (event) => {
                            if (event.defaultPrevented || event.repeat) {
                                return;
                            }

                            if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
                                return;
                            }

                            if (isEditableTarget(event.target)) {
                                return;
                            }

                            if ((event.key || '').toUpperCase() !== 'M' && event.code !== 'KeyM') {
                                return;
                            }

                            event.preventDefault();
                            event.stopImmediatePropagation();
                            event.stopPropagation();
                            try {
                                window.chrome?.webview?.postMessage('jdbd:add-timecode');
                            } catch {
                                // no-op
                            }
                        }, { capture: true });
                    };

                    const installReservedShortcutBlocker = () => {
                        if (window.__jdbdReservedShortcutBlockerInstalled) {
                            return;
                        }

                        window.__jdbdReservedShortcutBlockerInstalled = true;
                        const blockReservedShortcut = (event) => {
                            if (!isReservedAppShortcut(event) || isEditableTarget(event.target)) {
                                return;
                            }

                            const key = (event.key || '').toUpperCase();
                            const code = event.code || '';
                            if (isPanelToggleKey(event) || key === 'M' || code === 'KeyM') {
                                return;
                            }

                            event.preventDefault();
                            event.stopImmediatePropagation();
                            event.stopPropagation();
                        };

                        document.addEventListener('keydown', blockReservedShortcut, { capture: true });
                        document.addEventListener('keyup', blockReservedShortcut, { capture: true });
                    };

                    const getPlayerControlText = (element) => {
                        if (!element) {
                            return '';
                        }

                        const parts = [
                            element.getAttribute?.('aria-label') || '',
                            element.getAttribute?.('title') || '',
                            element.getAttribute?.('aria-controls') || '',
                            element.getAttribute?.('data-tooltip-target-id') || '',
                            element.id || '',
                            element.className || ''
                        ];
                        return parts.join(' ').toLowerCase();
                    };

                    const ensurePlayerNotMutedByStartup = () => {
                        try {
                            const player = document.getElementById('movie_player');
                            const video = document.querySelector('video');
                            if (!(player instanceof HTMLElement) || !(video instanceof HTMLVideoElement)) {
                                return false;
                            }

                            let changed = false;
                            const currentVolume = Number.isFinite(video.volume) ? video.volume : 0;
                            if (video.muted || currentVolume <= 0.001) {
                                video.muted = false;
                                if (currentVolume <= 0.001) {
                                    video.volume = 1;
                                }
                                changed = true;
                            }

                            const controls = Array.from(player.querySelectorAll('.ytp-button, button, [role="button"]'));
                            for (const control of controls) {
                                if (!(control instanceof HTMLElement)) {
                                    continue;
                                }

                                const text = getPlayerControlText(control);
                                const looksLikeUnmute =
                                    text.includes('unmute') ||
                                    text.includes('включить звук') ||
                                    text.includes('turn on sound') ||
                                    text.includes('mute toggle');
                                if (!looksLikeUnmute) {
                                    continue;
                                }

                                if (text.includes('mute') && !text.includes('unmute') && !text.includes('включить звук')) {
                                    continue;
                                }

                                try {
                                    control.click();
                                } catch {
                                    // no-op
                                }
                                changed = true;
                                break;
                            }

                            return changed;
                        } catch {
                            return false;
                        }
                    };

                    const hideInlinePanelElement = (element) => {
                        if (!element || !element.style) {
                            return false;
                        }

                        element.style.setProperty('display', 'none', 'important');
                        element.style.setProperty('visibility', 'hidden', 'important');
                        element.style.setProperty('pointer-events', 'none', 'important');
                        element.style.setProperty('width', '0px', 'important');
                        element.style.setProperty('min-width', '0px', 'important');
                        element.style.setProperty('max-width', '0px', 'important');
                        element.style.setProperty('flex', '0 0 0px', 'important');
                        element.style.setProperty('opacity', '0', 'important');
                        return true;
                    };

                    const isPlayerInFullscreen = () => {
                        try {
                            const fullscreenElement = document.fullscreenElement;
                            const player = document.getElementById('movie_player');
                            if (!(fullscreenElement instanceof Element) || !(player instanceof HTMLElement)) {
                                return false;
                            }

                            return fullscreenElement === player ||
                                   player.contains(fullscreenElement) ||
                                   fullscreenElement.contains(player);
                        } catch {
                            return false;
                        }
                    };

                    const nudgeYouTubePlayerLayout = () => {
                        try {
                            const now = Date.now();
                            const last = Number(window.__jdbdLastLayoutNudgeTs || 0);
                            if (now - last < 250) {
                                return false;
                            }
                            window.__jdbdLastLayoutNudgeTs = now;

                            const player = document.getElementById('movie_player');
                            const html5Player = player?.querySelector?.('.html5-video-player') || null;
                            const playerContainer = document.getElementById('player-theater-container') ||
                                                    document.getElementById('full-bleed-container') ||
                                                    document.getElementById('player-container-inner') ||
                                                    document.getElementById('player-container-outer') ||
                                                    document.getElementById('player-container');
                            const flexy = document.querySelector('ytd-watch-flexy');
                            let changed = false;

                            if (player instanceof HTMLElement) {
                                if (playerContainer instanceof HTMLElement) {
                                    // Force wrapper/player width sync to trigger internal player resize observers.
                                    playerContainer.style.setProperty('width', '100%', 'important');
                                    playerContainer.style.setProperty('max-width', 'none', 'important');
                                    player.style.setProperty('width', '100%', 'important');
                                    player.style.setProperty('max-width', 'none', 'important');
                                    player.style.setProperty('margin-left', '0', 'important');
                                    player.style.setProperty('margin-right', '0', 'important');
                                    changed = true;
                                }

                                if (html5Player instanceof HTMLElement) {
                                    html5Player.style.setProperty('width', '100%', 'important');
                                    html5Player.style.setProperty('max-width', 'none', 'important');
                                    changed = true;
                                }

                                // Force synchronous reflow and wake internal observers without changing layout permanently.
                                player.style.setProperty('outline', '1px solid transparent', 'important');
                                void player.offsetWidth;
                                player.style.removeProperty('outline');
                                player.dispatchEvent(new Event('resize'));
                                html5Player?.dispatchEvent?.(new Event('resize'));
                                changed = true;

                                const maybeMethods = ['updateSize', 'onResize_', 'handleGlobalResize_', 'resize_', 'handleResize'];
                                for (const name of maybeMethods) {
                                    const fn = player[name];
                                    if (typeof fn === 'function') {
                                        try {
                                            fn.call(player);
                                            changed = true;
                                        } catch {
                                            // no-op
                                        }
                                    }
                                }

                                // Pulse width by 1px and back to emulate the same relayout path as UI panel toggles.
                                const previousMinWidth = player.style.getPropertyValue('min-width');
                                player.style.setProperty('min-width', '0px', 'important');
                                const currentWidth = Math.max(1, Math.round(player.getBoundingClientRect().width));
                                player.style.setProperty('width', `${Math.max(1, currentWidth - 1)}px`, 'important');
                                void player.offsetWidth;
                                player.style.setProperty('width', '100%', 'important');
                                if (previousMinWidth) {
                                    player.style.setProperty('min-width', previousMinWidth, 'important');
                                } else {
                                    player.style.removeProperty('min-width');
                                }
                                changed = true;
                            }

                            if (flexy instanceof HTMLElement) {
                                const hadTheater = flexy.hasAttribute('theater');
                                if (hadTheater) {
                                    flexy.removeAttribute('theater');
                                    void flexy.offsetWidth;
                                    flexy.setAttribute('theater', '');
                                } else {
                                    flexy.setAttribute('theater', '');
                                }
                                changed = true;
                            }

                            window.dispatchEvent(new Event('resize'));
                            document.dispatchEvent(new Event('fullscreenchange'));
                            return changed;
                        } catch {
                            return false;
                        }
                    };

                    const isPlayerMenuPopupOpen = () => {
                        try {
                            const candidates = document.querySelectorAll(
                                '.ytp-popup, .ytp-settings-menu, .ytp-panel-menu, [role="menu"], [role="dialog"]');
                            for (const element of candidates) {
                                if (!(element instanceof HTMLElement)) {
                                    continue;
                                }

                                const style = getComputedStyle(element);
                                if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity || '1') <= 0.01) {
                                    continue;
                                }

                                const rect = element.getBoundingClientRect();
                                if (rect.width >= 40 && rect.height >= 40) {
                                    return true;
                                }
                            }

                            return false;
                        } catch {
                            return false;
                        }
                    };

                    const pulseCommentPanelToggleForLayoutRepair = () => {
                        return false;
                        try {
                            const now = Date.now();
                            const last = Number(window.__jdbdLastCommentPulseTs || 0);
                            if (now - last < 1200) {
                                return false;
                            }

                            const player = document.getElementById('movie_player');
                            if (!player) {
                                return false;
                            }

                            const buttons = Array.from(player.querySelectorAll('.ytp-button, button, [role="button"]'));
                            let targetButton = null;
                            for (const button of buttons) {
                                if (!(button instanceof HTMLElement)) {
                                    continue;
                                }

                                const text = getPlayerControlText(button);
                                const looksLikeCommentsOrChat =
                                    text.includes('comment') ||
                                    text.includes('comments') ||
                                    text.includes('комментар') ||
                                    text.includes('chat') ||
                                    text.includes('чат') ||
                                    text.includes('discussion') ||
                                    text.includes('replay chat');
                                if (!looksLikeCommentsOrChat) {
                                    continue;
                                }

                                targetButton = button;
                                break;
                            }

                            if (!(targetButton instanceof HTMLElement)) {
                                return false;
                            }

                            window.__jdbdLastCommentPulseTs = now;
                            targetButton.click();
                            setTimeout(() => {
                                try {
                                    targetButton.click();
                                } catch {
                                    // no-op
                                }

                                setTimeout(() => {
                                    try {
                                        collapseInlinePlayerSidePanels();
                                    } catch {
                                        // no-op
                                    }
                                }, 60);
                            }, 60);
                            return true;
                        } catch {
                            return false;
                        }
                    };

                    const collapseInlinePlayerSidePanels = () => {
                        try {
                            const player = document.getElementById('movie_player');
                            if (!player) {
                                return false;
                            }

                            if (isPlayerMenuPopupOpen()) {
                                return false;
                            }

                            const keepOpen = window.__jdbdKeepSidePanelOpen === true && !isPlayerInFullscreen();
                            let changed = false;
                            const knownPanelSelectors = [
                                '#movie_player [class*="watch-comments" i]',
                                '#movie_player [class*="comments-panel" i]',
                                '#movie_player [class*="comment-panel" i]',
                                '#movie_player [class*="chat-panel" i]',
                                '#movie_player [class*="engagement-panel" i]',
                                '#movie_player [id*="watch-comments" i]',
                                '#movie_player [id*="comments-panel" i]',
                                '#movie_player [id*="chat-panel" i]',
                                '#movie_player [id*="engagement-panel" i]',
                                '#movie_player [data-panel-target-id]',
                                '#movie_player [data-panel-id]'
                            ];

                            if (!keepOpen) {
                                for (const selector of knownPanelSelectors) {
                                    for (const panel of document.querySelectorAll(selector)) {
                                        changed = hideInlinePanelElement(panel) || changed;
                                    }
                                }
                            }

                            const playerRootCandidates = [
                                player,
                                player.querySelector('.html5-video-player'),
                                player.querySelector('.ytp-embed'),
                                player.querySelector('.ytp-player-content')
                            ].filter(Boolean);

                            if (!keepOpen) {
                                for (const root of playerRootCandidates) {
                                    if (!root.classList) {
                                        continue;
                                    }

                                    for (const className of Array.from(root.classList)) {
                                        const name = String(className || '').toLowerCase();
                                        if ((name.includes('comment') || name.includes('chat') || name.includes('engagement')) &&
                                            (name.includes('panel') || name.includes('peek') || name.includes('sidebar') || name.includes('dock')))
                                        {
                                            try {
                                                root.classList.remove(className);
                                                changed = true;
                                            } catch {
                                                // no-op
                                            }
                                        }
                                    }
                                }
                            }

                            if (!keepOpen) {
                                const playerButtons = Array.from(
                                    player.querySelectorAll('.ytp-button, button, [role="button"]'));
                                for (const button of playerButtons) {
                                    if (!(button instanceof HTMLElement)) {
                                        continue;
                                    }

                                    const text = getPlayerControlText(button);
                                    const looksLikeCommentsOrChat =
                                        text.includes('comment') ||
                                        text.includes('comments') ||
                                        text.includes('комментар') ||
                                        text.includes('chat') ||
                                        text.includes('чат') ||
                                        text.includes('discussion') ||
                                        text.includes('discuss') ||
                                        text.includes('replay chat');
                                    if (!looksLikeCommentsOrChat) {
                                        continue;
                                    }

                                    const isPressed = (button.getAttribute('aria-pressed') || '').toLowerCase() === 'true';
                                    const isExpanded = (button.getAttribute('aria-expanded') || '').toLowerCase() === 'true';
                                    if (isPressed || isExpanded) {
                                        try {
                                            button.click();
                                            changed = true;
                                        } catch {
                                            // no-op
                                        }
                                    }
                                }
                            }

                            const video = player.querySelector('video');
                            if (!(video instanceof HTMLElement)) {
                                return changed;
                            }

                            const playerRect = player.getBoundingClientRect();
                            const videoRect = video.getBoundingClientRect();
                            const hostCandidates = [
                                document.getElementById('player-theater-container'),
                                document.getElementById('full-bleed-container'),
                                document.getElementById('player-container-inner'),
                                document.getElementById('player-container-outer'),
                                document.getElementById('player-container'),
                                document.getElementById('player'),
                                player.parentElement
                            ].filter(Boolean);

                            let hostRect = null;
                            for (const candidate of hostCandidates) {
                                if (!(candidate instanceof HTMLElement)) {
                                    continue;
                                }

                                const rect = candidate.getBoundingClientRect();
                                if (rect.width <= 0 || rect.height <= 0) {
                                    continue;
                                }

                                if (!hostRect || rect.width > hostRect.width) {
                                    hostRect = rect;
                                }
                            }

                            if (!(playerRect.width > 0 && videoRect.width > 0)) {
                                return changed;
                            }

                            const playerToHostWidthRatio = hostRect && hostRect.width > 0
                                ? playerRect.width / hostRect.width
                                : 1;
                            const playerTopOffset = hostRect
                                ? Math.max(0, playerRect.top - hostRect.top)
                                : 0;
                            const suspiciousUndersizedPlayer =
                                !!hostRect &&
                                hostRect.width >= 520 &&
                                playerToHostWidthRatio < 0.90;
                            const suspiciousVerticalOffset =
                                !!hostRect &&
                                hostRect.height >= 320 &&
                                playerTopOffset > Math.max(18, hostRect.height * 0.08);

                            if (!keepOpen && (suspiciousUndersizedPlayer || suspiciousVerticalOffset)) {
                                changed = nudgeYouTubePlayerLayout() || changed;
                                changed = pulseCommentPanelToggleForLayoutRepair() || changed;
                            }

                            const widthRatio = videoRect.width / playerRect.width;
                            const suspiciousSplitLayout = widthRatio < 0.82 && playerRect.width >= 420;
                            if (!suspiciousSplitLayout) {
                                return changed;
                            }

                            if (keepOpen) {
                                return changed;
                            }

                            changed = nudgeYouTubePlayerLayout() || changed;
                            changed = pulseCommentPanelToggleForLayoutRepair() || changed;

                            const minPanelWidth = Math.max(140, playerRect.width * 0.14);
                            const minPanelHeight = Math.max(120, playerRect.height * 0.2);
                            const playerLeft = playerRect.left;
                            const playerTop = playerRect.top;
                            const playerRight = playerRect.right;
                            const playerBottom = playerRect.bottom;
                            const rightThreshold = playerLeft + (playerRect.width * 0.58);

                            const isProtectedNode = (element) => {
                                if (!(element instanceof HTMLElement)) {
                                    return true;
                                }

                                if (element === video ||
                                    element.contains(video) ||
                                    video.contains(element))
                                {
                                    return true;
                                }

                                const idAndClass = `${element.id || ''} ${element.className || ''}`.toLowerCase();
                                if (idAndClass.includes('ytp-popup') ||
                                    idAndClass.includes('ytp-menu') ||
                                    idAndClass.includes('ytp-panel-menu') ||
                                    idAndClass.includes('ytp-settings') ||
                                    idAndClass.includes('ytp-quality') ||
                                    idAndClass.includes('ytp-subtitles') ||
                                    element.closest('.ytp-popup, .ytp-menuitem, .ytp-panel-menu, .ytp-settings-menu'))
                                {
                                    return true;
                                }

                                return idAndClass.includes('html5-video') ||
                                       idAndClass.includes('ytp-chrome') ||
                                       idAndClass.includes('ytp-gradient') ||
                                       idAndClass.includes('ytp-progress') ||
                                       idAndClass.includes('ytp-tooltip') ||
                                       idAndClass.includes('ytp-ce-') ||
                                       idAndClass.includes('caption') ||
                                       idAndClass.includes('subtitle');
                            };

                            const candidatePanels = [];
                            for (const element of player.querySelectorAll('*')) {
                                if (!(element instanceof HTMLElement) || isProtectedNode(element)) {
                                    continue;
                                }

                                const style = getComputedStyle(element);
                                if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity || '1') <= 0.01) {
                                    continue;
                                }

                                const rect = element.getBoundingClientRect();
                                if (rect.width < minPanelWidth || rect.height < minPanelHeight) {
                                    continue;
                                }

                                if (rect.left < rightThreshold) {
                                    continue;
                                }

                                if (rect.left < playerLeft || rect.top < playerTop || rect.right > playerRight + 1 || rect.bottom > playerBottom + 1) {
                                    continue;
                                }

                                if (rect.width > playerRect.width * 0.75 || rect.height > playerRect.height * 0.98) {
                                    continue;
                                }

                                candidatePanels.push({ element, rect, area: rect.width * rect.height });
                            }

                            candidatePanels.sort((a, b) => b.area - a.area);
                            for (const candidate of candidatePanels.slice(0, 4)) {
                                changed = hideInlinePanelElement(candidate.element) || changed;
                            }

                            const layoutResetCandidates = [
                                player,
                                player.querySelector('.html5-video-player'),
                                player.querySelector('.html5-video-container'),
                                document.getElementById('player-theater-container'),
                                document.getElementById('full-bleed-container'),
                                document.getElementById('player-container-inner'),
                                document.getElementById('player-container-outer'),
                                document.getElementById('player-container')
                            ].filter(Boolean);

                            for (const element of layoutResetCandidates) {
                                if (!(element instanceof HTMLElement)) {
                                    continue;
                                }

                                element.style.setProperty('margin-right', '0', 'important');
                                element.style.setProperty('padding-right', '0', 'important');
                                element.style.setProperty('right', '0', 'important');
                                element.style.setProperty('max-width', 'none', 'important');
                                element.style.setProperty('width', '100%', 'important');
                                changed = true;
                            }

                            return changed;
                        } catch {
                            return false;
                        }
                    };

                    const installYouTubeSidePanelGuard = () => {
                        if (window.__jdbdSidePanelGuardInstalled) {
                            return;
                        }

                        window.__jdbdSidePanelGuardInstalled = true;

                        const getSidePanelControlKind = (button) => {
                            if (!button) {
                                return '';
                            }

                            const text = getPlayerControlText(button);
                            if (!text) {
                                return '';
                            }

                            if (text.includes('comment') ||
                                text.includes('comments') ||
                                text.includes('комментар') ||
                                text.includes('discussion') ||
                                text.includes('discuss'))
                            {
                                return 'comments';
                            }

                            if (text.includes('live chat') ||
                                text.includes('chat replay') ||
                                text.includes('replay chat') ||
                                text.includes('chat') ||
                                text.includes('чат') ||
                                text.includes('message') ||
                                text.includes('сообщен'))
                            {
                                return 'chat';
                            }

                            return '';
                        };

                        const isControlExpanded = (button) => {
                            if (!button) {
                                return false;
                            }

                            return (button.getAttribute('aria-pressed') || '').toLowerCase() === 'true' ||
                                   (button.getAttribute('aria-expanded') || '').toLowerCase() === 'true' ||
                                   button.classList?.contains('ytp-button-active') === true;
                        };

                        const blockPanelToggle = (event) => {
                            if (!event.isTrusted) {
                                return;
                            }

                            const control = event.target && event.target.closest ? event.target.closest('.ytp-button, button, [role="button"], a[role="button"]') : null;
                            const panelKind = getSidePanelControlKind(control);
                            if (panelKind) {
                                const currentKind = String(window.__jdbdOpenSidePanelKind || '');
                                const isAlreadyOpen = isControlExpanded(control) || (window.__jdbdKeepSidePanelOpen === true && currentKind === panelKind);
                                const shouldOpen = !isAlreadyOpen;

                                window.__jdbdKeepSidePanelOpen = shouldOpen;
                                window.__jdbdOpenSidePanelKind = shouldOpen ? panelKind : '';
                                setTimeout(() => {
                                    try {
                                        window.__jdbdRepairImmersiveLayout?.();
                                    } catch {
                                        // no-op
                                    }
                                }, shouldOpen ? 180 : 120);
                                return;
                            }

                            const text = getPlayerControlText(control);
                            if (text.includes('close') || text.includes('закры') || text === 'x') {
                                window.__jdbdKeepSidePanelOpen = false;
                                window.__jdbdOpenSidePanelKind = '';
                            }
                        };

                        document.addEventListener('click', blockPanelToggle, { capture: true });
                        document.addEventListener('mousedown', blockPanelToggle, { capture: true });
                        document.addEventListener('pointerdown', blockPanelToggle, { capture: true });

                        let repairTimer = null;
                        const scheduleRepair = () => {
                            if (repairTimer !== null) { return; }
                            repairTimer = setTimeout(() => {
                                repairTimer = null;
                                try { applyImmersiveVideoLayout(); } catch { /* no-op */ }
                            }, 200);
                        };

                        window.__jdbdRepairImmersiveLayout = scheduleRepair;

                        const resetPanelsToClosed = () => {
                            window.__jdbdKeepSidePanelOpen = false;
                            window.__jdbdOpenSidePanelKind = '';
                        };

                        document.addEventListener('yt-page-data-updated', () => {
                            resetPanelsToClosed();
                            scheduleRepair();
                        }, true);
                        document.addEventListener('yt-navigate-finish', () => {
                            resetPanelsToClosed();
                            scheduleRepair();
                        }, true);
                        document.addEventListener('fullscreenchange', () => {
                            if (isPlayerInFullscreen()) {
                                resetPanelsToClosed();
                            }
                            scheduleRepair();
                        }, true);
                        window.addEventListener('resize', scheduleRepair, { passive: true });

                        for (const delay of [0, 80, 180, 350, 650, 1100, 1800, 2800, 4200]) {
                            setTimeout(scheduleRepair, delay);
                        }
                    };

                    const normalizeInlineSidePanels = () => {
                        try {
                            const player = document.getElementById('movie_player');
                            if (!player) {
                                return false;
                            }

                            if (isPlayerMenuPopupOpen()) {
                                return false;
                            }

                            const playerRect = player.getBoundingClientRect();
                            if (playerRect.width <= 0 || playerRect.height <= 0) {
                                return false;
                            }

                            let changed = false;
                            const rightThreshold = playerRect.left + (playerRect.width * 0.58);
                            const candidates = [];
                            for (const element of player.querySelectorAll('*')) {
                                if (!(element instanceof HTMLElement)) {
                                    continue;
                                }

                                const idAndClass = `${element.id || ''} ${element.className || ''}`.toLowerCase();
                                if (!idAndClass.includes('comment') &&
                                    !idAndClass.includes('chat') &&
                                    !idAndClass.includes('engagement') &&
                                    !element.hasAttribute('data-panel-target-id') &&
                                    !element.hasAttribute('data-panel-id'))
                                {
                                    continue;
                                }

                                const style = getComputedStyle(element);
                                if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity || '1') <= 0.01) {
                                    continue;
                                }

                                const rect = element.getBoundingClientRect();
                                if (rect.width < 180 || rect.height < 140 || rect.left < rightThreshold) {
                                    continue;
                                }

                                if (rect.left < playerRect.left || rect.right > playerRect.right + 1) {
                                    continue;
                                }

                                candidates.push({ element, rect, area: rect.width * rect.height });
                            }

                            candidates.sort((a, b) => b.area - a.area);
                            const panels = [];
                            for (const candidate of candidates) {
                                if (panels.some((existing) => existing.element.contains(candidate.element) || candidate.element.contains(existing.element))) {
                                    continue;
                                }

                                panels.push(candidate);
                            }

                            const findHeaderControls = (panel) => {
                                const panelRect = panel.getBoundingClientRect();
                                const controls = [];
                                for (const node of panel.querySelectorAll('button, [role="button"], a[href]')) {
                                    if (!(node instanceof HTMLElement)) {
                                        continue;
                                    }

                                    const rect = node.getBoundingClientRect();
                                    if (rect.top <= panelRect.top + 72) {
                                        controls.push(node);
                                    }
                                }

                                return controls;
                            };

                            for (const panelInfo of panels) {
                                const panel = panelInfo.element;
                                const panelRect = panelInfo.rect;
                                const clampedWidth = Math.min(420, Math.max(300, Math.round(panelRect.width)));
                                panel.style.setProperty('width', `${clampedWidth}px`, 'important');
                                panel.style.setProperty('min-width', `${clampedWidth}px`, 'important');
                                panel.style.setProperty('max-width', `${clampedWidth}px`, 'important');
                                panel.style.setProperty('flex', `0 0 ${clampedWidth}px`, 'important');
                                panel.style.setProperty('overflow', 'hidden', 'important');

                                const allowedControls = new Set(findHeaderControls(panel));
                                for (const node of panel.querySelectorAll('a[href], button, [role="button"]')) {
                                    if (!(node instanceof HTMLElement)) {
                                        continue;
                                    }

                                    if (allowedControls.has(node)) {
                                        continue;
                                    }

                                    const text = getPlayerControlText(node);
                                    const looksLikeItemAction =
                                        text.includes('reply') ||
                                        text.includes('ответ') ||
                                        text.includes('like') ||
                                        text.includes('dislike') ||
                                        text.includes('react') ||
                                        text.includes('menu') ||
                                        text.includes('ещё') ||
                                        text.includes('more');

                                    if (node.matches('a[href]')) {
                                        node.style.setProperty('pointer-events', 'none', 'important');
                                        node.style.setProperty('cursor', 'default', 'important');
                                        changed = true;
                                        continue;
                                    }

                                    if (looksLikeItemAction) {
                                        node.style.setProperty('display', 'none', 'important');
                                        changed = true;
                                        continue;
                                    }

                                    node.style.setProperty('pointer-events', 'none', 'important');
                                    changed = true;
                                }

                                const text = (panel.innerText || '').toLowerCase();
                                const commentNodes = panel.querySelectorAll('ytd-comment-thread-renderer, ytd-comment-view-model, [id*="comment"], [class*="comment"]').length;
                                const chatNodes = panel.querySelectorAll('[class*="chat"], [data-a-target*="chat"]').length;
                                const allowPanelBecauseUserJustOpenedIt = window.__jdbdKeepSidePanelOpen === true && !isPlayerInFullscreen();
                                const looksPlaceholderCommentPanel =
                                    (text.includes('featured comments') || text.includes('top is selected')) &&
                                    commentNodes < 3 &&
                                    chatNodes < 3;

                                const shouldCloseByDefault = !allowPanelBecauseUserJustOpenedIt;
                                const shouldClosePlaceholderPanel = !allowPanelBecauseUserJustOpenedIt && looksPlaceholderCommentPanel;
                                if (shouldCloseByDefault || shouldClosePlaceholderPanel) {
                                    const closeControl = [...allowedControls].find((node) => {
                                        const text = getPlayerControlText(node);
                                        return text.includes('close') || text.includes('закры') || text === 'x';
                                    });

                                    if (closeControl instanceof HTMLElement) {
                                        try {
                                            closeControl.click();
                                            changed = true;
                                            continue;
                                        } catch {
                                            // no-op
                                        }
                                    }

                                    changed = hideInlinePanelElement(panel) || changed;
                                }
                            }

                            return changed;
                        } catch {
                            return false;
                        }
                    };

                    // Returns true if the layout is fully stable:
                    //   - ytd-watch-flexy has theater, no two-columns attrs
                    //   - #secondary is explicitly JS-hidden (can't rely on CSS alone because
                    //     YouTube's own JS may re-set display:block with !important inline style)
                    // When stable, applyImmersiveVideoLayout skips all DOM writes, breaking the loop.
                    const isLayoutStable = () => {
                        try {
                            const flexy = document.querySelector('ytd-watch-flexy');
                            if (!flexy) { return false; }
                            if (!flexy.hasAttribute('theater')) { return false; }
                            for (const attr of Array.from(flexy.attributes)) {
                                if ((attr.name || '').toLowerCase().includes('two-columns')) { return false; }
                            }
                            // Verify #secondary is force-hidden
                            const sec = document.getElementById('secondary');
                            if (sec && sec.style.display !== 'none') { return false; }
                            // Verify all engagement panels are force-hidden (multiple instances exist:
                            // right-side panel, bottom comments panel, description panel, etc.)
                            const panels = document.querySelectorAll('ytd-engagement-panel-section-list-renderer');
                            for (const p of panels) {
                                if (p.style.display !== 'none') { return false; }
                            }
                            return true;
                        } catch { return false; }
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
                                html, body {
                                    margin: 0 !important;
                                    padding: 0 !important;
                                    background: #000 !important;
                                    overflow: hidden !important;
                                    width: 100vw !important;
                                    height: 100vh !important;
                                }

                                /* Nuclear fix: pin #movie_player to the full viewport so no YouTube
                                   layout quirk (masthead padding, theater offset, etc.) can shrink it.
                                   All player chrome (controls, title bar) lives inside movie_player
                                   so they stay correctly positioned relative to the video. */
                                #movie_player, .html5-video-player {
                                    position: fixed !important;
                                    top: 0 !important;
                                    left: 0 !important;
                                    right: 0 !important;
                                    bottom: 0 !important;
                                    width: 100vw !important;
                                    height: 100vh !important;
                                    max-width: none !important;
                                    max-height: none !important;
                                    z-index: 9000 !important;
                                    overflow: visible !important;
                                }

                                .html5-video-container {
                                    width: 100% !important;
                                    height: 100% !important;
                                    max-width: none !important;
                                    max-height: none !important;
                                }

                                .html5-main-video, video {
                                    width: 100% !important;
                                    height: 100% !important;
                                    max-height: none !important;
                                    object-fit: contain !important;
                                    left: 0 !important;
                                    top: 0 !important;
                                }

                                /* Background elements — don't need to look right, just not flicker */
                                ytd-app, #content, #page-manager, ytd-watch-flexy, #columns, #primary, #primary-inner {
                                    margin: 0 !important;
                                    padding: 0 !important;
                                    background: #000 !important;
                                }

                                /* ── Pointer-events lock ──────────────────────────────────────────────
                                   Kill pointer events on everything, restore only for the player tree.
                                   ytd-player contains #movie_player AND the gesture/click overlays that
                                   handle play/pause — those overlays are siblings of #movie_player inside
                                   ytd-player, so we must restore the whole ytd-player subtree, not just
                                   #movie_player. Engagement panels / chat live outside ytd-player as
                                   siblings of #columns, so they stay pointer-events:none. */
                                body * {
                                    pointer-events: none !important;
                                }
                                ytd-player, ytd-player *,
                                #movie_player, #movie_player *,
                                #player, #player * {
                                    pointer-events: auto !important;
                                }

                                /* Hide all page chrome outside the video player */
                                #secondary, #secondary-inner, #related,
                                ytd-watch-metadata, ytd-watch-info-text,
                                #description, #description-inline-expander, #description-inner,
                                ytd-text-inline-expander,
                                ytd-merch-shelf-renderer, ytd-reel-shelf-renderer,
                                ytd-watch-next-secondary-results-renderer, ytd-watch-next-feed-renderer,
                                #end, #meta, #guide, ytd-mini-guide-renderer, tp-yt-app-drawer,
                                ytd-masthead, #masthead-container, #header,
                                #columns > :not(#primary) {
                                    display: none !important;
                                    visibility: hidden !important;
                                    pointer-events: none !important;
                                }

                                /* Hide live chat, comments panel and all engagement overlays */
                                #chat, #chat-container, ytd-live-chat-frame,
                                .ytp-chat-widget, .ytp-fullerscreen-edu-button,
                                ytd-engagement-panel-section-list-renderer,
                                #engagement, ytd-engagement-panel-title-header-renderer,
                                .ytp-comments-header-renderer, .ytp-panel-anchor,
                                .ytp-suggested-action-badge-expanded,
                                .ytp-inline-preview-ui, .ytp-inline-preview-overlay {
                                    display: none !important;
                                    visibility: hidden !important;
                                    pointer-events: none !important;
                                }

                                /* Keep player controls visible — only hide distracting overlays */
                                .ytp-chrome-top,
                                .ytp-gradient-top,
                                .ytp-title,
                                .ytp-title-link,
                                .ytp-title-channel,
                                .ytp-title-text,
                                .ytp-title-expanded-overlay,
                                .ytp-title-expanded-heading,
                                .ytp-title-expanded-content,
                                .ytp-unmute,
                                .ytp-bezel-text-wrapper,
                                .ytp-endscreen-content,
                                .ytp-ce-element,
                                .ytp-cards-teaser,
                                .ytp-cards-button,
                                .ytp-pause-overlay,
                                .ytp-share-button-visible {
                                    display: none !important;
                                    visibility: hidden !important;
                                    pointer-events: none !important;
                                    opacity: 0 !important;
                                }

                            `;
                            (document.head || document.documentElement).appendChild(style);
                        };

                        ensureLayoutStyle();

                        // Early exit if layout is already correct — prevents the write→observer→write loop.
                        // CSS is injected above (idempotent). Unmute is checked once below.
                        if (isLayoutStable()) {
                            if (!window.__jdbdUnmuteDone) {
                                window.__jdbdUnmuteDone = ensurePlayerNotMutedByStartup();
                            }
                            return;
                        }

                        const flexy = document.querySelector('ytd-watch-flexy');
                        if (flexy) {
                            flexy.setAttribute('theater', '');
                            for (const attr of Array.from(flexy.attributes)) {
                                const name = (attr.name || '').toLowerCase();
                                if (name.includes('two-columns'))
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

                        // Force-hide sidebar/chat/engagement elements via inline JS style.
                        // CSS alone can be overridden by YouTube's reactive JS (inline !important).
                        // Single-instance elements: getElementById is faster and sufficient.
                        const hideIds = ['secondary', 'secondary-inner', 'related', 'chat', 'chat-container', 'engagement'];
                        for (const id of hideIds) {
                            const el = document.getElementById(id);
                            if (el && el.style.display !== 'none') {
                                el.style.setProperty('display', 'none', 'important');
                            }
                        }
                        // Single-instance selectors (querySelector is fine — only one in the DOM).
                        const hideSingleSelectors = [
                            'ytd-watch-metadata', 'ytd-watch-info-text',
                            'ytd-masthead', '#masthead-container',
                            'ytd-live-chat-frame', '.ytp-chat-widget'
                        ];
                        for (const sel of hideSingleSelectors) {
                            try {
                                const el = document.querySelector(sel);
                                if (el && el.style.display !== 'none') {
                                    el.style.setProperty('display', 'none', 'important');
                                }
                            } catch { /* no-op */ }
                        }
                        // Multi-instance: YouTube renders several engagement panels (comments, description,
                        // right-side panel, bottom panel). Must use querySelectorAll to catch all of them.
                        try {
                            document.querySelectorAll('ytd-engagement-panel-section-list-renderer').forEach(el => {
                                if (el.style.display !== 'none') {
                                    el.style.setProperty('display', 'none', 'important');
                                }
                            });
                        } catch { /* no-op */ }

                        window.__jdbdUnmuteDone = ensurePlayerNotMutedByStartup();

                        if (isPlayerMenuPopupOpen()) {
                            return;
                        }

                        collapseInlinePlayerSidePanels();
                        normalizeInlineSidePanels();
                    };

                    installPanelToggleBridge();
                    installTimecodeBridge();
                    installReservedShortcutBlocker();
                    installYouTubeSidePanelGuard();
                    applyImmersiveVideoLayout();
                    // Debounced observer with a proper setTimeout delay.
                    // childList+subtree catches YouTube's deep initial render; attributes are NOT watched globally
                    // (global attribute watching fires thousands of times/sec on YouTube's reactive UI).
                    // Specific layout elements get their own narrow attribute observers.
                    let layoutRepairTimer = null;
                    const scheduleLayoutRepair = () => {
                        if (layoutRepairTimer !== null) { return; }
                        layoutRepairTimer = setTimeout(() => {
                            layoutRepairTimer = null;
                            applyImmersiveVideoLayout();
                        }, 1500);
                    };
                    const observer = new MutationObserver(scheduleLayoutRepair);
                    // childList+subtree (no attributes) to catch when YouTube renders its components.
                    observer.observe(document.body || document.documentElement, {
                        childList: true, subtree: true, attributes: false
                    });
                    // Narrow attribute watchers on specific layout nodes only.
                    const watchAttrs = { attributes: true, attributeFilter: ['class', 'theater', 'hidden', 'two-columns'] };
                    const flexyEl = document.querySelector('ytd-watch-flexy');
                    if (flexyEl) { observer.observe(flexyEl, watchAttrs); }
                    const colsEl = document.getElementById('columns');
                    if (colsEl) { observer.observe(colsEl, watchAttrs); }
                    const primaryEl = document.getElementById('primary');
                    if (primaryEl) { observer.observe(primaryEl, watchAttrs); }

                    // Dedicated fast-path for engagement panels (comments, right-side panel, etc.).
                    // YouTube keeps these elements in the DOM permanently and shows them by setting a
                    // CSS class or inline style — so a childList observer never fires for this.
                    // We attach a narrow per-element attribute observer (style + class) on each panel
                    // so any YouTube-side show attempt is reverted synchronously (no debounce needed).
                    const attachEngagementPanelGuard = (el) => {
                        el.style.setProperty('display', 'none', 'important');
                        const guard = new MutationObserver(() => {
                            // YouTube changed style/class on this panel — re-hide immediately.
                            el.style.setProperty('display', 'none', 'important');
                        });
                        guard.observe(el, { attributes: true, attributeFilter: ['style', 'class', 'hidden', 'visibility'] });
                    };
                    const hideAllEngagementPanels = () => {
                        try {
                            document.querySelectorAll('ytd-engagement-panel-section-list-renderer').forEach(attachEngagementPanelGuard);
                            const eng = document.getElementById('engagement');
                            if (eng) { eng.style.setProperty('display', 'none', 'important'); }
                        } catch { /* no-op */ }
                    };
                    hideAllEngagementPanels();
                    // Also watch for newly added engagement panel elements (when YouTube adds them
                    // after initial render). childList+subtree is enough here — attribute changes on
                    // existing elements are handled by the per-element guards above.
                    let engagementScanTimer = null;
                    const engagementObserver = new MutationObserver(() => {
                        if (engagementScanTimer !== null) { return; }
                        engagementScanTimer = setTimeout(() => {
                            engagementScanTimer = null;
                            hideAllEngagementPanels();
                        }, 0);
                    });
                    engagementObserver.observe(document.documentElement, {
                        childList: true, subtree: true, attributes: false
                    });
                })();
                """;
        }

        private static string BuildEarlyKioskBootstrapScript(string videoId)
        {
            var safeVideoId = SanitizeVideoId(videoId);
            if (string.IsNullOrEmpty(safeVideoId))
            {
                return string.Empty;
            }
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
                            ytd-watch-metadata, ytd-watch-info-text,
                            #description, #description-inline-expander, #description-inner,
                            ytd-masthead, #masthead-container, #header,
                            #columns > :not(#primary) {
                                display: none !important;
                                visibility: hidden !important;
                                pointer-events: none !important;
                            }

                            .ytp-chrome-top,
                            .ytp-gradient-top,
                            .ytp-title,
                            .ytp-title-link,
                            .ytp-title-channel,
                            .ytp-title-text,
                            .ytp-title-expanded-overlay,
                            .ytp-title-expanded-heading,
                            .ytp-title-expanded-content,
                            .ytp-unmute,
                            .ytp-bezel-text-wrapper,
                            .ytp-endscreen-content,
                            .ytp-ce-element,
                            .ytp-pause-overlay {
                                display: none !important;
                                visibility: hidden !important;
                                pointer-events: none !important;
                                opacity: 0 !important;
                            }

                        `;
                        (document.head || document.documentElement).appendChild(style);
                    };

                    ensureEarlyImmersiveStyle();
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

            // Notify Watch Together peers about this playback action.
            if (action is "togglePlayPause" or "seekBackward" or "seekForward")
            {
                var pos = await GetCurrentPlaybackPositionAsync();
                if (action == "togglePlayPause")
                {
                    // Determine actual state after toggle by checking pause status.
                    var stateResult = await ExecuteWebScriptWithTimeoutAsync(
                        "(() => { const v = document.querySelector('video'); return v ? (v.paused ? 'paused' : 'playing') : 'unknown'; })()",
                        timeoutMs: 400,
                        operation: "WtSync.CheckPauseState");
                    var wtType = stateResult == "\"paused\"" || stateResult == "paused" ? "pause" : "play";
                    NotifyWtPlaybackAction(wtType, pos >= 0 ? pos : null);
                }
                else
                {
                    NotifyWtPlaybackAction(action, pos >= 0 ? pos : null);
                }
            }
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

                var isTwitchHost = HostMatches(host, "twitch.tv") ||
                                   HostMatches(host, "ttvnw.net") ||
                                   HostMatches(host, "twitchsvc.net") ||
                                   HostMatches(host, "jtvnw.net") ||
                                   HostMatches(host, "twitchapps.com") ||
                                   HostMatches(host, "passport.twitch.tv") ||
                                   HostMatches(host, "id.twitch.tv") ||
                                   HostMatches(host, "ext-twitch.tv") ||
                                   HostMatches(host, "hcaptcha.com") ||
                                   HostMatches(host, "recaptcha.net") ||
                                   HostMatches(host, "amazon.com") ||
                                   HostMatches(host, "amazonaws.com") ||
                                   HostMatches(host, "cloudfront.net") ||
                                   HostMatches(host, "gstatic.com") ||
                                   HostMatches(host, "google.com") ||
                                   HostMatches(host, "apple.com") ||
                                   HostMatches(host, "icloud.com");
                return isTwitchHost;
            }

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
                return !string.IsNullOrEmpty(videoId); // Allow any YouTube video; XP gating is done in playback layer
            }

            // Allow embed player and YouTube internal API/resource paths.
            if (uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase) ||
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
