using System;
using System.Collections.Generic;
using System.IO;
using PaleoSphere.Creatures;
using PaleoSphere.Ecology;
using PaleoSphere.Simulation;
using PaleoSphere.World;

namespace PaleoSphere.Modding;

public readonly record struct DataFile(string Name, string Text);

/// <summary>정의 파일을 읽어 오는 출처. 앱은 res://data를, 테스트는 디스크 폴더를 연결한다.</summary>
public interface IDataSource
{
    /// <summary>category("species", "biomes", "plants", "scenarios") 폴더의 .json 파일 전부.</summary>
    IReadOnlyList<DataFile> ReadAll(string category, ValidationReport report);
}

/// <summary>디스크 폴더에서 읽는 출처.</summary>
public sealed class DirectoryDataSource : IDataSource
{
    private readonly string _root;

    public DirectoryDataSource(string root) => _root = root;

    public IReadOnlyList<DataFile> ReadAll(string category, ValidationReport report)
    {
        var files = new List<DataFile>();
        string dir = Path.Combine(_root, category);
        if (!Directory.Exists(dir)) return files;
        string[] paths = Directory.GetFiles(dir, "*.json");
        Array.Sort(paths, StringComparer.Ordinal);
        foreach (string path in paths)
        {
            string name = category + "/" + Path.GetFileName(path);
            if (files.Count >= JsonGuard.MaxFilesPerCategory)
            {
                report.Warn(name, "파일이 너무 많아 나머지는 건너뜁니다.");
                break;
            }
            try
            {
                if (new FileInfo(path).Length > JsonGuard.MaxDataFileBytes)
                {
                    report.Error(name, $"파일이 너무 큽니다(최대 {JsonGuard.MaxDataFileBytes / 1024} KiB).");
                    continue;
                }
                files.Add(new DataFile(name, File.ReadAllText(path)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                report.Error(name, "파일을 읽지 못했습니다: " + e.Message);
            }
        }
        return files;
    }
}

/// <summary>
/// 게임이 쓰는 정의 데이터 묶음. 잘못된 파일은 건너뛰고 Report에 기록한다.
/// 시뮬레이션에 꼭 필요한 정의가 빠지면 IsUsable이 false가 된다.
/// </summary>
public sealed class GameData
{
    public BiomeRegistry Biomes { get; }
    public SpeciesRegistry Species { get; }
    public PlantDefinition? Forage { get; }
    public IReadOnlyList<ScenarioDefinition> Scenarios { get; }
    public ValidationReport Report { get; }
    public bool IsUsable { get; }

    private GameData(BiomeRegistry biomes, SpeciesRegistry species, PlantDefinition? forage,
        List<ScenarioDefinition> scenarios, ValidationReport report, bool usable)
    {
        Biomes = biomes;
        Species = species;
        Forage = forage;
        Scenarios = scenarios;
        Report = report;
        IsUsable = usable;
    }

    public static GameData LoadFromDirectory(string dataRoot) => Load(new DirectoryDataSource(dataRoot));

    public static GameData Load(IDataSource source)
    {
        var report = new ValidationReport();

        var biomes = new List<BiomeDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataFile file in source.ReadAll("biomes", report))
        {
            BiomeDefinition? b = DefinitionLoaders.ParseBiome(file.Text, file.Name, report);
            if (b == null) continue;
            if (!seen.Add(b.Id)) { report.Error(file.Name, $"생물군계 id '{b.Id}'이(가) 중복됩니다."); continue; }
            biomes.Add(b);
        }
        var biomeRegistry = new BiomeRegistry(biomes);

        PlantDefinition? forage = null;
        foreach (DataFile file in source.ReadAll("plants", report))
        {
            PlantDefinition? p = DefinitionLoaders.ParsePlant(file.Text, file.Name, report);
            if (p == null) continue;
            if (forage == null || string.CompareOrdinal(p.Id, forage.Id) < 0) forage = p;
        }

        var species = new List<SpeciesDefinition>();
        seen.Clear();
        foreach (DataFile file in source.ReadAll("species", report))
        {
            SpeciesDefinition? s = DefinitionLoaders.ParseSpecies(file.Text, file.Name, report);
            if (s == null) continue;
            if (!seen.Add(s.Id)) { report.Error(file.Name, $"종 id '{s.Id}'이(가) 중복됩니다."); continue; }
            foreach (string biomeId in s.PlacementBiomes)
                if (!biomeRegistry.TryGetIndex(biomeId, out _))
                    report.Warn(file.Name, $"placement.biomes의 '{biomeId}' 생물군계 정의가 없습니다.");
            species.Add(s);
        }
        var speciesRegistry = new SpeciesRegistry(species);

        var scenarios = new List<ScenarioDefinition>();
        seen.Clear();
        foreach (DataFile file in source.ReadAll("scenarios", report))
        {
            ScenarioDefinition? sc = DefinitionLoaders.ParseScenario(file.Text, file.Name, report);
            if (sc == null) continue;
            if (!seen.Add(sc.Id)) { report.Error(file.Name, $"시나리오 id '{sc.Id}'이(가) 중복됩니다."); continue; }
            bool ok = true;
            foreach (ScenarioPopulation p in sc.Populations)
            {
                if (speciesRegistry.TryGetIndex(p.SpeciesId, out _)) continue;
                report.Error(file.Name, $"시나리오가 참조하는 종 '{p.SpeciesId}' 정의가 없습니다.");
                ok = false;
            }
            if (ok) scenarios.Add(sc);
        }
        scenarios.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        bool usable = true;
        List<string> missing = biomeRegistry.MissingRequired();
        if (missing.Count > 0)
        {
            report.Error("biomes", "필수 생물군계 정의가 없습니다: " + string.Join(", ", missing));
            usable = false;
        }
        if (forage == null)
        {
            report.Error("plants", "식생 정의가 하나도 없습니다.");
            usable = false;
        }
        if (speciesRegistry.Count == 0)
        {
            report.Error("species", "종 정의가 하나도 없습니다.");
            usable = false;
        }

        return new GameData(biomeRegistry, speciesRegistry, forage, scenarios, report, usable);
    }

    public SimulationCore CreateSimulation(WorldConfig config)
    {
        if (!IsUsable || Forage == null) throw new InvalidOperationException("정의 데이터가 불완전해 세계를 만들 수 없습니다.");
        return SimulationCore.CreateNew(config, Biomes, Species, Forage);
    }

    public SimulationCore CreateSimulation(ScenarioDefinition scenario)
    {
        if (!IsUsable || Forage == null) throw new InvalidOperationException("정의 데이터가 불완전해 세계를 만들 수 없습니다.");
        return scenario.Create(Biomes, Species, Forage);
    }
}
