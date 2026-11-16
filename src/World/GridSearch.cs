using System;
using System.Collections.Generic;

namespace PaleoSphere.World;

/// <summary>정사각 격자 위의 다익스트라 탐색. 8방향 이동, 대각선은 모서리를 가로지르지 않는다.</summary>
public static class GridSearch
{
    private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
    private const float Diagonal = 1.41421356f;

    /// <summary>
    /// 출발 셀들(sources)에서 각 셀까지의 최단 이동 거리(m). 닿지 못하면 +∞.
    /// passable이 null이면 모든 셀을 지날 수 있다.
    /// </summary>
    public static float[] DistanceField(int size, float cellSize, bool[] sources, bool[]? passable, float maxDistance = float.PositiveInfinity)
    {
        int count = size * size;
        var dist = new float[count];
        Array.Fill(dist, float.PositiveInfinity);
        var queue = new PriorityQueue<int, float>();
        for (int i = 0; i < count; i++)
        {
            if (!sources[i]) continue;
            dist[i] = 0f;
            queue.Enqueue(i, 0f);
        }

        while (queue.TryDequeue(out int cell, out float d))
        {
            if (d > dist[cell]) continue;
            int cx = cell % size, cz = cell / size;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + Dx[k], nz = cz + Dz[k];
                if (nx < 0 || nz < 0 || nx >= size || nz >= size) continue;
                int next = nz * size + nx;
                if (passable != null)
                {
                    if (!passable[next]) continue;
                    if (k >= 4 && (!passable[cz * size + nx] || !passable[nz * size + cx])) continue;
                }
                float nd = d + (k < 4 ? cellSize : cellSize * Diagonal);
                if (nd > maxDistance || nd >= dist[next]) continue;
                dist[next] = nd;
                queue.Enqueue(next, nd);
            }
        }
        return dist;
    }

    /// <summary>셀별 비용(cellCost ≥ 0)을 따라 start에서 goal까지 가장 싼 경로. 없으면 null.</summary>
    public static List<int>? LeastCostPath(int size, int start, int goal, float[] cellCost)
    {
        int count = size * size;
        var dist = new float[count];
        var prev = new int[count];
        Array.Fill(dist, float.PositiveInfinity);
        Array.Fill(prev, -1);
        var queue = new PriorityQueue<int, float>();
        dist[start] = 0f;
        queue.Enqueue(start, 0f);

        while (queue.TryDequeue(out int cell, out float d))
        {
            if (d > dist[cell]) continue;
            if (cell == goal) break;
            int cx = cell % size, cz = cell / size;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + Dx[k], nz = cz + Dz[k];
                if (nx < 0 || nz < 0 || nx >= size || nz >= size) continue;
                int next = nz * size + nx;
                float step = (k < 4 ? 1f : Diagonal) * 0.5f * (cellCost[cell] + cellCost[next]);
                float nd = d + step;
                if (nd >= dist[next]) continue;
                dist[next] = nd;
                prev[next] = cell;
                queue.Enqueue(next, nd);
            }
        }

        if (float.IsPositiveInfinity(dist[goal])) return null;
        var path = new List<int>();
        for (int c = goal; c != -1; c = prev[c]) path.Add(c);
        path.Reverse();
        return path;
    }
}
