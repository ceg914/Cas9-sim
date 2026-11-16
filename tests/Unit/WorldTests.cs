using System;
using PaleoSphere.Core;
using PaleoSphere.Simulation;
using PaleoSphere.World;
using Xunit;

namespace PaleoSphere.Tests.Unit;

public class WorldTests
{
    [Fact]
    public void Terrain_SameSeed_IsIdentical()
    {
        TerrainGrid a = TerrainGenerator.Generate(TestData.SmallConfig(11), TestData.Data.Biomes);
        TerrainGrid b = TerrainGenerator.Generate(TestData.SmallConfig(11), TestData.Data.Biomes);
        StateHasher ha = StateHasher.Create(), hb = StateHasher.Create();
        a.AddToHash(ref ha);
        b.AddToHash(ref hb);
        Assert.Equal(ha.Value, hb.Value);
    }

    [Fact]
    public void Terrain_DifferentSeed_Differs()
    {
        TerrainGrid a = TerrainGenerator.Generate(TestData.SmallConfig(11), TestData.Data.Biomes);
        TerrainGrid b = TerrainGenerator.Generate(TestData.SmallConfig(12), TestData.Data.Biomes);
        StateHasher ha = StateHasher.Create(), hb = StateHasher.Create();
        a.AddToHash(ref ha);
        b.AddToHash(ref hb);
        Assert.NotEqual(ha.Value, hb.Value);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(20261116UL)]
    public void Terrain_HasWaterLandAndSeveralBiomes(ulong seed)
    {
        var cfg = new WorldConfig { Seed = seed };
        TerrainGrid t = TerrainGenerator.Generate(cfg, TestData.Data.Biomes);
        int water = 0, walkable = 0, drinkable = 0;
        var biomes = new bool[TestData.Data.Biomes.Count];
        for (int i = 0; i < t.CellCount; i++)
        {
            if (t.IsWater(i)) water++;
            if (t.Walkable[i]) walkable++;
            if (t.Drinkable[i]) drinkable++;
            biomes[t.Biome[i]] = true;
            Assert.InRange(t.Moisture[i], 0f, 1f);
            Assert.InRange(t.Fertility[i], 0f, 1f);
            Assert.True(MathUtil.IsFinite(t.Height[i]));
        }
        Assert.InRange(water / (double)t.CellCount, 0.03, 0.30);
        Assert.True(walkable > t.CellCount / 2);
        Assert.True(drinkable > 0);
        Assert.True(Array.FindAll(biomes, b => b).Length >= 3);
    }

    [Fact]
    public void Terrain_WaterCellsUseWaterBiome_AndDeepWaterIsNotWalkable()
    {
        TerrainGrid t = TerrainGenerator.Generate(new WorldConfig { Seed = 5 }, TestData.Data.Biomes);
        int waterBiome = TestData.Data.Biomes.IndexOf("water");
        for (int i = 0; i < t.CellCount; i++)
        {
            Assert.Equal(t.IsWater(i), t.Biome[i] == waterBiome);
            if (t.WaterDepth(i) > TerrainGrid.WadeDepth) Assert.False(t.Walkable[i]);
        }
    }

    [Fact]
    public void WaterDistance_FollowingFlowReachesDrinkableCell()
    {
        TerrainGrid t = TerrainGenerator.Generate(new WorldConfig { Seed = 3 }, TestData.Data.Biomes);
        int tested = 0;
        for (int i = 0; i < t.CellCount && tested < 40; i += 997)
        {
            if (!t.Walkable[i] || float.IsPositiveInfinity(t.WaterPathDistance[i])) continue;
            tested++;
            Vec2 p = t.CellCenter(i);
            int guard = 0;
            while (!t.Drinkable[t.CellIndexAt(p)] && guard++ < 5000)
            {
                Assert.True(t.TryGetDirectionToWater(p, out Vec2 dir));
                Assert.True(dir.LengthSquared > 0f, "물가가 아닌데 방향이 없습니다.");
                p += dir * 0.5f;
                Assert.True(t.IsWalkableAt(p), "물로 가는 길이 걸을 수 없는 셀을 지납니다.");
            }
            Assert.True(t.Drinkable[t.CellIndexAt(p)]);
        }
        Assert.True(tested > 5);
    }

    [Fact]
    public void HeightAt_MatchesCellCenters()
    {
        TerrainGrid t = TerrainGenerator.Generate(TestData.SmallConfig(), TestData.Data.Biomes);
        for (int i = 0; i < t.CellCount; i += 131)
            Assert.Equal(t.Height[i], t.HeightAt(t.CellCenter(i)), 3);
    }

    [Fact]
    public void LineWalkable_RejectsPathsThroughDeepWater()
    {
        TerrainGrid t = TerrainGenerator.Generate(new WorldConfig { Seed = 20261116 }, TestData.Data.Biomes);
        int deep = -1;
        for (int i = 0; i < t.CellCount; i++)
            if (t.WaterDepth(i) > TerrainGrid.WadeDepth + 0.2f) { deep = i; break; }
        Assert.True(deep >= 0);
        Vec2 c = t.CellCenter(deep);
        Assert.False(t.IsLineWalkable(c + new Vec2(-30, 0), c + new Vec2(30, 0)) && t.Walkable[deep]);
        Assert.False(t.IsLineWalkable(new Vec2(-5, 10), new Vec2(10, 10)));
    }

    [Fact]
    public void Environment_DayIsBrightNightIsDark()
    {
        var cfg = new WorldConfig();
        var noon = new SimClock(6000, 10, 12f);
        var midnight = new SimClock(6000, 10, 0f);
        Assert.True(EnvironmentState.Compute(noon, cfg).Light > 0.95f);
        Assert.False(EnvironmentState.Compute(noon, cfg).IsNight);
        Assert.Equal(0f, EnvironmentState.Compute(midnight, cfg).Light);
        Assert.True(EnvironmentState.Compute(midnight, cfg).IsNight);
        Assert.True(EnvironmentState.Compute(noon, cfg).TemperatureC > EnvironmentState.Compute(midnight, cfg).TemperatureC);
    }

    [Fact]
    public void Config_Validate_ReportsOutOfRange()
    {
        Assert.Empty(new WorldConfig().Validate());
        Assert.NotEmpty(new WorldConfig { SizeCells = 5 }.Validate());
        Assert.NotEmpty(new WorldConfig { CellSize = float.NaN }.Validate());
        Assert.NotEmpty(new WorldConfig { WaterFraction = 0.9f }.Validate());
        Assert.Throws<ArgumentException>(() => SimulationCore.CreateNew(new WorldConfig { SizeCells = 5 },
            TestData.Data.Biomes, TestData.Data.Species, TestData.Data.Forage!));
    }
}
