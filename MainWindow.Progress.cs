using System.Globalization;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private enum QuestMetric
        {
            DailyWatchSeconds,
            DailyStreamsCount,
            DailyBestSessionSeconds,
            DailyEffectSessionsCount,
            DailyPresetSessionsCount,
            WeeklyWatchSeconds,
            WeeklyWatchDaysCount,
            WeeklyStreamsCount,
            WeeklyBestSessionSeconds,
            WeeklyEffectSessionsCount,
            WeeklyPresetSessionsCount
        }

        private sealed record DailyQuestTemplate(
            string Id,
            string TitleRu,
            string TitleEn,
            string DescriptionRu,
            string DescriptionEn,
            int RewardXp,
            int Target,
            QuestMetric Metric,
            string Unit);

        private sealed record WeeklyQuestTemplate(
            string Id,
            string TitleRu,
            string TitleEn,
            string DescriptionRu,
            string DescriptionEn,
            int RewardXp,
            int Target,
            QuestMetric Metric,
            string Unit);

        private const int ActiveDailyQuestCount = 5;
        private const int ActiveWeeklyQuestCount = 4;

        private readonly Dictionary<DateOnly, int> _watchedSecondsByDay = [];
        private readonly Dictionary<DateOnly, int> _bestSessionSecondsByDay = [];
        private readonly Dictionary<DateOnly, int> _effectSessionsByDay = [];
        private readonly Dictionary<DateOnly, int> _presetSessionsByDay = [];
        private readonly HashSet<string> _rewardedQuestKeys = new(StringComparer.Ordinal);

        private static readonly DailyQuestTemplate[] DailyQuestPool =
        [
            new("daily_watch_30m", "30 минут просмотра", "30 minutes watch", "Набери 30 минут просмотра за день.", "Reach 30 minutes of watch time today.", 850, 30 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_45m", "45 минут просмотра", "45 minutes watch", "Набери 45 минут просмотра за день.", "Reach 45 minutes of watch time today.", 1100, 45 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_60m", "60 минут просмотра", "60 minutes watch", "Набери 60 минут просмотра за день.", "Reach 60 minutes of watch time today.", 1450, 60 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_90m", "90 минут просмотра", "90 minutes watch", "Набери 90 минут просмотра за день.", "Reach 90 minutes of watch time today.", 2200, 90 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_120m", "2 часа просмотра", "2 hours watch", "Набери 2 часа просмотра за день.", "Reach 2 hours of watch time today.", 2900, 120 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_150m", "2.5 часа просмотра", "2.5 hours watch", "Набери 2.5 часа просмотра за день.", "Reach 2.5 hours of watch time today.", 3650, 150 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_180m", "3 часа просмотра", "3 hours watch", "Набери 3 часа просмотра за день.", "Reach 3 hours of watch time today.", 4500, 180 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_streams_2", "2 стрима в день", "2 streams a day", "Открой два разных стрима сегодня.", "Open two different streams today.", 820, 2, QuestMetric.DailyStreamsCount, "count"),
            new("daily_streams_3", "3 стрима в день", "3 streams a day", "Открой три разных стрима сегодня.", "Open three different streams today.", 1180, 3, QuestMetric.DailyStreamsCount, "count"),
            new("daily_streams_4", "4 стрима в день", "4 streams a day", "Открой четыре разных стрима сегодня.", "Open four different streams today.", 1540, 4, QuestMetric.DailyStreamsCount, "count"),
            new("daily_streams_5", "5 стримов в день", "5 streams a day", "Открой пять разных стримов сегодня.", "Open five different streams today.", 2050, 5, QuestMetric.DailyStreamsCount, "count"),
            new("daily_streams_6", "6 стримов в день", "6 streams a day", "Открой шесть разных стримов сегодня.", "Open six different streams today.", 2720, 6, QuestMetric.DailyStreamsCount, "count"),
            new("daily_streams_8", "8 стримов в день", "8 streams a day", "Открой восемь разных стримов сегодня.", "Open eight different streams today.", 3900, 8, QuestMetric.DailyStreamsCount, "count"),
            new("daily_session_15m", "Сессия 15 минут", "15-minute session", "Сделай одну сессию минимум 15 минут.", "Complete one session of at least 15 minutes.", 980, 15 * 60, QuestMetric.DailyBestSessionSeconds, "sec"),
            new("daily_session_25m", "Сессия 25 минут", "25-minute session", "Сделай одну сессию минимум 25 минут.", "Complete one session of at least 25 minutes.", 1500, 25 * 60, QuestMetric.DailyBestSessionSeconds, "sec"),
            new("daily_session_35m", "Сессия 35 минут", "35-minute session", "Сделай одну сессию минимум 35 минут.", "Complete one session of at least 35 minutes.", 2100, 35 * 60, QuestMetric.DailyBestSessionSeconds, "sec"),
            new("daily_session_50m", "Сессия 50 минут", "50-minute session", "Сделай одну сессию минимум 50 минут.", "Complete one session of at least 50 minutes.", 3000, 50 * 60, QuestMetric.DailyBestSessionSeconds, "sec"),
            new("daily_session_70m", "Сессия 70 минут", "70-minute session", "Сделай одну сессию минимум 70 минут.", "Complete one session of at least 70 minutes.", 4100, 70 * 60, QuestMetric.DailyBestSessionSeconds, "sec"),
            new("daily_effects_1", "Эффект-сессия", "Effect session", "Проведи хотя бы 1 сессию с эффектами.", "Complete at least 1 session with effects.", 1200, 1, QuestMetric.DailyEffectSessionsCount, "count"),
            new("daily_effects_2", "2 эффект-сессии", "2 effect sessions", "Проведи 2 сессии с эффектами.", "Complete 2 sessions with effects.", 1750, 2, QuestMetric.DailyEffectSessionsCount, "count"),
            new("daily_effects_3", "3 эффект-сессии", "3 effect sessions", "Проведи 3 сессии с эффектами.", "Complete 3 sessions with effects.", 2450, 3, QuestMetric.DailyEffectSessionsCount, "count"),
            new("daily_effects_4", "4 эффект-сессии", "4 effect sessions", "Проведи 4 сессии с эффектами.", "Complete 4 sessions with effects.", 3380, 4, QuestMetric.DailyEffectSessionsCount, "count"),
            new("daily_effects_5", "5 эффект-сессий", "5 effect sessions", "Проведи 5 сессий с эффектами.", "Complete 5 sessions with effects.", 4550, 5, QuestMetric.DailyEffectSessionsCount, "count"),
            new("daily_presets_1", "1 пресет-сессия", "1 preset session", "Примени пресет хотя бы в 1 сессии сегодня.", "Use a preset in at least 1 session today.", 1250, 1, QuestMetric.DailyPresetSessionsCount, "count"),
            new("daily_presets_2", "2 пресет-сессии", "2 preset sessions", "Примени пресет в 2 сессиях сегодня.", "Use presets in 2 sessions today.", 1820, 2, QuestMetric.DailyPresetSessionsCount, "count"),
            new("daily_presets_3", "3 пресет-сессии", "3 preset sessions", "Примени пресет в 3 сессиях сегодня.", "Use presets in 3 sessions today.", 2480, 3, QuestMetric.DailyPresetSessionsCount, "count"),
            new("daily_presets_4", "4 пресет-сессии", "4 preset sessions", "Примени пресет в 4 сессиях сегодня.", "Use presets in 4 sessions today.", 3320, 4, QuestMetric.DailyPresetSessionsCount, "count"),
            new("daily_presets_5", "5 пресет-сессий", "5 preset sessions", "Примени пресет в 5 сессиях сегодня.", "Use presets in 5 sessions today.", 4300, 5, QuestMetric.DailyPresetSessionsCount, "count")
        ];

        private static readonly WeeklyQuestTemplate[] WeeklyQuestPool =
        [
            new("weekly_days_3", "3 активных дня", "3 active days", "Смотри стримы минимум в 3 дня этой недели.", "Watch streams on at least 3 days this week.", 2900, 3, QuestMetric.WeeklyWatchDaysCount, "count"),
            new("weekly_days_4", "4 активных дня", "4 active days", "Смотри стримы минимум в 4 дня этой недели.", "Watch streams on at least 4 days this week.", 3600, 4, QuestMetric.WeeklyWatchDaysCount, "count"),
            new("weekly_days_5", "5 активных дней", "5 active days", "Смотри стримы минимум в 5 днях этой недели.", "Watch streams on at least 5 days this week.", 4700, 5, QuestMetric.WeeklyWatchDaysCount, "count"),
            new("weekly_days_6", "6 активных дней", "6 active days", "Смотри стримы минимум в 6 днях этой недели.", "Watch streams on at least 6 days this week.", 6200, 6, QuestMetric.WeeklyWatchDaysCount, "count"),
            new("weekly_days_7", "7 активных дней", "7 active days", "Смотри стримы каждый день этой недели.", "Watch streams every day this week.", 8400, 7, QuestMetric.WeeklyWatchDaysCount, "count"),
            new("weekly_watch_4h", "4 часа за неделю", "4 hours this week", "Набери 4 часа просмотра в текущей неделе.", "Accumulate 4 watch hours this week.", 3900, 4 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_watch_6h", "6 часов за неделю", "6 hours this week", "Набери 6 часов просмотра в текущей неделе.", "Accumulate 6 watch hours this week.", 5600, 6 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_watch_8h", "8 часов за неделю", "8 hours this week", "Набери 8 часов просмотра в текущей неделе.", "Accumulate 8 watch hours this week.", 7600, 8 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_watch_12h", "12 часов за неделю", "12 hours this week", "Набери 12 часов просмотра в текущей неделе.", "Accumulate 12 watch hours this week.", 9800, 12 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_watch_16h", "16 часов за неделю", "16 hours this week", "Набери 16 часов просмотра в текущей неделе.", "Accumulate 16 watch hours this week.", 12800, 16 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_watch_20h", "20 часов за неделю", "20 hours this week", "Набери 20 часов просмотра в текущей неделе.", "Accumulate 20 watch hours this week.", 16200, 20 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_streams_8", "8 стримов за неделю", "8 streams this week", "Открой 8 разных стримов за неделю.", "Open 8 different streams this week.", 4300, 8, QuestMetric.WeeklyStreamsCount, "count"),
            new("weekly_streams_12", "12 стримов за неделю", "12 streams this week", "Открой 12 разных стримов за неделю.", "Open 12 different streams this week.", 6500, 12, QuestMetric.WeeklyStreamsCount, "count"),
            new("weekly_streams_16", "16 стримов за неделю", "16 streams this week", "Открой 16 разных стримов за неделю.", "Open 16 different streams this week.", 9100, 16, QuestMetric.WeeklyStreamsCount, "count"),
            new("weekly_streams_20", "20 стримов за неделю", "20 streams this week", "Открой 20 разных стримов за неделю.", "Open 20 different streams this week.", 11800, 20, QuestMetric.WeeklyStreamsCount, "count"),
            new("weekly_streams_28", "28 стримов за неделю", "28 streams this week", "Открой 28 разных стримов за неделю.", "Open 28 different streams this week.", 15600, 28, QuestMetric.WeeklyStreamsCount, "count"),
            new("weekly_best_session_45m", "Сильная сессия", "Strong session", "Сделай сессию минимум 45 минут в течение недели.", "Complete a session of at least 45 minutes this week.", 5200, 45 * 60, QuestMetric.WeeklyBestSessionSeconds, "sec"),
            new("weekly_best_session_75m", "Мощная сессия", "Power session", "Сделай сессию минимум 75 минут в течение недели.", "Complete a session of at least 75 minutes this week.", 7900, 75 * 60, QuestMetric.WeeklyBestSessionSeconds, "sec"),
            new("weekly_best_session_105m", "Железная сессия", "Iron session", "Сделай сессию минимум 105 минут в течение недели.", "Complete a session of at least 105 minutes this week.", 11200, 105 * 60, QuestMetric.WeeklyBestSessionSeconds, "sec"),
            new("weekly_best_session_150m", "Легендарная сессия", "Legendary session", "Сделай сессию минимум 150 минут в течение недели.", "Complete a session of at least 150 minutes this week.", 14600, 150 * 60, QuestMetric.WeeklyBestSessionSeconds, "sec"),
            new("weekly_effect_sessions_5", "Эффект-марафон", "Effect marathon", "Проведи 5 сессий с эффектами за неделю.", "Complete 5 effect sessions this week.", 5800, 5, QuestMetric.WeeklyEffectSessionsCount, "count"),
            new("weekly_effect_sessions_8", "Эффект-шторм", "Effect storm", "Проведи 8 сессий с эффектами за неделю.", "Complete 8 effect sessions this week.", 8600, 8, QuestMetric.WeeklyEffectSessionsCount, "count"),
            new("weekly_effect_sessions_12", "Эффект-буря", "Effect tempest", "Проведи 12 сессий с эффектами за неделю.", "Complete 12 effect sessions this week.", 12200, 12, QuestMetric.WeeklyEffectSessionsCount, "count"),
            new("weekly_effect_sessions_16", "Эффект-хаос", "Effect chaos", "Проведи 16 сессий с эффектами за неделю.", "Complete 16 effect sessions this week.", 15900, 16, QuestMetric.WeeklyEffectSessionsCount, "count"),
            new("weekly_presets_3", "3 пресет-сессии", "3 preset sessions", "Примени пресеты в 3 сессиях на неделе.", "Use presets in 3 sessions this week.", 5200, 3, QuestMetric.WeeklyPresetSessionsCount, "count"),
            new("weekly_presets_5", "5 пресет-сессий", "5 preset sessions", "Примени пресеты в 5 сессиях на неделе.", "Use presets in 5 sessions this week.", 7100, 5, QuestMetric.WeeklyPresetSessionsCount, "count"),
            new("weekly_presets_8", "8 пресет-сессий", "8 preset sessions", "Примени пресеты в 8 сессиях на неделе.", "Use presets in 8 sessions this week.", 9800, 8, QuestMetric.WeeklyPresetSessionsCount, "count"),
            new("weekly_presets_12", "12 пресет-сессий", "12 preset sessions", "Примени пресеты в 12 сессиях на неделе.", "Use presets in 12 sessions this week.", 12900, 12, QuestMetric.WeeklyPresetSessionsCount, "count"),
            new("weekly_presets_16", "16 пресет-сессий", "16 preset sessions", "Примени пресеты в 16 сессиях на неделе.", "Use presets in 16 sessions this week.", 16300, 16, QuestMetric.WeeklyPresetSessionsCount, "count")
        ];

        private sealed class QuestState
        {
            public required string Id { get; init; }
            public required string ClaimKey { get; init; }
            public required string Title { get; init; }
            public required string Description { get; init; }
            public required int RewardXp { get; init; }
            public required int Progress { get; init; }
            public required int Target { get; init; }
            public required string Unit { get; init; }
            public required bool IsRewardClaimed { get; init; }
            public bool IsCompleted => Progress >= Target;
        }

        private void ApplySessionXpBonuses(int eligibleWatchSeconds)
        {
            if (eligibleWatchSeconds <= 0)
            {
                return;
            }

            var today = GetTrustedToday();
            _watchedSecondsByDay.TryGetValue(today, out var todayWatchSeconds);
            _watchedSecondsByDay[today] = todayWatchSeconds + eligibleWatchSeconds;

            _bestSessionSecondsByDay.TryGetValue(today, out var bestSessionSeconds);
            _bestSessionSecondsByDay[today] = Math.Max(bestSessionSeconds, eligibleWatchSeconds);
        }

        private bool TryClaimQuestReward(string claimKey)
        {
            if (string.IsNullOrWhiteSpace(claimKey) || _rewardedQuestKeys.Contains(claimKey))
            {
                return false;
            }

            var today = GetTrustedToday();
            var candidate = GetActiveDailyQuestStates(today)
                .Concat(GetActiveWeeklyQuestStates(today))
                .FirstOrDefault(x => string.Equals(x.ClaimKey, claimKey, StringComparison.Ordinal));
            if (candidate is null || !candidate.IsCompleted)
            {
                return false;
            }

            _rewardedQuestKeys.Add(claimKey);
            AddXp((int)Math.Round(candidate.RewardXp * QuestRewardXpMultiplier));
            return true;
        }

        private List<QuestState> GetActiveDailyQuestStates(DateOnly day)
        {
            EnsureQuestRotationSchedule(day);
            var byId = DailyQuestPool.ToDictionary(x => x.Id, StringComparer.Ordinal);
            return _activeDailyQuestIds
                .Where(byId.ContainsKey)
                .Select(id =>
                {
                    var template = byId[id];
                    var progress = ReadMetricProgress(template.Metric, day);
                    var claimKey = $"daily:{day:yyyy-MM-dd}:{template.Id}";
                    return new QuestState
                    {
                        Id = template.Id,
                        ClaimKey = claimKey,
                        Title = T(template.TitleRu, template.TitleEn),
                        Description = T(template.DescriptionRu, template.DescriptionEn),
                        RewardXp = template.RewardXp,
                        Progress = progress,
                        Target = template.Target,
                        Unit = template.Unit,
                        IsRewardClaimed = _rewardedQuestKeys.Contains(claimKey)
                    };
                })
                .ToList();
        }

        private List<QuestState> GetActiveWeeklyQuestStates(DateOnly day)
        {
            EnsureQuestRotationSchedule(day);
            var weekKey = GetIsoWeekKey(day);
            var byId = WeeklyQuestPool.ToDictionary(x => x.Id, StringComparer.Ordinal);
            return _activeWeeklyQuestIds
                .Where(byId.ContainsKey)
                .Select(id =>
                {
                    var template = byId[id];
                    var progress = ReadMetricProgress(template.Metric, day);
                    var claimKey = $"weekly:{weekKey}:{template.Id}";
                    return new QuestState
                    {
                        Id = template.Id,
                        ClaimKey = claimKey,
                        Title = T(template.TitleRu, template.TitleEn),
                        Description = T(template.DescriptionRu, template.DescriptionEn),
                        RewardXp = template.RewardXp,
                        Progress = progress,
                        Target = template.Target,
                        Unit = template.Unit,
                        IsRewardClaimed = _rewardedQuestKeys.Contains(claimKey)
                    };
                })
                .ToList();
        }

        private void EnsureQuestRotationSchedule(DateOnly day)
        {
            var changed = false;
            if (_activeDailyQuestDate != day || !IsValidQuestSelection(_activeDailyQuestIds, DailyQuestPool.Select(x => x.Id), ActiveDailyQuestCount))
            {
                _activeDailyQuestDate = day;
                _activeDailyQuestIds.Clear();
                _activeDailyQuestIds.AddRange(SelectQuestTemplates(DailyQuestPool, ActiveDailyQuestCount, day.DayNumber).Select(x => x.Id));
                changed = true;
            }

            var weekKey = GetIsoWeekKey(day);
            if (!string.Equals(_activeWeeklyQuestWeekKey, weekKey, StringComparison.Ordinal) ||
                !IsValidQuestSelection(_activeWeeklyQuestIds, WeeklyQuestPool.Select(x => x.Id), ActiveWeeklyQuestCount))
            {
                _activeWeeklyQuestWeekKey = weekKey;
                _activeWeeklyQuestIds.Clear();
                _activeWeeklyQuestIds.AddRange(SelectQuestTemplates(WeeklyQuestPool, ActiveWeeklyQuestCount, GetStableStringHash(weekKey)).Select(x => x.Id));
                changed = true;
            }

            if (changed)
            {
                _ = SaveHistoryAsync();
            }
        }

        private static bool IsValidQuestSelection(IReadOnlyList<string> selectedIds, IEnumerable<string> poolIds, int expectedCount)
        {
            var pool = new HashSet<string>(poolIds, StringComparer.Ordinal);
            if (selectedIds.Count != expectedCount)
            {
                return false;
            }

            return selectedIds.All(id => !string.IsNullOrWhiteSpace(id) && pool.Contains(id));
        }

        private static List<T> SelectQuestTemplates<T>(IReadOnlyList<T> pool, int count, int seed)
        {
            return pool
                .Select((item, index) => new { item, index })
                .OrderBy(x => GetStableQuestOrder(seed, x.index))
                .ThenBy(x => x.index)
                .Take(Math.Min(count, pool.Count))
                .Select(x => x.item)
                .ToList();
        }

        private static int GetStableQuestOrder(int seed, int index)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= (uint)(index + 1) * 0x9E3779B9u;
                value ^= value >> 16;
                value *= 0x85EBCA6Bu;
                value ^= value >> 13;
                value *= 0xC2B2AE35u;
                value ^= value >> 16;
                return (int)value;
            }
        }

        private static int GetStableStringHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in value)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }

                return (int)hash;
            }
        }

        private int ReadMetricProgress(QuestMetric metric, DateOnly day)
        {
            return metric switch
            {
                QuestMetric.DailyWatchSeconds => GetWatchedSecondsForDay(day),
                QuestMetric.DailyStreamsCount => GetWatchedStreamsCountForDay(day),
                QuestMetric.DailyBestSessionSeconds => GetBestSessionSecondsForDay(day),
                QuestMetric.DailyEffectSessionsCount => GetEffectSessionsForDay(day),
                QuestMetric.DailyPresetSessionsCount => GetPresetSessionsForDay(day),
                QuestMetric.WeeklyWatchSeconds => GetWatchedSecondsForWeek(day),
                QuestMetric.WeeklyWatchDaysCount => GetWatchedDaysCountForWeek(day),
                QuestMetric.WeeklyStreamsCount => GetWatchedStreamsCountForWeek(day),
                QuestMetric.WeeklyBestSessionSeconds => GetBestSessionSecondsForWeek(day),
                QuestMetric.WeeklyEffectSessionsCount => GetEffectSessionsForWeek(day),
                QuestMetric.WeeklyPresetSessionsCount => GetPresetSessionsForWeek(day),
                _ => 0
            };
        }

        private int GetWatchedStreamsCountForDay(DateOnly day)
        {
            var localDate = day.ToDateTime(TimeOnly.MinValue).Date;
            return _watchHistory.Values.Count(v => v.ToLocalTime().Date == localDate);
        }

        private int GetWatchedSecondsForDay(DateOnly day)
        {
            return _watchedSecondsByDay.TryGetValue(day, out var value) ? Math.Max(0, value) : 0;
        }

        private int GetBestSessionSecondsForDay(DateOnly day)
        {
            return _bestSessionSecondsByDay.TryGetValue(day, out var value) ? Math.Max(0, value) : 0;
        }

        private int GetEffectSessionsForDay(DateOnly day)
        {
            return _effectSessionsByDay.TryGetValue(day, out var value) ? Math.Max(0, value) : 0;
        }

        private int GetPresetSessionsForDay(DateOnly day)
        {
            return _presetSessionsByDay.TryGetValue(day, out var value) ? Math.Max(0, value) : 0;
        }

        private int GetWatchedDaysCountForWeek(DateOnly anchorDay)
        {
            var weekKey = GetIsoWeekKey(anchorDay);
            return _watchedDays.Count(day => string.Equals(GetIsoWeekKey(day), weekKey, StringComparison.Ordinal));
        }

        private int GetWatchedStreamsCountForWeek(DateOnly anchorDay)
        {
            var weekKey = GetIsoWeekKey(anchorDay);
            return _watchHistory.Values.Count(dt =>
            {
                var localDay = DateOnly.FromDateTime(dt.ToLocalTime().Date);
                return string.Equals(GetIsoWeekKey(localDay), weekKey, StringComparison.Ordinal);
            });
        }

        private int GetWatchedSecondsForWeek(DateOnly anchorDay)
        {
            var weekKey = GetIsoWeekKey(anchorDay);
            return _watchedSecondsByDay
                .Where(pair => string.Equals(GetIsoWeekKey(pair.Key), weekKey, StringComparison.Ordinal))
                .Sum(pair => Math.Max(0, pair.Value));
        }

        private int GetBestSessionSecondsForWeek(DateOnly anchorDay)
        {
            var weekKey = GetIsoWeekKey(anchorDay);
            return _bestSessionSecondsByDay
                .Where(pair => string.Equals(GetIsoWeekKey(pair.Key), weekKey, StringComparison.Ordinal))
                .Select(pair => Math.Max(0, pair.Value))
                .DefaultIfEmpty(0)
                .Max();
        }

        private int GetEffectSessionsForWeek(DateOnly anchorDay)
        {
            var weekKey = GetIsoWeekKey(anchorDay);
            return _effectSessionsByDay
                .Where(pair => string.Equals(GetIsoWeekKey(pair.Key), weekKey, StringComparison.Ordinal))
                .Sum(pair => Math.Max(0, pair.Value));
        }

        private int GetPresetSessionsForWeek(DateOnly anchorDay)
        {
            var weekKey = GetIsoWeekKey(anchorDay);
            return _presetSessionsByDay
                .Where(pair => string.Equals(GetIsoWeekKey(pair.Key), weekKey, StringComparison.Ordinal))
                .Sum(pair => Math.Max(0, pair.Value));
        }

        private static string GetIsoWeekKey(DateOnly day)
        {
            var dt = day.ToDateTime(TimeOnly.MinValue);
            var year = ISOWeek.GetYear(dt);
            var week = ISOWeek.GetWeekOfYear(dt);
            return $"{year}-W{week:00}";
        }
    }
}
