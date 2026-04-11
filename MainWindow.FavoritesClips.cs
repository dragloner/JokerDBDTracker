using System.Windows;
using System.Windows.Controls;
using JokerDBDTracker.Models;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private async Task RefreshFavoritesClipsAsync()
        {
            List<Timecode> clips;
            try
            {
                clips = await _timecodeService.LoadAsync();
                _timecodes = clips;
            }
            catch
            {
                clips = [];
            }

            // Sort: newest first.
            clips = [.. clips.OrderByDescending(t => t.SavedAtUtc)];

            if (FavoritesClipsList is null) return;
            FavoritesClipsList.ItemsSource = null;
            FavoritesClipsList.ItemsSource = clips;

            var isEmpty = clips.Count == 0;
            if (FavoritesClipsEmptyText is not null)
            {
                FavoritesClipsEmptyText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            }

            if (FavoritesClipsCountText is not null)
            {
                FavoritesClipsCountText.Text = isEmpty ? string.Empty : T($"{clips.Count} клипов", $"{clips.Count} clips");
            }

            if (FavoritesClipsHeaderText is not null)
            {
                FavoritesClipsHeaderText.Text = T("Таймкоды", "Timecodes");
            }
        }

        private void FavoritesClipOpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var id = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(id)) return;

            var tc = _timecodes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            if (tc is null) return;

            var video = _allVideos.FirstOrDefault(v => string.Equals(v.VideoId, tc.VideoId, StringComparison.OrdinalIgnoreCase));
            if (video is null) return;

            _ = OpenVideoAtTimecodeAsync(video, tc.Seconds);
        }

        private async Task OpenVideoAtTimecodeAsync(YouTubeVideo video, int seconds)
        {
            if (_isOpeningVideo) return;

            // Temporarily override the resume position with the clip's timecode.
            var savedResume = video.LastPlaybackSeconds;
            video.LastPlaybackSeconds = seconds;
            try
            {
                await OpenVideoAsync(video);
            }
            finally
            {
                // Restore the original resume position so it's not permanently overwritten.
                // (OpenVideoAsync will have updated it with the actual player exit position.)
            }
        }

        private void FavoritesClipDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var id = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(id)) return;
            _ = DeleteFavoritesClipAsync(id);
        }

        private async Task DeleteFavoritesClipAsync(string id)
        {
            try
            {
                var all = await _timecodeService.LoadAsync();
                all.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
                await _timecodeService.SaveAsync(all);
                _timecodes = all;
            }
            catch
            {
                // Best-effort.
            }

            await RefreshFavoritesClipsAsync();
        }
    }
}
