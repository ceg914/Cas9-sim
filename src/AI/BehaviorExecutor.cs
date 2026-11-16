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

    private static void UpdateForage(Creature c, SpeciesDefinition sp, WorldState w, int cell, float hoursDt, long tick)
    {
        var terrain = w.Terrain;
        var veg = w.Vegetation;
        int n = terrain.Size;
        float want = sp.IntakeKgPerHour * hoursDt * c.TraitBodySize;

        // 입이 닿는 범위: 지금 셀과 사방 이웃.
        int cx = cell % n, cz = cell / n;
        float reachable = veg.EdibleAt(cell, w.Forage);
        if (cx > 0) reachable += veg.EdibleAt(cell - 1, w.Forage);
        if (cx < n - 1) reachable += veg.EdibleAt(cell + 1, w.Forage);
        if (cz > 0) reachable += veg.EdibleAt(cell - n, w.Forage);
        if (cz < n - 1) reachable += veg.EdibleAt(cell + n, w.Forage);

        // 먹기 시작하려면 넉넉해야 하고, 먹던 중이면 거의 바닥날 때까지 계속한다.
        bool eating = c.Activity == ActivityKind.Eat;
        float threshold = eating ? want * 0.5f : want * 3f;

        if (reachable >= threshold && reachable > 0f)
        {
            float got = veg.Consume(cell, want, w.Forage);
            if (got < want && cx > 0) got += veg.Consume(cell - 1, want - got, w.Forage);
            if (got < want && cx < n - 1) got += veg.Consume(cell + 1, want - got, w.Forage);
            if (got < want && cz > 0) got += veg.Consume(cell - n, want - got, w.Forage);
            if (got < want && cz < n - 1) got += veg.Consume(cell + n, want - got, w.Forage);

            c.Energy = MathUtil.Clamp01(c.Energy + got * sp.EnergyPerKg / c.TraitBodySize);
            c.BoutAmount += got;
            c.HasTarget = false;
            c.Activity = ActivityKind.Eat;
            ClearIssue(c);
            return;
        }

        if (!c.HasTarget || tick >= c.NextRetargetTick)
        {
            if (Perception.FindForageTarget(c, sp, w, want * 3f, out c.Target))
            {
                c.HasTarget = true;
                ClearIssue(c);
            }
            else
            {
                // 보이는 먹이가 없으면 멀리 옮겨 가며 찾는다.
                c.HasTarget = Perception.TryPickWanderTarget(c, w, 30f, 80f, out c.Target);
                NoteIssue(c, BehaviorResult.NoFoodFound);
            }
            c.NextRetargetTick = tick + ForageRetargetTicks;
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
