using System;
using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Simulation;

namespace PaleoSphere.AI;

/// <summary>
/// 고른 행동을 실제 동작으로 옮기는 상태 머신. 섭식·음수의 자원 변화도 여기서 일어난다.
/// 생리 갱신 주기마다 한 번 호출된다.
/// </summary>
public static class BehaviorExecutor
{
    public const float ArriveDistance = 1.5f;
    public const int ForageRetargetTicks = 100;
    public const int ExploreRetargetTicks = 600;
    public const int RelocateRetargetTicks = 900;

    /// <summary>먹기 시작하는 기준: 닿는 범위의 먹이가 한 번 뜯는 양의 몇 배인지.</summary>
    public const float GoodPatchFactor = 8f;
    public const float StarvingEnergy = 0.25f;

    /// <summary>행동을 바꾸고 로그를 남긴다. 같은 행동이면 아무것도 하지 않는다.</summary>
    public static void SwitchBehavior(Creature c, BehaviorKind next, long tick)
    {
        if (c.Behavior == next && c.Log.Count > 0) return;

        if (c.Log.Count > 0)
        {
            BehaviorLogEntry last = c.Log[^1];
            if (last.Result == BehaviorResult.Ongoing)
                last.Result = UtilityBrain.IsCompleted(c) ? BehaviorResult.Completed : BehaviorResult.Interrupted;
            last.ResultAmount = c.BoutAmount;
            last.EndTick = tick;
            c.Log[^1] = last;
        }

        c.Behavior = next;
        c.BehaviorStartTick = tick;
        c.BoutAmount = 0f;
        c.HasTarget = false;
        c.NextRetargetTick = 0;
        c.StuckTicks = 0;

        c.Log.Add(new BehaviorLogEntry
        {
            Tick = tick,
            Behavior = next,
            Reason = c.LastReason,
            Score = c.LastScores[(int)next],
            Energy = c.Energy,
            Hydration = c.Hydration,
            Fatigue = c.Fatigue,
            Fear = c.Fear,
            Result = BehaviorResult.Ongoing,
            EndTick = -1,
        });
        if (c.Log.Count > Creature.MaxLogEntries) c.Log.RemoveAt(0);
    }

    private static void NoteIssue(Creature c, BehaviorResult issue)
    {
        if (c.Log.Count == 0) return;
        BehaviorLogEntry last = c.Log[^1];
        last.Result = issue;
        c.Log[^1] = last;
    }

    private static void ClearIssue(Creature c)
    {
        if (c.Log.Count == 0) return;
        BehaviorLogEntry last = c.Log[^1];
        if (last.Result == BehaviorResult.Ongoing) return;
        last.Result = BehaviorResult.Ongoing;
        c.Log[^1] = last;
    }

    public static void Update(Creature c, SpeciesDefinition sp, WorldState w, float hoursDt)
    {
        long tick = w.Clock.Tick;
        var terrain = w.Terrain;
        int cell = terrain.CellIndexAt(c.Position);

        if (c.HasTarget && Vec2.DistanceSquared(c.Position, c.Target) < ArriveDistance * ArriveDistance)
            c.HasTarget = false;

        switch (c.Behavior)
        {
            case BehaviorKind.Rest:
                c.HasTarget = false;
                c.Activity = ActivityKind.Rest;
                c.BoutAmount += hoursDt;
                break;

            case BehaviorKind.Drink:
                c.HasTarget = false;
                if (terrain.Drinkable[cell])
                {
                    c.Activity = ActivityKind.Drink;
                    float before = c.Hydration;
                    c.Hydration = MathUtil.Clamp01(c.Hydration + hoursDt / sp.DrinkHoursToFull);
                    c.BoutAmount += c.Hydration - before;
                    ClearIssue(c);
                }
                else if (!float.IsPositiveInfinity(terrain.WaterPathDistance[cell]))
                {
                    c.Activity = ActivityKind.Walk;
                }
                else
                {
                    c.Activity = ActivityKind.Stand;
                    NoteIssue(c, BehaviorResult.NoWaterReachable);
                }
                break;

            case BehaviorKind.Forage:
                UpdateForage(c, sp, w, cell, hoursDt, tick);
                break;

            case BehaviorKind.Explore:
                if (!c.HasTarget || tick >= c.NextRetargetTick)
                {
                    c.HasTarget = Perception.TryPickWanderTarget(c, w, 20f, 70f, out c.Target);
                    c.NextRetargetTick = tick + ExploreRetargetTicks;
                }
                c.Activity = c.HasTarget ? ActivityKind.Walk : ActivityKind.Stand;
                break;

            case BehaviorKind.Flee:
            {
                Vec2 away = c.Position - c.ThreatPosition;
                Vec2 dir = away.LengthSquared > 1e-4f ? away.Normalized() : Vec2.FromHeading(c.Heading);
                float margin = terrain.CellSize * 2f;
                float size = terrain.WorldSize;
                Vec2 p = c.Position + dir * 40f;
                c.Target = new Vec2(MathUtil.Clamp(p.X, margin, size - margin), MathUtil.Clamp(p.Z, margin, size - margin));
                c.HasTarget = true;
                c.Activity = ActivityKind.Run;
                break;
            }

            default:
                c.HasTarget = false;
                c.Activity = ActivityKind.Stand;
                break;
        }
    }

    /// <summary>입이 닿는 범위(지금 셀과 둘레 여덟 셀)에서 먹을 수 있는 양(kg).</summary>
    private static float ReachableForage(WorldState w, int cell)
    {
        int n = w.Terrain.Size;
        int cx = cell % n, cz = cell / n;
        float total = 0f;
        for (int dz = -1; dz <= 1; dz++)
        {
            int z = cz + dz;
            if (z < 0 || z >= n) continue;
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx;
                if (x < 0 || x >= n) continue;
                total += w.Vegetation.EdibleAt(z * n + x, w.Forage);
            }
        }
        return total;
    }

    /// <summary>닿는 범위에서 wantKg까지 뜯어 먹고 실제로 먹은 양을 돌려준다. 지금 셀부터 고정된 순서로 먹는다.</summary>
    private static float ConsumeReachable(WorldState w, int cell, float wantKg)
    {
        int n = w.Terrain.Size;
        int cx = cell % n, cz = cell / n;
        float got = w.Vegetation.Consume(cell, wantKg, w.Forage);
        for (int dz = -1; dz <= 1 && got < wantKg; dz++)
        {
            int z = cz + dz;
            if (z < 0 || z >= n) continue;
            for (int dx = -1; dx <= 1 && got < wantKg; dx++)
            {
                int x = cx + dx;
                if (x < 0 || x >= n || (dx == 0 && dz == 0)) continue;
                got += w.Vegetation.Consume(z * n + x, wantKg - got, w.Forage);
            }
        }
        return got;
    }

    private static void UpdateForage(Creature c, SpeciesDefinition sp, WorldState w, int cell, float hoursDt, long tick)
    {
        float want = sp.IntakeKgPerHour * hoursDt * c.TraitBodySize;
        float reachable = ReachableForage(w, cell);

        // 넉넉한 자리에서만 먹기 시작하고, 먹던 중이면 거의 바닥날 때까지 계속한다.
        // 굶주림이 심하면 기준을 낮춰 부스러기라도 먹는다.
        bool eating = c.Activity == ActivityKind.Eat;
        float goodCell = Perception.GoodCellKg(w, want);
        float startKg = MathF.Min(want * GoodPatchFactor, goodCell * 8f);
        if (c.Energy < StarvingEnergy) startKg = MathF.Min(startKg, want * 3f);
        float threshold = eating ? MathF.Min(want, startKg * 0.5f) : startKg;

        if (reachable >= threshold && reachable > 0f)
        {
            float got = ConsumeReachable(w, cell, want);
            c.Energy = MathUtil.Clamp01(c.Energy + got * sp.EnergyPerKg / c.TraitBodySize);
            c.BoutAmount += got;
            c.HasTarget = false;
            c.Activity = ActivityKind.Eat;
            ClearIssue(c);
            return;
        }

        if (!c.HasTarget || tick >= c.NextRetargetTick)
        {
            bool found = Perception.FindForageTarget(c, sp, w, goodCell * 0.5f, out Vec2 target, out float bestEdible);
            if (found && bestEdible >= goodCell)
            {
                c.Target = target;
                c.HasTarget = true;
                c.NextRetargetTick = tick + ForageRetargetTicks;
                ClearIssue(c);
            }
            else if (Perception.TryPickRelocationTarget(c, w, 60f, 140f, out c.Target))
            {
                // 주변이 빈약하면 부스러기를 찾아다니지 않고 멀리 옮겨 간다.
                c.HasTarget = true;
                c.NextRetargetTick = tick + RelocateRetargetTicks;
                NoteIssue(c, BehaviorResult.NoFoodFound);
            }
            else
            {
                c.Target = target;
                c.HasTarget = found;
                c.NextRetargetTick = tick + ForageRetargetTicks;
                NoteIssue(c, BehaviorResult.NoFoodFound);
            }
        }
        c.Activity = c.HasTarget ? ActivityKind.Walk : ActivityKind.Stand;
    }

    /// <summary>이동이 계속 막혔을 때 호출. 목표를 버리고 다음 갱신에서 다시 고르게 한다.</summary>
    public static void OnBlocked(Creature c)
    {
        c.HasTarget = false;
        c.NextRetargetTick = 0;
        c.StuckTicks = 0;
        NoteIssue(c, BehaviorResult.TargetBlocked);
    }
}
