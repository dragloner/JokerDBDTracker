using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow : Window
    {
        private const string StreamsUrl = "https://www.youtube.com/@JokerDBD/streams";
        private const string AchievementCursed15 = "cursed_15_effects_full_stream";
        private const int MaxRecentStreamsInProfile = 5;
        private const int MaxLevel = 100;
        private const int MaxPrestige = 100;
        private const int XpFirstWatch = 120;
        private const int XpAchievement = 800;

        private readonly ObservableCollection<YouTubeVideo> _videos = [];
        private readonly List<YouTubeVideo> _allVideos = [];
        private readonly Dictionary<string, DateTime> _watchHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _playbackSecondsHistory = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<DateOnly> _watchedDays = [];
        private readonly HashSet<string> _favoriteVideoIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unlockedAchievements = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firstViewRewardedVideoIds = new(StringComparer.OrdinalIgnoreCase);
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

        private readonly YouTubeStreamsService _streamsService = new();
        private readonly WatchHistoryService _watchHistoryService = new();
        private readonly DispatcherTimer _searchDebounceTimer = new();

        private bool _suppressSelectionEvents;
        private bool _isOpeningVideo;
        private string? _selectedVideoId;
        private string _searchText = string.Empty;
        private static readonly Brush TopNavSelectedBackground = BrushFromHex("#E4EEF6");
        private static readonly Brush TopNavSelectedForeground = BrushFromHex("#173041");
        private static readonly Brush TopNavSelectedBorder = BrushFromHex("#8FB4CD");
        private static readonly Brush TopNavDefaultBackground = BrushFromHex("#2C4357");
        private static readonly Brush TopNavDefaultForeground = BrushFromHex("#EAF6FF");
        private static readonly Brush TopNavDefaultBorder = BrushFromHex("#6E91A9");

        public MainWindow()
        {
            InitializeComponent();
            WindowBoundsHelper.Attach(this);
            VideoList.ItemsSource = _videos;
            UpdateTopNavButtonsVisualState();
            StateChanged += MainWindow_StateChanged;
            UpdateMainWindowButtonsState();
            Loaded += MainWindow_Loaded;
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(180);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
        }
    }
}
