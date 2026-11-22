using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        private readonly MainWindow _host;
        internal event EventHandler? Closed;

        internal PlayerWindow(MainWindow host, YouTubeVideo video, int startSeconds, Func<DateTime>? utcNow = null)
        {
            _host = host;
            InitializeComponent();
            _video = video;
            if (Uri.TryCreate(video.ThumbnailUrl, UriKind.Absolute, out var cover) && cover.Scheme == Uri.UriSchemeHttps)
                PlayerLoadingCover.Source = new System.Windows.Media.Imaging.BitmapImage(cover);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _startSeconds = Math.Max(startSeconds, 0);
            LastPlaybackSeconds = _startSeconds;
            _effectDetails =
            [
                (Fx4, Fx4Details),
                (Fx5, Fx5Details),
                (Fx6, Fx6Details),
                (Fx7, Fx7Details),
                (Fx8, Fx8Details),
                (Fx9, Fx9Details),
                (Fx10, Fx10Details),
                (Fx11, Fx11Details),
                (Fx13, Fx13Details),
                (Fx14, Fx14Details)
            ];

            if (_host.IsPlayingQueue)
            {
                StopQueueButton.Visibility = System.Windows.Visibility.Visible;
                StopQueueButton.Content = PT("Остановить очередь", "Stop after this video");
            }

            Loaded += PlayerWindow_Loaded;
            Closed += PlayerWindow_Closed;
            _host.Activated += PlayerWindow_Activated;
            _host.Deactivated += PlayerWindow_Deactivated;
            _host.StateChanged += PlayerWindow_StateChanged;
            SizeChanged += PlayerWindow_SizeChanged;

            _positionTimer.Interval = TimeSpan.FromSeconds(2);
            _positionTimer.Tick += PositionTimer_Tick;
            _effectsApplyTimer.Interval = TimeSpan.FromMilliseconds(EffectsUpdateIntervalMilliseconds);
            _effectsApplyTimer.Tick += EffectsApplyTimer_Tick;
            _resizeSettleDebounceTimer.Interval = TimeSpan.FromMilliseconds(ResizeSettleDelayMilliseconds);
            _resizeSettleDebounceTimer.Tick += ResizeSettleDebounceTimer_Tick;
            PreviewMouseMove += PlayerWindow_PreviewMouseMove;
            PreviewMouseLeftButtonUp += PlayerWindow_PreviewMouseLeftButtonUp;
            PreviewMouseDown += PlayerWindow_PreviewMouseDown;
            PreviewKeyDown += PlayerWindow_PreviewKeyDown;
            PreviewGotKeyboardFocus += PlayerWindow_PreviewGotKeyboardFocus;
            PreviewLostKeyboardFocus += PlayerWindow_PreviewLostKeyboardFocus;
        }

        private void StopQueueButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            _host.StopAfterCurrentVideo();
            StopQueueButton.IsEnabled = false;
        }
    }
}
