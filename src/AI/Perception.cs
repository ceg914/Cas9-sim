using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Simulation;

namespace PaleoSphere.AI;

/// <summary>먹이 탐지. 주변 셀을 고리 모양으로 표본 조사한다(전체 탐색 대신 비용을 고정).</summary>
public static class Perception
{
    private static readonly float[] Rings = { 5f, 10f, 17f, 26f, 37f, 50f };
    private const int Directions = 8;

    /// <summary>
    /// '갈 만한 셀'의 기준(kg): 한 번 뜯는 양의 두 배와 평균 수용량의 30% 중 작은 쪽.
    /// 척박한 세계에서도 기준이 닿을 수 있는 수준으로 내려간다.
    /// </summary>
    public static float GoodCellKg(WorldState w, float biteKg) =>
        System.MathF.Min(biteKg * 2f, w.Vegetation.MeanLandCapacity * 0.3f);

    /// <summary>현재 위치나 가까운 곳에 먹을 만한 식생이 있는지(난수 사용 없음).</summary>
    public static bool IsFoodNearby(Creature c, WorldState w, float minEdibleKg)
    {
        var terrain = w.Terrain;
        if (w.Vegetation.EdibleAt(terrain.CellIndexAt(c.Position), w.Forage) >= minEdibleKg) return true;
        for (int ring = 1; ring <= 2; ring++)
        {
            float r = ring == 1 ? 8f : 20f;
            for (int k = 0; k < Directions; k++)
            {
                Vec2 p = c.Position + Vec2.FromHeading(k * MathUtil.TwoPi / Directions) * r;
                if (!terrain.IsWalkableAt(p)) continue;
                if (w.Vegetation.EdibleAt(terrain.CellIndexAt(p), w.Forage) < minEdibleKg) continue;
                if (terrain.IsLineWalkable(c.Position, p)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 지각 반경 안에서 '먹을 수 있는 양 ÷ 거리'가 가장 좋은 지점을 고른다.
    /// 개체마다 조사 방향을 난수로 돌려 모두가 같은 셀로 몰리지 않게 한다.
    /// </summary>
    public static bool FindForageTarget(Creature c, SpeciesDefinition sp, WorldState w, float minEdibleKg, out Vec2 target, out float bestEdible)
    {
        var terrain = w.Terrain;
        float rot = c.Rng.NextFloat() * (MathUtil.TwoPi / Directions);
        float bestScore = 0f;
        target = c.Position;
        bestEdible = 0f;
        bool found = false;
        for (int ri = 0; ri < Rings.Length; ri++)
        {
            float r = Rings[ri];
            if (r > sp.PerceptionRadiusM) break;
            for (int k = 0; k < Directions; k++)
            {
                float angle = rot + k * (MathUtil.TwoPi / Directions) + ri * 0.39f;
                Vec2 p = c.Position + Vec2.FromHeading(angle) * r;
                if (!terrain.IsWalkableAt(p)) continue;
                int cell = terrain.CellIndexAt(p);
                float edible = w.Vegetation.EdibleAt(cell, w.Forage);
                if (edible < minEdibleKg) continue;
                float score = edible / (1f + r / 15f);
                if (score > bestScore && terrain.IsLineWalkable(c.Position, p))
                {
                    bestScore = score;
                    bestEdible = edible;
                    target = terrain.CellCenter(cell);
                    found = true;
                }
            }
        }
        return found;
    }

    /// <summary>minDist~maxDist 떨어진 걸을 수 있는 임의 지점. 몇 번 시도해 못 찾으면 false.</summary>
    public static bool TryPickWanderTarget(Creature c, WorldState w, float minDist, float maxDist, out Vec2 target)
    {
        var terrain = w.Terrain;
        float margin = terrain.CellSize * 2f;
        float size = terrain.WorldSize;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float angle = c.Rng.NextFloat() * MathUtil.TwoPi;
            float dist = c.Rng.Range(minDist, maxDist);
            Vec2 p = c.Position + Vec2.FromHeading(angle) * dist;
            if (p.X < margin || p.Z < margin || p.X > size - margin || p.Z > size - margin) continue;
            if (!terrain.IsLineWalkable(c.Position, p)) continue;
            target = p;
            return true;
        }
        target = c.Position;
        return false;
    }

    /// <summary>
    /// 먹이가 빈약한 곳을 떠날 때의 먼 목적지. 지금 향하던 쪽을 우선해 같은 자리를 맴돌지 않게 한다.
    /// </summary>
    public static bool TryPickRelocationTarget(Creature c, WorldState w, float minDist, float maxDist, out Vec2 target)
    {
        var terrain = w.Terrain;
        float margin = terrain.CellSize * 4f;
        float size = terrain.WorldSize;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = attempt < 3
                ? c.Heading + c.Rng.Range(-0.9f, 0.9f)
                : c.Rng.NextFloat() * MathUtil.TwoPi;
            float dist = c.Rng.Range(minDist, maxDist);
            Vec2 p = c.Position + Vec2.FromHeading(angle) * dist;
            if (p.X < margin || p.Z < margin || p.X > size - margin || p.Z > size - margin) continue;
            if (!terrain.IsDryLandAt(p) || !terrain.IsLineWalkable(c.Position, p)) continue;
            target = p;
            return true;
        }
        target = c.Position;
        return false;
    }
}
