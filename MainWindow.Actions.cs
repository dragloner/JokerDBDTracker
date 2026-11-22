using System.Windows;
using System.Windows.Controls;
using JokerDBDTracker.Models;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private PlayerWindow? _activePlayer;
        private bool _closeAfterPlayer;

        private async Task<bool> OpenVideoAsync(YouTubeVideo video, int? startSeconds = null)
        {
            if (_isOpeningVideo || video is null)
            {
                return false;
            }

            _isOpeningVideo = true;
            _selectedVideoId = video.VideoId;
            try
            {

                var player = new PlayerWindow(this, video, startSeconds ?? video.LastPlaybackSeconds, GetTrustedUtcNow);
                ConnectPlayerProgress(player, video);
                var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                player.Closed += (_, _) => exited.TrySetResult();
                _activePlayer = player;
                try
                {
                    await AnimateCoverToPlayerAsync(video);
                    LibraryWorkspace.Visibility = Visibility.Collapsed;
                    LibraryNavigation.Visibility = Visibility.Collapsed;
                    PlayerWorkspace.Visibility = Visibility.Visible;
                    PlayerWorkspace.Content = player;
                    player.Focus();
                    await exited.Task;
                }
                finally
                {
                    PlayerWorkspace.Content = null;
                    PlayerWorkspace.Visibility = Visibility.Collapsed;
                    LibraryNavigation.Visibility = Visibility.Visible;
                    LibraryWorkspace.Visibility = Visibility.Visible;
                    _activePlayer = null;
                    AnimatePanelEntrance(LibraryWorkspace);
                }

                if (player.PlaybackStarted)
                {
                    video.LastPlaybackSeconds = player.LastPlaybackSeconds;
                    _playbackSecondsHistory[video.VideoId] = player.LastPlaybackSeconds;
                }
                if (!_settingsReadFailed) await LoadAndApplySettingsAsync();
                await RefreshFavoritesClipsAsync();

                await SaveHistoryAsync();
                RefreshRecommendations();
                RefreshProfile();
                RefreshHomeSummary();
                VideoList.Items.Refresh();
                RefreshStartCard();
                ClearAllSelections();
                return player.PlaybackStarted;
            }
            catch (Exception ex)
            {
                if (!IsVisible)
                {
                    Show();
                }

                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                Activate();
                MessageBox.Show(
                    $"{T("Ошибка при открытии/закрытии плеера:", "Error while opening/closing player:")}{Environment.NewLine}{ex.Message}",
                    T("Ошибка", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                _isOpeningVideo = false;
                if (_closeAfterPlayer) Close();
            }
        }

        private async void VideoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_suppressSelectionEvents || IsProfileTabSelected())
                {
                    return;
                }

                if (VideoList.SelectedItem is not YouTubeVideo video)
                {
                    return;
                }

                await OpenVideoAsync(video);
            }
            catch
            {
                // Keep UI alive even if an async operation fails.
            }
        }

        private async void RecommendationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_suppressSelectionEvents || IsProfileTabSelected())
                {
                    return;
                }

                if (RecommendationsList.SelectedItem is not YouTubeVideo video)
                {
                    return;
                }

                await OpenVideoAsync(video);
            }
            catch
            {
                // Keep UI alive even if an async operation fails.
            }
        }

        private async void RecentStreamsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_suppressSelectionEvents)
                {
                    return;
                }

                if (RecentStreamsList.SelectedItem is not YouTubeVideo video)
                {
                    return;
                }

                await OpenVideoAsync(video);
            }
            catch
            {
                // Keep UI alive even if an async operation fails.
            }
        }

        private async void ToggleFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                e.Handled = true;

                if (sender is not Button button || button.Tag is not string videoId)
                {
                    return;
                }

                var video = _allVideos.FirstOrDefault(v => v.VideoId == videoId);
                if (video is null)
                {
                    return;
                }

                video.IsFavorite = !video.IsFavorite;
                if (video.IsFavorite)
                {
                    _favoriteVideoIds.Add(video.VideoId);
                }
                else
                {
                    _favoriteVideoIds.Remove(video.VideoId);
                }

                await SaveHistoryAsync();
                RefreshRecommendations();
                RefreshProfile();
                RefreshHomeSummary();
                RefreshVisibleVideos(video.VideoId);
                RefreshFavoritesClipsView();
                RefreshFavoritesSummary();
                ClearAllSelections();
            }
            catch
            {
                // Keep UI alive even if an async operation fails.
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
