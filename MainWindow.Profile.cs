using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private sealed class ProfileAchievement
        {
            public required string Title { get; init; }
            public required string Description { get; init; }
            public bool IsUnlocked { get; init; }
        }

        private sealed class ProfileQuest
        {
            public required string ClaimKey { get; init; }
            public required string Title { get; init; }
            public required string Description { get; init; }
            public required string StatusText { get; init; }
            public required string ProgressText { get; init; }
            public required string RewardText { get; init; }
            public required string ClaimButtonText { get; init; }
            public required string StatusGlyph { get; init; }
            public required Brush StatusColor { get; init; }
            public bool IsClaimVisible { get; init; }
            public bool IsClaimEnabled { get; init; }
        }

        private void UpdateStreakText()
        {
            var streak = CalculateWatchStreakDays();
            ProfileStreakText.Text = T($"Серия: {streak} дн.", $"Streak: {streak} days");
        }

        private int CalculateWatchStreakDays()
        {
            if (_watchedDays.Count == 0)
            {
                return 0;
            }

            var allDays = _watchedDays.OrderByDescending(d => d).ToList();
            var latest = allDays[0];
            var streak = 1;
            for (var i = 1; i < allDays.Count; i++)
            {
                var expected = latest.AddDays(-1);
                if (allDays[i] != expected)
                {
                    break;
                }

                streak++;
                latest = allDays[i];
            }

            return streak;
        }

        private void RefreshProfile()
        {
            var streakDays = CalculateWatchStreakDays();
            AchievementsList.ItemsSource = BuildAchievements(streakDays);

            var recent = _allVideos
                .Where(v => v.LastViewedAtUtc.HasValue)
                .OrderByDescending(v => v.LastViewedAtUtc)
                .Take(MaxRecentStreamsInProfile)
                .ToList();
            RecentStreamsList.ItemsSource = recent;

            var prestigeXpCap = TotalXpForLevel(MaxLevel);
            _prestigeXp = Math.Clamp(_prestigeXp, 0, prestigeXpCap);
            var level = CalculateLevelFromXp(_prestigeXp);
            var prev = TotalXpForLevel(level);
            var next = level >= MaxLevel ? prev : TotalXpForLevel(level + 1);
            var currentInLevel = _prestigeXp - prev;
            var requiredInLevel = Math.Max(1, next - prev);
            var clampedCurrentLevelXp = Math.Clamp(currentInLevel, 0, requiredInLevel);
            var xpToNextLevel = Math.Max(0, requiredInLevel - clampedCurrentLevelXp);

            PrestigeValueText.Text = _prestige.ToString();
            ApplyPrestigeIcon();
            ProfileLevelText.Text = T($"Уровень: {level}/{MaxLevel}", $"Level: {level}/{MaxLevel}");
            ProfileXpText.Text = T($"XP до следующего уровня: {xpToNextLevel}", $"XP to next level: {xpToNextLevel}");
            var today = GetTrustedToday();
            _watchedSecondsByDay.TryGetValue(today, out var todayWatchSeconds);
            var todayGoalPercent = Math.Min(100, (int)Math.Round((todayWatchSeconds / (double)(45 * 60)) * 100));
            var localNow = GetTrustedLocalNow();
            ProfileTodayText.Text = T(
                $"Сегодня: {localNow:yyyy-MM-dd} • цель дня {todayGoalPercent}%",
                $"Today: {localNow:yyyy-MM-dd} • daily goal {todayGoalPercent}%");
            var totalWatchedSeconds = Math.Max(0, _watchedSecondsByDay.Values.Sum(v => Math.Max(0, v)));
            var totalHours = totalWatchedSeconds / 3600.0;
            ProfileHoursText.Text = T(
                $"Время в программе: {totalHours:0.0} ч ({totalWatchedSeconds / 60} мин)",
                $"Time in app: {totalHours:0.0} h ({totalWatchedSeconds / 60} min)");
            ProfileXpProgress.Maximum = requiredInLevel;
            ProfileXpProgress.Value = clampedCurrentLevelXp;
            PrestigeButton.IsEnabled = level >= MaxLevel && _prestige < MaxPrestige;
        }

        private List<ProfileAchievement> BuildAchievements(int streakDays)
        {
            var totalWatchedSeconds = Math.Max(0, _watchedSecondsByDay.Values.Sum(v => Math.Max(0, v)));
            var totalWatchedHours = totalWatchedSeconds / 3600.0;
            var claimedQuestsCount = _rewardedQuestKeys.Count;

            return
            [
                BuildAchievement(T("Первый просмотр", "First watch"), T("Открой любой стрим хотя бы один раз.", "Open any stream at least once."), _watchHistory.Count >= 1),
                BuildAchievement(T("10 стримов", "10 streams"), T("Посмотри 10 разных стримов.", "Watch ten different streams."), _watchHistory.Count >= 10),
                BuildAchievement(T("25 стримов", "25 streams"), T("Посмотри 25 разных стримов.", "Watch 25 different streams."), _watchHistory.Count >= 25),
                BuildAchievement(T("50 стримов", "50 streams"), T("Посмотри 50 разных стримов.", "Watch 50 different streams."), _watchHistory.Count >= 50),
                BuildAchievement(T("100 стримов", "100 streams"), T("Посмотри 100 разных стримов.", "Watch 100 different streams."), _watchHistory.Count >= 100),
                BuildAchievement(T("150 стримов", "150 streams"), T("Посмотри 150 разных стримов.", "Watch 150 different streams."), _watchHistory.Count >= 150),
                BuildAchievement(T("Серия 3 дня", "3-day streak"), T("Смотри стримы 3 дня подряд.", "Watch streams 3 days in a row."), streakDays >= 3),
                BuildAchievement(T("Серия 7 дней", "7-day streak"), T("Смотри стримы 7 дней подряд.", "Watch streams 7 days in a row."), streakDays >= 7),
                BuildAchievement(T("Серия 14 дней", "14-day streak"), T("Смотри стримы 14 дней подряд.", "Watch streams 14 days in a row."), streakDays >= 14),
                BuildAchievement(T("Серия 30 дней", "30-day streak"), T("Смотри стримы 30 дней подряд.", "Watch streams 30 days in a row."), streakDays >= 30),
                BuildAchievement(T("Серия 60 дней", "60-day streak"), T("Смотри стримы 60 дней подряд.", "Watch streams 60 days in a row."), streakDays >= 60),
                BuildAchievement(T("Серия 100 дней", "100-day streak"), T("Смотри стримы 100 дней подряд.", "Watch streams 100 days in a row."), streakDays >= 100),
                BuildAchievement(T("Избранное x5", "Favorites x5"), T("Добавь 5 стримов в избранное.", "Add five streams to favorites."), _favoriteVideoIds.Count >= 5),
                BuildAchievement(T("Избранное x15", "Favorites x15"), T("Добавь 15 стримов в избранное.", "Add 15 streams to favorites."), _favoriteVideoIds.Count >= 15),
                BuildAchievement(T("Избранное x30", "Favorites x30"), T("Добавь 30 стримов в избранное.", "Add 30 streams to favorites."), _favoriteVideoIds.Count >= 30),
                BuildAchievement(T("Избранное x50", "Favorites x50"), T("Добавь 50 стримов в избранное.", "Add 50 streams to favorites."), _favoriteVideoIds.Count >= 50),
                BuildAchievement(T("Эффекты x3", "Effects x3"), T("Сделай 3 сессии с любыми эффектами.", "Watch 3 sessions with any effects."), _effectSessionsAny >= 3),
                BuildAchievement(T("Эффекты x15", "Effects x15"), T("Сделай 15 сессий с включенными эффектами.", "Watch 15 sessions with enabled effects."), _effectSessionsAny >= 15),
                BuildAchievement(T("Эффекты x50", "Effects x50"), T("Сделай 50 сессий с включенными эффектами.", "Watch 50 sessions with enabled effects."), _effectSessionsAny >= 50),
                BuildAchievement(T("Эффекты x100", "Effects x100"), T("Сделай 100 сессий с включенными эффектами.", "Watch 100 sessions with enabled effects."), _effectSessionsAny >= 100),
                BuildAchievement(T("Эффекты x250", "Effects x250"), T("Сделай 250 сессий с включенными эффектами.", "Watch 250 sessions with enabled effects."), _effectSessionsAny >= 250),
                BuildAchievement(T("5 эффектов разом", "5 effects at once"), T("Сделай 5 сессий с 5+ эффектами.", "Do 5 sessions with 5+ effects enabled."), _effectSessionsFivePlus >= 5),
                BuildAchievement(T("10 эффектов разом", "10 effects at once"), T("Сделай 3 сессии с 10+ эффектами.", "Do 3 sessions with 10+ effects enabled."), _effectSessionsTenPlus >= 3),
                BuildAchievement(T("10 эффектов x12", "10 effects x12"), T("Сделай 12 сессий с 10+ эффектами.", "Do 12 sessions with 10+ effects enabled."), _effectSessionsTenPlus >= 12),
                BuildAchievement(T("Сильное размытие", "Heavy blur"), T("Сделай 3 сессии с сильным размытием (75%+).", "Watch 3 sessions with strong blur (75%+)."), _effectSessionsStrongBlur >= 3),
                BuildAchievement(T("Размытие x10", "Heavy blur x10"), T("Сделай 10 сессий с сильным размытием (75%+).", "Watch 10 sessions with strong blur (75%+)."), _effectSessionsStrongBlur >= 10),
                BuildAchievement(T("Сильный фишай", "Heavy fisheye"), T("Сделай 3 сессии с силой фишая от 75% по модулю.", "Watch 3 sessions with fisheye strength of at least 75% in either direction."), _effectSessionsStrongRedGlow >= 3),
                BuildAchievement(T("Фишай x10", "Heavy fisheye x10"), T("Сделай 10 сессий с сильным фишаем (75%+).", "Watch 10 sessions with heavy fisheye (75%+)."), _effectSessionsStrongRedGlow >= 10),
                BuildAchievement(T("Сильные JPEG-помехи", "Heavy JPEG damage"), T("Сделай 3 сессии с сильными JPEG-помехами (75%+).", "Watch 3 sessions with heavy JPEG damage (75%+)."), _effectSessionsStrongVioletGlow >= 3),
                BuildAchievement(T("JPEG-помехи x10", "Heavy JPEG x10"), T("Сделай 10 сессий с сильными JPEG-помехами (75%+).", "Watch 10 sessions with heavy JPEG damage (75%+)."), _effectSessionsStrongVioletGlow >= 10),
                BuildAchievement(T("Сильная тряска", "Heavy shake"), T("Сделай 3 сессии с сильной тряской (75%+).", "Watch 3 sessions with strong shake (75%+)."), _effectSessionsStrongShake >= 3),
                BuildAchievement(T("Тряска x10", "Heavy shake x10"), T("Сделай 10 сессий с сильной тряской (75%+).", "Watch 10 sessions with strong shake (75%+)."), _effectSessionsStrongShake >= 10),
                BuildAchievement(T("Пресет: старт", "Preset: first run"), T("Посмотри стрим, используя любой пресет эффектов.", "Watch a stream using any effect preset."), _effectPresetSessionsAny >= 1),
                BuildAchievement(T("Пресеты x10", "Presets x10"), T("Сделай 10 сессий с применением пресетов.", "Do 10 sessions with presets applied."), _effectPresetSessionsAny >= 10),
                BuildAchievement(T("Пресеты x25", "Presets x25"), T("Сделай 25 сессий с применением пресетов.", "Do 25 sessions with presets applied."), _effectPresetSessionsAny >= 25),
                BuildAchievement(T("Пресеты x50", "Presets x50"), T("Сделай 50 сессий с применением пресетов.", "Do 50 sessions with presets applied."), _effectPresetSessionsAny >= 50),
                BuildAchievement(T("Пресеты x100", "Presets x100"), T("Сделай 100 сессий с применением пресетов.", "Do 100 sessions with presets applied."), _effectPresetSessionsAny >= 100),
                BuildAchievement(T("Свой стиль", "Custom style"), T("Сделай 3 сессии с кастомным пресетом.", "Do 3 sessions with a custom preset."), _effectPresetSessionsCustom >= 3),
                BuildAchievement(T("Свой стиль x10", "Custom style x10"), T("Сделай 10 сессий с кастомным пресетом.", "Do 10 sessions with a custom preset."), _effectPresetSessionsCustom >= 10),
                BuildAchievement(T("Свой стиль x25", "Custom style x25"), T("Сделай 25 сессий с кастомным пресетом.", "Do 25 sessions with a custom preset."), _effectPresetSessionsCustom >= 25),
                BuildAchievement(T("Ретро-удар", "Retro hit"), T("Сделай 3 сессии с пресетом Retro/VHS.", "Do 3 sessions with the Retro/VHS preset."), _effectPresetSessionsRetro >= 3),
                BuildAchievement(T("Ретро-удар x10", "Retro hit x10"), T("Сделай 10 сессий с пресетом Retro/VHS.", "Do 10 sessions with the Retro/VHS preset."), _effectPresetSessionsRetro >= 10),
                BuildAchievement(T("Хаос-картинка", "Chaos vision"), T("Сделай 3 сессии с пресетом Chaos.", "Do 3 sessions with the Chaos preset."), _effectPresetSessionsChaos >= 3),
                BuildAchievement(T("Хаос-картинка x10", "Chaos vision x10"), T("Сделай 10 сессий с пресетом Chaos.", "Do 10 sessions with the Chaos preset."), _effectPresetSessionsChaos >= 10),
                BuildAchievement(T("Сонный режим", "Dream mode"), T("Сделай 3 сессии с пресетом Dream.", "Do 3 sessions with the Dream preset."), _effectPresetSessionsDream >= 3),
                BuildAchievement(T("Сонный режим x10", "Dream mode x10"), T("Сделай 10 сессий с пресетом Dream.", "Do 10 sessions with the Dream preset."), _effectPresetSessionsDream >= 10),
                BuildAchievement(T("10 часов в приложении", "10 hours in app"), T("Проведи в просмотре суммарно 10 часов.", "Accumulate 10 hours of watch time."), totalWatchedHours >= 10),
                BuildAchievement(T("25 часов в приложении", "25 hours in app"), T("Проведи в просмотре суммарно 25 часов.", "Accumulate 25 hours of watch time."), totalWatchedHours >= 25),
                BuildAchievement(T("50 часов в приложении", "50 hours in app"), T("Проведи в просмотре суммарно 50 часов.", "Accumulate 50 hours of watch time."), totalWatchedHours >= 50),
                BuildAchievement(T("100 часов в приложении", "100 hours in app"), T("Проведи в просмотре суммарно 100 часов.", "Accumulate 100 hours of watch time."), totalWatchedHours >= 100),
                BuildAchievement(T("250 часов в приложении", "250 hours in app"), T("Проведи в просмотре суммарно 250 часов.", "Accumulate 250 hours of watch time."), totalWatchedHours >= 250),
                BuildAchievement(T("Охотник за XP", "XP hunter"), T("Набери 500 000 общего XP.", "Reach 500,000 total XP."), _totalXp >= 500_000),
                BuildAchievement(T("Миллион XP", "1M XP"), T("Набери 1 000 000 общего XP.", "Reach 1,000,000 total XP."), _totalXp >= 1_000_000),
                BuildAchievement(T("Квестоман", "Quest runner"), T("Забери награды за 10 заданий.", "Claim rewards from 10 quests."), claimedQuestsCount >= 10),
                BuildAchievement(T("Квестоман x50", "Quest runner x50"), T("Забери награды за 50 заданий.", "Claim rewards from 50 quests."), claimedQuestsCount >= 50),
                BuildAchievement(T("Квестоман x100", "Quest runner x100"), T("Забери награды за 100 заданий.", "Claim rewards from 100 quests."), claimedQuestsCount >= 100),
                BuildAchievement(T("Путь престижа", "Prestige path"), T("Достигни 1 престижа.", "Reach prestige 1."), _prestige >= 1),
                BuildAchievement(T("Ветеран престижа", "Prestige veteran"), T("Достигни 5 престижа.", "Reach prestige 5."), _prestige >= 5),
                BuildAchievement(T("Легенда престижа", "Prestige legend"), T("Достигни 10 престижа.", "Reach prestige 10."), _prestige >= 10),
                BuildAchievement(T("100k XP", "100k XP"), T("Набери 100 000 общего XP.", "Reach 100,000 total XP."), _totalXp >= 100_000),
                BuildAchievement(T("250k XP", "250k XP"), T("Набери 250 000 общего XP.", "Reach 250,000 total XP."), _totalXp >= 250_000),
                BuildAchievement(T("Мастер проклятия", "Cursed master"), T("Досмотри полный стрим с 15 проклятыми эффектами.", "Finish a full stream with all 15 cursed effects."), _unlockedAchievements.Contains(AchievementCursed15))
            ];
        }

        private List<ProfileQuest> BuildDailyTasks()
        {
            var today = GetTrustedToday();
            return GetActiveDailyQuestStates(today)
                .Select(BuildQuestFromState)
                .ToList();
        }

        private List<ProfileQuest> BuildWeeklyTasks()
        {
            var today = GetTrustedToday();
            return GetActiveWeeklyQuestStates(today)
                .Select(BuildQuestFromState)
                .ToList();
        }

        private ProfileQuest BuildQuestFromState(QuestState state)
        {
            var progressText = state.Unit switch
            {
                "sec" => T(
                    $"Прогресс: {Math.Min(state.Progress, state.Target) / 60}/{Math.Max(1, state.Target / 60)} мин",
                    $"Progress: {Math.Min(state.Progress, state.Target) / 60}/{Math.Max(1, state.Target / 60)} min"),
                _ => T(
                    $"Прогресс: {Math.Min(state.Progress, state.Target)}/{state.Target}",
                    $"Progress: {Math.Min(state.Progress, state.Target)}/{state.Target}")
            };

            var statusColor = state.IsRewardClaimed
                ? BrushFromHex("#8BE6B4")
                : state.IsCompleted
                    ? BrushFromHex("#F0D186")
                    : BrushFromHex("#8FB7CE");
            var statusGlyph = state.IsRewardClaimed ? "\u2713" : state.IsCompleted ? "\u2605" : "\u25B8";
            var statusText = state.IsRewardClaimed
                ? T("Награда уже получена", "Reward already claimed")
                : state.IsCompleted
                    ? T("Задание выполнено - нажмите \"Забрать XP\"", "Quest completed - click \"Claim XP\"")
                    : T("В процессе выполнения", "In progress");
            var rewardText = state.IsRewardClaimed
                ? T($"Награда получена: +{state.RewardXp} XP", $"Reward claimed: +{state.RewardXp} XP")
                : T($"Награда: +{state.RewardXp} XP", $"Reward: +{state.RewardXp} XP");
            var claimEnabled = state.IsCompleted && !state.IsRewardClaimed;
            var claimButtonText = state.IsRewardClaimed
                ? T("Получено", "Claimed")
                : claimEnabled
                    ? T("Забрать XP", "Claim XP")
                    : T("Не выполнено", "Not completed");

            return new ProfileQuest
            {
                ClaimKey = state.ClaimKey,
                Title = state.Title,
                Description = state.Description,
                StatusText = statusText,
                ProgressText = progressText,
                RewardText = rewardText,
                ClaimButtonText = claimButtonText,
                StatusGlyph = statusGlyph,
                StatusColor = statusColor,
                IsClaimVisible = true,
                IsClaimEnabled = claimEnabled
            };
        }

        private void RefreshHomeSummary()
        {
            var watchedCount = _allVideos.Count(v => v.LastViewedAtUtc.HasValue);
            var totalCount = _allVideos.Count;
            var favoritesCount = _favoriteVideoIds.Count;
            var unwatchedCount = Math.Max(0, totalCount - watchedCount);

            HomeStatsText.Text = T(
                $"Стримы: {totalCount} • Просмотрено: {watchedCount} • Избранное: {favoritesCount} • Непросмотрено: {unwatchedCount}",
                $"Streams: {totalCount} • Watched: {watchedCount} • Favorites: {favoritesCount} • Unwatched: {unwatchedCount}");
            HomeHintText.Text = T(
                "Совет: открой рекомендации слева, чтобы быстрее находить похожие стримы.",
                "Tip: open recommendations on the left to find similar streams faster.");
        }

        private static ProfileAchievement BuildAchievement(string title, string description, bool unlocked)
        {
            return new ProfileAchievement
            {
                Title = title,
                Description = description,
                IsUnlocked = unlocked
            };
        }

        private void ApplyPrestigeIcon()
        {
            var iconUri = ResolvePrestigeIconUri(_prestige);
            try
            {
                if (iconUri is not null)
                {
                    var streamInfo = Application.GetResourceStream(iconUri);
                    if (streamInfo?.Stream is not null)
                    {
                        using var resourceStream = streamInfo.Stream;
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = resourceStream;
                        image.EndInit();
                        image.Freeze();
                        PrestigeIconImage.Source = image;
                        return;
                    }
                }

                PrestigeIconImage.Source = CreateGeneratedPrestigeIcon(_prestige);
            }
            catch
            {
                PrestigeIconImage.Source = CreateGeneratedPrestigeIcon(_prestige);
            }
        }

        private static Uri? ResolvePrestigeIconUri(int prestige)
        {
            var stage = Math.Clamp(prestige / 10, 0, 10);
            for (var distance = 0; distance <= 10; distance++)
            {
                var lowerStage = stage - distance;
                if (lowerStage >= 0)
                {
                    var lowerUri = new Uri($"pack://application:,,,/Assets/PrestigeIcons/prestige_{lowerStage}.png", UriKind.Absolute);
                    if (HasResource(lowerUri))
                    {
                        return lowerUri;
                    }
                }

                if (distance == 0)
                {
                    continue;
                }

                var upperStage = stage + distance;
                if (upperStage <= 10)
                {
                    var upperUri = new Uri($"pack://application:,,,/Assets/PrestigeIcons/prestige_{upperStage}.png", UriKind.Absolute);
                    if (HasResource(upperUri))
                    {
                        return upperUri;
                    }
                }
            }

            return null;
        }

        private static BitmapSource CreateGeneratedPrestigeIcon(int prestige)
        {
            const int size = 172;
            var stage = Math.Clamp(prestige / 10, 0, 10);
            var colorShift = stage / 10.0;
            var outer = Color.FromRgb(
                (byte)(72 + colorShift * 130),
                (byte)(102 + colorShift * 80),
                (byte)(140 + colorShift * 55));
            var inner = Color.FromRgb(
                (byte)(30 + colorShift * 60),
                (byte)(58 + colorShift * 55),
                (byte)(92 + colorShift * 50));

            var visual = new DrawingVisual();
            using (var drawingContext = visual.RenderOpen())
            {
                drawingContext.DrawEllipse(
                    new RadialGradientBrush(inner, outer),
                    new Pen(new SolidColorBrush(Color.FromRgb(195, 226, 247)), 6),
                    new Point(size / 2.0, size / 2.0),
                    size * 0.45,
                    size * 0.45);

                drawingContext.DrawEllipse(
                    Brushes.Transparent,
                    new Pen(new SolidColorBrush(Color.FromArgb(175, 255, 255, 255)), 2),
                    new Point(size / 2.0, size / 2.0),
                    size * 0.33,
                    size * 0.33);
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static bool HasResource(Uri uri)
        {
            try
            {
                var streamInfo = Application.GetResourceStream(uri);
                if (streamInfo?.Stream is null)
                {
                    return false;
                }

                using (streamInfo.Stream)
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static int XpToReachNextLevel(int level)
        {
            var n = Math.Max(0, level - 1);
            return 80 + (int)Math.Round(n * 1.2 + n * n * 0.015);
        }

        private static int TotalXpForLevel(int level)
        {
            if (level <= 1)
            {
                return 0;
            }

            var total = 0;
            for (var i = 1; i < level; i++)
            {
                total += XpToReachNextLevel(i);
            }

            return total;
        }

        private static int CalculateLevelFromXp(int xp)
        {
            var level = 1;
            var accumulated = 0;
            while (level < MaxLevel)
            {
                var need = XpToReachNextLevel(level);
                if (xp < accumulated + need)
                {
                    break;
                }

                accumulated += need;
                level++;
            }

            return level;
        }

        private void AddXp(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            _totalXp += amount;
            var prestigeXpCap = TotalXpForLevel(MaxLevel);
            _prestigeXp = Math.Min(prestigeXpCap, _prestigeXp + amount);
        }
    }
}


