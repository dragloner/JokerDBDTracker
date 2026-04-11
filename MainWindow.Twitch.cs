using System.Windows;
using JokerDBDTracker.Models;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private void TwitchWatchButton_Click(object sender, RoutedEventArgs e)
        {
            _ = OpenTwitchStreamAsync();
        }

        private async Task OpenTwitchStreamAsync()
        {
            if (_isOpeningVideo)
            {
                return;
            }

            _isOpeningVideo = true;
            try
            {
                var twitchVideo = new YouTubeVideo
                {
                    VideoId = "twitch:home",
                    Title = "Twitch",
                    IsTwitchStream = true
                };

                var player = new PlayerWindow(twitchVideo, 0)
                {
                    Owner = this
                };

                try
                {
                    await AnimateWindowOpacityAsync(0.0, 220);
                    Hide();
                    player.ShowDialog();
                }
                finally
                {
                    if (!IsVisible)
                    {
                        Show();
                    }

                    if (AreAnimationsEnabled)
                    {
                        Opacity = 0;
                    }

                    if (WindowState == WindowState.Minimized)
                    {
                        WindowState = WindowState.Normal;
                    }

                    Activate();
                    await AnimateWindowOpacityAsync(1.0, 260);
                }

                // Twitch: no XP, no watch history, no achievement tracking.
            }
            catch (Exception ex)
            {
                if (!IsVisible)
                {
                    Show();
                }

                Activate();
                MessageBox.Show(
                    $"{T("Ошибка при открытии Twitch:", "Error opening Twitch:")}{Environment.NewLine}{ex.Message}",
                    T("Ошибка", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _isOpeningVideo = false;
            }
        }
    }
}
