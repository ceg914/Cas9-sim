using System.Collections.Generic;
using PaleoSphere.World;

namespace PaleoSphere.Simulation;

public sealed record ScenarioPopulation(string SpeciesId, int Count);

/// <summary>시나리오(data/scenarios/*.json): 세계 설정과 초기 개체군.</summary>
public sealed class ScenarioDefinition
{
    public string Id { get; init; } = "";
    public string NameKo { get; init; } = "";
    public string NameEn { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>
    /// "scientific": 시대·지역이 겹치는 종만 쓰는 시나리오. "sandbox": 사용자가 자유롭게 섞은 구성.
    /// </summary>
    public string Mode { get; init; } = "sandbox";

    public WorldConfig World { get; init; } = new();
    public IReadOnlyList<ScenarioPopulation> Populations { get; init; } = new List<ScenarioPopulation>();

    /// <summary>시나리오대로 세계를 만들고 초기 개체군을 지도 전역에 흩어 배치한다.</summary>
    public SimulationCore Create(BiomeRegistry biomes, Creatures.SpeciesRegistry species, Ecology.PlantDefinition forage)
    {
        SimulationCore sim = SimulationCore.CreateNew(World, biomes, species, forage);
        float size = sim.World.Terrain.WorldSize;
        var center = new Core.Vec2(size * 0.5f, size * 0.5f);
        foreach (ScenarioPopulation p in Populations)
            sim.Enqueue(new SpawnCreaturesCommand(p.SpeciesId, p.Count, center, size * 0.45f));
        sim.ApplyPendingCommands();
        return sim;
    }
}
