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
            if (!IsLoaded)
            {
                return;
            }

            UpdateTopNavButtonsVisualState();

            var profileMode = IsProfileTabSelected();
            var questsMode = IsQuestsTabSelected();
            var settingsMode = IsSettingsTabSelected();
            var favoritesMode = IsFavoritesTabSelected();
            StreamsPanel.Visibility = profileMode || questsMode || settingsMode ? Visibility.Collapsed : Visibility.Visible;
            RecommendationsPanel.Visibility = profileMode || questsMode || settingsMode || favoritesMode ? Visibility.Collapsed : Visibility.Visible;
            HomeSummaryPanel.Visibility = profileMode || questsMode || settingsMode || favoritesMode ? Visibility.Collapsed : Visibility.Visible;
            ProfilePanel.Visibility = profileMode ? Visibility.Visible : Visibility.Collapsed;
            QuestsPanel.Visibility = questsMode ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = settingsMode ? Visibility.Visible : Visibility.Collapsed;
            RecommendationsColumn.Width = profileMode || questsMode || settingsMode || favoritesMode
                ? new GridLength(0)
                : new GridLength(420);
            MainColumnsSpacer.Width = profileMode || questsMode || settingsMode || favoritesMode
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
            else if (!settingsMode)
            {
                RefreshVisibleVideos();
                RefreshRecommendations();
                RefreshHomeSummary();
            }

            foreach (var panel in new FrameworkElement[]
                     {
                         StreamsPanel,
                         RecommendationsPanel,
                         HomeSummaryPanel,
                         ProfilePanel,
                         QuestsPanel,
                         SettingsPanel
                     })
            {
                AnimatePanelEntrance(panel);
            }
        }

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
