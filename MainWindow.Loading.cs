using System.Windows;
using System.Windows.Media.Animation;

namespace JokerDBDTracker;

public partial class MainWindow
{
    private const int LoadingRevealDurationMs = 320;
    private int _loadingTransitionVersion;

    private void ShowLoadingOverlay(string statusText, bool isIndeterminate)
    {
        _loadingTransitionVersion++;
        LoadingOverlay.BeginAnimation(OpacityProperty, null);
        LoadingOverlay.Opacity = 1;
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingStatusText.Text = statusText;
        LoadingProgressBar.IsIndeterminate = isIndeterminate;
        LoadingProgressBar.Value = 0;
        LoadingProgressText.Text = isIndeterminate ? string.Empty : T("Загрузка обновления: 0%", "Update download: 0%");
    }

    private void HideLoadingOverlay()
    {
        if (LoadingOverlay.Visibility != Visibility.Visible) return;
        var transitionVersion = ++_loadingTransitionVersion;
        if (!AreAnimationsEnabled)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var reveal = new DoubleAnimation(0, TimeSpan.FromMilliseconds(LoadingRevealDurationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        reveal.Completed += (_, _) =>
        {
            if (transitionVersion != _loadingTransitionVersion) return;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            LoadingOverlay.BeginAnimation(OpacityProperty, null);
            AnimatePanelEntrance(LibraryHero);
        };
        LoadingOverlay.BeginAnimation(OpacityProperty, reveal);
    }
}
