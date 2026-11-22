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
    public partial class PlayerWindow : System.Windows.Controls.UserControl
    {
        private System.Windows.Rect _boundsBeforeFullscreen;

        private bool IsActive => _host.IsActive;

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
                _windowStateBeforePlayerFullscreen = _host.WindowState;
                _boundsBeforeFullscreen = _host.RestoreBounds;
                _resizeModeBeforePlayerFullscreen = _host.ResizeMode;
                _mainRootMarginBeforePlayerFullscreen = MainRootGrid.Margin;
                _playerHostMarginBeforeFullscreen = PlayerHostBorder.Margin;
                _playerHostCornerRadiusBeforeFullscreen = PlayerHostBorder.CornerRadius;
                ApplyPlayerFullscreenChrome(isFullscreen: true);
                _host.ResizeMode = ResizeMode.NoResize;
                if (_appSettings.FullscreenBehavior != "windowed")
                {
                    _host.WindowState = WindowState.Normal;
                    ApplyFullMonitorBounds();
                }
                _host.Activate();
                PulseTopmost();
                RequestApplyEffects(immediate: false);
                return;
            }

            RestoreWindowAfterPlayerFullscreen();
        }

        private void RestoreWindowAfterPlayerFullscreen()
        {
            ApplyPlayerFullscreenChrome(isFullscreen: false);
            _host.ResizeMode = _resizeModeBeforePlayerFullscreen == ResizeMode.NoResize
                ? ResizeMode.CanResize
                : _resizeModeBeforePlayerFullscreen;
            if (_host.WindowState == WindowState.Normal && !_boundsBeforeFullscreen.IsEmpty)
            {
                _host.Left = _boundsBeforeFullscreen.Left;
                _host.Top = _boundsBeforeFullscreen.Top;
                _host.Width = _boundsBeforeFullscreen.Width;
                _host.Height = _boundsBeforeFullscreen.Height;
            }
            _host.WindowState = _windowStateBeforePlayerFullscreen == WindowState.Minimized
                ? WindowState.Maximized
                : _windowStateBeforePlayerFullscreen;
            // Re-apply panel layout after window state transition settles to ensure column widths stick.
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isPlayerElementFullScreen && !_isPlayerClosing)
                {
                    ApplyEffectsPanelLayout();
                }
            }, DispatcherPriority.Loaded);
            RequestApplyEffects(immediate: false);
        }

        private void ApplyPlayerFullscreenChrome(bool isFullscreen)
        {
            if (TopBarPanel is not null)
            {
                TopBarPanel.Visibility = isFullscreen ? Visibility.Collapsed : Visibility.Visible;
            }

            if (MainRootGrid is not null)
            {
                MainRootGrid.Margin = isFullscreen ? new Thickness(0) : _mainRootMarginBeforePlayerFullscreen;
            }

            if (PlayerHostBorder is not null)
            {
                PlayerHostBorder.Margin = isFullscreen ? new Thickness(0) : _playerHostMarginBeforeFullscreen;
                PlayerHostBorder.CornerRadius = isFullscreen ? new CornerRadius(0) : _playerHostCornerRadiusBeforeFullscreen;
            }

            // WindowChrome keeps a ResizeBorderThickness hit-test strip around the window edges even
            // when _host.ResizeMode = NoResize. In fullscreen that 12 px strip sits on top of the WebView2
            // and swallows clicks intended for YouTube's bottom/right player controls. Zero it out.
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(_host);
            if (chrome is not null)
            {
                if (isFullscreen)
                {
                    _windowChromeResizeBorderBeforeFullscreen = chrome.ResizeBorderThickness;
                    chrome.ResizeBorderThickness = new Thickness(0);
                }
                else
                {
                    chrome.ResizeBorderThickness = _windowChromeResizeBorderBeforeFullscreen;
                }
            }

            if (isFullscreen)
            {
                _effectsPanelExpandedBeforePlayerFullscreen = _effectsPanelExpanded;
                _effectsPanelExpanded = false;
                ApplyCommunityFullscreenState(isFullscreen: true);
                if (EffectsPanel is not null)
                {
                    EffectsPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    EffectsPanel.Opacity = 0;
                    EffectsPanel.Visibility = Visibility.Collapsed;
                }

                if (EffectsColumn is not null)
                {
                    EffectsColumn.Width = new GridLength(0);
                }

                if (EffectsSplitterColumn is not null)
                {
                    EffectsSplitterColumn.Width = new GridLength(0);
                }

                if (ToggleEffectsPanelButton is not null)
                {
                    ToggleEffectsPanelButton.Content = PT("Показать эффекты", "Show effects");
                }

                return;
            }

            _effectsPanelExpanded = _effectsPanelExpandedBeforePlayerFullscreen;
            ApplyEffectsPanelLayout();
            ApplyCommunityFullscreenState(isFullscreen: false);
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
                await ExecuteWebScriptWithTimeoutAsync(
                    exitFullscreenScript,
                    timeoutMs: 1200,
                    operation: "ExitEmbeddedPlayerFullscreenAsync");
                await Task.Delay(60);
                if (_isPlayerClosing)
                {
                    return;
                }
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
            _host.Topmost = true;
            Dispatcher.BeginInvoke(() =>
            {
                _host.Topmost = false;
            }, DispatcherPriority.ApplicationIdle);
        }

        private void ApplyFullMonitorBounds()
        {
            if (_isApplyingFullMonitorBounds)
            {
                return;
            }

            _isApplyingFullMonitorBounds = true;
            try
            {
                var hwnd = new WindowInteropHelper(_host).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    _host.Left = 0;
                    _host.Top = 0;
                    _host.Width = SystemParameters.PrimaryScreenWidth;
                    _host.Height = SystemParameters.PrimaryScreenHeight;
                    return;
                }

                var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                if (monitor == IntPtr.Zero)
                {
                    _host.Left = 0;
                    _host.Top = 0;
                    _host.Width = SystemParameters.PrimaryScreenWidth;
                    _host.Height = SystemParameters.PrimaryScreenHeight;
                    return;
                }

                var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref monitorInfo))
                {
                    _host.Left = 0;
                    _host.Top = 0;
                    _host.Width = SystemParameters.PrimaryScreenWidth;
                    _host.Height = SystemParameters.PrimaryScreenHeight;
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
                    _host.Left = topLeftDip.X;
                    _host.Top = topLeftDip.Y;
                    _host.Width = Math.Max(1, bottomRightDip.X - topLeftDip.X);
                    _host.Height = Math.Max(1, bottomRightDip.Y - topLeftDip.Y);
                    return;
                }

                _host.Left = monitorInfo.rcMonitor.left;
                _host.Top = monitorInfo.rcMonitor.top;
                _host.Width = Math.Max(1, monitorInfo.rcMonitor.right - monitorInfo.rcMonitor.left);
                _host.Height = Math.Max(1, monitorInfo.rcMonitor.bottom - monitorInfo.rcMonitor.top);
            }
            finally
            {
                _isApplyingFullMonitorBounds = false;
            }
        }


        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            await AnimatePlayerMinimizeAsync();
            _host.WindowState = WindowState.Minimized;
        }

        private async void ToggleWindowSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlayerElementFullScreen)
            {
                await ExitEmbeddedPlayerFullscreenAsync();
                return;
            }

            if (!CanProcessPlayerCommands())
            {
                return;
            }

            _host.WindowState = _host.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void UpdateWindowSizeButtonState()
        {
            if (WindowSizeButton is null)
            {
                return;
            }

            WindowSizeButton.Content = _host.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            _host.Close();
        }

        private void TopBarPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TryBeginWindowDrag(e);
        }

        private void MainRootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TryBeginWindowDrag(e);
        }

        private void TryBeginWindowDrag(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (_isPlayerElementFullScreen)
            {
                _shouldStartDragAfterExitingPlayerFullscreen = true;
                _ = ExitPlayerFullscreenAndStartDragAsync();
                e.Handled = true;
                return;
            }

            if (e.ClickCount == 2)
            {
                if (!CanProcessPlayerCommands())
                {
                    e.Handled = true;
                    return;
                }

                _host.WindowState = _host.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

                e.Handled = true;
                return;
            }

            if (_host.WindowState == WindowState.Maximized)
            {
                BeginPendingRestoreDrag(e.GetPosition(this));
                e.Handled = true;
                return;
            }

            try
            {
                _host.DragMove();
                e.Handled = true;
            }
            catch
            {
                // Ignore drag interruptions caused by rapid pointer transitions.
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

        internal async void Close()
        {
            if (!_closingAfterCheckpoint)
            {
                if (_isPlayerClosing) return;
                _isPlayerClosing = true;
                _positionTimer.Stop();
                try { await PersistPlaybackPositionAsync(force: true); }
                catch (Exception ex)
                {
                    DiagnosticsService.LogException("Player.CloseCheckpoint", ex);
                    MessageBox.Show(PT("Не удалось сохранить прогресс: ", "Could not save progress: ") + ex.Message);
                }
                _closingAfterCheckpoint = true;
            }
            try
            {
                _isPlayerClosing = true;
                _shouldStartDragAfterExitingPlayerFullscreen = false;
                _isPlayerNavigationInProgress = false;
                _isPlayerRuntimeReady = false;
                SetPlayerInteractionsEnabled(false);
                _host.Topmost = false;
                _positionTimer.Stop();
                _effectsApplyTimer.Stop();
                _resizeSettleDebounceTimer.Stop();
                _positionTimer.Tick -= PositionTimer_Tick;
                _effectsApplyTimer.Tick -= EffectsApplyTimer_Tick;
                _resizeSettleDebounceTimer.Tick -= ResizeSettleDebounceTimer_Tick;
                StopAllSoundEffects();
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
                UnregisterGlobalHotkeys();
                _hotkeySource?.RemoveHook(HotkeyWndProc);
                _hotkeySource = null;
                Loaded -= PlayerWindow_Loaded;
                if (_isPlayerElementFullScreen)
                {
                    _isPlayerElementFullScreen = false;
                    RestoreWindowAfterPlayerFullscreen();
                }
                _host.Activated -= PlayerWindow_Activated;
                _host.Deactivated -= PlayerWindow_Deactivated;
                _host.StateChanged -= PlayerWindow_StateChanged;
                SizeChanged -= PlayerWindow_SizeChanged;
                PreviewMouseMove -= PlayerWindow_PreviewMouseMove;
                PreviewMouseLeftButtonUp -= PlayerWindow_PreviewMouseLeftButtonUp;
                PreviewMouseDown -= PlayerWindow_PreviewMouseDown;
                PreviewKeyDown -= PlayerWindow_PreviewKeyDown;
                if (Player.CoreWebView2 is not null)
                {
                    Player.CoreWebView2.ContainsFullScreenElementChanged -= CoreWebView2_ContainsFullScreenElementChanged;
                    Player.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                    Player.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    Player.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    Player.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                    Player.CoreWebView2.SourceChanged -= CoreWebView2_SourceChanged;
                    Player.CoreWebView2.WebResourceResponseReceived -= CoreWebView2_WebResourceResponseReceived;
                    Player.CoreWebView2.Stop();
                    Player.CoreWebView2.Navigate("about:blank");
                }

                TeardownCommunityBrowsers();
            }
            catch
            {
                // Ignore teardown errors.
            }
            finally
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void PlayerWindow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_pendingDragRestoreFromMaximized)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelPendingRestoreDrag();
                return;
            }

            var currentPoint = e.GetPosition(this);
            var movedEnough =
                Math.Abs(currentPoint.X - _pendingDragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPoint.Y - _pendingDragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
            if (!movedEnough)
            {
                return;
            }

            BeginRestoreFromMaximizedDrag(currentPoint);
        }

        private void PlayerWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CancelPendingRestoreDrag();
            _shouldStartDragAfterExitingPlayerFullscreen = false;
        }

        private void BeginPendingRestoreDrag(Point startPoint)
        {
            _pendingDragRestoreFromMaximized = true;
            _pendingDragStartPoint = startPoint;
            CaptureMouse();
        }

        private void BeginRestoreFromMaximizedDrag(Point currentPoint)
        {
            var screenPosition = PointToScreen(currentPoint);
            var widthRatio = ActualWidth > 0 ? currentPoint.X / ActualWidth : 0.5;
            widthRatio = Math.Clamp(widthRatio, 0.0, 1.0);
            var restoreWidth = _host.RestoreBounds.Width > 0 ? _host.RestoreBounds.Width : Math.Max(MinWidth, 1200);

            _host.WindowState = WindowState.Normal;
            _host.Left = screenPosition.X - restoreWidth * widthRatio;
            _host.Top = screenPosition.Y - 20;
            CancelPendingRestoreDrag();

            try
            {
                _host.DragMove();
            }
            catch
            {
                // Ignore drag interruptions caused by rapid pointer transitions.
            }
        }

        private void CancelPendingRestoreDrag()
        {
            if (!_pendingDragRestoreFromMaximized)
            {
                return;
            }

            _pendingDragRestoreFromMaximized = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        private async Task ExitPlayerFullscreenAndStartDragAsync()
        {
            await ExitEmbeddedPlayerFullscreenAsync();

            await Dispatcher.InvokeAsync(() =>
            {
                if (!_shouldStartDragAfterExitingPlayerFullscreen || Mouse.LeftButton != MouseButtonState.Pressed)
                {
                    _shouldStartDragAfterExitingPlayerFullscreen = false;
                    return;
                }

                _shouldStartDragAfterExitingPlayerFullscreen = false;

                if (_host.WindowState == WindowState.Maximized)
                {
                    BeginPendingRestoreDrag(Mouse.GetPosition(this));
                    return;
                }

                try
                {
                    _host.DragMove();
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
                CancelPendingRestoreDrag();
                Closed -= PlayerWindow_Closed;
                Player.Dispose();
                CommunityChatBrowser?.Dispose();
                CommunityCommentsBrowser?.Dispose();
            }
            catch
            {
                // Ignore disposal errors.
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
