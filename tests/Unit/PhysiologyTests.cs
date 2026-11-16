using PaleoSphere.AI;
using PaleoSphere.Creatures;
using PaleoSphere.Simulation;
using PaleoSphere.World;
using Xunit;

namespace PaleoSphere.Tests.Unit;

/// <summary>TEST-03: 먹이·물이 없을 때 생리 상태가 규칙대로 변한다.</summary>
public class PhysiologyTests
{
    private static readonly EnvironmentState Mild = new() { Light = 1f, TemperatureC = 20f };

    private static Creature NewCreature() => new() { Id = 1, Energy = 0.8f, Hydration = 0.8f, Fatigue = 0.2f };

    private static SpeciesDefinition Species => TestData.Data.Species[0];

    [Fact]
    public void StandingStill_DrainsEnergyAtReserveRate()
    {
        Creature c = NewCreature();
        c.Activity = ActivityKind.Stand;
        Physiology.Update(c, Species, Mild, 24f);
        Assert.Equal(0.8f - 1f / Species.EnergyReserveDays, c.Energy, 4);
        Assert.Equal(0.8f - 1f / Species.HydrationReserveDays, c.Hydration, 4);
    }

    [Fact]
    public void Running_CostsMoreThanWalking_WhichCostsMoreThanResting()
    {
        float Drain(ActivityKind a)
        {
            Creature c = NewCreature();
            c.Activity = a;
            Physiology.Update(c, Species, Mild, 2f);
            return 0.8f - c.Energy;
        }
        Assert.True(Drain(ActivityKind.Run) > Drain(ActivityKind.Walk));
        Assert.True(Drain(ActivityKind.Walk) > Drain(ActivityKind.Stand));
        Assert.True(Drain(ActivityKind.Stand) > Drain(ActivityKind.Rest));
    }

    [Fact]
    public void Heat_IncreasesWaterLoss()
    {
        Creature mild = NewCreature(), hot = NewCreature();
        Physiology.Update(mild, Species, Mild, 4f);
        Physiology.Update(hot, Species, new EnvironmentState { TemperatureC = 38f }, 4f);
        Assert.True(hot.Hydration < mild.Hydration);
    }

    [Fact]
    public void Rest_LowersFatigue_ActivityRaisesIt()
    {
        Creature c = NewCreature();
        c.Activity = ActivityKind.Walk;
        Physiology.Update(c, Species, Mild, 2f);
        Assert.True(c.Fatigue > 0.2f);
        float tired = c.Fatigue;
        c.Activity = ActivityKind.Rest;
        Physiology.Update(c, Species, Mild, 1f);
        Assert.True(c.Fatigue < tired);
    }

    [Fact]
    public void Starvation_DamagesHealthAndKills()
    {
        Creature c = NewCreature();
        c.Energy = 0f;
        c.Hydration = 1f;
        float hours = 0;
        while (c.Alive && hours < 24f * 30)
        {
            c.Hydration = 1f;
            Physiology.Update(c, Species, Mild, 1f);
            hours += 1f;
        }
        Assert.False(c.Alive);
        Assert.Equal(DeathCause.Starvation, c.DeathCause);
        Assert.InRange(hours / 24f, Species.StarveHealthDays - 0.2f, Species.StarveHealthDays + 0.2f);
    }

    [Fact]
    public void Dehydration_KillsFasterAndIsRecordedAsCause()
    {
        Creature c = NewCreature();
        c.Hydration = 0f;
        float hours = 0;
        while (c.Alive && hours < 24f * 30)
        {
            c.Energy = 1f;
            Physiology.Update(c, Species, Mild, 1f);
            hours += 1f;
        }
        Assert.False(c.Alive);
        Assert.Equal(DeathCause.Dehydration, c.DeathCause);
        Assert.True(Species.DehydrationHealthDays < Species.StarveHealthDays);
    }

    [Fact]
    public void Health_RecoversWhenFedAndWatered()
    {
        Creature c = NewCreature();
        c.Health = 0.5f;
        Physiology.Update(c, Species, Mild, 12f);
        Assert.True(c.Health > 0.5f);
        Assert.True(c.Health <= 1f);
    }

    [Fact]
    public void Eating_MovesBiomassIntoEnergy()
    {
        SimulationCore sim = TestData.NewSim(creatures: 1);
        Creature c = sim.Creatures[0];
        var veg = sim.World.Vegetation;
        // 가장 풍부한 마른 땅으로 옮긴다.
        int best = -1;
        for (int i = 0; i < veg.Biomass.Length; i++)
            if (sim.World.Terrain.Walkable[i] && !sim.World.Terrain.IsWater(i) && (best < 0 || veg.Biomass[i] > veg.Biomass[best])) best = i;
        c.Position = sim.World.Terrain.CellCenter(best);
        c.Energy = 0.3f;
        c.Behavior = BehaviorKind.Forage;
        double before = veg.SumBiomass();

        BehaviorExecutor.Update(c, Species, sim.World, 0.02f);

        Assert.Equal(ActivityKind.Eat, c.Activity);
        Assert.True(c.BoutAmount > 0f);
        Assert.Equal(c.BoutAmount, (float)(before - veg.SumBiomass()), 3);
        Assert.Equal(0.3f + c.BoutAmount * Species.EnergyPerKg / c.TraitBodySize, c.Energy, 5);
    }

    [Fact]
    public void Drinking_OnlyWorksAtTheWaterEdge()
    {
        SimulationCore sim = TestData.NewSim(seed: 20261116, creatures: 1, config: new WorldConfig { Seed = 20261116 });
        Creature c = sim.Creatures[0];
        var t = sim.World.Terrain;
        int shore = -1, inland = -1;
        for (int i = 0; i < t.CellCount; i++)
        {
            if (shore < 0 && t.Drinkable[i]) shore = i;
            if (inland < 0 && t.Walkable[i] && t.WaterPathDistance[i] > 30f && !float.IsPositiveInfinity(t.WaterPathDistance[i])) inland = i;
        }
        Assert.True(shore >= 0 && inland >= 0);
        c.Behavior = BehaviorKind.Drink;

        c.Position = t.CellCenter(inland);
        c.Hydration = 0.3f;
        BehaviorExecutor.Update(c, Species, sim.World, 0.05f);
        Assert.Equal(0.3f, c.Hydration);
        Assert.Equal(ActivityKind.Walk, c.Activity);

        c.Position = t.CellCenter(shore);
        BehaviorExecutor.Update(c, Species, sim.World, 0.05f);
        Assert.True(c.Hydration > 0.3f);
        Assert.Equal(ActivityKind.Drink, c.Activity);
    }
}
