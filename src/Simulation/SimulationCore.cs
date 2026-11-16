using System;
using System.Collections.Generic;
using PaleoSphere.AI;
using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Diagnostics;
using PaleoSphere.Ecology;
using PaleoSphere.World;

namespace PaleoSphere.Simulation;

/// <summary>
/// 시뮬레이션 코어. Step() 한 번이 고정 틱 하나다. 렌더링 프레임률과 무관하게 같은 순서로 계산하므로
/// 같은 시드·설정·명령 순서에서 같은 상태 해시가 나온다.
/// </summary>
public sealed class SimulationCore
{
    /// <summary>생리·행동 실행 주기(틱). 개체 id로 엇갈려 부하를 나눈다.</summary>
    public const int PhysiologyIntervalTicks = 5;

    /// <summary>의사결정 주기(틱).</summary>
    public const int DecisionIntervalTicks = 10;

    /// <summary>식생 갱신 주기(틱).</summary>
    public const int VegetationIntervalTicks = 50;

    public const int MaxCreatures = 5000;
    public const int MaxSpawnPerCommand = 500;
    private const int ThreatMemoryTicks = 300;
    private const float FearDecayPerHour = 1.0f;

    public WorldState World { get; }
    public EventBus Events { get; } = new();
    public PopulationStats Stats { get; }
    public TickProfiler Profiler { get; } = new();

    private readonly List<Creature> _creatures = new();
    private readonly Dictionary<int, Creature> _byId = new();
    private readonly Queue<ISimCommand> _pending = new();
    private readonly List<CommandResult> _results = new();
    private readonly Steering _steering = new();

    public int NextCreatureId { get; internal set; } = 1;

    /// <summary>배치 위치 등 세계 수준의 난수.</summary>
    public Pcg32 WorldRng;

    public IReadOnlyList<Creature> Creatures => _creatures;
    public int CreatureCount => _creatures.Count;
    public SimClock Clock => World.Clock;

    public SimulationCore(WorldState world, PopulationStats? stats = null)
    {
        World = world;
        Stats = stats ?? new PopulationStats(world.Species.Count, Math.Max(1, world.Config.TicksPerDay / 48));
        WorldRng = Pcg32.Create(world.Config.Seed, 0x574F524CUL);
    }

    /// <summary>설정과 정의 데이터로 새 세계를 만든다.</summary>
    public static SimulationCore CreateNew(WorldConfig config, BiomeRegistry biomes, SpeciesRegistry species, PlantDefinition forage)
    {
        List<string> errors = config.Validate();
        if (errors.Count > 0) throw new ArgumentException("세계 설정 오류: " + string.Join(" ", errors));
        List<string> missing = biomes.MissingRequired();
        if (missing.Count > 0) throw new ArgumentException("필수 생물군계 정의 누락: " + string.Join(", ", missing));

        config = config.Clone();
        var clock = new SimClock(config.TicksPerDay, config.DaysPerSeason, config.StartHour);
        TerrainGrid terrain = TerrainGenerator.Generate(config, biomes);
        var vegetation = new VegetationGrid(config.SizeCells);
        vegetation.RebuildCapacity(terrain, biomes, config);
        vegetation.InitializeBiomass(terrain, config);

        var sim = new SimulationCore(new WorldState(config, clock, terrain, vegetation, biomes, species, forage));
        sim.Publish(SimEventType.WorldCreated, -1, "", "", Vec2.Zero);
        sim.Stats.Sample(0, 0f, sim._creatures, vegetation.LastTotal);
        return sim;
    }

    // ---------------------------------------------------------------- 명령

    public void Enqueue(ISimCommand command) => _pending.Enqueue(command);

    public int PendingCommandCount => _pending.Count;

    /// <summary>쌓인 명령을 넣은 순서대로 적용한다. Step()이 틱 시작에 부르고, 일시정지 중에는 앱이 직접 부른다.</summary>
    public void ApplyPendingCommands()
    {
        while (_pending.Count > 0)
        {
            ISimCommand cmd = _pending.Dequeue();
            CommandResult result = cmd switch
            {
                SpawnCreaturesCommand spawn => ExecuteSpawn(spawn),
                RemoveCreatureCommand remove => ExecuteRemove(remove),
                DisturbanceCommand disturbance => ExecuteDisturbance(disturbance),
                _ => new CommandResult(Clock.Tick, cmd.Name, false, 0, "cmd_unknown"),
            };
            _results.Add(result);
            if (_results.Count > 64) _results.RemoveAt(0);
            if (!result.Ok) Publish(SimEventType.CommandRejected, -1, "", result.MessageKey, Vec2.Zero);
        }
    }

    /// <summary>처리된 명령 결과를 꺼내 간다(UI 알림용).</summary>
    public List<CommandResult> DrainCommandResults()
    {
        var copy = new List<CommandResult>(_results);
        _results.Clear();
        return copy;
    }

    private CommandResult ExecuteSpawn(SpawnCreaturesCommand cmd)
    {
        long tick = Clock.Tick;
        if (!World.Species.TryGetIndex(cmd.SpeciesId, out int speciesIndex))
            return new CommandResult(tick, cmd.Name, false, 0, "spawn_unknown_species");
        if (cmd.Count < 1 || cmd.Count > MaxSpawnPerCommand || !MathUtil.IsFinite(cmd.Radius) || cmd.Radius < 0f
            || !MathUtil.IsFinite(cmd.Center.X) || !MathUtil.IsFinite(cmd.Center.Z))
            return new CommandResult(tick, cmd.Name, false, 0, "spawn_bad_args");
        if (_creatures.Count + cmd.Count > MaxCreatures)
            return new CommandResult(tick, cmd.Name, false, 0, "spawn_limit");

        SpeciesDefinition sp = World.Species[speciesIndex];
        var terrain = World.Terrain;
        int spawned = 0;
        for (int k = 0; k < cmd.Count; k++)
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = WorldRng.NextFloat() * MathUtil.TwoPi;
                float r = cmd.Radius * MathF.Sqrt(WorldRng.NextFloat());
                Vec2 p = cmd.Center + Vec2.FromHeading(angle) * r;
                if (!terrain.IsDryLandAt(p) || !IsPlacementBiomeOk(sp, p)) continue;
                AddCreature(CreateCreature(speciesIndex, sp, p));
                spawned++;
                break;
            }
        }
        return spawned > 0
            ? new CommandResult(tick, cmd.Name, true, spawned, spawned == cmd.Count ? "spawn_ok" : "spawn_partial")
            : new CommandResult(tick, cmd.Name, false, 0, "spawn_no_land");
    }

    private bool IsPlacementBiomeOk(SpeciesDefinition sp, Vec2 p)
    {
        if (sp.PlacementBiomes.Count == 0) return true;
        string biomeId = World.Biomes[World.Terrain.Biome[World.Terrain.CellIndexAt(p)]].Id;
        for (int i = 0; i < sp.PlacementBiomes.Count; i++)
            if (sp.PlacementBiomes[i] == biomeId) return true;
        return false;
    }

    private Creature CreateCreature(int speciesIndex, SpeciesDefinition sp, Vec2 position)
    {
        int id = NextCreatureId++;
        var c = new Creature
        {
            Id = id,
            SpeciesIndex = speciesIndex,
            Position = position,
            PrevPosition = position,
            Rng = Pcg32.Create(World.Config.Seed, (ulong)id),
            Sex = Sex.Unknown,
            LifeStage = LifeStage.Adult,
        };
        c.Heading = c.Rng.Range(-MathUtil.Pi, MathUtil.Pi);
        c.PrevHeading = c.Heading;
        float ageSpan = MathF.Max(0f, sp.MaxAgeDays * 0.6f - sp.AdultAgeDays);
        c.AgeDays = sp.AdultAgeDays + c.Rng.NextFloat() * ageSpan;
        c.Energy = c.Rng.Range(0.70f, 0.92f);
        c.Hydration = c.Rng.Range(0.70f, 0.92f);
        c.Fatigue = c.Rng.Range(0.05f, 0.30f);
        c.TraitBodySize = MathUtil.Clamp(1f + 0.04f * c.Rng.NextGaussianApprox(), 0.85f, 1.15f);
        c.TraitMetabolism = MathUtil.Clamp(1f + 0.04f * c.Rng.NextGaussianApprox(), 0.85f, 1.15f);
        c.TraitSpeed = MathUtil.Clamp(1f + 0.04f * c.Rng.NextGaussianApprox(), 0.85f, 1.15f);
        return c;
    }

    private void AddCreature(Creature c)
    {
        _creatures.Add(c);
        _byId[c.Id] = c;
        Stats.OnSpawn(c.SpeciesIndex);
        Publish(SimEventType.CreatureSpawned, c.Id, World.Species[c.SpeciesIndex].Id, "", c.Position);
    }

    /// <summary>불러오기 전용: 저장된 개체를 그대로 넣는다(통계·사건을 다시 만들지 않는다).</summary>
    internal void RestoreCreature(Creature c)
    {
        if (_byId.ContainsKey(c.Id)) throw new InvalidOperationException($"개체 id {c.Id} 중복");
        _creatures.Add(c);
        _byId[c.Id] = c;
    }

    internal void SortCreaturesById() => _creatures.Sort((a, b) => a.Id.CompareTo(b.Id));

    private CommandResult ExecuteRemove(RemoveCreatureCommand cmd)
    {
        if (!_byId.TryGetValue(cmd.CreatureId, out Creature? c) || !c.Alive)
            return new CommandResult(Clock.Tick, cmd.Name, false, 0, "remove_not_found");
        c.Alive = false;
        c.DeathCause = DeathCause.Removed;
        RemoveDead();
        return new CommandResult(Clock.Tick, cmd.Name, true, 1, "remove_ok");
    }

    private CommandResult ExecuteDisturbance(DisturbanceCommand cmd)
    {
        if (!MathUtil.IsFinite(cmd.Radius) || cmd.Radius <= 0f || cmd.Radius > 500f
            || !MathUtil.IsFinite(cmd.Intensity) || !World.Terrain.InBounds(cmd.Position))
            return new CommandResult(Clock.Tick, cmd.Name, false, 0, "disturbance_bad_args");
        World.Threats.Add(new ThreatSource
        {
            Position = cmd.Position,
            Radius = cmd.Radius,
            Intensity = MathUtil.Clamp01(cmd.Intensity),
            UntilTick = Clock.Tick + 50,
        });
        return new CommandResult(Clock.Tick, cmd.Name, true, 1, "disturbance_ok");
    }

    // ---------------------------------------------------------------- 틱

    public void Step()
    {
        ApplyPendingCommands();
        Profiler.BeginTick();

        WorldState w = World;
        SimClock clock = w.Clock;
        long tick = clock.Tick;
        w.Env = EnvironmentState.Compute(clock, w.Config);

        for (int i = w.Threats.Count - 1; i >= 0; i--)
            if (w.Threats[i].UntilTick < tick) w.Threats.RemoveAt(i);

        if (tick % VegetationIntervalTicks == 0)
        {
            Profiler.BeginSection();
            w.Vegetation.Update(w.Terrain, w.Biomes, w.Forage, w.Config, w.Env, VegetationIntervalTicks * clock.DaysPerTick);
            Profiler.EndSection(TickProfiler.Section.Vegetation);
        }

        Profiler.BeginSection();
        float hoursDt = PhysiologyIntervalTicks * clock.HoursPerTick;
        bool anyDead = false;
        for (int i = 0; i < _creatures.Count; i++)
        {
            Creature c = _creatures[i];
            long phase = tick + c.Id;
            SpeciesDefinition sp = w.Species[c.SpeciesIndex];

            if (phase % DecisionIntervalTicks == 0)
            {
                SenseThreats(c, w, tick);
                float bite = sp.IntakeKgPerHour * hoursDt * c.TraitBodySize;
                bool foodNearby = Perception.IsFoodNearby(c, w, Perception.GoodCellKg(w, bite) * 0.5f);
                BehaviorKind chosen = UtilityBrain.Decide(c, w, foodNearby);
                BehaviorExecutor.SwitchBehavior(c, chosen, tick);
            }

            if (phase % PhysiologyIntervalTicks == 0)
            {
                BehaviorExecutor.Update(c, sp, w, hoursDt);
                Physiology.Update(c, sp, w.Env, hoursDt);
                if (c.ThreatUntilTick < tick && c.Fear > 0f)
                {
                    c.Fear -= FearDecayPerHour * hoursDt;
                    if (c.Fear < 0f) c.Fear = 0f;
                }
                if (!c.Alive) anyDead = true;
            }
        }
        Profiler.EndSection(TickProfiler.Section.Brain);

        Profiler.BeginSection();
        _steering.Step(_creatures, w);
        Profiler.EndSection(TickProfiler.Section.Movement);

        if (anyDead) RemoveDead();

        int dayBefore = clock.Day;
        Season seasonBefore = clock.Season;
        clock.Advance();
        if (clock.Day != dayBefore)
        {
            Publish(SimEventType.DayStarted, -1, "", "", Vec2.Zero);
            if (clock.Season != seasonBefore)
                Publish(SimEventType.SeasonChanged, -1, "", ((int)clock.Season).ToString(), Vec2.Zero);
        }

        if (clock.Tick % Stats.SampleIntervalTicks == 0)
            Stats.Sample(clock.Tick, (float)clock.ElapsedDays, _creatures, w.Vegetation.LastTotal);

        Profiler.EndTick();
    }

    private static void SenseThreats(Creature c, WorldState w, long tick)
    {
        for (int i = 0; i < w.Threats.Count; i++)
        {
            ThreatSource t = w.Threats[i];
            float dist = Vec2.Distance(c.Position, t.Position);
            if (dist >= t.Radius) continue;
            float fear = t.Intensity * (1f - 0.5f * dist / t.Radius);
            if (fear > c.Fear) c.Fear = fear;
            c.ThreatPosition = t.Position;
            c.ThreatUntilTick = tick + ThreatMemoryTicks;
        }
    }

    /// <summary>죽은 개체를 id 순서를 유지한 채 목록에서 뺀다.</summary>
    private void RemoveDead()
    {
        int write = 0;
        for (int read = 0; read < _creatures.Count; read++)
        {
            Creature c = _creatures[read];
            if (c.Alive)
            {
                _creatures[write++] = c;
                continue;
            }
            _byId.Remove(c.Id);
            Stats.OnDeath(c.SpeciesIndex, c.DeathCause);
            Publish(c.DeathCause == DeathCause.Removed ? SimEventType.CreatureRemoved : SimEventType.CreatureDied,
                c.Id, World.Species[c.SpeciesIndex].Id, BehaviorKinds.ToId(c.DeathCause), c.Position);
        }
        _creatures.RemoveRange(write, _creatures.Count - write);
    }

    private void Publish(SimEventType type, int creatureId, string speciesId, string detail, Vec2 position)
    {
        Events.Publish(new SimEvent(Clock.Tick, Clock.Day, Clock.TimeOfDayHours, type, creatureId, speciesId, detail, position));
    }

    // ---------------------------------------------------------------- 읽기

    public bool TryGetCreature(int id, out Creature? creature) => _byId.TryGetValue(id, out creature);

    /// <summary>재현성 검증용 해시. 이후 진행에 영향을 주는 상태를 모두 포함한다.</summary>
    public ulong ComputeStateHash()
    {
        StateHasher h = StateHasher.Create();
        h.Add(SimVersion.Simulation);
        World.Config.AddToHash(ref h);
        h.Add(Clock.Tick);
        h.Add(NextCreatureId);
        h.Add(WorldRng.State); h.Add(WorldRng.Inc);
        World.Terrain.AddToHash(ref h);
        World.Vegetation.AddToHash(ref h);
        h.Add(World.Threats.Count);
        foreach (ThreatSource t in World.Threats)
        {
            h.Add(t.Position); h.Add(t.Radius); h.Add(t.Intensity); h.Add(t.UntilTick);
        }
        h.Add(_creatures.Count);
        for (int i = 0; i < _creatures.Count; i++) _creatures[i].AddToHash(ref h);
        return h.Value;
    }

    public void FillSnapshot(SimSnapshot snapshot)
    {
        int count = _creatures.Count;
        if (snapshot.Creatures.Length < count)
            snapshot.Creatures = new CreatureSnapshot[Math.Max(count, snapshot.Creatures.Length * 2)];
        for (int i = 0; i < count; i++)
        {
            Creature c = _creatures[i];
            snapshot.Creatures[i] = new CreatureSnapshot
            {
                Id = c.Id,
                SpeciesIndex = c.SpeciesIndex,
                PrevPosition = c.PrevPosition,
                Position = c.Position,
                PrevHeading = c.PrevHeading,
                Heading = c.Heading,
                Speed = c.CurrentSpeed,
                BodySize = c.TraitBodySize,
                Health = c.Health,
                Energy = c.Energy,
                Hydration = c.Hydration,
                Activity = c.Activity,
                Behavior = c.Behavior,
            };
        }
        snapshot.Count = count;
        snapshot.Tick = Clock.Tick;
    }

    public CreatureDetails? GetDetails(int id)
    {
        if (!_byId.TryGetValue(id, out Creature? c)) return null;
        var d = new CreatureDetails
        {
            Id = c.Id,
            SpeciesIndex = c.SpeciesIndex,
            Position = c.Position,
            AgeDays = c.AgeDays,
            Sex = c.Sex,
            LifeStage = c.LifeStage,
            Health = c.Health,
            Energy = c.Energy,
            Hydration = c.Hydration,
            Fatigue = c.Fatigue,
            Fear = c.Fear,
            Behavior = c.Behavior,
            Activity = c.Activity,
            BehaviorStartTick = c.BehaviorStartTick,
            BoutAmount = c.BoutAmount,
            TraitBodySize = c.TraitBodySize,
            TraitMetabolism = c.TraitMetabolism,
            TraitSpeed = c.TraitSpeed,
            ParentA = c.ParentA,
            ParentB = c.ParentB,
            Reason = c.LastReason,
            LastDecisionTick = c.LastDecisionTick,
        };
        Array.Copy(c.LastScores, d.Scores, d.Scores.Length);
        d.Log.AddRange(c.Log);
        return d;
    }
}
