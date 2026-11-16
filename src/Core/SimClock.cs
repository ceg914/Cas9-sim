using System;

namespace PaleoSphere.Core;

public enum Season
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}

/// <summary>
/// 고정 틱 시계. 한 틱은 이동 기준 0.1초이고, 하루 길이는 TicksPerDay로 압축한다(게임적 단순화).
/// 생리·식생 속도는 '게임 내 하루/시간' 단위로 정의하고 이 시계로 틱 단위로 바꾼다.
/// </summary>
public sealed class SimClock
{
    /// <summary>한 틱이 나타내는 이동 시간(초).</summary>
    public const float TickSeconds = 0.1f;

    public long Tick { get; private set; }
    public int TicksPerDay { get; }
    public int DaysPerSeason { get; }
    public float StartHour { get; }

    public SimClock(int ticksPerDay, int daysPerSeason, float startHour, long tick = 0)
    {
        if (ticksPerDay < 240) throw new ArgumentOutOfRangeException(nameof(ticksPerDay));
        if (daysPerSeason < 1) throw new ArgumentOutOfRangeException(nameof(daysPerSeason));
        TicksPerDay = ticksPerDay;
        DaysPerSeason = daysPerSeason;
        StartHour = MathUtil.Clamp(startHour, 0f, 23.99f);
        Tick = tick;
    }

    private long StartOffsetTicks => (long)(StartHour / 24f * TicksPerDay);
    private long AbsoluteTicks => Tick + StartOffsetTicks;

    public void Advance() => Tick++;

    /// <summary>1부터 세는 날짜.</summary>
    public int Day => (int)(AbsoluteTicks / TicksPerDay) + 1;

    /// <summary>0 이상 24 미만의 시각.</summary>
    public float TimeOfDayHours => (AbsoluteTicks % TicksPerDay) * 24f / TicksPerDay;

    public int DaysPerYear => DaysPerSeason * 4;
    public int DayOfYear => (Day - 1) % DaysPerYear;
    public int Year => (Day - 1) / DaysPerYear + 1;
    public Season Season => (Season)(DayOfYear / DaysPerSeason);

    /// <summary>연중 위치 0~1 (0 = 봄 첫날).</summary>
    public float YearFraction => (DayOfYear + TimeOfDayHours / 24f) / DaysPerYear;

    /// <summary>한 틱이 나타내는 게임 내 시간(시).</summary>
    public float HoursPerTick => 24f / TicksPerDay;

    /// <summary>한 틱이 나타내는 게임 내 일수.</summary>
    public float DaysPerTick => 1f / TicksPerDay;

    public double ElapsedDays => (double)Tick / TicksPerDay;
}
