using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private bool IsFavoritesTabSelected() => TopTabControl.SelectedIndex == 1;
        private bool IsProfileTabSelected() => TopTabControl.SelectedIndex == 2;
        private bool IsSettingsTabSelected() => TopTabControl.SelectedIndex == 3;

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
            ApplyTopNavState(ProfileNavButton, TopTabControl.SelectedIndex == 2);
            ApplyTopNavState(SettingsNavButton, TopTabControl.SelectedIndex == 3);

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
            var settingsMode = IsSettingsTabSelected();
            var favoritesMode = IsFavoritesTabSelected();
            StreamsPanel.Visibility = profileMode || settingsMode ? Visibility.Collapsed : Visibility.Visible;
            RecommendationsPanel.Visibility = profileMode || settingsMode || favoritesMode ? Visibility.Collapsed : Visibility.Visible;
            HomeSummaryPanel.Visibility = profileMode || settingsMode || favoritesMode ? Visibility.Collapsed : Visibility.Visible;
            ProfilePanel.Visibility = profileMode ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = settingsMode ? Visibility.Visible : Visibility.Collapsed;
            RecommendationsColumn.Width = profileMode || settingsMode || favoritesMode
                ? new GridLength(0)
                : new GridLength(420);
            MainColumnsSpacer.Width = profileMode || settingsMode || favoritesMode
                ? new GridLength(0)
                : new GridLength(12);

            if (profileMode)
            {
                RefreshProfile();
            }
            else if (!settingsMode)
            {
                RefreshVisibleVideos();
                RefreshRecommendations();
                RefreshHomeSummary();
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
            SelectTopTab(3);
        }
    }
}
