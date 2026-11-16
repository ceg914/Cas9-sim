using System.Linq;
using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Simulation;
using PaleoSphere.World;
using Xunit;

namespace PaleoSphere.Tests.Simulation;

/// <summary>기획서의 필수 시나리오 가운데 초식동물 한 종으로 볼 수 있는 것들.</summary>
public class ScenarioTests
{
    private static void AssertInvariants(SimulationCore sim)
    {
        var t = sim.World.Terrain;
        foreach (Creature c in sim.Creatures)
        {
            Assert.True(c.Alive);
            Assert.True(MathUtil.IsFinite(c.Position.X) && MathUtil.IsFinite(c.Position.Z));
            Assert.True(t.InBounds(c.Position), $"개체 {c.Id}이(가) 세계 밖에 있습니다: {c.Position}");
            Assert.True(t.Walkable[t.CellIndexAt(c.Position)], $"개체 {c.Id}이(가) 걸을 수 없는 셀에 있습니다: {c.Position}");
            Assert.InRange(c.Health, 0f, 1f);
            Assert.InRange(c.Energy, 0f, 1f);
            Assert.InRange(c.Hydration, 0f, 1f);
            Assert.InRange(c.Fatigue, 0f, 1f);
            Assert.InRange(c.Fear, 0f, 1f);
            Assert.True(MathUtil.IsFinite(c.Heading));
        }
    }

    [Fact]
    public void LushLowland_HerbivoresFeedDrinkRestAndSurvive()
    {
        SimulationCore sim = TestData.Data.CreateSimulation(TestData.Scenario("lush_lowland"));
        int initial = sim.CreatureCount;
        Assert.Equal(30, initial);
        bool ate = false, drank = false, rested = false, walked = false;
        for (int day = 0; day < 20; day++)
        {
            for (int i = 0; i < sim.World.Config.TicksPerDay / 100; i++)
            {
                TestData.Run(sim, 100);
                foreach (Creature c in sim.Creatures)
                {
                    ate |= c.Activity == ActivityKind.Eat;
                    drank |= c.Activity == ActivityKind.Drink;
                    rested |= c.Activity == ActivityKind.Rest;
                    walked |= c.Activity == ActivityKind.Walk;
                }
            }
            AssertInvariants(sim);
        }
        Assert.True(ate && drank && rested && walked, $"섭식 {ate}, 음수 {drank}, 휴식 {rested}, 이동 {walked}");
        Assert.Equal(initial, sim.CreatureCount);
        Assert.True(sim.Creatures.Average(c => c.Energy) > 0.6f);
        Assert.True(sim.Creatures.Average(c => c.Hydration) > 0.6f);
        Assert.True(sim.World.Vegetation.TotalConsumed > 20000, "20일 동안 먹은 양이 너무 적습니다.");
    }

    [Fact]
    public void ScarceResources_PopulationDeclinesFromStarvation()
    {
        SimulationCore sim = TestData.Data.CreateSimulation(TestData.Scenario("scarce_resources"));
        int initial = sim.CreatureCount;
        for (int day = 0; day < 45; day++)
        {
            TestData.Run(sim, sim.World.Config.TicksPerDay);
            AssertInvariants(sim);
        }
        int starved = sim.Stats.TotalDeathsByCause(DeathCause.Starvation);
        Assert.True(starved > 0, "먹이가 부족한데도 아사가 없습니다.");
        Assert.Equal(initial - sim.CreatureCount, sim.Stats.TotalNaturalDeaths());
        Assert.True(sim.CreatureCount < initial);
        Assert.Contains(sim.Events.Recent, e => e.Type == SimEventType.CreatureDied && e.Detail == "starvation");
        Assert.True(sim.Stats.Samples[^1].TotalDeaths == sim.Stats.TotalNaturalDeaths());
    }

    [Fact]
    public void NoWater_EveryoneDiesOfDehydration()
    {
        var cfg = new WorldConfig { Seed = 3, SizeCells = 64, WaterFraction = 0f, River = false };
        SimulationCore sim = TestData.NewSim(config: cfg, creatures: 8);
        float reserve = sim.World.Species[0].HydrationReserveDays;
        TestData.Run(sim, (int)(cfg.TicksPerDay * 0.5f));
        Assert.Equal(8, sim.CreatureCount);
        TestData.Run(sim, (int)(cfg.TicksPerDay * (reserve + sim.World.Species[0].DehydrationHealthDays + 2f)));
        Assert.Equal(0, sim.CreatureCount);
        Assert.Equal(8, sim.Stats.TotalDeathsByCause(DeathCause.Dehydration));
    }

    [Fact]
    public void NoVegetation_EveryoneStarves_AndEnergyOnlyFalls()
    {
        var cfg = new WorldConfig { Seed = 20261116, SizeCells = 96, VegetationCapacityScale = 0f };
        SimulationCore sim = TestData.NewSim(config: cfg, creatures: 6);
        float lastMean = sim.Creatures.Average(c => c.Energy);
        for (int i = 0; i < 40 && sim.CreatureCount == 6; i++)
        {
            TestData.Run(sim, 1000);
            if (sim.CreatureCount < 6) break;
            float mean = sim.Creatures.Average(c => c.Energy);
            Assert.True(mean <= lastMean + 1e-6f, "먹이가 없는데 평균 에너지가 올랐습니다.");
            lastMean = mean;
        }
        TestData.Run(sim, cfg.TicksPerDay * 16);
        Assert.Equal(0, sim.CreatureCount);
        Assert.Equal(6, sim.Stats.TotalDeathsByCause(DeathCause.Starvation));
        Assert.Equal(0.0, sim.World.Vegetation.TotalConsumed);
    }

    [Fact]
    public void Overcrowding_ReducesVegetationAndPopulation()
    {
        var cfg = TestData.Scenario("lush_lowland").World.Clone();
        cfg.SizeCells = 128;
        SimulationCore crowded = TestData.NewSim(config: cfg, creatures: 60);
        SimulationCore sparse = TestData.NewSim(config: cfg, creatures: 4);
        for (int day = 0; day < 30; day++)
        {
            TestData.Run(crowded, cfg.TicksPerDay);
            TestData.Run(sparse, cfg.TicksPerDay);
        }
        Assert.True(crowded.World.Vegetation.SumBiomass() < sparse.World.Vegetation.SumBiomass());
        Assert.True(crowded.CreatureCount < 60, "과밀인데도 아무도 죽지 않았습니다.");
        Assert.Equal(4, sparse.CreatureCount);
    }

    [Fact]
    public void Stats_AreSampledOverTime()
    {
        SimulationCore sim = TestData.NewSim(creatures: 10);
        TestData.Run(sim, sim.World.Config.TicksPerDay * 2);
        var samples = sim.Stats.Samples;
        Assert.True(samples.Count >= 90);
        Assert.Equal(0, samples[0].Tick);
        Assert.Equal(10, samples[^1].Counts.Sum());
        Assert.True(samples[^1].VegetationKg > 0);
        Assert.InRange(samples[^1].MeanEnergy, 0f, 1f);
        Assert.Equal(10, sim.Stats.Spawned[0]);
        Assert.Contains(sim.Events.Recent, e => e.Type == SimEventType.DayStarted);
    }

    [Fact]
    public void Snapshot_CarriesInterpolationData()
    {
        SimulationCore sim = TestData.NewSim(creatures: 10, config: new WorldConfig { Seed = 20261116, StartHour = 12f });
        var snap = new SimSnapshot();
        bool sawMovement = false;
        for (int i = 0; i < 600 && !sawMovement; i++)
        {
            sim.Step();
            sim.FillSnapshot(snap);
            for (int k = 0; k < snap.Count; k++)
            {
                CreatureSnapshot s = snap.Creatures[k];
                float moved = Vec2.Distance(s.PrevPosition, s.Position);
                Assert.True(moved <= 8f * SimClock.TickSeconds + 1e-3f, "한 틱 이동 거리가 최대 속도를 넘습니다.");
                sawMovement |= moved > 0.05f;
            }
        }
        Assert.True(sawMovement);
        Assert.True(snap.TryFind(sim.Creatures[0].Id, out CreatureSnapshot first));
        Assert.Equal(sim.Creatures[0].Position, first.Position);
        Assert.False(snap.TryFind(-5, out _));
    }

    [Fact]
    public void Details_ExposeDecisionScoresAndLog()
    {
        SimulationCore sim = TestData.NewSim(creatures: 3);
        TestData.Run(sim, 200);
        CreatureDetails? d = sim.GetDetails(sim.Creatures[0].Id);
        Assert.NotNull(d);
        Assert.Equal(BehaviorKinds.Count, d!.Scores.Length);
        Assert.True(d.LastDecisionTick >= 0);
        Assert.NotEmpty(d.Log);
        Assert.Equal(d.Behavior, d.Log[^1].Behavior);
        // 복사본을 바꿔도 코어 상태는 그대로다.
        d.Scores[0] = 999f;
        d.Log.Clear();
        Assert.NotEqual(999f, sim.Creatures[0].LastScores[0]);
        Assert.NotEmpty(sim.Creatures[0].Log);
    }
}
