using System.Collections.Generic;
using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Ecology;
using PaleoSphere.World;

namespace PaleoSphere.Simulation;

/// <summary>위협 자극. 반경 안의 개체가 공포를 느끼고 달아난다. 포식자 구현 전에는 사용자 교란 도구가 만든다.</summary>
public struct ThreatSource
{
    public Vec2 Position;
    public float Radius;
    public float Intensity;
    public long UntilTick;
}

/// <summary>세계의 단일 권위 상태: 설정, 시계, 지형, 식생, 환경, 정의 데이터.</summary>
public sealed class WorldState
{
    public WorldConfig Config { get; }
    public SimClock Clock { get; }
    public TerrainGrid Terrain { get; }
    public VegetationGrid Vegetation { get; }
    public BiomeRegistry Biomes { get; }
    public SpeciesRegistry Species { get; }
    public PlantDefinition Forage { get; }
    public EnvironmentState Env;
    public List<ThreatSource> Threats { get; } = new();

    public WorldState(WorldConfig config, SimClock clock, TerrainGrid terrain, VegetationGrid vegetation,
        BiomeRegistry biomes, SpeciesRegistry species, PlantDefinition forage)
    {
        Config = config;
        Clock = clock;
        Terrain = terrain;
        Vegetation = vegetation;
        Biomes = biomes;
        Species = species;
        Forage = forage;
        Env = EnvironmentState.Compute(clock, config);
    }
}
