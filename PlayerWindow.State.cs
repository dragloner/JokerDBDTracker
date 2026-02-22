using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class PlayerWindow
    {
        private readonly YouTubeVideo _video;
        private readonly int _startSeconds;

        // Timers.
        private readonly DispatcherTimer _positionTimer = new();
        private readonly DispatcherTimer _effectsApplyDebounceTimer = new();
        private readonly DispatcherTimer _resizeSettleDebounceTimer = new();

        // Effects runtime state.
        private readonly (CheckBox toggle, FrameworkElement details)[] _effectDetails;
        private bool _effectsPanelExpanded = true;
        private bool _isApplyingEffects;
        private bool _pendingEffectsApply;
        private bool _pendingEffectsApplyForce;
        private string _lastAppliedEffectsSignature = string.Empty;
        private int _effectRefreshCounter;
        private bool _isEffectsBurstReapplyRunning;

        // Fullscreen/window chrome state.
        private bool _isPlayerElementFullScreen;
        private WindowState _windowStateBeforePlayerFullscreen = WindowState.Maximized;
        private ResizeMode _resizeModeBeforePlayerFullscreen = ResizeMode.CanResize;
        private Thickness _mainRootMarginBeforePlayerFullscreen = new Thickness(8);
        private Thickness _playerHostMarginBeforeFullscreen = new Thickness(0, 8, 0, 8);
        private CornerRadius _playerHostCornerRadiusBeforeFullscreen = new CornerRadius(10);
        private bool _effectsPanelExpandedBeforePlayerFullscreen = true;
        private bool _isApplyingFullMonitorBounds;

        // Playback and XP tracking.
        private double _lastMeasuredTime = -1;
        private double _watchXpBuffer;
        private double _eligibleWatchSeconds;
        private DateTime? _lastXpSampleUtc;
        private bool _halfHourBonusGranted;
        private bool _hourBonusGranted;
        private bool _ninetyMinuteBonusGranted;
        private int _lastPersistedPlaybackSeconds;
        private DateTime _lastPlaybackPersistUtc = DateTime.MinValue;
        private bool _isPersistingPlayback;

        // Resize/drag interaction state.
        private bool _isResizeInteractionInProgress;
        private bool _pendingDragRestoreFromMaximized;
        private Point _pendingDragStartPoint;
        private bool _shouldStartDragAfterExitingPlayerFullscreen;

        // Runtime lifecycle flags.
        private bool _isRecoveringBlockedNavigation;
        private bool _isPlayerClosing;
        private bool _isPlayerNavigationInProgress;
        private bool _isPlayerRuntimeReady;
        private int _playerNavigationVersion;
        private DateTime _lastUserInteractionUtc = DateTime.UtcNow;
        private int _navigationCompletedFailureCount;
        private bool _isPlayerMinimizeAnimating;
        private bool _hasShownSoundPlaybackWarning;
        private DateTime _lastWebScriptTimeoutLogUtc = DateTime.MinValue;

        // Hotkeys.
        private readonly Dictionary<int, Key> _registeredHotkeys = [];
        private HwndSource? _hotkeySource;
        private int _nextHotkeyId = 4000;
        private Key _lastProcessedAppKeybind = Key.None;
        private DateTime _lastProcessedAppKeybindUtc = DateTime.MinValue;

        // Services/configuration.
        private readonly WatchHistoryService _watchHistoryPersistService = new();
        private readonly AppSettingsService _settingsService = new();
        private AppSettingsData _appSettings = new();

        // Shared constants.
        private const string DesktopChromeUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private static readonly SemaphoreSlim PlaybackPersistLock = new(1, 1);
        private const int PlaybackPersistIntervalSeconds = 10;
        private const int EffectRefreshTickInterval = 3;
        private const int ResizeSettleDelayMilliseconds = 180;
        private const int HalfHourWatchBonusXp = 1100;
        private const int OneHourWatchBonusXp = 2300;
        private const int NinetyMinutesWatchBonusXp = 3800;

        private sealed class EffectSettings
        {
            public bool[] Flags { get; init; } = [];
            public double Contrast { get; init; }
            public double Darkness { get; init; }
            public double Saturation { get; init; }
            public double HueShift { get; init; }
            public double Blur { get; init; }
            public double Fisheye { get; init; }
            public double Vhs { get; init; }
            public double Shake { get; init; }
            public double JpegDamage { get; init; }
            public double ColdTone { get; init; }
            public double AudioVolumeBoost { get; init; }
            public double AudioPitchSemitones { get; init; }
            public double AudioReverb { get; init; }
            public double AudioEcho { get; init; }
            public double AudioDistortion { get; init; }
            public double AudioEqLowDb { get; init; }
            public double AudioEqMidDb { get; init; }
            public double AudioEqHighDb { get; init; }
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
    }
}
