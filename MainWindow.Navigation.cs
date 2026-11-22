using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private bool IsFavoritesTabSelected() => TopTabControl.SelectedIndex == 1;
        private bool IsProfileTabSelected() => TopTabControl.SelectedIndex == 2;
        private bool IsQuestsTabSelected() => TopTabControl.SelectedIndex == 3;
        private bool IsSettingsTabSelected() => TopTabControl.SelectedIndex == 4;

        private void SelectTopTab(int index)
        {
            if (TopTabControl.SelectedIndex == index)
            {
                return;
            }

            TopTabControl.SelectedIndex = index;
        }

        private void UpdateTopNavButtonsVisualState()
        {
            ApplyTopNavState(CatalogNavButton, TopTabControl.SelectedIndex == 5);
            ApplyTopNavState(HomeNavButton, TopTabControl.SelectedIndex == 0);
            ApplyTopNavState(FavoritesNavButton, TopTabControl.SelectedIndex == 1);
            ApplyTopNavState(ProfileNavButton, TopTabControl.SelectedIndex == 2 || TopTabControl.SelectedIndex == 3);
            ApplyTopNavState(SettingsNavButton, TopTabControl.SelectedIndex == 4);

            void ApplyTopNavState(Button button, bool selected)
            {
                button.Background = selected ? TopNavSelectedBackground : TopNavDefaultBackground;
                button.Foreground = selected ? TopNavSelectedForeground : TopNavDefaultForeground;
                button.BorderBrush = selected ? TopNavSelectedBorder : TopNavDefaultBorder;
            }
        }

        private static Brush BrushFromHex(string hex)
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }

        private void TopTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || e.OriginalSource != TopTabControl)
            {
                return;
            }

            UpdateTopNavButtonsVisualState();
            RefreshLibraryPageTitle();

            var profileMode = IsProfileTabSelected();
            var questsMode = IsQuestsTabSelected();
            var settingsMode = IsSettingsTabSelected();
            var favoritesMode = IsFavoritesTabSelected();
            var isOverlayTab = profileMode || questsMode || settingsMode;
            var catalogMode = TopTabControl.SelectedIndex == 5;
            StreamsPanel.Visibility = catalogMode || favoritesMode ? Visibility.Visible : Visibility.Collapsed;
            RecommendationsPanel.Visibility = (!isOverlayTab && !favoritesMode && !catalogMode) ? Visibility.Visible : Visibility.Collapsed;
            FavoritesClipsPanel.Visibility = (!isOverlayTab && favoritesMode) ? Visibility.Visible : Visibility.Collapsed;
            FavoritesSummaryPanel.Visibility = (!isOverlayTab && favoritesMode) ? Visibility.Visible : Visibility.Collapsed;
            LibraryHero.Visibility = isOverlayTab || favoritesMode || catalogMode ? Visibility.Collapsed : Visibility.Visible;
            ProfilePanel.Visibility = profileMode ? Visibility.Visible : Visibility.Collapsed;
            QuestsPanel.Visibility = questsMode ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = settingsMode ? Visibility.Visible : Visibility.Collapsed;
            RecommendationsColumn.Width = !favoritesMode
                ? new GridLength(0)
                : new GridLength(340);
            MainColumnsSpacer.Width = !favoritesMode
                ? new GridLength(0)
                : new GridLength(12);

            if (profileMode)
            {
                RefreshProfile();
            }
            else if (questsMode)
            {
                RefreshQuestsPage();
            }
            else if (favoritesMode)
            {
                RefreshVisibleVideos();
                RefreshFavoritesSummary();
                _ = RefreshFavoritesClipsAsync();
            }
            else if (!settingsMode)
            {
                RefreshVisibleVideos();
                RefreshRecommendations();
                RefreshHomeSummary();
            }

            RefreshSearchPlaceholderText();
            AnimatePanelEntrance(LibraryHero);

            foreach (var panel in new FrameworkElement[]
                     {
                         StreamsPanel,
                         RecommendationsPanel,
                         FavoritesClipsPanel,
                         FavoritesSummaryPanel,
                         HomeSummaryPanel,
                         ProfilePanel,
                         QuestsPanel,
                         SettingsPanel,
                     })
            {
                AnimatePanelEntrance(panel);
            }
        }

        private void RefreshLibraryPageTitle()
        {
            LibraryPageTitle.Text = TopTabControl.SelectedIndex switch
            {
                1 => T("Избранное", "Favorites"),
                2 => T("Профиль", "Profile"),
                3 => T("Задания", "Quests"),
                4 => T("Настройки", "Settings"),
                5 => T("Каталог", "Catalog"),
                _ => T("Главная", "Home")
            };
        }

        private void CatalogNavButton_Click(object sender, RoutedEventArgs e) => SelectTopTab(5);

        private void HomeNavButton_Click(object sender, RoutedEventArgs e)
        {
            SelectTopTab(0);
        }

        private void FavoritesNavButton_Click(object sender, RoutedEventArgs e)
        {
            SelectTopTab(1);
        }

        private void ProfileNavButton_Click(object sender, RoutedEventArgs e)
        {
            SelectTopTab(2);
        }



        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            SelectTopTab(4);
        }
    }
}
