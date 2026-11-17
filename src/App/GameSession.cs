using System;
using System.Collections.Generic;
using PaleoSphere.Core;
using PaleoSphere.Diagnostics;
using PaleoSphere.Modding;
using PaleoSphere.Persistence;
using PaleoSphere.Simulation;
using PaleoSphere.World;

namespace PaleoSphere.App;

public sealed record LoadOutcome(bool Ok, string Path, List<string> Errors, List<string> Warnings, bool UsedBackup);

/// <summary>
/// 실행 중인 세계 하나와 그 주변(시간 진행, 스냅샷, 저장)을 묶는다.
/// UI와 렌더러는 Snapshot과 읽기용 조회만 쓰고, 변경은 Sim.Enqueue(명령)로만 한다.
/// </summary>
public sealed class GameSession
{
    public GameData Data { get; }
    public SaveFileStore Store { get; }
    public FixedTimestepDriver Driver { get; } = new();
    public SimSnapshot Snapshot { get; } = new();
    public SimulationCore? Sim { get; private set; }
    public string WorldName { get; private set; } = "";

    /// <summary>자동 저장 간격(초). 0이면 끔.</summary>
    public double AutosaveIntervalSeconds { get; set; } = 300;
    private double _sinceAutosave;

    /// <summary>세계가 새로 만들어지거나 불러와졌을 때. 표현 계층이 지형을 다시 만든다.</summary>
    public event Action? WorldReplaced;

    /// <summary>자동 저장 결과(성공 여부, 메시지).</summary>
    public event Action<bool, string>? Autosaved;

    public GameSession(GameData data, string saveDirectory)
    {
        Data = data;
        Store = new SaveFileStore(saveDirectory);
    }

    public void NewWorld(ScenarioDefinition scenario)
    {
        Replace(Data.CreateSimulation(scenario), scenario.NameKo);
    }

    public void NewWorld(WorldConfig config, IEnumerable<ScenarioPopulation> populations, string name)
    {
        SimulationCore sim = Data.CreateSimulation(config);
        float size = sim.World.Terrain.WorldSize;
        var center = new Vec2(size * 0.5f, size * 0.5f);
        foreach (ScenarioPopulation p in populations)
            if (p.Count > 0) sim.Enqueue(new SpawnCreaturesCommand(p.SpeciesId, p.Count, center, size * 0.45f));
        sim.ApplyPendingCommands();
        Replace(sim, name);
    }

    private void Replace(SimulationCore sim, string name)
    {
        Sim = sim;
        WorldName = name;
        Driver.Reset();
        _sinceAutosave = 0;
        sim.FillSnapshot(Snapshot);
        WorldReplaced?.Invoke();
    }

    /// <summary>프레임마다 호출. 고정 틱을 필요한 만큼 진행하고 스냅샷을 갱신한다.</summary>
    public int Update(double realDelta)
    {
        if (Sim == null) return 0;
        int steps = Driver.Advance(realDelta, Sim);
        // 멈춰 있을 때도 배치·삭제 명령은 바로 반영한다(틱 경계에서 적용되는 점은 같다).
        if (steps == 0 && Sim.PendingCommandCount > 0) Sim.ApplyPendingCommands();
        Sim.FillSnapshot(Snapshot);

        if (AutosaveIntervalSeconds > 0 && !Driver.Paused)
        {
            _sinceAutosave += realDelta;
            if (_sinceAutosave >= AutosaveIntervalSeconds)
            {
                _sinceAutosave = 0;
                (bool ok, string message) = Save(SaveFileStore.AutosaveSlot);
                Autosaved?.Invoke(ok, message);
            }
        }
        return steps;
    }

    public (bool Ok, string Message) Save(string slot)
    {
        if (Sim == null) return (false, "저장할 세계가 없습니다.");
        try
        {
            Sim.ApplyPendingCommands();
            string path = Store.Write(slot, SaveSerializer.Serialize(Sim, WorldName));
            return (true, path);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SimLog.Error("저장 실패: " + e.Message);
            return (false, e.Message);
        }
    }

    /// <summary>슬롯을 불러온다. 원본이 깨졌으면 백업을 최신 순으로 시도한다.</summary>
    public LoadOutcome Load(string slot)
    {
        var errors = new List<string>();
        List<string> candidates = Store.CandidatePaths(slot);
        if (candidates.Count == 0)
        {
            errors.Add("세이브 파일이 없습니다: " + Store.PathFor(slot));
            return new LoadOutcome(false, "", errors, new List<string>(), false);
        }
        for (int i = 0; i < candidates.Count; i++)
        {
            string path = candidates[i];
            try
            {
                LoadResult result = SaveSerializer.Deserialize(Store.Read(path), Data);
                if (result.Ok && result.Sim != null)
                {
                    Replace(result.Sim, result.Header?.DisplayName ?? slot);
                    Driver.Paused = true;
                    return new LoadOutcome(true, path, errors, result.Warnings, i > 0);
                }
                foreach (string e in result.Errors) errors.Add($"{System.IO.Path.GetFileName(path)}: {e}");
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                errors.Add($"{System.IO.Path.GetFileName(path)}: {e.Message}");
            }
        }
        return new LoadOutcome(false, "", errors, new List<string>(), false);
    }
}
