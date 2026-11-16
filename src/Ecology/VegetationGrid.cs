using System;
using PaleoSphere.Core;
using PaleoSphere.World;

namespace PaleoSphere.Ecology;

/// <summary>
/// 셀별 식생 바이오매스(kg). 성장·고사·섭식이 실제 값을 바꾸고, 변화량을 누계로 남겨 보존 여부를 검증한다.
/// </summary>
public sealed class VegetationGrid
{
    public int Size { get; }
    public float[] Biomass { get; }
    public float[] Capacity { get; }

    public double InitialTotal { get; set; }
    public double TotalGrown { get; set; }
    public double TotalConsumed { get; set; }
    public double TotalDieback { get; set; }

    /// <summary>마지막 Update에서 계산한 전체 바이오매스(kg).</summary>
    public double LastTotal { get; private set; }

    public VegetationGrid(int size)
    {
        Size = size;
        Biomass = new float[size * size];
        Capacity = new float[size * size];
    }

    /// <summary>생물군계·토양·설정에서 셀별 수용량을 계산한다. 지형이 바뀌면 다시 부른다.</summary>
    public void RebuildCapacity(TerrainGrid terrain, BiomeRegistry biomes, WorldConfig cfg)
    {
        float area = terrain.CellSize * terrain.CellSize;
        for (int i = 0; i < Capacity.Length; i++)
        {
            BiomeDefinition b = biomes[terrain.Biome[i]];
            float k = terrain.IsWater(i) ? 0f : b.VegetationCapacityKgM2 * area * (0.5f + 0.5f * terrain.Fertility[i]) * cfg.VegetationCapacityScale;
            Capacity[i] = k;
            if (Biomass[i] > k) Biomass[i] = k;
        }
    }

    public void InitializeBiomass(TerrainGrid terrain, WorldConfig cfg)
    {
        float cs = terrain.CellSize;
        int n = Size;
        double total = 0;
        for (int cz = 0; cz < n; cz++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                int i = cz * n + cx;
                float noise = ValueNoise.Fbm(cfg.Seed + 67UL, (cx + 0.5f) * cs / 40f, (cz + 0.5f) * cs / 40f, 2);
                float b = Capacity[i] * MathUtil.Clamp01(cfg.InitialVegetation * (0.7f + 0.6f * noise));
                Biomass[i] = b;
                total += b;
            }
        }
        InitialTotal = total;
        LastTotal = total;
        TotalGrown = TotalConsumed = TotalDieback = 0;
    }

    /// <summary>뜯어 먹을 수 있는 양(kg). 잔여분은 남긴다.</summary>
    public float EdibleAt(int cell, PlantDefinition plant)
    {
        float e = Biomass[cell] - plant.ResidualFraction * Capacity[cell];
        return e > 0f ? e : 0f;
    }

    /// <summary>wantKg만큼 먹으려 시도하고 실제로 줄어든 양을 돌려준다. 음수가 되지 않는다.</summary>
    public float Consume(int cell, float wantKg, PlantDefinition plant)
    {
        if (!(wantKg > 0f)) return 0f;
        float edible = EdibleAt(cell, plant);
        float take = wantKg < edible ? wantKg : edible;
        if (take <= 0f) return 0f;
        float before = Biomass[cell];
        float after = before - take;
        if (after < 0f) after = 0f;
        Biomass[cell] = after;
        float actual = before - after;
        TotalConsumed += actual;
        return actual;
    }

    /// <summary>daysDt(게임 내 일수)만큼 성장·고사를 진행한다.</summary>
    public void Update(TerrainGrid terrain, BiomeRegistry biomes, PlantDefinition plant, WorldConfig cfg, in EnvironmentState env, float daysDt)
    {
        float temp = env.TemperatureC;
        float tempFactor;
        if (temp <= plant.MinTemperatureC || temp >= plant.MaxTemperatureC) tempFactor = 0f;
        else if (temp < plant.OptimalLowC) tempFactor = (temp - plant.MinTemperatureC) / (plant.OptimalLowC - plant.MinTemperatureC);
        else if (temp > plant.OptimalHighC) tempFactor = (plant.MaxTemperatureC - temp) / (plant.MaxTemperatureC - plant.OptimalHighC);
        else tempFactor = 1f;

        float baseRate = plant.GrowthRatePerDay * env.Light * tempFactor * cfg.VegetationGrowthScale;
        float moistSpan = MathF.Max(1e-3f, plant.OptimalMoisture - plant.MinMoisture);

        double grown = 0, died = 0, total = 0;
        for (int i = 0; i < Biomass.Length; i++)
        {
            float k = Capacity[i];
            if (k <= 0f) continue;
            float b = Biomass[i];
            float m = MathUtil.Clamp01((terrain.Moisture[i] - plant.MinMoisture) / moistSpan);

            float growth = 0f;
            if (b < k && baseRate > 0f)
            {
                float rate = baseRate * m * biomes[terrain.Biome[i]].GrowthFactor;
                growth = rate * (b + plant.SeedFraction * k) * (1f - b / k) * daysDt;
                if (growth > k - b) growth = k - b;
            }

            float stress = MathF.Max(1f - m, 1f - tempFactor) - 0.5f;
            float dieback = 0f;
            if (stress > 0f)
            {
                dieback = plant.DiebackRatePerDay * (stress * 2f) * b * daysDt;
                if (dieback > b) dieback = b;
            }

            float nb = b + growth - dieback;
            if (nb < 0f) nb = 0f;
            Biomass[i] = nb;
            grown += growth;
            died += dieback;
            total += nb;
        }
        TotalGrown += grown;
        TotalDieback += died;
        LastTotal = total;
    }

    public double SumBiomass()
    {
        double total = 0;
        for (int i = 0; i < Biomass.Length; i++) total += Biomass[i];
        return total;
    }

    public double SumCapacity()
    {
        double total = 0;
        for (int i = 0; i < Capacity.Length; i++) total += Capacity[i];
        return total;
    }

    public void AddToHash(ref StateHasher h)
    {
        h.Add(Biomass.AsSpan());
        h.Add(TotalGrown); h.Add(TotalConsumed); h.Add(TotalDieback); h.Add(InitialTotal);
    }
}
