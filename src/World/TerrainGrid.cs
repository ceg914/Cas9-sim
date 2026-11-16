using System;
using PaleoSphere.Core;

namespace PaleoSphere.World;

/// <summary>
/// 높이·생물군계·습윤도·토양을 셀 단위로 담는 지형. 값은 셀 중심에서 정의된다.
/// Walkable 등 파생 배열은 RebuildDerived()로 다시 계산한다(저장하지 않는다).
/// </summary>
public sealed class TerrainGrid
{
    /// <summary>걸어서 건널 수 있는 최대 수심(m). 게임적 단순화.</summary>
    public const float WadeDepth = 0.7f;

    /// <summary>걸을 수 있는 최대 경사(높이차/수평거리).</summary>
    public const float MaxWalkSlope = 0.9f;

    public int Size { get; }
    public float CellSize { get; }
    public float WaterLevel { get; set; }

    public float[] Height { get; }
    public byte[] Biome { get; }
    public float[] Moisture { get; }
    public float[] Fertility { get; }

    public bool[] Walkable { get; }
    public bool[] Drinkable { get; }

    /// <summary>걸어서 가장 가까운 물가까지의 거리(m). 닿을 수 없으면 +∞.</summary>
    public float[] WaterPathDistance { get; private set; }

    public TerrainGrid(int size, float cellSize)
    {
        Size = size;
        CellSize = cellSize;
        int count = size * size;
        Height = new float[count];
        Biome = new byte[count];
        Moisture = new float[count];
        Fertility = new float[count];
        Walkable = new bool[count];
        Drinkable = new bool[count];
        WaterPathDistance = new float[count];
    }

    public int CellCount => Size * Size;
    public float WorldSize => Size * CellSize;

    public int Index(int cx, int cz) => cz * Size + cx;
    public bool InBounds(int cx, int cz) => cx >= 0 && cz >= 0 && cx < Size && cz < Size;
    public bool InBounds(Vec2 p) => p.X >= 0f && p.Z >= 0f && p.X < WorldSize && p.Z < WorldSize;

    public int CellIndexAt(Vec2 p)
    {
        int cx = MathUtil.Clamp((int)(p.X / CellSize), 0, Size - 1);
        int cz = MathUtil.Clamp((int)(p.Z / CellSize), 0, Size - 1);
        return cz * Size + cx;
    }

    public Vec2 CellCenter(int index) => new((index % Size + 0.5f) * CellSize, (index / Size + 0.5f) * CellSize);

    public float WaterDepth(int index)
    {
        float d = WaterLevel - Height[index];
        return d > 0f ? d : 0f;
    }

    public bool IsWater(int index) => Height[index] < WaterLevel - 0.02f;

    /// <summary>셀 중심 사이를 쌍선형 보간한 지면 높이.</summary>
    public float HeightAt(Vec2 p)
    {
        float fx = MathUtil.Clamp(p.X / CellSize - 0.5f, 0f, Size - 1);
        float fz = MathUtil.Clamp(p.Z / CellSize - 0.5f, 0f, Size - 1);
        int x0 = (int)fx, z0 = (int)fz;
        int x1 = Math.Min(x0 + 1, Size - 1), z1 = Math.Min(z0 + 1, Size - 1);
        float tx = fx - x0, tz = fz - z0;
        float a = MathUtil.Lerp(Height[z0 * Size + x0], Height[z0 * Size + x1], tx);
        float b = MathUtil.Lerp(Height[z1 * Size + x0], Height[z1 * Size + x1], tx);
        return MathUtil.Lerp(a, b, tz);
    }

    public bool IsWalkableAt(Vec2 p) => InBounds(p) && Walkable[CellIndexAt(p)];

    /// <summary>
    /// a에서 b까지 직선으로 걸어갈 수 있는지. 반 셀 간격으로 짚어 보며 물·급경사 셀이 끼어 있으면 false.
    /// 이동이 직선 조향이라, 목표를 고를 때 이 검사로 건널 수 없는 강 건너편을 걸러 낸다.
    /// </summary>
    public bool IsLineWalkable(Vec2 a, Vec2 b)
    {
        if (!InBounds(a) || !InBounds(b)) return false;
        Vec2 d = b - a;
        float len = d.Length;
        int steps = (int)(len / (CellSize * 0.5f)) + 1;
        for (int i = 1; i <= steps; i++)
        {
            Vec2 p = a + d * ((float)i / steps);
            if (!Walkable[CellIndexAt(p)]) return false;
        }
        return true;
    }

    /// <summary>개체를 놓을 수 있는 마른 땅인지.</summary>
    public bool IsDryLandAt(Vec2 p)
    {
        if (!InBounds(p)) return false;
        int i = CellIndexAt(p);
        return Walkable[i] && !IsWater(i);
    }

    public void RebuildDerived()
    {
        int n = Size;
        for (int cz = 0; cz < n; cz++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                int i = cz * n + cx;
                float h = Height[i];
                float maxDiff = 0f;
                if (cx > 0) maxDiff = MathF.Max(maxDiff, MathF.Abs(h - Height[i - 1]));
                if (cx < n - 1) maxDiff = MathF.Max(maxDiff, MathF.Abs(h - Height[i + 1]));
                if (cz > 0) maxDiff = MathF.Max(maxDiff, MathF.Abs(h - Height[i - n]));
                if (cz < n - 1) maxDiff = MathF.Max(maxDiff, MathF.Abs(h - Height[i + n]));
                Walkable[i] = WaterDepth(i) <= WadeDepth && maxDiff / CellSize <= MaxWalkSlope;
            }
        }

        for (int cz = 0; cz < n; cz++)
        {
            for (int cx = 0; cx < n; cx++)
            {
                int i = cz * n + cx;
                bool drink = false;
                if (Walkable[i])
                {
                    for (int dz = -1; dz <= 1 && !drink; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, nz = cz + dz;
                            if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                            if (IsWater(nz * n + nx)) { drink = true; break; }
                        }
                    }
                }
                Drinkable[i] = drink;
            }
        }

        WaterPathDistance = GridSearch.DistanceField(n, CellSize, Drinkable, Walkable);
    }

    /// <summary>
    /// 물가로 가는 최단 경로의 다음 방향. 이미 물가면 dir = 0.
    /// 물에 닿을 길이 없으면 false.
    /// </summary>
    public bool TryGetDirectionToWater(Vec2 p, out Vec2 dir)
    {
        dir = Vec2.Zero;
        int cell = CellIndexAt(p);
        float best = WaterPathDistance[cell];
        if (float.IsPositiveInfinity(best)) return false;
        if (best <= 0f) return true;

        int n = Size, cx = cell % n, cz = cell / n;
        int bestCell = -1;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = cx + dx, nz = cz + dz;
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                int next = nz * n + nx;
                if (!Walkable[next]) continue;
                if (dx != 0 && dz != 0 && (!Walkable[cz * n + nx] || !Walkable[nz * n + cx])) continue;
                float d = WaterPathDistance[next];
                if (d < best)
                {
                    best = d;
                    bestCell = next;
                }
            }
        }
        if (bestCell < 0) return true;
        dir = (CellCenter(bestCell) - p).Normalized();
        return true;
    }

    public void AddToHash(ref StateHasher h)
    {
        h.Add(Size); h.Add(CellSize); h.Add(WaterLevel);
        h.Add(Height.AsSpan());
        h.Add(Biome.AsSpan());
        h.Add(Moisture.AsSpan());
        h.Add(Fertility.AsSpan());
    }
}
