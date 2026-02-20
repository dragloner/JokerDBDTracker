using System;
using System.Windows;
using System.Windows.Controls;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private void RefreshQuestsPage()
        {
            var daily = BuildDailyTasks();
            var weekly = BuildWeeklyTasks();
            QuestsDailyList.ItemsSource = daily;
            QuestsWeeklyList.ItemsSource = weekly;
            var claimableCount = daily.Count(x => x.IsClaimEnabled) + weekly.Count(x => x.IsClaimEnabled);
            QuestsSummaryText.Text = T(
                $"Активно заданий: ежедневных {daily.Count}, еженедельных {weekly.Count}. Можно забрать сейчас: {claimableCount}.",
                $"Active quests: daily {daily.Count}, weekly {weekly.Count}. Claimable now: {claimableCount}.");
            RefreshQuestResetCountdownTexts();
        }

        private void RefreshQuestResetCountdownTexts()
        {
            if (QuestsDailyResetText is null || QuestsWeeklyResetText is null)
            {
                return;
            }

            var localNow = GetTrustedLocalNow();
            var dailyResetIn = GetNextDailyReset(localNow) - localNow;
            var weeklyResetIn = GetNextWeeklyReset(localNow) - localNow;
            if (dailyResetIn < TimeSpan.Zero)
            {
                dailyResetIn = TimeSpan.Zero;
            }
            if (weeklyResetIn < TimeSpan.Zero)
            {
                weeklyResetIn = TimeSpan.Zero;
            }

            QuestsDailyResetText.Text = T(
                $"Обновление через: {FormatCountdown(dailyResetIn)} (МСК)",
                $"Resets in: {FormatCountdown(dailyResetIn)} (MSK)");
            QuestsWeeklyResetText.Text = T(
                $"Обновление через: {FormatCountdown(weeklyResetIn)} (МСК)",
                $"Resets in: {FormatCountdown(weeklyResetIn)} (MSK)");
        }

        private static DateTime GetNextDailyReset(DateTime localNow)
        {
            return localNow.Date.AddDays(1);
        }

        private static DateTime GetNextWeeklyReset(DateTime localNow)
        {
            var daysUntilMonday = ((int)DayOfWeek.Monday - (int)localNow.DayOfWeek + 7) % 7;
            if (daysUntilMonday == 0)
            {
                daysUntilMonday = 7;
            }

            return localNow.Date.AddDays(daysUntilMonday);
        }

        private static string FormatCountdown(TimeSpan timeLeft)
        {
            if (timeLeft.TotalDays >= 1)
            {
                return $"{(int)timeLeft.TotalDays}d {timeLeft.Hours:D2}:{timeLeft.Minutes:D2}:{timeLeft.Seconds:D2}";
            }

            return $"{Math.Max(0, timeLeft.Hours):D2}:{Math.Max(0, timeLeft.Minutes):D2}:{Math.Max(0, timeLeft.Seconds):D2}";
        }

        private void OpenQuestsButton_Click(object sender, RoutedEventArgs e)
        {
            SelectTopTab(3);
        }

        private void BackToProfileButton_Click(object sender, RoutedEventArgs e)
        {
            SelectTopTab(2);
        }

        private void QuestUiRefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsQuestsTabSelected())
            {
                return;
            }

            RefreshQuestResetCountdownTexts();
        }

        private async void ClaimQuestButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string claimKey)
            {
                return;
            }

            if (!TryClaimQuestReward(claimKey))
            {
                return;
            }

            await SaveHistoryAsync();
            RefreshProfile();
            RefreshQuestsPage();
        }
    }
}
