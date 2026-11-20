using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker;

public partial class MainWindow
{
    private readonly Dictionary<string, HashSet<string>> _watchedVideoIdsByDay = new(StringComparer.Ordinal);
    private const int FirstWatchMinimumSeconds = 60;

    private void ConnectPlayerProgress(PlayerWindow player, YouTubeVideo video)
    {
        var creditedXp = 0;
        var creditedDays = new Dictionary<DateOnly, int>();
        var awarded = new HashSet<string>(StringComparer.Ordinal);
        player.ProgressCheckpoint = async () =>
        {
            video.LastPlaybackSeconds = player.LastPlaybackSeconds;
            _playbackSecondsHistory[video.VideoId] = player.LastPlaybackSeconds;
            var totalXp = (int)Math.Round(player.WatchXpEarned * WatchSessionXpMultiplier);
            AddXp(Math.Max(0, totalXp - creditedXp));
            creditedXp = totalXp;
            foreach (var pair in player.WatchSecondsByDay)
            {
                var totalSeconds = (int)Math.Floor(pair.Value);
                creditedDays.TryGetValue(pair.Key, out var credited);
                var delta = Math.Max(0, totalSeconds - credited);
                if (delta == 0) continue;
                creditedDays[pair.Key] = totalSeconds;
                _watchedSecondsByDay.TryGetValue(pair.Key, out var previous);
                _watchedSecondsByDay[pair.Key] = previous + delta;
                _bestSessionSecondsByDay.TryGetValue(pair.Key, out var best);
                _bestSessionSecondsByDay[pair.Key] = Math.Max(best, totalSeconds);
                _watchedDays.Add(pair.Key);
                var dayKey = pair.Key.ToString("yyyy-MM-dd");
                if (!_watchedVideoIdsByDay.TryGetValue(dayKey, out var ids)) _watchedVideoIdsByDay[dayKey] = ids = new(StringComparer.Ordinal);
                ids.Add(video.VideoId);
            }
            if (player.EligibleWatchSeconds > 0)
            {
                var now = GetTrustedUtcNow();
                _watchHistory[video.VideoId] = now;
                video.LastViewedAtUtc = now;
            }
            if (player.EligibleWatchSeconds >= FirstWatchMinimumSeconds && _firstViewRewardedVideoIds.Add(video.VideoId)) AddXp(XpFirstWatch);
            if (player.EligibleWatchSeconds >= FirstWatchMinimumSeconds)
            {
                var today = GetTrustedToday();
                Award("effects", player.WatchedWithAnyEffects, () => { _effectSessionsAny++; _effectSessionsByDay[today] = _effectSessionsByDay.GetValueOrDefault(today) + 1; });
                Award("five", player.MaxEnabledEffectsCount >= 5, () => _effectSessionsFivePlus++);
                Award("ten", player.MaxEnabledEffectsCount >= 10, () => _effectSessionsTenPlus++);
                Award("blur", player.UsedStrongBlur, () => _effectSessionsStrongBlur++);
                Award("fisheye", player.UsedStrongRedGlow, () => _effectSessionsStrongRedGlow++);
                Award("jpeg", player.UsedStrongVioletGlow, () => _effectSessionsStrongVioletGlow++);
                Award("shake", player.UsedStrongShake, () => _effectSessionsStrongShake++);
                Award("preset", player.UsedAnyPreset, () => { _effectPresetSessionsAny++; _presetSessionsByDay[today] = _presetSessionsByDay.GetValueOrDefault(today) + 1; });
                Award("custom", player.UsedCustomPreset, () => _effectPresetSessionsCustom++);
                Award("retro", player.UsedRetroPreset, () => _effectPresetSessionsRetro++);
                Award("chaos", player.UsedChaosPreset, () => _effectPresetSessionsChaos++);
                Award("dream", player.UsedDreamPreset, () => _effectPresetSessionsDream++);
            }
            if (player.CursedMasterUnlocked && _unlockedAchievements.Add(AchievementCursed15)) AddXp(XpAchievement);
            await SaveHistoryAsync();
        };
        void Award(string key, bool condition, Action apply)
        {
            if (condition && awarded.Add(key)) apply();
        }
    }
}
