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
            WeeklyWatchSeconds,
            WeeklyWatchDaysCount,
            WeeklyStreamsCount,
            WeeklyBestSessionSeconds,
            WeeklyEffectSessionsCount
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
        private readonly HashSet<string> _rewardedQuestKeys = new(StringComparer.Ordinal);

        private static readonly DailyQuestTemplate[] DailyQuestPool =
        [
            new("daily_watch_30m", "30 минут просмотра", "30 minutes watch", "Набери 30 минут просмотра за день.", "Reach 30 minutes of watch time today.", 850, 30 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_45m", "45 минут просмотра", "45 minutes watch", "Набери 45 минут просмотра за день.", "Reach 45 minutes of watch time today.", 1100, 45 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_60m", "60 минут просмотра", "60 minutes watch", "Набери 60 минут просмотра за день.", "Reach 60 minutes of watch time today.", 1450, 60 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_90m", "90 минут просмотра", "90 minutes watch", "Набери 90 минут просмотра за день.", "Reach 90 minutes of watch time today.", 2200, 90 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_watch_120m", "2 часа просмотра", "2 hours watch", "Набери 2 часа просмотра за день.", "Reach 2 hours of watch time today.", 2900, 120 * 60, QuestMetric.DailyWatchSeconds, "sec"),
            new("daily_streams_2", "2 стрима в день", "2 streams a day", "Открой два разных стрима сегодня.", "Open two different streams today.", 820, 2, QuestMetric.DailyStreamsCount, "count"),
            new("daily_streams_3", "3 стрима в день", "3 streams a day", "Открой три разных стрима сегодня.", "Open three different streams today.", 1180, 3, QuestMetric.DailyStreamsCount, "count"),
            new("daily_streams_4", "4 стрима в день", "4 streams a day", "Открой четыре разных стрима сегодня.", "Open four different streams today.", 1540, 4, QuestMetric.DailyStreamsCount, "count"),
            new("daily_streams_5", "5 стримов в день", "5 streams a day", "Открой пять разных стримов сегодня.", "Open five different streams today.", 2050, 5, QuestMetric.DailyStreamsCount, "count"),
            new("daily_session_15m", "Сессия 15 минут", "15-minute session", "Сделай одну сессию минимум 15 минут.", "Complete one session of at least 15 minutes.", 980, 15 * 60, QuestMetric.DailyBestSessionSeconds, "sec"),
            new("daily_session_25m", "Сессия 25 минут", "25-minute session", "Сделай одну сессию минимум 25 минут.", "Complete one session of at least 25 minutes.", 1500, 25 * 60, QuestMetric.DailyBestSessionSeconds, "sec"),
            new("daily_session_35m", "Сессия 35 минут", "35-minute session", "Сделай одну сессию минимум 35 минут.", "Complete one session of at least 35 minutes.", 2100, 35 * 60, QuestMetric.DailyBestSessionSeconds, "sec"),
            new("daily_effects_1", "Эффект-сессия", "Effect session", "Проведи хотя бы 1 сессию с эффектами.", "Complete at least 1 session with effects.", 1200, 1, QuestMetric.DailyEffectSessionsCount, "count"),
            new("daily_effects_2", "2 эффект-сессии", "2 effect sessions", "Проведи 2 сессии с эффектами.", "Complete 2 sessions with effects.", 1750, 2, QuestMetric.DailyEffectSessionsCount, "count"),
            new("daily_effects_3", "3 эффект-сессии", "3 effect sessions", "Проведи 3 сессии с эффектами.", "Complete 3 sessions with effects.", 2450, 3, QuestMetric.DailyEffectSessionsCount, "count")
        ];

        private static readonly WeeklyQuestTemplate[] WeeklyQuestPool =
        [
            new("weekly_days_3", "3 активных дня", "3 active days", "Смотри стримы минимум в 3 дня этой недели.", "Watch streams on at least 3 days this week.", 2900, 3, QuestMetric.WeeklyWatchDaysCount, "count"),
            new("weekly_days_4", "4 активных дня", "4 active days", "Смотри стримы минимум в 4 дня этой недели.", "Watch streams on at least 4 days this week.", 3600, 4, QuestMetric.WeeklyWatchDaysCount, "count"),
            new("weekly_days_5", "5 активных дней", "5 active days", "Смотри стримы минимум в 5 днях этой недели.", "Watch streams on at least 5 days this week.", 4700, 5, QuestMetric.WeeklyWatchDaysCount, "count"),
            new("weekly_days_6", "6 активных дней", "6 active days", "Смотри стримы минимум в 6 днях этой недели.", "Watch streams on at least 6 days this week.", 6200, 6, QuestMetric.WeeklyWatchDaysCount, "count"),
            new("weekly_watch_4h", "4 часа за неделю", "4 hours this week", "Набери 4 часа просмотра в текущей неделе.", "Accumulate 4 watch hours this week.", 3900, 4 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_watch_6h", "6 часов за неделю", "6 hours this week", "Набери 6 часов просмотра в текущей неделе.", "Accumulate 6 watch hours this week.", 5600, 6 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_watch_8h", "8 часов за неделю", "8 hours this week", "Набери 8 часов просмотра в текущей неделе.", "Accumulate 8 watch hours this week.", 7600, 8 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_watch_12h", "12 часов за неделю", "12 hours this week", "Набери 12 часов просмотра в текущей неделе.", "Accumulate 12 watch hours this week.", 9800, 12 * 60 * 60, QuestMetric.WeeklyWatchSeconds, "sec"),
            new("weekly_streams_8", "8 стримов за неделю", "8 streams this week", "Открой 8 разных стримов за неделю.", "Open 8 different streams this week.", 4300, 8, QuestMetric.WeeklyStreamsCount, "count"),
            new("weekly_streams_12", "12 стримов за неделю", "12 streams this week", "Открой 12 разных стримов за неделю.", "Open 12 different streams this week.", 6500, 12, QuestMetric.WeeklyStreamsCount, "count"),
            new("weekly_streams_16", "16 стримов за неделю", "16 streams this week", "Открой 16 разных стримов за неделю.", "Open 16 different streams this week.", 9100, 16, QuestMetric.WeeklyStreamsCount, "count"),
            new("weekly_best_session_45m", "Сильная сессия", "Strong session", "Сделай сессию минимум 45 минут в течение недели.", "Complete a session of at least 45 minutes this week.", 5200, 45 * 60, QuestMetric.WeeklyBestSessionSeconds, "sec"),
            new("weekly_best_session_75m", "Мощная сессия", "Power session", "Сделай сессию минимум 75 минут в течение недели.", "Complete a session of at least 75 minutes this week.", 7900, 75 * 60, QuestMetric.WeeklyBestSessionSeconds, "sec"),
            new("weekly_effect_sessions_5", "Эффект-марафон", "Effect marathon", "Проведи 5 сессий с эффектами за неделю.", "Complete 5 effect sessions this week.", 5800, 5, QuestMetric.WeeklyEffectSessionsCount, "count"),
            new("weekly_effect_sessions_8", "Эффект-шторм", "Effect storm", "Проведи 8 сессий с эффектами за неделю.", "Complete 8 effect sessions this week.", 8600, 8, QuestMetric.WeeklyEffectSessionsCount, "count")
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
                QuestMetric.WeeklyWatchSeconds => GetWatchedSecondsForWeek(day),
                QuestMetric.WeeklyWatchDaysCount => GetWatchedDaysCountForWeek(day),
                QuestMetric.WeeklyStreamsCount => GetWatchedStreamsCountForWeek(day),
                QuestMetric.WeeklyBestSessionSeconds => GetBestSessionSecondsForWeek(day),
                QuestMetric.WeeklyEffectSessionsCount => GetEffectSessionsForWeek(day),
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

        private static string GetIsoWeekKey(DateOnly day)
        {
            var dt = day.ToDateTime(TimeOnly.MinValue);
            var year = ISOWeek.GetYear(dt);
            var week = ISOWeek.GetWeekOfYear(dt);
            return $"{year}-W{week:00}";
        }
    }
}
