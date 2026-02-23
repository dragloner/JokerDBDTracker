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
        private double _fisheyeCenterX = 0.5;
        private double _fisheyeCenterY = 0.5;
        private bool _isDraggingFisheyeCenter;
        private bool _suppressEffectUiEvents;
        private int _selectedCustomPresetSlotIndex;
        private readonly List<PlayerPresetSlot> _customPresetSlots = [];
        private readonly HashSet<string> _presetKeysUsedThisSession = new(StringComparer.OrdinalIgnoreCase);

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
        private const int MaxCustomPlayerPresets = 20;

        private sealed class EffectSettings
        {
            public bool[] Flags { get; set; } = [];
            public double Contrast { get; set; }
            public double Darkness { get; set; }
            public double Saturation { get; set; }
            public double HueShift { get; set; }
            public double Blur { get; set; }
            public double Fisheye { get; set; }
            public double FisheyeCenterX { get; set; }
            public double FisheyeCenterY { get; set; }
            public double Vhs { get; set; }
            public double Shake { get; set; }
            public double JpegDamage { get; set; }
            public double ColdTone { get; set; }
            public double AudioVolumeBoost { get; set; }
            public double AudioPitchSemitones { get; set; }
            public double AudioReverb { get; set; }
            public double AudioEcho { get; set; }
            public double AudioDistortion { get; set; }
            public double AudioEqLowDb { get; set; }
            public double AudioEqMidDb { get; set; }
            public double AudioEqHighDb { get; set; }
        }

        private sealed class PlayerPresetSlot
        {
            public int SlotIndex { get; init; }
            public string Name { get; set; } = string.Empty;
            public string PayloadJson { get; set; } = string.Empty;
            public bool HasPayload => !string.IsNullOrWhiteSpace(PayloadJson);
        }

        private sealed class PlayerPresetDefinition
        {
            public string Key { get; init; } = string.Empty;
            public string DisplayNameRu { get; init; } = string.Empty;
            public string DisplayNameEn { get; init; } = string.Empty;
            public string DescriptionRu { get; init; } = string.Empty;
            public string DescriptionEn { get; init; } = string.Empty;
            public Func<EffectSettings> Factory { get; init; } = null!;
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
        public bool UsedAnyPreset => _presetKeysUsedThisSession.Count > 0;
        public bool UsedCustomPreset => _presetKeysUsedThisSession.Contains("custom");
        public bool UsedRetroPreset => _presetKeysUsedThisSession.Contains("retro_vhs");
        public bool UsedChaosPreset => _presetKeysUsedThisSession.Contains("chaos");
        public bool UsedDreamPreset => _presetKeysUsedThisSession.Contains("dream");
    }
}
