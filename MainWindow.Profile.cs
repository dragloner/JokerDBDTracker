using System.IO;
using System.Windows;
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

        private void UpdateStreakText()
        {
            var streak = CalculateWatchStreakDays();
            ProfileStreakText.Text = $"🔥 Стрик: {streak} дн.";
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
            var achievements = new List<ProfileAchievement>
            {
                BuildAchievement("Первый просмотр", "Открой любой стрим хотя бы один раз.", _watchHistory.Count >= 1),
                BuildAchievement("10 стримов", "Посмотри десять разных стримов.", _watchHistory.Count >= 10),
                BuildAchievement("25 стримов", "Посмотри 25 разных стримов.", _watchHistory.Count >= 25),
                BuildAchievement("50 стримов", "Посмотри 50 разных стримов.", _watchHistory.Count >= 50),
                BuildAchievement("Стрик 3 дня", "Заходи и смотри стримы три дня подряд.", streakDays >= 3),
                BuildAchievement("Стрик 7 дней", "Смотри стримы семь дней подряд.", streakDays >= 7),
                BuildAchievement("Избранное x5", "Добавь пять стримов в избранное.", _favoriteVideoIds.Count >= 5),
                BuildAchievement("Избранное x15", "Добавь 15 стримов в избранное.", _favoriteVideoIds.Count >= 15),
                BuildAchievement("Эффекты x3", "Посмотри 3 сессии с любыми эффектами.", _effectSessionsAny >= 3),
                BuildAchievement("Эффекты x15", "Посмотри 15 сессий с включенными эффектами.", _effectSessionsAny >= 15),
                BuildAchievement("5 эффектов сразу", "Сделай 5 сессий, где было включено 5+ эффектов.", _effectSessionsFivePlus >= 5),
                BuildAchievement("10 эффектов сразу", "Сделай 3 сессии, где было включено 10+ эффектов.", _effectSessionsTenPlus >= 3),
                BuildAchievement("Сильное размытие", "Посмотри 3 сессии с сильным размытием (75%+).", _effectSessionsStrongBlur >= 3),
                BuildAchievement("Сильное красное свечение", "Посмотри 3 сессии с сильным красным свечением (75%+).", _effectSessionsStrongRedGlow >= 3),
                BuildAchievement("Сильное фиолетовое свечение", "Посмотри 3 сессии с сильным фиолетовым свечением (75%+).", _effectSessionsStrongVioletGlow >= 3),
                BuildAchievement("Сильная тряска", "Посмотри 3 сессии с сильной тряской кадра (75%+).", _effectSessionsStrongShake >= 3),
                BuildAchievement("Мастер cursed", "Пройди полный стрим с 15 cursed-эффектами.", _unlockedAchievements.Contains(AchievementCursed15))
            };
            AchievementsList.ItemsSource = achievements;

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
            ProfileLevelText.Text = $"Уровень: {level}/{MaxLevel}";
            ProfileXpText.Text = $"XP до следующего уровня: {xpToNextLevel}";
            ProfileTodayText.Text = $"Сегодня: {DateTime.Now:yyyy-MM-dd}";
            ProfileXpProgress.Maximum = requiredInLevel;
            ProfileXpProgress.Value = clampedCurrentLevelXp;
            PrestigeButton.IsEnabled = level >= MaxLevel && _prestige < MaxPrestige;
        }

        private void RefreshHomeSummary()
        {
            var watchedCount = _allVideos.Count(v => v.LastViewedAtUtc.HasValue);
            var totalCount = _allVideos.Count;
            var favoritesCount = _favoriteVideoIds.Count;
            var unwatchedCount = Math.Max(0, totalCount - watchedCount);

            HomeStatsText.Text = $"Стримов: {totalCount} • Просмотрено: {watchedCount} • Избранное: {favoritesCount} • Непросмотрено: {unwatchedCount}";
            HomeHintText.Text = "Совет: открывай рекомендации слева, чтобы быстрее находить похожие стримы.";
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
            var iconPath = ResolvePrestigeIconPath(_prestige);
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                PrestigeIconImage.Source = null;
                return;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(iconPath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            PrestigeIconImage.Source = image;
        }

        private static string ResolvePrestigeIconPath(int prestige)
        {
            var stage = Math.Clamp(prestige / 10, 0, 10);
            return Path.Combine(AppContext.BaseDirectory, "Assets", "PrestigeIcons", $"prestige_{stage}.png");
        }

        private static int XpToReachNextLevel(int level)
        {
            return 220 + (level - 1) * 35 + (level - 1) * (level - 1) * 4;
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
