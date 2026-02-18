using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyStartupMaximized();
            UpdateMainWindowButtonsState();
            ShowLoadingOverlay(string.Empty, isIndeterminate: true);

            try
            {
                var loadVideosTask = LoadVideosAsync();
                var updateCheckTask = CheckForUpdatesDuringStartupAsync();
                await Task.WhenAll(loadVideosTask, updateCheckTask);
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

        private void MainMinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MainWindowSizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void MainCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateMainWindowButtonsState();
        }

        private void UpdateMainWindowButtonsState()
        {
            if (MainWindowSizeButton is null)
            {
                return;
            }

            MainWindowSizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void TopHeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TryBeginWindowDrag(e);
        }

        private void MainRootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TryBeginWindowDrag(e);
        }

        private void TryBeginWindowDrag(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
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
                _pendingDragRestoreFromMaximized = true;
                _pendingDragStartPoint = e.GetPosition(this);
                CaptureMouse();
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

        private void MainWindow_PreviewMouseMove(object sender, MouseEventArgs e)
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

            var current = e.GetPosition(this);
            var movedEnough =
                Math.Abs(current.X - _pendingDragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(current.Y - _pendingDragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
            if (!movedEnough)
            {
                return;
            }

            var screenPosition = PointToScreen(current);
            var widthRatio = ActualWidth > 0 ? current.X / ActualWidth : 0.5;
            widthRatio = Math.Clamp(widthRatio, 0.0, 1.0);
            var restoreWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Math.Max(1060, Width);

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

        private void MainWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CancelPendingRestoreDrag();
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
    }
}
