using System;
using System.Collections.Generic;
using PaleoSphere.Core;

namespace PaleoSphere.World;

/// <summary>
/// 시드 기반 절차적 지형 생성. 완만한 저지대, 호수, 굽이치는 강, 생물군계 분포를 만든다.
/// 수면은 세계 전체에서 한 높이로 평평하다(범람원 가정, 게임적 단순화).
/// </summary>
public static class TerrainGenerator
{
    public static TerrainGrid Generate(WorldConfig cfg, BiomeRegistry biomes)
    {
        int n = cfg.SizeCells;
        float cs = cfg.CellSize;
        ulong seed = cfg.Seed;
        var t = new TerrainGrid(n, cs);
        int count = n * n;

        // 1. 높이: 넓은 굴곡 + 잔굴곡.
        for (int cz = 0; cz < n; cz++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                float wx = (cx + 0.5f) * cs, wz = (cz + 0.5f) * cs;
                float broad = ValueNoise.Fbm(seed + 11UL, wx / 260f, wz / 260f, 3);
                float detail = ValueNoise.Fbm(seed + 23UL, wx / 70f, wz / 70f, 4);
                t.Height[cz * n + cx] = cfg.Relief * (1.6f * (broad - 0.5f) + 0.5f * (detail - 0.5f));
            }
        }

        // 2. 수면 높이: 지정한 비율의 셀이 잠기도록 분위수로 정한다. 시드와 무관하게 호수가 생긴다.
        var sorted = (float[])t.Height.Clone();
        Array.Sort(sorted);
        if (cfg.WaterFraction > 0f)
        {
            int idx = MathUtil.Clamp((int)(cfg.WaterFraction * count), 0, count - 1);
            t.WaterLevel = sorted[idx];
        }
        else
        {
            t.WaterLevel = sorted[0] - 1f;
        }

        // 3. 강: 낮은 땅을 따라가는 최소 비용 경로를 파낸다.
        if (cfg.River) CarveRiver(t, cfg);

        // 4. 습윤도·토양.
        var water = new bool[count];
        bool anyWater = false;
        for (int i = 0; i < count; i++)
        {
            water[i] = t.IsWater(i);
            anyWater |= water[i];
        }
        float[] waterDist = anyWater
            ? GridSearch.DistanceField(n, cs, water, null, 400f)
            : CreateFilled(count, 400f);

        for (int cz = 0; cz < n; cz++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                int i = cz * n + cx;
                float wx = (cx + 0.5f) * cs, wz = (cz + 0.5f) * cs;
                float dw = MathF.Min(waterDist[i], 400f);
                float above = t.Height[i] - t.WaterLevel;
                float noise = ValueNoise.Fbm(seed + 31UL, wx / 120f, wz / 120f, 3);
                float moisture = 0.30f + 0.55f * DetMath.Exp(-dw / 35f) + 0.30f * (noise - 0.5f)
                                 + cfg.MoistureBias - 0.012f * MathF.Max(0f, above - 4f);
                t.Moisture[i] = MathUtil.Clamp01(moisture);

                float fert = 0.55f + 0.45f * ValueNoise.Fbm(seed + 43UL, wx / 90f, wz / 90f, 2)
                             + 0.10f * DetMath.Exp(-dw / 25f);
                t.Fertility[i] = MathUtil.Clamp01(fert);
            }
        }

        // 5. 생물군계.
        byte plain = (byte)biomes.IndexOf("open_plain");
        byte forest = (byte)biomes.IndexOf("forest");
        byte wetland = (byte)biomes.IndexOf("wetland");
        byte upland = (byte)biomes.IndexOf("upland");
        byte waterBiome = (byte)biomes.IndexOf("water");

        for (int cz = 0; cz < n; cz++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                int i = cz * n + cx;
                float wx = (cx + 0.5f) * cs, wz = (cz + 0.5f) * cs;
                float above = t.Height[i] - t.WaterLevel;
                float m = t.Moisture[i];
                byte b;
                if (t.IsWater(i)) b = waterBiome;
                else if ((above < 0.9f && waterDist[i] < 14f) || m > 0.88f) b = wetland;
                else if (above > 7.5f && m < 0.45f) b = upland;
                else if (ValueNoise.Fbm(seed + 53UL, wx / 95f, wz / 95f, 3) > 0.52f && m > 0.40f) b = forest;
                else b = plain;
                t.Biome[i] = b;
            }
        }

        t.RebuildDerived();
        return t;
    }

    private static float[] CreateFilled(int count, float value)
    {
        var a = new float[count];
        Array.Fill(a, value);
        return a;
    }

    private static void CarveRiver(TerrainGrid t, WorldConfig cfg)
    {
        int n = t.Size;
        float cs = t.CellSize;
        ulong seed = cfg.Seed;
        var rng = Pcg32.Create(seed, 0x52495645UL);
        bool westEast = rng.NextFloat() < 0.5f;

        int start = PickEdgeCell(t, ref rng, westEast, first: true);
        int goal = PickEdgeCell(t, ref rng, westEast, first: false);

        var cost = new float[n * n];
        for (int cz = 0; cz < n; cz++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                int i = cz * n + cx;
                float wx = (cx + 0.5f) * cs, wz = (cz + 0.5f) * cs;
                float above = MathF.Max(0f, t.Height[i] - t.WaterLevel);
                float meander = ValueNoise.Fbm(seed + 37UL, wx / 60f, wz / 60f, 2);
                cost[i] = 1f + 0.9f * above * MathF.Sqrt(above) + 2.5f * meander;
            }
        }

        List<int>? path = GridSearch.LeastCostPath(n, start, goal, cost);
        if (path == null) return;

        var onPath = new bool[n * n];
        foreach (int c in path) onPath[c] = true;

        const float halfWidth = 4f;
        const float bankRadius = 12f;
        float[] dist = GridSearch.DistanceField(n, cs, onPath, null, bankRadius);

        for (int cz = 0; cz < n; cz++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                int i = cz * n + cx;
                float d = dist[i];
                if (d > bankRadius) continue;
                float h = t.Height[i];
                if (d <= halfWidth)
                {
                    // 여울(얕은 곳)과 깊은 곳이 번갈아 나오도록 수심을 바꾼다.
                    float wx = (cx + 0.5f) * cs, wz = (cz + 0.5f) * cs;
                    float ford = ValueNoise.Sample(seed + 41UL, wx / 45f, wz / 45f);
                    float depthCenter = MathUtil.Lerp(0.45f, 1.5f, MathUtil.SmoothStep(0.35f, 0.6f, ford));
                    float r = d / halfWidth;
                    float bed = t.WaterLevel - 0.06f - depthCenter * (1f - r * r);
                    if (bed < h) t.Height[i] = bed;
                }
                else if (h > t.WaterLevel + 0.1f)
                {
                    float k = MathUtil.SmoothStep(halfWidth, bankRadius, d);
                    float bank = MathUtil.Lerp(t.WaterLevel + 0.1f, h, k);
                    if (bank < h) t.Height[i] = bank;
                }
            }
        }
    }

    /// <summary>가장자리의 임의 구간에서 가장 낮은 셀을 고른다.</summary>
    private static int PickEdgeCell(TerrainGrid t, ref Pcg32 rng, bool westEast, bool first)
    {
        int n = t.Size;
        int window = Math.Max(4, n * 2 / 5);
        int from = rng.NextInt(n - window + 1);
        int best = -1;
        float bestH = float.PositiveInfinity;
        for (int k = from; k < from + window; k++)
        {
            int cx = westEast ? (first ? 0 : n - 1) : k;
            int cz = westEast ? k : (first ? 0 : n - 1);
            int i = cz * n + cx;
            if (t.Height[i] < bestH)
            {
                bestH = t.Height[i];
                best = i;
            }
        }
        return best;
    }
}
