namespace JokerDBDTracker.Services;

public static class ProfileClock
{
    public static readonly TimeZoneInfo TimeZone = ResolveTimeZone();
    public static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone));
    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { "Russian Standard Time", "Europe/Moscow" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Moscow", TimeSpan.FromHours(3), "Moscow", "Moscow");
    }
}
