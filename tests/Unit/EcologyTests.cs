using System;
using PaleoSphere.Ecology;
using PaleoSphere.Simulation;
using PaleoSphere.World;
using Xunit;

namespace PaleoSphere.Tests.Unit;

/// <summary>TEST-02: 섭식이 식생에 실제로 반영되고 음수가 되지 않는다.</summary>
public class EcologyTests
{
    private static int RichCell(SimulationCore sim)
    {
        var veg = sim.World.Vegetation;
        int best = 0;
        for (int i = 1; i < veg.Biomass.Length; i++)
            if (veg.Biomass[i] > veg.Biomass[best]) best = i;
        return best;
    }

    [Fact]
    public void Consume_ReducesBiomassByReturnedAmount()
    {
        SimulationCore sim = TestData.NewSim();
        var veg = sim.World.Vegetation;
        int cell = RichCell(sim);
        float before = veg.Biomass[cell];
        float got = veg.Consume(cell, 0.2f, sim.World.Forage);
        Assert.Equal(0.2f, got, 5);
        Assert.Equal(before - got, veg.Biomass[cell], 5);
        Assert.Equal(got, (float)veg.TotalConsumed, 5);
    }

    [Fact]
    public void Consume_NeverGoesBelowResidualOrZero()
    {
        SimulationCore sim = TestData.NewSim();
        var veg = sim.World.Vegetation;
        int cell = RichCell(sim);
        float before = veg.Biomass[cell];
        float got = veg.Consume(cell, 1e6f, sim.World.Forage);
        float residual = sim.World.Forage.ResidualFraction * veg.Capacity[cell];
        Assert.True(got < before);
        Assert.True(veg.Biomass[cell] >= 0f);
        Assert.Equal(residual, veg.Biomass[cell], 3);
        // 잔여분만 남은 뒤에는 반올림 오차 수준 말고는 더 먹을 수 없다.
        Assert.True(veg.Consume(cell, 1f, sim.World.Forage) < 1e-5f);
        Assert.Equal(0f, veg.Consume(cell, -1f, sim.World.Forage));
        Assert.Equal(0f, veg.Consume(cell, float.NaN, sim.World.Forage));
    }

    [Fact]
    public void WaterCells_HaveNoVegetation()
    {
        SimulationCore sim = TestData.NewSim(seed: 20261116, config: new WorldConfig { Seed = 20261116 });
        var t = sim.World.Terrain;
        var veg = sim.World.Vegetation;
        for (int i = 0; i < t.CellCount; i++)
        {
            if (!t.IsWater(i)) continue;
            Assert.Equal(0f, veg.Capacity[i]);
            Assert.Equal(0f, veg.Biomass[i]);
        }
    }

    [Fact]
    public void Growth_HappensInDaylightOnly_AndStaysWithinCapacity()
    {
        SimulationCore sim = TestData.NewSim();
        WorldState w = sim.World;
        var veg = w.Vegetation;
        double start = veg.SumBiomass();

        var night = new EnvironmentState { Light = 0f, TemperatureC = 22f, IsNight = true };
        veg.Update(w.Terrain, w.Biomes, w.Forage, w.Config, night, 0.5f);
        Assert.Equal(0.0, veg.TotalGrown);

        var day = new EnvironmentState { Light = 1f, TemperatureC = 22f };
        for (int i = 0; i < 400; i++) veg.Update(w.Terrain, w.Biomes, w.Forage, w.Config, day, 0.05f);
        Assert.True(veg.TotalGrown > 0);
        Assert.True(veg.SumBiomass() > start * 0.9);
        for (int i = 0; i < veg.Biomass.Length; i++)
        {
            Assert.True(veg.Biomass[i] >= 0f);
            Assert.True(veg.Biomass[i] <= veg.Capacity[i] * 1.0001f);
        }
    }

    [Fact]
    public void Dieback_HappensWhenTooColdOrTooDry()
    {
        SimulationCore sim = TestData.NewSim();
        WorldState w = sim.World;
        var veg = w.Vegetation;
        double start = veg.SumBiomass();
        var frozen = new EnvironmentState { Light = 1f, TemperatureC = -5f };
        for (int i = 0; i < 20; i++) veg.Update(w.Terrain, w.Biomes, w.Forage, w.Config, frozen, 0.1f);
        Assert.True(veg.TotalDieback > 0);
        Assert.True(veg.SumBiomass() < start);
        Assert.Equal(0.0, veg.TotalGrown);
    }

    [Fact]
    public void Balance_GrownMinusConsumedMinusDieback_MatchesBiomass()
    {
        SimulationCore sim = TestData.NewSim(creatures: 12);
        TestData.Run(sim, 18000);
        var veg = sim.World.Vegetation;
        Assert.True(veg.TotalConsumed > 0, "개체가 아무것도 먹지 않았습니다.");
        double expected = veg.InitialTotal + veg.TotalGrown - veg.TotalConsumed - veg.TotalDieback;
        double actual = veg.SumBiomass();
        Assert.True(Math.Abs(expected - actual) < Math.Max(1.0, actual * 1e-3),
            $"식생 수지가 맞지 않습니다: 기대 {expected:0.00} kg, 실제 {actual:0.00} kg");
        for (int i = 0; i < veg.Biomass.Length; i++) Assert.True(veg.Biomass[i] >= 0f);
    }

    [Fact]
    public void Stats_AgeHistogramCountsEveryCreature()
    {
        SimulationCore sim = TestData.NewSim(creatures: 15);
        int[] hist = PopulationStats.AgeHistogram(sim.Creatures, 0, sim.World.Species[0].MaxAgeDays, 8);
        int total = 0;
        foreach (int n in hist) total += n;
        Assert.Equal(sim.CreatureCount, total);
    }

    [Fact]
    public void Stats_DecimatesWhenFull()
    {
        var stats = new PopulationStats(1, 10);
        var none = Array.Empty<PaleoSphere.Creatures.Creature>();
        for (int i = 0; i < PopulationStats.MaxSamples + 10; i++) stats.Sample(i * 10, i, none, 1.0);
        Assert.True(stats.Samples.Count < PopulationStats.MaxSamples);
        Assert.Equal(20, stats.SampleIntervalTicks);
        for (int i = 1; i < stats.Samples.Count; i++) Assert.True(stats.Samples[i].Tick > stats.Samples[i - 1].Tick);
    }
}
