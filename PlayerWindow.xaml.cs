using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class PlayerWindow : Window
    {
        private readonly YouTubeVideo _video;
        private readonly int _startSeconds;
        private readonly DispatcherTimer _positionTimer = new();
        private readonly (CheckBox toggle, FrameworkElement details)[] _effectDetails;
        private bool _isRecoveringBlockedNavigation;
        private const string DesktopChromeUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private bool _effectsPanelExpanded = true;
        private bool _isApplyingEffects;
        private bool _pendingEffectsApply;
        private bool _pendingEffectsApplyForce;
        private string _lastAppliedEffectsSignature = string.Empty;
        private bool _isPlayerElementFullScreen;
        private WindowState _windowStateBeforePlayerFullscreen = WindowState.Maximized;
        private ResizeMode _resizeModeBeforePlayerFullscreen = ResizeMode.CanResize;
        private double _lastMeasuredTime = -1;
        private double _watchXpBuffer;
        private double _eligibleWatchSeconds;
        private DateTime? _lastXpSampleUtc;
        private bool _halfHourBonusGranted;
        private bool _hourBonusGranted;
        private readonly DispatcherTimer _effectsApplyDebounceTimer = new();
        private readonly DispatcherTimer _resizeSettleDebounceTimer = new();
        private readonly WatchHistoryService _watchHistoryPersistService = new();
        private readonly AppSettingsService _settingsService = new();
        private AppSettingsData _appSettings = new();
        private int _lastPersistedPlaybackSeconds;
        private DateTime _lastPlaybackPersistUtc = DateTime.MinValue;
        private bool _isPersistingPlayback;
        private int _effectRefreshCounter;
        private bool _isEffectsBurstReapplyRunning;
        private bool _isApplyingFullMonitorBounds;
        private Thickness _mainRootMarginBeforePlayerFullscreen = new Thickness(8);
        private static readonly SemaphoreSlim PlaybackPersistLock = new(1, 1);
        private const int PlaybackPersistIntervalSeconds = 10;
        private const int EffectRefreshTickInterval = 3;
        private const int ResizeSettleDelayMilliseconds = 180;
        private const int HalfHourWatchBonusXp = 1100;
        private const int OneHourWatchBonusXp = 2300;
        private const int NinetyMinutesWatchBonusXp = 3800;
        private bool _isResizeInteractionInProgress;
        private bool _pendingDragRestoreFromMaximized;
        private Point _pendingDragStartPoint;
        private bool _shouldStartDragAfterExitingPlayerFullscreen;
        private bool _ninetyMinuteBonusGranted;
        private DateTime _lastUserInteractionUtc = DateTime.UtcNow;
        private int _navigationCompletedFailureCount;
        private bool _isPlayerMinimizeAnimating;
        private readonly Dictionary<int, Key> _registeredHotkeys = [];
        private HwndSource? _hotkeySource;
        private int _nextHotkeyId = 4000;
        private bool _effectsPanelExpandedBeforePlayerFullscreen = true;
        private Thickness _playerHostMarginBeforeFullscreen = new Thickness(0, 8, 0, 8);
        private CornerRadius _playerHostCornerRadiusBeforeFullscreen = new CornerRadius(10);

        private sealed class EffectSettings
        {
            public bool[] Flags { get; init; } = [];
            public double Contrast { get; init; }
            public double Darkness { get; init; }
            public double Saturation { get; init; }
            public double HueShift { get; init; }
            public double Blur { get; init; }
            public double RedGlow { get; init; }
            public double Vhs { get; init; }
            public double Shake { get; init; }
            public double Pixelation { get; init; }
            public double ColdTone { get; init; }
            public double VioletGlow { get; init; }
        }

        public int LastPlaybackSeconds { get; private set; }
        public int EligibleWatchSeconds { get; private set; }
        public bool CursedMasterUnlocked { get; private set; }
        public int WatchXpEarned { get; private set; }
        public bool WatchedWithAnyEffects { get; private set; }
        public int MaxEnabledEffectsCount { get; private set; }
        public bool UsedStrongBlur { get; private set; }
        public bool UsedStrongRedGlow { get; private set; }
        public bool UsedStrongVioletGlow { get; private set; }
        public bool UsedStrongShake { get; private set; }

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
