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
            await LoadVideosAsync();
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
            TryBeginWindowDrag(e, 1060);
        }

        private void MainRootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TryBeginWindowDrag(e, 1060);
        }

        private void TryBeginWindowDrag(MouseButtonEventArgs e, double minimumRestoreWidth)
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
