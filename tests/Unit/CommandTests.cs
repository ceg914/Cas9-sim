using System.Collections.Generic;
using System.Linq;
using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Simulation;
using PaleoSphere.World;
using Xunit;

namespace PaleoSphere.Tests.Unit;

/// <summary>명령 검증과 TEST-06(개체 id).</summary>
public class CommandTests
{
    private static Vec2 Center(SimulationCore sim) => new(sim.World.Terrain.WorldSize / 2, sim.World.Terrain.WorldSize / 2);

    [Fact]
    public void Spawn_PlacesRequestedCountOnDryLand()
    {
        SimulationCore sim = TestData.NewSim();
        sim.Enqueue(new SpawnCreaturesCommand(TestData.Herbivore, 25, Center(sim), 50f));
        Assert.Equal(0, sim.CreatureCount);
        sim.ApplyPendingCommands();
        Assert.Equal(25, sim.CreatureCount);
        foreach (Creature c in sim.Creatures)
        {
            Assert.True(sim.World.Terrain.IsDryLandAt(c.Position));
            Assert.InRange(c.Energy, 0f, 1f);
            Assert.InRange(c.Hydration, 0f, 1f);
        }
        CommandResult r = sim.DrainCommandResults().Single();
        Assert.True(r.Ok);
        Assert.Equal(25, r.Count);
        Assert.Empty(sim.DrainCommandResults());
    }

    [Theory]
    [InlineData("no_such_species", 5, 10f, "spawn_unknown_species")]
    [InlineData(TestData.Herbivore, 0, 10f, "spawn_bad_args")]
    [InlineData(TestData.Herbivore, 100000, 10f, "spawn_bad_args")]
    [InlineData(TestData.Herbivore, 5, float.NaN, "spawn_bad_args")]
    [InlineData(TestData.Herbivore, 5, -1f, "spawn_bad_args")]
    public void Spawn_RejectsBadInput(string species, int count, float radius, string expectedKey)
    {
        SimulationCore sim = TestData.NewSim();
        sim.Enqueue(new SpawnCreaturesCommand(species, count, Center(sim), radius));
        sim.ApplyPendingCommands();
        CommandResult r = sim.DrainCommandResults().Single();
        Assert.False(r.Ok);
        Assert.Equal(expectedKey, r.MessageKey);
        Assert.Equal(0, sim.CreatureCount);
        Assert.Contains(sim.Events.Recent, e => e.Type == SimEventType.CommandRejected);
    }

    [Fact]
    public void Spawn_OutsideWorldOrInWater_IsRejected()
    {
        SimulationCore sim = TestData.NewSim(config: new WorldConfig { Seed = 20261116 });
        sim.Enqueue(new SpawnCreaturesCommand(TestData.Herbivore, 5, new Vec2(-500, -500), 5f));
        var t = sim.World.Terrain;
        int deep = Enumerable.Range(0, t.CellCount).First(i => t.WaterDepth(i) > 1f);
        sim.Enqueue(new SpawnCreaturesCommand(TestData.Herbivore, 5, t.CellCenter(deep), 0.5f));
        sim.ApplyPendingCommands();
        List<CommandResult> results = sim.DrainCommandResults();
        Assert.All(results, r => Assert.Equal("spawn_no_land", r.MessageKey));
        Assert.Equal(0, sim.CreatureCount);
    }

    [Fact]
    public void Spawn_RespectsGlobalLimit()
    {
        SimulationCore sim = TestData.NewSim(config: new WorldConfig { Seed = 1 });
        for (int i = 0; i < SimulationCore.MaxCreatures / SimulationCore.MaxSpawnPerCommand + 1; i++)
            sim.Enqueue(new SpawnCreaturesCommand(TestData.Herbivore, SimulationCore.MaxSpawnPerCommand, Center(sim), 200f));
        sim.ApplyPendingCommands();
        Assert.True(sim.CreatureCount <= SimulationCore.MaxCreatures);
        Assert.Equal("spawn_limit", sim.DrainCommandResults().Last().MessageKey);
    }

    [Fact]
    public void Remove_DeletesCreatureAndRecordsIt()
    {
        SimulationCore sim = TestData.NewSim(creatures: 5);
        int id = sim.Creatures[2].Id;
        sim.Enqueue(new RemoveCreatureCommand(id));
        sim.Enqueue(new RemoveCreatureCommand(987654));
        sim.ApplyPendingCommands();
        Assert.Equal(4, sim.CreatureCount);
        Assert.False(sim.TryGetCreature(id, out _));
        Assert.Null(sim.GetDetails(id));
        Assert.Equal(1, sim.Stats.DeathsOf(0, DeathCause.Removed));
        Assert.Equal(0, sim.Stats.TotalNaturalDeaths());
        Assert.Contains(sim.DrainCommandResults(), r => r.MessageKey == "remove_not_found");
    }

    [Fact]
    public void Ids_AreUniqueAndNeverReused()
    {
        SimulationCore sim = TestData.NewSim(creatures: 10);
        var seen = new HashSet<int>(sim.Creatures.Select(c => c.Id));
        for (int round = 0; round < 20; round++)
        {
            sim.Enqueue(new RemoveCreatureCommand(sim.Creatures[0].Id));
            sim.Enqueue(new RemoveCreatureCommand(sim.Creatures[^1].Id));
            sim.Enqueue(new SpawnCreaturesCommand(TestData.Herbivore, 3, Center(sim), 40f));
            sim.ApplyPendingCommands();
            TestData.Run(sim, 20);
            foreach (Creature c in sim.Creatures)
            {
                if (seen.Contains(c.Id)) continue;
                Assert.True(c.Id > seen.Max(), "지워진 id가 다시 쓰였습니다.");
                seen.Add(c.Id);
            }
        }
        Assert.Equal(sim.CreatureCount, sim.Creatures.Select(c => c.Id).Distinct().Count());
        for (int i = 1; i < sim.Creatures.Count; i++) Assert.True(sim.Creatures[i].Id > sim.Creatures[i - 1].Id);
        foreach (Creature c in sim.Creatures)
        {
            Assert.True(sim.TryGetCreature(c.Id, out Creature? found));
            Assert.Same(c, found);
        }
    }

    [Fact]
    public void Disturbance_MakesNearbyCreaturesFlee_AndTheyCalmDown()
    {
        var cfg = new WorldConfig { Seed = 20261116, StartHour = 12f };
        SimulationCore sim = TestData.NewSim(config: cfg);
        sim.Enqueue(new SpawnCreaturesCommand(TestData.Herbivore, 6, new Vec2(300, 300), 15f));
        sim.Enqueue(new SpawnCreaturesCommand(TestData.Herbivore, 3, new Vec2(60, 420), 10f));
        sim.ApplyPendingCommands();
        Assert.Equal(9, sim.CreatureCount);
        Dictionary<int, Vec2> start = sim.Creatures.ToDictionary(c => c.Id, c => c.Position);

        sim.Enqueue(new DisturbanceCommand(new Vec2(300, 300), 45f, 1f));
        TestData.Run(sim, 40);

        List<Creature> near = sim.Creatures.Where(c => Vec2.Distance(start[c.Id], new Vec2(300, 300)) < 20f).ToList();
        List<Creature> far = sim.Creatures.Where(c => Vec2.Distance(start[c.Id], new Vec2(300, 300)) > 150f).ToList();
        Assert.NotEmpty(near);
        Assert.NotEmpty(far);
        Assert.All(near, c => Assert.Equal(BehaviorKind.Flee, c.Behavior));
        Assert.All(near, c => Assert.Equal(ActivityKind.Run, c.Activity));
        Assert.All(far, c => Assert.NotEqual(BehaviorKind.Flee, c.Behavior));
        Assert.Contains(near, c => c.Log.Any(l => l.Behavior == BehaviorKind.Flee));

        TestData.Run(sim, 160);
        Assert.All(near, c => Assert.True(Vec2.Distance(c.Position, new Vec2(300, 300)) > Vec2.Distance(start[c.Id], new Vec2(300, 300))));

        TestData.Run(sim, 1500);
        Assert.All(sim.Creatures, c => Assert.NotEqual(BehaviorKind.Flee, c.Behavior));
        Assert.All(sim.Creatures, c => Assert.True(c.Fear < 0.05f));
    }

    [Fact]
    public void Disturbance_RejectsBadArguments()
    {
        SimulationCore sim = TestData.NewSim();
        sim.Enqueue(new DisturbanceCommand(new Vec2(10, 10), -5f, 1f));
        sim.Enqueue(new DisturbanceCommand(new Vec2(-10, 10), 5f, 1f));
        sim.Enqueue(new DisturbanceCommand(new Vec2(10, 10), 5f, float.NaN));
        sim.ApplyPendingCommands();
        Assert.All(sim.DrainCommandResults(), r => Assert.False(r.Ok));
        Assert.Empty(sim.World.Threats);
    }
}
