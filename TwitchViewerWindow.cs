using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace JokerDBDTracker
{
    public partial class TwitchViewerWindow : Window
    {
        private const string TwitchUrl = "https://www.twitch.tv/";
        private const double EffectsPanelWidth = 220;

        private bool _effectsPanelVisible = false;
        private bool _webViewReady = false;

        private static readonly string[] AllowedHosts =
        [
            "twitch.tv", "www.twitch.tv", "clips.twitch.tv",
            "static-cdn.jtvnw.net", "gql.twitch.tv",
            "usher.twitchapps.com", "twitchapps.com",
            "player.twitch.tv", "embed.twitch.tv",
            "jtvnw.net", "twitchsvc.net",
            "cdn.jsdelivr.net", "cloudfront.net",
            "imasdk.googleapis.com", "twitchstatic.com",
        ];

        public TwitchViewerWindow()
        {
            InitializeComponent();
            Loaded += TwitchViewerWindow_Loaded;
        }

        private async void TwitchViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await TwitchWebView.EnsureCoreWebView2Async();
                TwitchWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                TwitchWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                TwitchWebView.Source = new Uri(TwitchUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть Twitch:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                return;

            var host = uri.Host.ToLowerInvariant();
            var allowed = AllowedHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                e.Cancel = true;
            }
        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webViewReady = true;
            await ApplyCssFilterAsync();
        }

        // ── Effects panel toggle ──────────────────────────────────────────────

        private void ToggleEffectsPanel_Click(object sender, RoutedEventArgs e)
        {
            _effectsPanelVisible = !_effectsPanelVisible;
            EffectsPanelColumn.Width = _effectsPanelVisible
                ? new GridLength(EffectsPanelWidth)
                : new GridLength(0);
            ToggleEffectsPanelButton.Content = _effectsPanelVisible
                ? "Скрыть эффекты"
                : "Показать эффекты";
        }

        // ── CSS filter application ────────────────────────────────────────────

        private async Task ApplyCssFilterAsync()
        {
            if (!_webViewReady) return;

            var filter = BuildCssFilter();
            var js = $$"""
                try {
                    document.documentElement.style.setProperty('filter', '{{filter}}', 'important');
                } catch(e) {}
                """;

            try
            {
                await TwitchWebView.CoreWebView2.ExecuteScriptAsync(js);
            }
            catch { /* ignore */ }
        }

        private string BuildCssFilter()
        {
            var parts = new List<string>();

            var grayscale = GrayscaleSlider.Value;
            var sepia = SepiaSlider.Value;
            var invert = InvertSlider.Value;
            var brightness = BrightnessSlider.Value;
            var contrast = ContrastSlider.Value;
            var saturation = SaturationSlider.Value;
            var hue = HueSlider.Value;
            var blur = BlurSlider.Value;

            if (grayscale > 0.001) parts.Add(F($"grayscale({grayscale:0.00})"));
            if (sepia     > 0.001) parts.Add(F($"sepia({sepia:0.00})"));
            if (invert    > 0.001) parts.Add(F($"invert({invert:0.00})"));
            if (Math.Abs(brightness - 1.0) > 0.01) parts.Add(F($"brightness({brightness:0.00})"));
            if (Math.Abs(contrast   - 1.0) > 0.01) parts.Add(F($"contrast({contrast:0.00})"));
            if (Math.Abs(saturation - 1.0) > 0.01) parts.Add(F($"saturate({saturation:0.00})"));
            if (hue       > 0.5) parts.Add(F($"hue-rotate({hue:0}deg)"));
            if (blur      > 0.1) parts.Add(F($"blur({blur:0.0}px)"));

            return parts.Count > 0 ? string.Join(" ", parts) : "none";
        }

        // Ensure decimal separator is always '.'
        private static string F(string s) => s.Replace(',', '.');

        private async void EffectSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            await ApplyCssFilterAsync();
        }

        private void ResetEffectsButton_Click(object sender, RoutedEventArgs e)
        {
            GrayscaleSlider.Value = 0;
            SepiaSlider.Value = 0;
            InvertSlider.Value = 0;
            BrightnessSlider.Value = 1;
            ContrastSlider.Value = 1;
            SaturationSlider.Value = 1;
            HueSlider.Value = 0;
            BlurSlider.Value = 0;
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            TwitchWebView.CoreWebView2?.Stop();
        }
    }
}
