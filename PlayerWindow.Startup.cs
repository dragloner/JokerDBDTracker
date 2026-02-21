using JokerDBDTracker.Models;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        public PlayerWindow(YouTubeVideo video, int startSeconds)
        {
            InitializeComponent();
            WindowBoundsHelper.Attach(this);
            _video = video;
            _startSeconds = Math.Max(startSeconds, 0);
            LastPlaybackSeconds = _startSeconds;
            _lastPersistedPlaybackSeconds = _startSeconds;
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
                (Fx14, Fx14Details),
                (Fx15, Fx15Details)
            ];

            Loaded += PlayerWindow_Loaded;
            Closing += PlayerWindow_Closing;
            Closed += PlayerWindow_Closed;
            Activated += PlayerWindow_Activated;
            Deactivated += PlayerWindow_Deactivated;
            StateChanged += PlayerWindow_StateChanged;
            SizeChanged += PlayerWindow_SizeChanged;

            _positionTimer.Interval = TimeSpan.FromSeconds(2);
            _positionTimer.Tick += PositionTimer_Tick;
            _effectsApplyDebounceTimer.Interval = TimeSpan.FromMilliseconds(70);
            _effectsApplyDebounceTimer.Tick += EffectsApplyDebounceTimer_Tick;
            _resizeSettleDebounceTimer.Interval = TimeSpan.FromMilliseconds(ResizeSettleDelayMilliseconds);
            _resizeSettleDebounceTimer.Tick += ResizeSettleDebounceTimer_Tick;
            PreviewMouseMove += PlayerWindow_PreviewMouseMove;
            PreviewMouseLeftButtonUp += PlayerWindow_PreviewMouseLeftButtonUp;
            PreviewMouseDown += PlayerWindow_PreviewMouseDown;
            PreviewKeyDown += PlayerWindow_PreviewKeyDown;
        }
    }
}
