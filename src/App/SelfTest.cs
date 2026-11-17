using System;
using System.Collections.Generic;
using System.Diagnostics;
using PaleoSphere.Core;
using PaleoSphere.Modding;
using PaleoSphere.Persistence;
using PaleoSphere.Simulation;

namespace PaleoSphere.App;

/// <summary>
/// 실행 파일 안에서 도는 통합 점검(--selftest). 엔진이 띄운 .NET 런타임에서
/// 데이터 읽기 → 시뮬레이션 → 저장 → 불러오기 → 재현성까지 확인한다.
/// 에디터 없이 내보낸 EXE에서도 같은 방법으로 돌릴 수 있다.
/// </summary>
public static class SelfTest
{
    public static bool Run(GameData data, string saveDirectory, Action<string> print)
    {
        var failures = new List<string>();
        void Check(bool ok, string what)
        {
            print((ok ? "  [통과] " : "  [실패] ") + what);
            if (!ok) failures.Add(what);
        }

        print("PALEOSPHERE 셀프테스트 시작 (시뮬레이션 " + SimVersion.Simulation + ", 세이브 스키마 " + SimVersion.SaveSchema + ")");
        Check(data.IsUsable, $"정의 데이터 읽기: 종 {data.Species.Count}, 생물군계 {data.Biomes.Count}, 시나리오 {data.Scenarios.Count}, 오류 {data.Report.Errors.Count}");
        foreach (string e in data.Report.Errors) print("    " + e);
        if (!data.IsUsable || data.Scenarios.Count == 0) return Finish(failures, print);

        ScenarioDefinition scenario = data.Scenarios[0];
        foreach (ScenarioDefinition s in data.Scenarios)
            if (s.Id == "lush_lowland") scenario = s;

        var sw = Stopwatch.StartNew();
        SimulationCore a = data.CreateSimulation(scenario);
        SimulationCore b = data.CreateSimulation(scenario);
        Check(a.CreatureCount > 0, $"세계 생성과 초기 배치: {a.World.Terrain.WorldSize:0} m, 개체 {a.CreatureCount} ({sw.ElapsedMilliseconds} ms)");

        const int ticks = 3000;
        sw.Restart();
        for (int i = 0; i < ticks; i++)
        {
            a.Step();
            b.Step();
        }
        double usPerTick = sw.Elapsed.TotalMilliseconds * 1000.0 / (ticks * 2);
        Check(a.ComputeStateHash() == b.ComputeStateHash(), $"같은 시드 재현성: {ticks}틱 뒤 해시 {StateHasher.ToHex(a.ComputeStateHash())} (틱당 {usPerTick:0.0} µs)");
        Check(a.World.Vegetation.TotalConsumed > 0, $"섭식으로 식생 감소: {a.World.Vegetation.TotalConsumed:0.0} kg");

        var store = new SaveFileStore(saveDirectory);
        string path = "";
        try
        {
            path = store.Write("selftest", SaveSerializer.Serialize(a, "selftest"));
            Check(System.IO.File.Exists(path), "세이브 파일 쓰기: " + path);
            LoadResult loaded = SaveSerializer.Deserialize(store.Read(path), data);
            Check(loaded.Ok, "세이브 파일 읽기" + (loaded.Ok ? "" : ": " + string.Join(" / ", loaded.Errors)));
            if (loaded.Ok && loaded.Sim != null)
            {
                Check(loaded.Sim.ComputeStateHash() == a.ComputeStateHash(), "불러온 상태가 저장 시점과 같음");
                for (int i = 0; i < 1000; i++)
                {
                    a.Step();
                    loaded.Sim.Step();
                }
                Check(loaded.Sim.ComputeStateHash() == a.ComputeStateHash(), "불러온 뒤 1000틱 진행 결과가 원본과 같음");
            }
            byte[] broken = { 1, 2, 3 };
            Check(!SaveSerializer.Deserialize(broken, data).Ok, "손상된 세이브 거부");
        }
        catch (Exception e)
        {
            Check(false, "저장·불러오기 중 예외: " + e.Message);
        }
        finally
        {
            store.Delete("selftest");
        }
        return Finish(failures, print);
    }

    private static bool Finish(List<string> failures, Action<string> print)
    {
        print(failures.Count == 0 ? "SELFTEST PASS" : $"SELFTEST FAIL ({failures.Count}건)");
        return failures.Count == 0;
    }
}
