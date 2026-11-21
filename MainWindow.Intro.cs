using System.Windows;

namespace JokerDBDTracker;

public partial class MainWindow
{
    private bool _introDismissed;

    private void InitializePsiIntro()
    {
        PsiIntro.Entered += (_, _) =>
        {
            _introDismissed = true;
            PsiIntro.Visibility = Visibility.Collapsed;
            PsiIntro.Dispose();
            AnimatePanelEntrance(LibraryHero);
        };
        PsiIntro.SoundChanged += async (_, _) =>
        {
            _appSettings.IntroSoundEnabled = PsiIntro.SoundEnabled;
            await SaveSettingsAsync();
        };
        PsiIntro.CloseRequested += (_, _) => Close();
        PsiIntro.RetryRequested += async (_, _) =>
        {
            if (_isCatalogLoading) return;
            PsiIntro.SetCatalogState(false, true);
            await LoadVideosAsync();
            if (!_introDismissed) PsiIntro.SetCatalogState(_allVideos.Count > 0, false);
        };
    }
}
