using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        private const double CommunityChatDockWidth = 356;
        private const double CommunityCommentsDockHeight = 258;

        private void EffectsWorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, e.OriginalSource))
            {
                return;
            }

            UpdatePresetWorkspaceSwitcherState();

            if (EffectsWorkspaceTimecodesTab?.IsSelected == true)
            {
                RefreshTimecodesPanelList();
            }
        }

        private async void ToggleChatDockButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            await ToggleCommunityDockAsync(PlayerCommunityDockKind.Chat);
        }

        private async void ToggleCommentsDockButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            await ToggleCommunityDockAsync(PlayerCommunityDockKind.Comments);
        }

        private async void CommunityChatReloadButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            await OpenCommunityDockAsync(PlayerCommunityDockKind.Chat, forceReload: true);
        }

        private void CommunityChatCloseButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            SetCommunityDockOpen(PlayerCommunityDockKind.Chat, false);
            ApplyCommunityDockLayout();
            UpdateCommunityUiState();
            StopCommunityBrowser(PlayerCommunityDockKind.Chat);
        }

        private async void CommunityCommentsReloadButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            await OpenCommunityDockAsync(PlayerCommunityDockKind.Comments, forceReload: true);
        }

        private void CommunityCommentsCloseButton_Click(object sender, RoutedEventArgs e)
        {
            MarkUserInteraction();
            SetCommunityDockOpen(PlayerCommunityDockKind.Comments, false);
            ApplyCommunityDockLayout();
            UpdateCommunityUiState();
            StopCommunityBrowser(PlayerCommunityDockKind.Comments);
        }

        // Legacy hidden-tab handlers kept so old XAML references do not break.
        private async void CommunityShowCommentsButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenCommunityDockAsync(PlayerCommunityDockKind.Comments, forceReload: false);
        }

        private async void CommunityShowChatButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenCommunityDockAsync(PlayerCommunityDockKind.Chat, forceReload: false);
        }

        private async void CommunityReloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCommunityCommentsOpen)
            {
                await OpenCommunityDockAsync(PlayerCommunityDockKind.Comments, forceReload: true);
                return;
            }

            if (_isCommunityChatOpen)
            {
                await OpenCommunityDockAsync(PlayerCommunityDockKind.Chat, forceReload: true);
            }
        }

        private async Task ToggleCommunityDockAsync(PlayerCommunityDockKind kind)
        {
            if (_video.IsTwitchStream)
            {
                return;
            }

            if (IsCommunityDockOpen(kind))
            {
                SetCommunityDockOpen(kind, false);
                ApplyCommunityDockLayout();
                UpdateCommunityUiState();
                StopCommunityBrowser(kind);
                return;
            }

            await OpenCommunityDockAsync(kind, forceReload: false);
        }

        private async Task OpenCommunityDockAsync(PlayerCommunityDockKind kind, bool forceReload)
        {
            if (_video.IsTwitchStream || _isPlayerClosing)
            {
                return;
            }

            SetCommunityDockOpen(kind, true);
            ApplyCommunityDockLayout();
            UpdateCommunityUiState();

            var browser = await EnsureCommunityBrowserAsync(kind);
            if (browser?.CoreWebView2 is null)
            {
                return;
            }

            var watchUrl = BuildCommunityWatchUrl();
            var currentSource = browser.CoreWebView2.Source;
            var needsNavigation = forceReload || !CommunityBrowserSourceIsCurrentVideo(currentSource);

            if (needsNavigation)
            {
                ResetCommunityState(kind);
                SetCommunityBrowserOverlay(
                    kind,
                    visible: true,
                    header: kind == PlayerCommunityDockKind.Chat
                        ? PT("Загрузка чата", "Loading chat")
                        : PT("Загрузка комментариев", "Loading comments"),
                    text: kind == PlayerCommunityDockKind.Chat
                        ? PT("Подготавливаю live chat или chat replay...", "Preparing live chat or chat replay...")
                        : PT("Подготавливаю ленту комментариев этого видео...", "Preparing this video's comments feed..."));
                browser.Visibility = Visibility.Collapsed;
                browser.CoreWebView2.Navigate(watchUrl);
                return;
            }

            browser.Visibility = Visibility.Visible;
            SetCommunityBrowserOverlay(kind, visible: false);
            UpdateCommunityUiState();
        }

        private async Task<WebView2?> EnsureCommunityBrowserAsync(PlayerCommunityDockKind kind)
        {
            var browser = GetCommunityBrowser(kind);
            if (_video.IsTwitchStream || browser is null)
            {
                return null;
            }

            if (IsCommunityBrowserReady(kind))
            {
                return browser;
            }

            SetCommunityBrowserOverlay(
                kind,
                visible: true,
                header: kind == PlayerCommunityDockKind.Chat
                    ? PT("Подготовка чата", "Preparing chat")
                    : PT("Подготовка комментариев", "Preparing comments"),
                text: kind == PlayerCommunityDockKind.Chat
                    ? PT("Инициализирую левую панель чата...", "Initializing the left chat dock...")
                    : PT("Инициализирую нижнюю панель комментариев...", "Initializing the bottom comments dock..."));

            try
            {
                if (_webViewEnvironment is null)
                {
                    var userDataFolder = await Task.Run(ResolveWebViewUserDataFolder);
                    _webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                        browserExecutableFolder: null,
                        userDataFolder: userDataFolder);
                }

                await browser.EnsureCoreWebView2Async(_webViewEnvironment);
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
                browser.CoreWebView2.Settings.UserAgent = DesktopChromeUserAgent;
                browser.ZoomFactor = 1.0;
                await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    BuildCommunityWorkspaceScript(kind));
                browser.CoreWebView2.NewWindowRequested += CommunityBrowser_CoreWebView2_NewWindowRequested;
                browser.CoreWebView2.NavigationStarting += CommunityBrowser_CoreWebView2_NavigationStarting;
                browser.CoreWebView2.NavigationCompleted += CommunityBrowser_CoreWebView2_NavigationCompleted;
                browser.CoreWebView2.WebMessageReceived += CommunityBrowser_CoreWebView2_WebMessageReceived;
                browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                browser.CoreWebView2.WebResourceRequested += CommunityBrowser_CoreWebView2_WebResourceRequested;
                SetCommunityBrowserReady(kind, true);
                return browser;
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException($"EnsureCommunityBrowserAsync.{kind}", ex);
                SetCommunityBrowserOverlay(
                    kind,
                    visible: true,
                    header: PT("Панель недоступна", "Panel unavailable"),
                    text: ex.Message);
                return null;
            }
        }

        private void CommunityBrowser_CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void CommunityBrowser_CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var kind = TryResolveCommunityDockKind(sender);
            if (kind is null)
            {
                return;
            }

            if (string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!IsAllowedPlayerNavigation(e.Uri))
            {
                e.Cancel = true;
                return;
            }

            // Block navigation to a different video — community browser must stay on the current video.
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var navUri) &&
                navUri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                var q = System.Web.HttpUtility.ParseQueryString(navUri.Query);
                if (!string.Equals(q.Get("v"), _video.VideoId, StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    return;
                }
            }

            ResetCommunityState(kind.Value);
            var browser = GetCommunityBrowser(kind.Value);
            if (browser is not null)
            {
                browser.Visibility = Visibility.Collapsed;
            }

            SetCommunityBrowserOverlay(
                kind.Value,
                visible: true,
                header: kind == PlayerCommunityDockKind.Chat
                    ? PT("Загрузка чата", "Loading chat")
                    : PT("Загрузка комментариев", "Loading comments"),
                text: kind == PlayerCommunityDockKind.Chat
                    ? PT("Подготавливаю live chat или chat replay...", "Preparing live chat or chat replay...")
                    : PT("Подготавливаю комментарии к этому видео...", "Preparing comments for this video..."));
        }

        private async void CommunityBrowser_CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_isPlayerClosing)
            {
                return;
            }

            var kind = TryResolveCommunityDockKind(sender);
            if (kind is null)
            {
                return;
            }

            var browser = GetCommunityBrowser(kind.Value);
            if (browser?.CoreWebView2 is null)
            {
                return;
            }

            if (string.Equals(browser.CoreWebView2.Source, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!e.IsSuccess)
            {
                SetCommunityBrowserOverlay(
                    kind.Value,
                    visible: true,
                    header: PT("Не удалось открыть панель", "Failed to open panel"),
                    text: kind == PlayerCommunityDockKind.Chat
                        ? PT("YouTube не дал загрузить чат для этого видео.", "YouTube did not allow loading chat for this video.")
                        : PT("YouTube не дал загрузить комментарии для этого видео.", "YouTube did not allow loading comments for this video."));
                return;
            }

            try
            {
                await ExecuteCommunityBrowserScriptWithTimeoutAsync(
                    browser,
                    BuildCommunityWorkspaceScript(kind.Value),
                    timeoutMs: 4500,
                    operation: $"CommunityBrowser.Install.{kind.Value}");
                browser.Visibility = Visibility.Visible;
                SetCommunityBrowserOverlay(kind.Value, visible: false);
                UpdateCommunityUiState();
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException($"CommunityBrowser_NavigationCompleted.{kind}", ex);
                SetCommunityBrowserOverlay(
                    kind.Value,
                    visible: true,
                    header: PT("Панель недоступна", "Panel unavailable"),
                    text: ex.Message);
            }
        }

        private void CommunityBrowser_CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                if (!ShouldBlockCommunityMediaRequest(e.Request.Uri))
                {
                    return;
                }

                if (sender is not CoreWebView2 coreWebView)
                {
                    return;
                }

                e.Response = coreWebView.Environment.CreateWebResourceResponse(
                    new MemoryStream(Array.Empty<byte>()),
                    204,
                    "No Content",
                    "Content-Type: text/plain");
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogException("CommunityBrowser_CoreWebView2_WebResourceRequested", ex);
            }
        }

        private void CommunityBrowser_CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var kind = TryResolveCommunityDockKind(sender);
            if (kind is null)
            {
                return;
            }

            var message = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(message) ||
                !message.StartsWith("jdbd:community-dock-state:", StringComparison.Ordinal))
            {
                return;
            }

            var parts = message.Split(':');
            if (parts.Length < 5)
            {
                return;
            }

            var stateKind = string.Equals(parts[3], "chat", StringComparison.OrdinalIgnoreCase)
                ? PlayerCommunityDockKind.Chat
                : PlayerCommunityDockKind.Comments;
            if (stateKind != kind.Value)
            {
                return;
            }

            var hasContent = string.Equals(parts[4], "1", StringComparison.Ordinal);
            if (kind == PlayerCommunityDockKind.Chat)
            {
                _isCommunityChatStateInitialized = true;
                _isCommunityChatAvailable = hasContent;
            }
            else
            {
                _isCommunityCommentsStateInitialized = true;
                _isCommunityCommentsAvailable = hasContent;
            }

            UpdateCommunityUiState();
        }

        private async Task<string?> ExecuteCommunityBrowserScriptWithTimeoutAsync(
            WebView2 browser,
            string script,
            int timeoutMs,
            string operation)
        {
            if (_isPlayerClosing || browser.CoreWebView2 is null)
            {
                return null;
            }

            try
            {
                var scriptTask = browser.CoreWebView2.ExecuteScriptAsync(script);
                var completedTask = await Task.WhenAny(scriptTask, Task.Delay(timeoutMs));
                if (_isPlayerClosing || completedTask != scriptTask)
                {
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

        private void OnSeekDetected()
        {
            if ((DateTime.UtcNow - _lastCommunitySeekReloadUtc).TotalSeconds < 4)
            {
                return;
            }

            _lastCommunitySeekReloadUtc = DateTime.UtcNow;

            if (_isCommunityChatOpen)
            {
                _ = OpenCommunityDockAsync(PlayerCommunityDockKind.Chat, forceReload: true);
            }

            if (_isCommunityCommentsOpen)
            {
                _ = OpenCommunityDockAsync(PlayerCommunityDockKind.Comments, forceReload: true);
            }
        }

        private void ApplyCommunityDockLayout(bool animate = false)
        {
            var canShowCommunity = !_video.IsTwitchStream && !_isPlayerElementFullScreen;
            ApplyCommunityChatDockLayout(canShowCommunity && _isCommunityChatOpen, animate);
            ApplyCommunityCommentsDockLayout(canShowCommunity && _isCommunityCommentsOpen, animate);
            UpdateCommunityUiState();
        }

        private void ApplyCommunityChatDockLayout(bool open, bool animate)
        {
            if (CommunityChatDock is null || CommunityChatColumn is null)
            {
                return;
            }

            CommunityChatDock.BeginAnimation(UIElement.OpacityProperty, null);

            if (open)
            {
                CommunityChatDock.Visibility = Visibility.Visible;
                CommunityChatDock.IsHitTestVisible = true;
                CommunityChatColumn.Width = new GridLength(CommunityChatDockWidth);
                if (animate && _appSettings.AnimationsEnabled)
                {
                    CommunityChatDock.Opacity = 0;
                    CommunityChatDock.BeginAnimation(
                        UIElement.OpacityProperty,
                        new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                }
                else
                {
                    CommunityChatDock.Opacity = 1;
                }

                return;
            }

            CommunityChatDock.IsHitTestVisible = false;
            CommunityChatColumn.Width = new GridLength(0);
            if (animate && _appSettings.AnimationsEnabled && CommunityChatDock.Visibility == Visibility.Visible)
            {
                var fade = new DoubleAnimation(CommunityChatDock.Opacity, 0, TimeSpan.FromMilliseconds(160));
                fade.Completed += (_, _) => CommunityChatDock.Visibility = Visibility.Collapsed;
                CommunityChatDock.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                CommunityChatDock.Visibility = Visibility.Collapsed;
                CommunityChatDock.Opacity = 0;
            }
        }

        private void ApplyCommunityCommentsDockLayout(bool open, bool animate)
        {
            if (CommunityCommentsDock is null || CommunityCommentsRow is null)
            {
                return;
            }

            CommunityCommentsDock.BeginAnimation(UIElement.OpacityProperty, null);

            if (open)
            {
                CommunityCommentsDock.Visibility = Visibility.Visible;
                CommunityCommentsDock.IsHitTestVisible = true;
                CommunityCommentsRow.Height = new GridLength(CommunityCommentsDockHeight);
                if (animate && _appSettings.AnimationsEnabled)
                {
                    CommunityCommentsDock.Opacity = 0;
                    CommunityCommentsDock.BeginAnimation(
                        UIElement.OpacityProperty,
                        new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                }
                else
                {
                    CommunityCommentsDock.Opacity = 1;
                }

                return;
            }

            CommunityCommentsDock.IsHitTestVisible = false;
            CommunityCommentsRow.Height = new GridLength(0);
            if (animate && _appSettings.AnimationsEnabled && CommunityCommentsDock.Visibility == Visibility.Visible)
            {
                var fade = new DoubleAnimation(CommunityCommentsDock.Opacity, 0, TimeSpan.FromMilliseconds(160));
                fade.Completed += (_, _) => CommunityCommentsDock.Visibility = Visibility.Collapsed;
                CommunityCommentsDock.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                CommunityCommentsDock.Visibility = Visibility.Collapsed;
                CommunityCommentsDock.Opacity = 0;
            }
        }

        private void UpdateCommunityUiState()
        {
            if (ToggleChatDockButton is not null)
            {
                ToggleChatDockButton.Visibility = _video.IsTwitchStream ? Visibility.Collapsed : Visibility.Visible;
                ToggleChatDockButton.Content = _isCommunityChatOpen
                    ? PT("Закрыть чат", "Close chat")
                    : PT("Чат слева", "Left chat");
            }

            if (ToggleCommentsDockButton is not null)
            {
                ToggleCommentsDockButton.Visibility = _video.IsTwitchStream ? Visibility.Collapsed : Visibility.Visible;
                ToggleCommentsDockButton.Content = _isCommunityCommentsOpen
                    ? PT("Закрыть комментарии", "Close comments")
                    : PT("Комментарии снизу", "Bottom comments");
            }

            if (CommunityChatStatusText is not null)
            {
                CommunityChatStatusText.Text = BuildCommunityStatusText(PlayerCommunityDockKind.Chat);
            }

            if (CommunityCommentsStatusText is not null)
            {
                CommunityCommentsStatusText.Text = BuildCommunityStatusText(PlayerCommunityDockKind.Comments);
            }

            if (CommunityStatusText is not null)
            {
                CommunityStatusText.Text = PT(
                    "Чат теперь открывается слева, а комментарии — снизу окна плеера.",
                    "Chat now opens on the left and comments open at the bottom of the player window.");
            }
        }

        private string BuildCommunityStatusText(PlayerCommunityDockKind kind)
        {
            var isOpen = IsCommunityDockOpen(kind);
            var isReady = kind == PlayerCommunityDockKind.Chat
                ? _isCommunityChatStateInitialized
                : _isCommunityCommentsStateInitialized;
            var isAvailable = kind == PlayerCommunityDockKind.Chat
                ? _isCommunityChatAvailable
                : _isCommunityCommentsAvailable;

            if (!isOpen)
            {
                return kind == PlayerCommunityDockKind.Chat
                    ? PT("Открой чат, и он появится в левой панели приложения.", "Open chat to dock it on the left side of the app.")
                    : PT("Открой комментарии, и они появятся снизу окна плеера.", "Open comments to dock them at the bottom of the player window.");
            }

            if (!isReady)
            {
                return kind == PlayerCommunityDockKind.Chat
                    ? PT("Загружаю live chat или chat replay в левую панель...", "Loading live chat or chat replay into the left dock...")
                    : PT("Загружаю комментарии в нижнюю панель...", "Loading comments into the bottom dock...");
            }

            if (!isAvailable)
            {
                return kind == PlayerCommunityDockKind.Chat
                    ? PT("Для этого видео YouTube не отдал live chat или chat replay.", "YouTube did not expose live chat or chat replay for this video.")
                    : PT("Для этого видео YouTube не отдал комментарии.", "YouTube did not expose comments for this video.");
            }

            return kind == PlayerCommunityDockKind.Chat
                ? PT("Чат закреплён слева и больше не вылезает поверх видео.", "Chat is docked on the left and no longer spills over the video.")
                : PT("Комментарии закреплены снизу и не перекрывают область плеера.", "Comments are docked at the bottom and do not cover the video area.");
        }

        private void ApplyCommunityLocalization()
        {
            if (EffectsWorkspaceCommunityTab is not null)
            {
                EffectsWorkspaceCommunityTab.Visibility = Visibility.Collapsed;
            }

            if (CommunityPanelHeaderText is not null)
            {
                CommunityPanelHeaderText.Text = PT("Комментарии и чат", "Comments and chat");
            }

            if (CommunityPanelHintText is not null)
            {
                CommunityPanelHintText.Text = PT(
                    "Эта вкладка скрыта: чат и комментарии теперь открываются прямо вокруг плеера.",
                    "This tab is hidden: chat and comments now open directly around the player.");
            }

            if (CommunityShowCommentsButton is not null)
            {
                CommunityShowCommentsButton.Content = PT("Открыть снизу", "Open below");
            }

            if (CommunityShowChatButton is not null)
            {
                CommunityShowChatButton.Content = PT("Открыть слева", "Open left");
            }

            if (CommunityReloadButton is not null)
            {
                CommunityReloadButton.Content = PT("Обновить", "Reload");
            }

            if (CommunityChatHeaderText is not null)
            {
                CommunityChatHeaderText.Text = PT("Чат стрима", "Stream chat");
            }

            if (CommunityChatReloadButton is not null)
            {
                CommunityChatReloadButton.Content = PT("Обновить", "Reload");
            }

            if (CommunityChatCloseButton is not null)
            {
                CommunityChatCloseButton.Content = PT("Закрыть", "Close");
            }

            if (CommunityChatOverlayHeaderText is not null)
            {
                CommunityChatOverlayHeaderText.Text = PT("Подготовка чата", "Preparing chat");
            }

            if (CommunityChatOverlayText is not null)
            {
                CommunityChatOverlayText.Text = PT(
                    "Загружаю live chat или chat replay для этого видео...",
                    "Loading live chat or chat replay for this video...");
            }

            if (CommunityCommentsHeaderText is not null)
            {
                CommunityCommentsHeaderText.Text = PT("Комментарии", "Comments");
            }

            if (CommunityCommentsReloadButton is not null)
            {
                CommunityCommentsReloadButton.Content = PT("Обновить", "Reload");
            }

            if (CommunityCommentsCloseButton is not null)
            {
                CommunityCommentsCloseButton.Content = PT("Закрыть", "Close");
            }

            if (CommunityCommentsOverlayHeaderText is not null)
            {
                CommunityCommentsOverlayHeaderText.Text = PT("Подготовка комментариев", "Preparing comments");
            }

            if (CommunityCommentsOverlayText is not null)
            {
                CommunityCommentsOverlayText.Text = PT(
                    "Загружаю комментарии к этому видео...",
                    "Loading comments for this video...");
            }

            UpdateCommunityUiState();
        }

        private void ApplyCommunityFullscreenState(bool isFullscreen)
        {
            if (_video.IsTwitchStream)
            {
                return;
            }

            if (isFullscreen)
            {
                _isCommunityChatOpenBeforePlayerFullscreen = _isCommunityChatOpen;
                _isCommunityCommentsOpenBeforePlayerFullscreen = _isCommunityCommentsOpen;
                _isCommunityChatOpen = false;
                _isCommunityCommentsOpen = false;
                ApplyCommunityDockLayout();
                return;
            }

            _isCommunityChatOpen = _isCommunityChatOpenBeforePlayerFullscreen;
            _isCommunityCommentsOpen = _isCommunityCommentsOpenBeforePlayerFullscreen;
            ApplyCommunityDockLayout();
        }

        private void StopCommunityBrowser(PlayerCommunityDockKind kind)
        {
            var browser = GetCommunityBrowser(kind);
            if (browser?.CoreWebView2 is null)
            {
                return;
            }

            ResetCommunityState(kind);
            try
            {
                browser.CoreWebView2.Stop();
                browser.CoreWebView2.Navigate("about:blank");
            }
            catch
            {
                // Ignore stop errors.
            }
        }

        private void TeardownCommunityBrowsers()
        {
            TeardownCommunityBrowser(PlayerCommunityDockKind.Chat);
            TeardownCommunityBrowser(PlayerCommunityDockKind.Comments);
        }

        private void TeardownCommunityBrowser(PlayerCommunityDockKind kind)
        {
            var browser = GetCommunityBrowser(kind);
            if (browser?.CoreWebView2 is null)
            {
                return;
            }

            try
            {
                browser.CoreWebView2.NewWindowRequested -= CommunityBrowser_CoreWebView2_NewWindowRequested;
                browser.CoreWebView2.NavigationStarting -= CommunityBrowser_CoreWebView2_NavigationStarting;
                browser.CoreWebView2.NavigationCompleted -= CommunityBrowser_CoreWebView2_NavigationCompleted;
                browser.CoreWebView2.WebMessageReceived -= CommunityBrowser_CoreWebView2_WebMessageReceived;
                browser.CoreWebView2.WebResourceRequested -= CommunityBrowser_CoreWebView2_WebResourceRequested;
                browser.CoreWebView2.Stop();
                browser.CoreWebView2.Navigate("about:blank");
            }
            catch
            {
                // Ignore teardown errors.
            }
        }

        private static string BuildCommunityWorkspaceScript(PlayerCommunityDockKind kind)
        {
            var mode = kind == PlayerCommunityDockKind.Chat ? "chat" : "comments";
            var contentSelector = kind == PlayerCommunityDockKind.Chat
                ? "#chat, #chat-container, ytd-live-chat-frame, .ytp-chat-widget"
                : "#comments, ytd-comments";
            var pointerSelector = kind == PlayerCommunityDockKind.Chat
                ? "#chat, #chat *, #chat-container, #chat-container *, ytd-live-chat-frame, ytd-live-chat-frame *, .ytp-chat-widget, .ytp-chat-widget *"
                : "#comments, #comments *, ytd-comments, ytd-comments *, ytd-comments-header-renderer, ytd-comments-header-renderer *";
            var hideSelector = kind == PlayerCommunityDockKind.Chat
                ? "#comments, ytd-comments"
                : "#chat, #chat-container, ytd-live-chat-frame, .ytp-chat-widget";
            var mediaKillSelector = "#movie_player, .html5-video-player, .html5-video-container, ytd-player, video, audio, #cinematics, ytd-reel-video-renderer";

            return $$"""
                (() => {
                    // Silence all media at prototype level before any YouTube JS runs.
                    // This intercepts volume/muted setters and play() so the background video is always silent.
                    try {
                        const _vd = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'volume');
                        if (_vd?.set) {
                            Object.defineProperty(HTMLMediaElement.prototype, 'volume', {
                                configurable: true, get: _vd.get,
                                set(v) { try { _vd.set.call(this, 0); } catch {} }
                            });
                        }
                        const _md = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'muted');
                        if (_md?.set) {
                            Object.defineProperty(HTMLMediaElement.prototype, 'muted', {
                                configurable: true, get: _md.get,
                                set(v) { try { _md.set.call(this, true); } catch {} }
                            });
                        }
                        const _op = HTMLMediaElement.prototype.play;
                        if (typeof _op === 'function') {
                            HTMLMediaElement.prototype.play = function() {
                                try { this.muted = true; this.volume = 0; } catch {}
                                return _op.apply(this, arguments);
                            };
                        }
                    } catch {}

                    const mode = '{{mode}}';
                    const statePrefix = `jdbd:community-dock-state:${mode}:`;
                    const contentSelector = `{{contentSelector}}`;
                    const pointerSelector = `{{pointerSelector}}`;
                    const hideSelector = `{{hideSelector}}`;
                    const mediaKillSelector = `{{mediaKillSelector}}`;

                    const ensureStyle = () => {
                        if (document.getElementById('jdbd-community-dock-style-' + mode)) {
                            return;
                        }

                        const style = document.createElement('style');
                        style.id = 'jdbd-community-dock-style-' + mode;
                        style.textContent = `
                            html, body, ytd-app, #content, #page-manager, ytd-watch-flexy, #columns, #primary, #primary-inner {
                                height: 100% !important;
                                width: 100% !important;
                                margin: 0 !important;
                                padding: 0 !important;
                                overflow: hidden !important;
                                background: #101822 !important;
                            }

                            body {
                                margin: 0 !important;
                            }

                            body * {
                                pointer-events: none !important;
                            }

                            ${pointerSelector} {
                                pointer-events: auto !important;
                            }

                            ytd-masthead, #masthead-container, #guide, ytd-mini-guide-renderer,
                            #player, #player-container, #player-theater-container, #full-bleed-container,
                            #secondary, #secondary-inner, #related, ytd-watch-metadata,
                            #meta, #description, #description-inline-expander, #description-inner,
                            #top-row, #bottom-row, #above-the-fold, ytd-watch-next-feed-renderer,
                            ytd-watch-next-secondary-results-renderer {
                                display: none !important;
                                visibility: hidden !important;
                            }

                            ${hideSelector} {
                                display: none !important;
                                visibility: hidden !important;
                            }

                            ${mediaKillSelector} {
                                display: none !important;
                                visibility: hidden !important;
                                opacity: 0 !important;
                                pointer-events: none !important;
                            }

                            ${contentSelector} {
                                display: block !important;
                                position: fixed !important;
                                inset: 0 !important;
                                width: 100vw !important;
                                height: 100vh !important;
                                min-height: 100vh !important;
                                margin: 0 !important;
                                padding: 0 !important;
                                max-width: none !important;
                                background: #101822 !important;
                                overflow: auto !important;
                                z-index: 9999 !important;
                            }

                            ${mode === 'chat' ? `
                                ytd-live-chat-frame iframe,
                                #chatframe {
                                    display: block !important;
                                    width: 100% !important;
                                    height: 100% !important;
                                    min-height: 100vh !important;
                                    border: 0 !important;
                                }
                            ` : ''}
                        `;
                        (document.head || document.documentElement).appendChild(style);
                    };

                    const hideElement = (el) => {
                        if (!(el instanceof HTMLElement)) {
                            return;
                        }

                        el.style.setProperty('display', 'none', 'important');
                        el.style.setProperty('visibility', 'hidden', 'important');
                        el.style.setProperty('pointer-events', 'none', 'important');
                    };

                    const showElement = (el) => {
                        if (!(el instanceof HTMLElement)) {
                            return;
                        }

                        el.style.removeProperty('display');
                        el.style.removeProperty('visibility');
                        el.style.setProperty('pointer-events', 'auto', 'important');
                    };

                    const killEmbeddedMedia = () => {
                        try {
                            document.querySelectorAll(mediaKillSelector).forEach(hideElement);
                        } catch {
                            // no-op
                        }

                        try {
                            document.querySelectorAll('video, audio').forEach((media) => {
                                if (!(media instanceof HTMLMediaElement)) {
                                    return;
                                }

                                // Pause only after the deferred window has elapsed — the video must play
                                // briefly so YouTube can send onStateChange events to the chat replay iframe.
                                if (window['__jdbdPauseReady_' + mode]) {
                                    try { media.pause(); } catch { /* no-op */ }
                                }
                                try { media.autoplay = false; } catch { /* no-op */ }
                                try { media.controls = false; } catch { /* no-op */ }
                                try { media.loop = false; } catch { /* no-op */ }
                                try { media.muted = true; } catch { /* no-op */ }
                                try { media.volume = 0; } catch { /* no-op */ }
                                try { media.removeAttribute('autoplay'); } catch { /* no-op */ }
                                try { media.setAttribute('muted', 'muted'); } catch { /* no-op */ }
                                try { media.style.setProperty('display', 'none', 'important'); } catch { /* no-op */ }
                                try { media.style.setProperty('visibility', 'hidden', 'important'); } catch { /* no-op */ }
                                try { media.style.setProperty('pointer-events', 'none', 'important'); } catch { /* no-op */ }
                            });
                        } catch {
                            // no-op
                        }
                    };

                    const reportState = () => {
                        try {
                            const hasContent = !!document.querySelector(contentSelector);
                            window.chrome?.webview?.postMessage(`${statePrefix}${hasContent ? '1' : '0'}`);
                        } catch {
                            // no-op
                        }
                    };

                    const blockNavigation = (event) => {
                        const target = event.target instanceof Element
                            ? event.target.closest('a[href], ytd-thumbnail, [data-sessionlink]')
                            : null;
                        if (!target) {
                            return;
                        }

                        event.preventDefault();
                        event.stopImmediatePropagation();
                        event.stopPropagation();
                    };

                    const repair = () => {
                        ensureStyle();
                        killEmbeddedMedia();

                        document.querySelectorAll([
                            'ytd-masthead',
                            '#masthead-container',
                            '#guide',
                            '#player',
                            '#player-container',
                            '#player-theater-container',
                            '#full-bleed-container',
                            '#secondary',
                            '#secondary-inner',
                            '#related',
                            'ytd-watch-metadata',
                            '#meta',
                            '#description',
                            '#description-inline-expander',
                            '#description-inner'
                        ].join(',')).forEach(hideElement);

                        document.querySelectorAll(hideSelector).forEach(hideElement);
                        document.querySelectorAll(contentSelector).forEach(showElement);

                        if (mode === 'comments') {
                            document.getElementById('comments')?.scrollIntoView?.({ block: 'start' });
                        }

                        reportState();
                    };

                    const rootFlag = '__jdbdCommunityDockInstalled_' + mode;
                    if (!window[rootFlag]) {
                        window[rootFlag] = true;
                        document.addEventListener('click', blockNavigation, { capture: true });
                        document.addEventListener('mousedown', blockNavigation, { capture: true });
                        document.addEventListener('pointerdown', blockNavigation, { capture: true });

                        // Let the video play silently for 4 s so YouTube fires onStateChange events
                        // that the chat replay iframe needs to initialise. After that, pause to stop
                        // consuming bandwidth. Audio is already muted by killEmbeddedMedia above.
                        setTimeout(() => {
                            window['__jdbdPauseReady_' + mode] = true;
                            try {
                                document.querySelectorAll('video, audio').forEach((m) => {
                                    if (m instanceof HTMLMediaElement) {
                                        try { m.pause(); } catch { /* no-op */ }
                                    }
                                });
                            } catch {
                                // no-op
                            }
                        }, 4000);

                        let repairTimer = null;
                        const scheduleRepair = () => {
                            if (repairTimer !== null) {
                                return;
                            }

                            repairTimer = setTimeout(() => {
                                repairTimer = null;
                                repair();
                            }, 120);
                        };

                        const observer = new MutationObserver(scheduleRepair);
                        observer.observe(document.documentElement, {
                            childList: true,
                            subtree: true,
                            attributes: true,
                            attributeFilter: ['style', 'class', 'hidden']
                        });

                        document.addEventListener('yt-page-data-updated', scheduleRepair, true);
                        document.addEventListener('yt-navigate-finish', scheduleRepair, true);
                        window.addEventListener('resize', scheduleRepair, { passive: true });
                    }

                    repair();
                    setTimeout(repair, 80);
                    setTimeout(repair, 350);
                    setTimeout(repair, 1200);
                })();
                """;
        }

        private WebView2? GetCommunityBrowser(PlayerCommunityDockKind kind)
        {
            return kind == PlayerCommunityDockKind.Chat
                ? CommunityChatBrowser
                : CommunityCommentsBrowser;
        }

        private PlayerCommunityDockKind? TryResolveCommunityDockKind(object? sender)
        {
            if (CommunityChatBrowser?.CoreWebView2 is not null &&
                ReferenceEquals(sender, CommunityChatBrowser.CoreWebView2))
            {
                return PlayerCommunityDockKind.Chat;
            }

            if (CommunityCommentsBrowser?.CoreWebView2 is not null &&
                ReferenceEquals(sender, CommunityCommentsBrowser.CoreWebView2))
            {
                return PlayerCommunityDockKind.Comments;
            }

            return null;
        }

        private void SetCommunityBrowserOverlay(
            PlayerCommunityDockKind kind,
            bool visible,
            string? header = null,
            string? text = null)
        {
            var overlay = kind == PlayerCommunityDockKind.Chat ? CommunityChatOverlay : CommunityCommentsOverlay;
            var headerText = kind == PlayerCommunityDockKind.Chat ? CommunityChatOverlayHeaderText : CommunityCommentsOverlayHeaderText;
            var bodyText = kind == PlayerCommunityDockKind.Chat ? CommunityChatOverlayText : CommunityCommentsOverlayText;

            if (overlay is not null)
            {
                overlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (!string.IsNullOrWhiteSpace(header) && headerText is not null)
            {
                headerText.Text = header;
            }

            if (!string.IsNullOrWhiteSpace(text) && bodyText is not null)
            {
                bodyText.Text = text;
            }
        }

        private void SetCommunityDockOpen(PlayerCommunityDockKind kind, bool isOpen)
        {
            if (kind == PlayerCommunityDockKind.Chat)
            {
                _isCommunityChatOpen = isOpen;
            }
            else
            {
                _isCommunityCommentsOpen = isOpen;
            }
        }

        private bool IsCommunityDockOpen(PlayerCommunityDockKind kind)
        {
            return kind == PlayerCommunityDockKind.Chat ? _isCommunityChatOpen : _isCommunityCommentsOpen;
        }

        private void ResetCommunityState(PlayerCommunityDockKind kind)
        {
            if (kind == PlayerCommunityDockKind.Chat)
            {
                _isCommunityChatStateInitialized = false;
                _isCommunityChatAvailable = true;
            }
            else
            {
                _isCommunityCommentsStateInitialized = false;
                _isCommunityCommentsAvailable = true;
            }
        }

        private bool IsCommunityBrowserReady(PlayerCommunityDockKind kind)
        {
            return kind == PlayerCommunityDockKind.Chat
                ? _isCommunityChatBrowserReady
                : _isCommunityCommentsBrowserReady;
        }

        private void SetCommunityBrowserReady(PlayerCommunityDockKind kind, bool value)
        {
            if (kind == PlayerCommunityDockKind.Chat)
            {
                _isCommunityChatBrowserReady = value;
            }
            else
            {
                _isCommunityCommentsBrowserReady = value;
            }
        }

        private static bool ShouldBlockCommunityMediaRequest(string? uriText)
        {
            if (string.IsNullOrWhiteSpace(uriText) ||
                !Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host.ToLowerInvariant();
            var path = uri.AbsolutePath.ToLowerInvariant();

            var isGoogleVideoHost = host.EndsWith(".googlevideo.com", StringComparison.Ordinal) ||
                                    host.EndsWith(".youtube.com", StringComparison.Ordinal) ||
                                    host.EndsWith(".youtube-nocookie.com", StringComparison.Ordinal);
            if (!isGoogleVideoHost)
            {
                return false;
            }

            if (path.Contains("/get_video_info", StringComparison.Ordinal) ||
                path.Contains("/api/stats/playback", StringComparison.Ordinal) ||
                path.Contains("/api/stats/qoe", StringComparison.Ordinal) ||
                path.Contains("/watchtime", StringComparison.Ordinal))
            {
                return true;
            }

            var extension = Path.GetExtension(path);
            return extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".mpd", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".m4s", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".webm", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildCommunityWatchUrl()
        {
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["v"] = _video.VideoId;
            query["autoplay"] = "1";
            if (LastPlaybackSeconds > 0)
            {
                query["t"] = $"{LastPlaybackSeconds}s";
            }
            return $"https://www.youtube.com/watch?{query}";
        }

        private bool CommunityBrowserSourceIsCurrentVideo(string? source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return string.Equals(q.Get("v"), _video.VideoId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
