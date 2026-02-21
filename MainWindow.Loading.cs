using System.Windows;
using System.Windows.Media.Imaging;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private void ShowLoadingOverlay(string statusText, bool isIndeterminate)
        {
            if (LoadingStatusText is not null)
            {
                LoadingStatusText.Text = statusText;
            }

            if (LoadingProgressBar is not null)
            {
                LoadingProgressBar.IsIndeterminate = isIndeterminate;
                LoadingProgressBar.Value = 0;
            }

            if (LoadingProgressText is not null)
            {
                LoadingProgressText.Text = isIndeterminate ? string.Empty : T("Загрузка обновления: 0%", "Update download: 0%");
            }

            if (LoadingOverlay is not null)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
            }
        }

        private void HideLoadingOverlay()
        {
            if (LoadingOverlay is not null)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void InitializeLoadingBackgroundImage()
        {
            if (LoadingBackgroundImage is null)
            {
                return;
            }

            try
            {
                var resourceUri = new Uri("Assets/loading-screen.jpg", UriKind.Relative);
                var streamInfo = Application.GetResourceStream(resourceUri);
                if (streamInfo?.Stream is null)
                {
                    return;
                }

                using var resourceStream = streamInfo.Stream;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = resourceStream;
                image.EndInit();
                image.Freeze();
                LoadingBackgroundImage.Source = image;
            }
            catch
            {
                // Keep default dark background if custom image fails to load.
            }
        }
    }
}