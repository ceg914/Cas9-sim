using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Diagnostics;
using PaleoSphere.Ecology;
using PaleoSphere.Modding;
using PaleoSphere.Simulation;

namespace PaleoSphere.Tools;

/// <summary>
/// 사용법: dotnet run --project tools/SimRunner -c Release -- [--data data] [--scenario id] [--days N]
///         [--count N] [--seed N] [--csv out.csv] [--quiet]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        string dataDir = "data";
        string scenarioId = "lush_lowland";
        double days = 10;
        int countOverride = -1;
        ulong? seed = null;
        string? csv = null;
        bool quiet = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data": dataDir = args[++i]; break;
                case "--scenario": scenarioId = args[++i]; break;
                case "--days": days = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--count": countOverride = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--seed": seed = ulong.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--csv": csv = args[++i]; break;
                case "--quiet": quiet = true; break;
                default:
                    Console.Error.WriteLine("알 수 없는 인자: " + args[i]);
                    return 2;
            }
        }

        GameData data = GameData.LoadFromDirectory(dataDir);
        foreach (string w in data.Report.Warnings) Console.Error.WriteLine("경고 " + w);
        foreach (string e in data.Report.Errors) Console.Error.WriteLine("오류 " + e);
        if (!data.IsUsable) return 1;

        ScenarioDefinition? scenario = null;
        foreach (ScenarioDefinition s in data.Scenarios)
            if (s.Id == scenarioId) scenario = s;
        if (scenario == null)
        {
            Console.Error.WriteLine($"시나리오 '{scenarioId}'이(가) 없습니다.");
            return 1;
        }

        var config = scenario.World.Clone();
        if (seed.HasValue) config.Seed = seed.Value;
        var sw = Stopwatch.StartNew();
        SimulationCore sim = data.CreateSimulation(config);
        double genMs = sw.Elapsed.TotalMilliseconds;

        float size = sim.World.Terrain.WorldSize;
        var center = new Vec2(size / 2, size / 2);
        foreach (ScenarioPopulation p in scenario.Populations)
            sim.Enqueue(new SpawnCreaturesCommand(p.SpeciesId, countOverride > 0 ? Math.Min(countOverride, 500) : p.Count, center, size * 0.45f));
        for (int extra = countOverride - 500; extra > 0; extra -= 500)
            sim.Enqueue(new SpawnCreaturesCommand(scenario.Populations[0].SpeciesId, Math.Min(extra, 500), center, size * 0.45f));
        sim.ApplyPendingCommands();

        var terrain = sim.World.Terrain;
        int water = 0, walkable = 0;
        for (int i = 0; i < terrain.CellCount; i++)
        {
            if (terrain.IsWater(i)) water++;
            if (terrain.Walkable[i]) walkable++;
        }
        Console.WriteLine($"세계 {size:0} m, 시드 {config.Seed}, 생성 {genMs:0} ms, 수면 {100.0 * water / terrain.CellCount:0.0}%, 보행 가능 {100.0 * walkable / terrain.CellCount:0.0}%");
        Console.WriteLine($"식생 수용량 {sim.World.Vegetation.SumCapacity() / 1000:0.0} t, 초기 {sim.World.Vegetation.SumBiomass() / 1000:0.0} t, 개체 {sim.CreatureCount}");

        long totalTicks = (long)(days * config.TicksPerDay);
        int reportEvery = config.TicksPerDay;
        sw.Restart();
        for (long t = 0; t < totalTicks; t++)
        {
            sim.Step();
            if (!quiet && sim.Clock.Tick % reportEvery == 0) PrintDay(sim);
        }
        double runMs = sw.Elapsed.TotalMilliseconds;

        PopulationStats st = sim.Stats;
        Console.WriteLine($"--- {days:0.##}일({totalTicks}틱) 실행: {runMs:0} ms, 틱당 평균 {runMs / totalTicks * 1000:0.0} µs, 최대 {sim.Profiler.MaxTickMs:0.00} ms");
        Console.WriteLine($"    구간 평균(ms): 식생 {sim.Profiler.SectionAverageMs(TickProfiler.Section.Vegetation):0.000}(갱신 틱만), 의사결정·생리 {sim.Profiler.SectionAverageMs(TickProfiler.Section.Brain):0.000}, 이동 {sim.Profiler.SectionAverageMs(TickProfiler.Section.Movement):0.000}");
        Console.WriteLine($"    생존 {sim.CreatureCount}, 아사 {st.TotalDeathsByCause(DeathCause.Starvation)}, 탈수 {st.TotalDeathsByCause(DeathCause.Dehydration)}");
        var veg = sim.World.Vegetation;
        double expected = veg.InitialTotal + veg.TotalGrown - veg.TotalConsumed - veg.TotalDieback;
        Console.WriteLine($"    식생 {veg.SumBiomass() / 1000:0.00} t (성장 {veg.TotalGrown / 1000:0.00}, 섭식 {veg.TotalConsumed / 1000:0.00}, 고사 {veg.TotalDieback / 1000:0.00}; 수지 오차 {Math.Abs(expected - veg.SumBiomass()):0.000} kg)");
        Console.WriteLine($"    상태 해시 {StateHasher.ToHex(sim.ComputeStateHash())}");

        if (csv != null)
        {
            using var w = new StreamWriter(csv);
            w.WriteLine("tick,day,creatures,vegetation_kg,mean_energy,mean_hydration,total_deaths");
            foreach (StatSample s in st.Samples)
            {
                int n = 0;
                foreach (int c in s.Counts) n += c;
                w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:0.###},{2},{3:0.#},{4:0.###},{5:0.###},{6}",
                    s.Tick, s.Day, n, s.VegetationKg, s.MeanEnergy, s.MeanHydration, s.TotalDeaths));
            }
            Console.WriteLine("CSV 저장: " + csv);
        }
        return 0;
    }

    private static void PrintDay(SimulationCore sim)
    {
        var counts = new int[BehaviorKinds.Count];
        double e = 0, h = 0, f = 0;
        foreach (Creature c in sim.Creatures)
        {
            counts[(int)c.Behavior]++;
            e += c.Energy;
            h += c.Hydration;
            f += c.Fatigue;
        }
        int n = Math.Max(1, sim.CreatureCount);
        Console.WriteLine($"Day {sim.Clock.Day,3} {sim.Clock.TimeOfDayHours,4:0.0}h 개체 {sim.CreatureCount,4} | 에너지 {e / n:0.00} 수분 {h / n:0.00} 피로 {f / n:0.00} | 식생 {sim.World.Vegetation.LastTotal / 1000,6:0.0} t | 대기 {counts[0]} 섭식 {counts[1]} 음수 {counts[2]} 휴식 {counts[3]} 도주 {counts[4]} 탐색 {counts[5]}");
    }
}
