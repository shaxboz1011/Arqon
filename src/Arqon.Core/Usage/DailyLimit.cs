namespace Arqon.Core.Usage;

public enum LimitLevel { None, Ok, Near, Reached }

/// <summary>Where today's traffic stands against the limit the user set; warns once at 80% and once at the limit.</summary>
public sealed class DailyLimit
{
    private DateOnly _day;
    private LimitLevel _told = LimitLevel.None;

    /// <summary>How far <paramref name="used"/> is towards <paramref name="limitBytes"/> (0 or less: no limit).</summary>
    public static LimitLevel LevelOf(long used, long limitBytes) => limitBytes <= 0 ? LimitLevel.None
        : used >= limitBytes ? LimitLevel.Reached
        : used >= limitBytes / 5 * 4 ? LimitLevel.Near
        : LimitLevel.Ok;

    /// <summary>
    /// The warning to give now, or null. Each level is announced once a day, and a new day starts again; after the limit was reached
    /// there is nothing more to say until the next day.
    /// </summary>
    public LimitLevel? Check(DateOnly today, long used, long limitBytes)
    {
        if (today != _day)
        {
            _day = today;
            _told = LimitLevel.None;
        }
        var level = LevelOf(used, limitBytes);
        if (level is not (LimitLevel.Near or LimitLevel.Reached) || level <= _told) return null;
        _told = level;
        return level;
    }
}
