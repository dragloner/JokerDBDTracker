using System.Diagnostics;
using JokerDBDTracker.Services;

namespace JokerDBDTracker;

public partial class PlayerWindow
{
    public Func<Task>? ProgressCheckpoint { get; set; }
    public Dictionary<DateOnly, double> WatchSecondsByDay { get; } = [];
    private readonly PlaybackCoverage _allEffectsCoverage = new();
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private bool _closingAfterCheckpoint;
    private bool _positionTickRunning;
    private readonly Func<DateTime> _utcNow;

    private void CreditWatchTime(DateTime endUtc, double seconds)
    {
        var startUtc = endUtc.AddSeconds(-seconds);
        while (startUtc < endUtc)
        {
            var day = ProfileClock.Day(startUtc);
            var midnight = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), ProfileClock.TimeZone);
            var segmentEnd = midnight < endUtc ? midnight : endUtc;
            WatchSecondsByDay[day] = WatchSecondsByDay.GetValueOrDefault(day) + (segmentEnd - startUtc).TotalSeconds;
            startUtc = segmentEnd;
        }
    }

    private async Task PersistPlaybackPositionAsync(bool force)
    {
        if (ProgressCheckpoint is null) return;
        if (!force && (_lastPlaybackPersistTimestamp == 0 ? double.PositiveInfinity : Stopwatch.GetElapsedTime(_lastPlaybackPersistTimestamp).TotalSeconds) < PlaybackPersistIntervalSeconds) return;
        await _checkpointGate.WaitAsync();
        try
        {
            if (!force && (_lastPlaybackPersistTimestamp == 0 ? double.PositiveInfinity : Stopwatch.GetElapsedTime(_lastPlaybackPersistTimestamp).TotalSeconds) < PlaybackPersistIntervalSeconds) return;
            await ProgressCheckpoint();
            _lastPlaybackPersistTimestamp = Stopwatch.GetTimestamp();
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogException("Player.SaveProgress", ex);
            if (force) throw;
        }
        finally { _checkpointGate.Release(); }
    }
}
