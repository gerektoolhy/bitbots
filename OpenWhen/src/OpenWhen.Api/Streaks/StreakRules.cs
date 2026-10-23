namespace OpenWhen.Api.Streaks;

public static class StreakRules
{
    public const string DefaultTimeZone = "Europe/Vilnius";
    public const int DefaultTargetDays = 7;

    public static DateOnly LocalToday(DateTimeOffset utcNow, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public static bool IsKnownTimeZone(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static int Effective(int currentStreak, DateOnly? lastCheckIn, DateOnly today)
    {
        if (lastCheckIn is null)
            return 0;
        var gap = today.DayNumber - lastCheckIn.Value.DayNumber;
        return gap is 0 or 1 ? currentStreak : 0;
    }

    /// <summary>
    /// Same local day does not count. The next local day continues. Any larger gap starts again at 1.
    /// </summary>
    public static (int Streak, bool Counted) ApplyCheckIn(int currentStreak, DateOnly? lastCheckIn, DateOnly today)
    {
        if (lastCheckIn == today)
            return (currentStreak, false);
        if (lastCheckIn == today.AddDays(-1))
            return (currentStreak + 1, true);
        return (1, true);
    }
}
