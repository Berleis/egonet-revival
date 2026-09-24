namespace RaceNetShowdown.Server.RaceNet;

internal static class Dirt4EventCalendar
{
    // Original GetEvents captures expire at 10:00 UTC, Monday / first of month.
    internal static (long Start, long End) Window(DateTimeOffset now, int eventType)
    {
        var utc = now.UtcDateTime;
        var start = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 10, 0, 0, TimeSpan.Zero);
        if (eventType == 2)
        {
            start = new DateTimeOffset(utc.Year, utc.Month, 1, 10, 0, 0, TimeSpan.Zero);
            if (now < start) start = start.AddMonths(-1);
            return (start.ToUnixTimeSeconds(), start.AddMonths(1).ToUnixTimeSeconds());
        }

        if (now < start) start = start.AddDays(-1);
        if (eventType is 1 or 5)
            start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        return (start.ToUnixTimeSeconds(), start.AddDays(eventType is 1 or 5 ? 7 : 1).ToUnixTimeSeconds());
    }
}
