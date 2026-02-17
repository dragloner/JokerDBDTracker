using System.Windows;
using System.Windows.Controls;
using JokerDBDTracker.Models;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private async Task OpenVideoAsync(YouTubeVideo video)
        {
            if (_isOpeningVideo || video is null)
            {
                return;
            }

            _isOpeningVideo = true;
            _selectedVideoId = video.VideoId;
            try
            {
                await MarkAsWatchedAsync(video);

                var player = new PlayerWindow(video, video.LastPlaybackSeconds)
                {
                    Owner = this
                };

                try
                {
                    Hide();
                    player.ShowDialog();
                }
                finally
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
                }

                video.LastPlaybackSeconds = player.LastPlaybackSeconds;
                _playbackSecondsHistory[video.VideoId] = player.LastPlaybackSeconds;

                if (!_firstViewRewardedVideoIds.Contains(video.VideoId))
                {
                    _firstViewRewardedVideoIds.Add(video.VideoId);
                    AddXp(XpFirstWatch);
                }

                AddXp(player.WatchXpEarned);

                if (player.WatchedWithAnyEffects)
                {
                    _effectSessionsAny++;
                }

                if (player.MaxEnabledEffectsCount >= 5)
                {
                    _effectSessionsFivePlus++;
                }

                if (player.MaxEnabledEffectsCount >= 10)
                {
                    _effectSessionsTenPlus++;
                }

                if (player.UsedStrongBlur)
                {
                    _effectSessionsStrongBlur++;
                }

                if (player.UsedStrongRedGlow)
                {
                    _effectSessionsStrongRedGlow++;
                }

                if (player.UsedStrongVioletGlow)
                {
                    _effectSessionsStrongVioletGlow++;
                }

                if (player.UsedStrongShake)
                {
                    _effectSessionsStrongShake++;
                }

                if (player.CursedMasterUnlocked && !_unlockedAchievements.Contains(AchievementCursed15))
                {
                    _unlockedAchievements.Add(AchievementCursed15);
                    AddXp(XpAchievement);
                }

                await SaveHistoryAsync();
                RefreshRecommendations();
                RefreshProfile();
                RefreshHomeSummary();
                RefreshVisibleVideos();
                ClearAllSelections();
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
                    $"Ошибка при открытии/закрытии плеера:{Environment.NewLine}{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _isOpeningVideo = false;
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
            }
            catch
            {
                // Keep UI alive even if an async operation fails.
            }
        }

        private async void PrestigeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var level = CalculateLevelFromXp(_prestigeXp);
                if (level < MaxLevel || _prestige >= MaxPrestige)
                {
                    return;
                }

                _prestige++;
                _prestigeXp = 0;

                await SaveHistoryAsync();
                RefreshProfile();
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
