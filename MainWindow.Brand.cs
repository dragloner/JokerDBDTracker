using System.Windows;
using JokerDBDTracker.Models;

namespace JokerDBDTracker;

public partial class MainWindow
{
    private const double CompactLayoutHeight = 760;

    private void BrandShell_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep the catalogue usable at 720p and with Windows display scaling.
        if (HeroLayout is null || SidebarPhoto is null || RecommendationsColumn is null) return;
        var compact = e.NewSize.Height < CompactLayoutHeight;
        HeroLayout.MinHeight = compact ? 132 : 190;
        HeroContent.Margin = compact ? new Thickness(22, 12, 16, 12) : new Thickness(26, 20, 18, 20);
        LibraryHeroTitle.FontSize = compact ? 25 : 30;
        HeroStreamTitle.Margin = compact ? new Thickness(0, 4, 0, 8) : new Thickness(0, 8, 0, 16);
        HeroStartButton.Padding = compact ? new Thickness(22, 9, 22, 9) : new Thickness(26, 13, 26, 13);
        HomeSummaryPanel.Padding = compact ? new Thickness(18, 8, 18, 8) : new Thickness(18, 16, 18, 16);
        SidebarPhoto.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (!IsFavoritesTabSelected() && !IsProfileTabSelected() && !IsSettingsTabSelected() && !IsQuestsTabSelected())
            RecommendationsColumn.Width = new GridLength(compact ? 290 : 320);
    }

    private static YouTubeVideo? SelectStartVideo(IEnumerable<YouTubeVideo> videos) => videos
        .Where(video => video.HasResumePosition)
        .OrderByDescending(video => video.LastViewedAtUtc)
        .FirstOrDefault() ?? videos.FirstOrDefault();

    private void RefreshStartCard()
    {
        var video = SelectStartVideo(_videos);
        HeroStartButton.IsEnabled = video is not null;
        HeroStartText.Text = T("Старт", "Start");
        HeroStreamTitle.Text = video?.Title ?? T("Нет стримов по выбранным фильтрам", "No streams match your filters");
        HeroResumeText.Text = video?.HasResumePosition == true
            ? T($"С {TimeSpan.FromSeconds(video.LastPlaybackSeconds):hh\\:mm\\:ss}", $"From {TimeSpan.FromSeconds(video.LastPlaybackSeconds):hh\\:mm\\:ss}")
            : T("С начала", "From the beginning");
        HeroResumeText.Visibility = video is null ? Visibility.Collapsed : Visibility.Visible;
        HeroStartButton.ToolTip = video?.Title;
    }

    private async void HeroStartButton_Click(object sender, RoutedEventArgs e)
    {
        var video = SelectStartVideo(_videos);
        if (video is not null)
        {
            await OpenVideoAsync(video);
        }
    }
}
