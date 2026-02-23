using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        // Stream catalog and profile data state.
        private readonly ObservableCollection<YouTubeVideo> _videos = [];
        private readonly List<YouTubeVideo> _allVideos = [];
        private readonly Dictionary<string, DateTime> _watchHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _playbackSecondsHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<DateOnly> _watchedDays = [];
        private readonly HashSet<string> _favoriteVideoIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unlockedAchievements = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firstViewRewardedVideoIds = new(StringComparer.OrdinalIgnoreCase);

        // Progression counters.
        private bool _isHistoryLoadedSuccessfully;
        private int _totalXp;
        private int _prestige;
        private int _prestigeXp;
        private int _effectSessionsAny;
        private int _effectSessionsFivePlus;
        private int _effectSessionsTenPlus;
        private int _effectSessionsStrongBlur;
        private int _effectSessionsStrongRedGlow;
        private int _effectSessionsStrongVioletGlow;
        private int _effectSessionsStrongShake;
        private int _effectPresetSessionsAny;
        private int _effectPresetSessionsCustom;
        private int _effectPresetSessionsRetro;
        private int _effectPresetSessionsChaos;
        private int _effectPresetSessionsDream;

        // Services and async coordination.
        private readonly YouTubeStreamsService _streamsService = new();
        private readonly WatchHistoryService _watchHistoryService = new();
        private readonly GitHubUpdateService _updateService = new();
        private readonly NetworkTimeService _networkTimeService = new();

        // Update subsystem state.
        private string _latestReleaseUrl = $"https://github.com/{GitHubRepoOwner}/{GitHubRepoName}/releases/latest";
        private GitHubUpdateInfo? _lastUpdateInfo;
        private bool _isDownloadingUpdate;
        private CancellationTokenSource? _updateDownloadCts;
        private bool _updateButtonOpensReleasePage;

        // UI interaction state.
        private bool _suppressSelectionEvents;
        private bool _isOpeningVideo;
        private string? _selectedVideoId;
        private string _searchText = string.Empty;
        private bool _pendingDragRestoreFromMaximized;
        private Point _pendingDragStartPoint;
        private bool _isMainMinimizeAnimating;

        // Trusted time and quest scheduling.
        private DateTime _internetUtcAtSync;
        private DateTime _localUtcAtSync;
        private bool _hasInternetTime;
        private DateOnly _lastQuestRefreshDay;
        private string _lastQuestRefreshWeekKey = string.Empty;
        private DateOnly? _activeDailyQuestDate;
        private readonly List<string> _activeDailyQuestIds = [];
        private string _activeWeeklyQuestWeekKey = string.Empty;
        private readonly List<string> _activeWeeklyQuestIds = [];

        // Timers.
        private readonly DispatcherTimer _searchDebounceTimer = new();
        private readonly DispatcherTimer _networkTimeSyncTimer = new();
        private readonly DispatcherTimer _questRolloverTimer = new();
        private readonly DispatcherTimer _questUiRefreshTimer = new();

        // Shared UI brushes.
        private static readonly Brush TopNavSelectedBackground = BrushFromHex("#223A4D");
        private static readonly Brush TopNavSelectedForeground = BrushFromHex("#F3FBFF");
        private static readonly Brush TopNavSelectedBorder = BrushFromHex("#7BC8F4");
        private static readonly Brush TopNavDefaultBackground = BrushFromHex("#1A3143");
        private static readonly Brush TopNavDefaultForeground = BrushFromHex("#EAF6FF");
        private static readonly Brush TopNavDefaultBorder = BrushFromHex("#5C86A2");
    }
}
