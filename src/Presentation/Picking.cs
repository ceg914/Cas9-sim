using Godot;
using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Simulation;
using PaleoSphere.World;

namespace PaleoSphere.Presentation;

/// <summary>마우스 광선과 지형·개체의 교차 계산. 물리 엔진 없이 높이맵과 스냅샷만으로 구한다.</summary>
public static class Picking
{
    public static bool RayTerrain(TerrainGrid terrain, Vector3 origin, Vector3 dir, out Vector3 hit, out float distance)
    {
        hit = Vector3.Zero;
        distance = 0f;
        float step = terrain.CellSize * 0.5f;
        float maxDist = terrain.WorldSize * 3f + 500f;
        float prevT = 0f;
        bool prevAbove = true;
        for (float t = 0f; t < maxDist; t += step)
        {
            Vector3 p = origin + dir * t;
            bool inside = p.X >= 0 && p.Z >= 0 && p.X <= terrain.WorldSize && p.Z <= terrain.WorldSize;
            bool above = !inside || p.Y > terrain.HeightAt(new Vec2(p.X, p.Z));
            if (!above && inside)
            {
                if (!prevAbove) return false;
                float lo = prevT, hi = t;
                for (int i = 0; i < 10; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Vector3 m = origin + dir * mid;
                    if (m.Y > terrain.HeightAt(new Vec2(m.X, m.Z))) lo = mid;
                    else hi = mid;
                }
                distance = hi;
                hit = origin + dir * hi;
                return true;
            }
            prevT = t;
            prevAbove = above;
            if (p.Y < -200f) break;
        }
        return false;
    }

    /// <summary>광선에 가장 먼저 닿는 개체의 id. 없으면 -1.</summary>
    public static int PickCreature(SimSnapshot snapshot, SpeciesRegistry species, TerrainGrid terrain,
        Vector3 origin, Vector3 dir, float maxDistance)
    {
        int best = -1;
        float bestT = maxDistance;
        for (int i = 0; i < snapshot.Count; i++)
        {
            CreatureSnapshot c = snapshot.Creatures[i];
            float radius = species[c.SpeciesIndex].BodyLengthM * 0.38f * c.BodySize;
            var center = new Vector3(c.Position.X, terrain.HeightAt(c.Position) + radius * 0.8f, c.Position.Z);
            Vector3 oc = origin - center;
            float b = oc.Dot(dir);
            float cc = oc.Dot(oc) - radius * radius;
            float disc = b * b - cc;
            if (disc < 0f) continue;
            float t = -b - Mathf.Sqrt(disc);
            if (t < 0f || t >= bestT) continue;
            bestT = t;
            best = c.Id;
        }
        return best;
    }
}
