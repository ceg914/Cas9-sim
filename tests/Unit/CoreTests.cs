using System.Collections.Generic;
using PaleoSphere.Core;
using Xunit;

namespace PaleoSphere.Tests.Unit;

public class CoreTests
{
    [Fact]
    public void Rng_SameSeedAndStream_ProducesSameSequence()
    {
        Pcg32 a = Pcg32.Create(123, 5), b = Pcg32.Create(123, 5);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void Rng_DifferentStreams_Diverge()
    {
        Pcg32 a = Pcg32.Create(123, 5), b = Pcg32.Create(123, 6);
        int same = 0;
        for (int i = 0; i < 100; i++)
            if (a.NextUInt() == b.NextUInt()) same++;
        Assert.True(same < 3);
    }

    [Fact]
    public void Rng_KnownSequence_IsStableAcrossBuilds()
    {
        // 별도로 구현한 참조 계산(파이썬)에서 얻은 값. 난수 구현이 바뀌면 같은 시드의 세계가 달라지므로
        // 이 테스트가 깨지면 SimVersion을 올려야 한다.
        uint[] expected = { 0x9ff47d57u, 0x2ddcca63u, 0xa976f6c9u, 0x2f63e9b6u, 0xc0aad345u, 0x9bdf32bbu };
        Pcg32 r = Pcg32.Create(42, 54);
        foreach (uint v in expected) Assert.Equal(v, r.NextUInt());
    }

    [Fact]
    public void Rng_StateRestore_ContinuesSequence()
    {
        Pcg32 a = Pcg32.Create(9, 1);
        for (int i = 0; i < 10; i++) a.NextUInt();
        var b = new Pcg32 { State = a.State, Inc = a.Inc };
        for (int i = 0; i < 50; i++) Assert.Equal(a.NextFloat(), b.NextFloat());
    }

    [Fact]
    public void Rng_FloatAndIntStayInRange()
    {
        Pcg32 r = Pcg32.Create(1, 1);
        for (int i = 0; i < 10000; i++)
        {
            float f = r.NextFloat();
            Assert.InRange(f, 0f, 0.99999994f);
            Assert.InRange(r.NextInt(7), 0, 6);
        }
    }

    [Fact]
    public void Clock_DayAndHourAdvance()
    {
        var clock = new SimClock(6000, 10, 8f);
        Assert.Equal(1, clock.Day);
        Assert.Equal(8f, clock.TimeOfDayHours, 3);
        for (int i = 0; i < 6000; i++) clock.Advance();
        Assert.Equal(2, clock.Day);
        Assert.Equal(8f, clock.TimeOfDayHours, 3);
        Assert.Equal(Season.Spring, clock.Season);
        for (int i = 0; i < 6000 * 10; i++) clock.Advance();
        Assert.Equal(Season.Summer, clock.Season);
    }

    [Fact]
    public void Hasher_IsOrderSensitive()
    {
        StateHasher a = StateHasher.Create(), b = StateHasher.Create();
        a.Add(1); a.Add(2);
        b.Add(2); b.Add(1);
        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public void EventBus_KeepsBoundedLogInOrder()
    {
        var bus = new EventBus(16);
        var seen = new List<long>();
        bus.Subscribe(e => seen.Add(e.Tick));
        for (int i = 0; i < 40; i++)
            bus.Publish(new SimEvent(i, 1, 0f, SimEventType.Notice, -1, "", "", Vec2.Zero));
        Assert.Equal(40, seen.Count);
        Assert.Equal(16, bus.Recent.Count);
        Assert.Equal(24, bus.Recent[0].Tick);
        Assert.Equal(40, bus.TotalPublished);
    }
}
