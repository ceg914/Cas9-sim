using System;
using System.Collections.Generic;
using PaleoSphere.Core;

namespace PaleoSphere.World;

/// <summary>생물군계 정의(data/biomes/*.json). 수치는 모두 게임적 단순화다.</summary>
public sealed class BiomeDefinition
{
    public string Id { get; init; } = "";
    public string NameKo { get; init; } = "";
    public string NameEn { get; init; } = "";
    public string Description { get; init; } = "";
    public EvidenceLevel Evidence { get; init; } = EvidenceLevel.Simplified;

    /// <summary>식생 수용량(kg/m²).</summary>
    public float VegetationCapacityKgM2 { get; init; }

    /// <summary>식생 성장률 배수.</summary>
    public float GrowthFactor { get; init; } = 1f;

    /// <summary>이동 속도 배수.</summary>
    public float MoveSpeedFactor { get; init; } = 1f;

    /// <summary>표시 색(식생이 풍부할 때). 0~1 RGB.</summary>
    public float ColorR { get; init; }
    public float ColorG { get; init; }
    public float ColorB { get; init; }

    /// <summary>표시 색(식생이 없을 때의 맨땅).</summary>
    public float BareR { get; init; }
    public float BareG { get; init; }
    public float BareB { get; init; }
}

public sealed class BiomeRegistry
{
    /// <summary>지형 생성기가 요구하는 생물군계 id. 데이터에 모두 있어야 한다.</summary>
    public static readonly string[] RequiredIds = { "open_plain", "forest", "wetland", "upland", "water" };

    private readonly List<BiomeDefinition> _list = new();
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public IReadOnlyList<BiomeDefinition> All => _list;
    public int Count => _list.Count;

    /// <summary>id 순으로 정렬해 등록한다. 색인이 파일 읽기 순서에 좌우되지 않게 하기 위함.</summary>
    public BiomeRegistry(IEnumerable<BiomeDefinition> biomes)
    {
        var sorted = new List<BiomeDefinition>(biomes);
        sorted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        foreach (var b in sorted)
        {
            if (_index.ContainsKey(b.Id)) continue;
            _index[b.Id] = _list.Count;
            _list.Add(b);
        }
        if (_list.Count > 250) throw new InvalidOperationException("생물군계는 250개까지 지원합니다.");
    }

    public bool TryGetIndex(string id, out int index) => _index.TryGetValue(id, out index);

    public int IndexOf(string id) =>
        _index.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"생물군계 '{id}' 정의가 없습니다.");

    public BiomeDefinition this[int index] => _list[index];

    public List<string> MissingRequired()
    {
        var missing = new List<string>();
        foreach (string id in RequiredIds)
            if (!_index.ContainsKey(id)) missing.Add(id);
        return missing;
    }
}
