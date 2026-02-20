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
        private void PositionWindowToOwnerMonitor()
        {
            if (Owner is null)
            {
                return;
            }

            System.Windows.Rect ownerBounds = Owner.WindowState == WindowState.Minimized
                ? Owner.RestoreBounds
                : new System.Windows.Rect(Owner.Left, Owner.Top, Owner.ActualWidth, Owner.ActualHeight);

            if (ownerBounds.Width < 10 || ownerBounds.Height < 10)
            {
                return;
            }

            WindowState = WindowState.Normal;
            Left = ownerBounds.Left;
            Top = ownerBounds.Top;
            Width = Math.Max(MinWidth, ownerBounds.Width);
            Height = Math.Max(MinHeight, ownerBounds.Height);
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
                _mainRootMarginBeforePlayerFullscreen = MainRootGrid.Margin;
                _playerHostMarginBeforeFullscreen = PlayerHostBorder.Margin;
                _playerHostCornerRadiusBeforeFullscreen = PlayerHostBorder.CornerRadius;
                ApplyPlayerFullscreenChrome(isFullscreen: true);
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                ApplyFullMonitorBounds();
                Activate();
                PulseTopmost();
                RequestApplyEffects(immediate: false);
                return;
            }

            RestoreWindowAfterPlayerFullscreen();
        }

        private void RestoreWindowAfterPlayerFullscreen()
        {
            ApplyPlayerFullscreenChrome(isFullscreen: false);
            ResizeMode = _resizeModeBeforePlayerFullscreen == ResizeMode.NoResize
                ? ResizeMode.CanResize
                : _resizeModeBeforePlayerFullscreen;
            WindowState = _windowStateBeforePlayerFullscreen == WindowState.Minimized
                ? WindowState.Maximized
                : _windowStateBeforePlayerFullscreen;
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

            if (isFullscreen)
            {
                _effectsPanelExpandedBeforePlayerFullscreen = _effectsPanelExpanded;
                _effectsPanelExpanded = false;
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
            if (_isApplyingFullMonitorBounds)
            {
                return;
            }

            _isApplyingFullMonitorBounds = true;
            try
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
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

                e.Handled = true;
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                BeginPendingRestoreDrag(e.GetPosition(this));
                e.Handled = true;
                return;
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
                StopAllSoundEffects();
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
                UnregisterGlobalHotkeys();
                _hotkeySource?.RemoveHook(HotkeyWndProc);
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
            var restoreWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Math.Max(MinWidth, 1200);

            WindowState = WindowState.Normal;
            Left = screenPosition.X - restoreWidth * widthRatio;
            Top = screenPosition.Y - 20;
            CancelPendingRestoreDrag();

            try
            {
                DragMove();
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

                if (WindowState == WindowState.Maximized)
                {
                    BeginPendingRestoreDrag(Mouse.GetPosition(this));
                    return;
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
                CancelPendingRestoreDrag();
                Player.Dispose();
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
