using System.Diagnostics;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private static readonly TimeZoneInfo MoscowTimeZone = ProfileClock.TimeZone;
        private long _clockTimestamp = Stopwatch.GetTimestamp();
        private DateTime _clockBaseUtc = DateTime.UtcNow;

        private async Task InitializeNetworkClockAsync()
        {
            await SyncNetworkClockAsync();
            _networkTimeSyncTimer.Stop();
            _networkTimeSyncTimer.Start();
        }

        private async void NetworkTimeSyncTimer_Tick(object? sender, EventArgs e)
        {
            await SyncNetworkClockAsync();
        }

        private async Task SyncNetworkClockAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var networkUtc = await _networkTimeService.GetUtcNowAsync(timeout.Token);
            if (networkUtc is null)
            {
                return;
            }

            _internetUtcAtSync = DateTime.SpecifyKind(networkUtc.Value, DateTimeKind.Utc);
            _clockBaseUtc = _internetUtcAtSync;
            _clockTimestamp = Stopwatch.GetTimestamp();
        }

        private DateTime GetTrustedUtcNow()
        {
            return _clockBaseUtc + Stopwatch.GetElapsedTime(_clockTimestamp);
        }

        private DateTime GetTrustedLocalNow()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(GetTrustedUtcNow(), MoscowTimeZone);
        }

        private DateOnly GetTrustedToday()
        {
            return DateOnly.FromDateTime(GetTrustedLocalNow().Date);
        }

        private void StartQuestRolloverMonitoring()
        {
            var today = GetTrustedToday();
            EnsureQuestRotationSchedule(today);
            _lastQuestRefreshDay = today;
            _lastQuestRefreshWeekKey = GetIsoWeekKey(today);
            _questRolloverTimer.Stop();
            _questRolloverTimer.Start();
        }

        private async void QuestRolloverTimer_Tick(object? sender, EventArgs e)
        {
            var today = GetTrustedToday();
            var weekKey = GetIsoWeekKey(today);
            if (today == _lastQuestRefreshDay && string.Equals(weekKey, _lastQuestRefreshWeekKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastQuestRefreshDay = today;
            _lastQuestRefreshWeekKey = weekKey;
            PurgeExpiredRewardedQuestKeys(today);
            EnsureQuestRotationSchedule(today);
            RefreshProfile();
            RefreshQuestsPage();
            await SaveHistoryAsync();
        }

        private void PurgeExpiredRewardedQuestKeys(DateOnly currentDay)
        {
            if (_rewardedQuestKeys.Count == 0)
            {
                return;
            }

            var currentWeekKey = GetIsoWeekKey(currentDay);
            var activePrefixDaily = $"daily:{currentDay:yyyy-MM-dd}:";
            var activePrefixWeekly = $"weekly:{currentWeekKey}:";
            var stale = _rewardedQuestKeys
                .Where(key =>
                    key.StartsWith("daily:", StringComparison.Ordinal) && !key.StartsWith(activePrefixDaily, StringComparison.Ordinal) ||
                    key.StartsWith("weekly:", StringComparison.Ordinal) && !key.StartsWith(activePrefixWeekly, StringComparison.Ordinal))
                .ToList();
            foreach (var key in stale)
            {
                _rewardedQuestKeys.Remove(key);
            }
        }


    }
}
