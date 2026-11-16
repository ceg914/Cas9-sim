using PaleoSphere.Core;

namespace PaleoSphere.Ecology;

/// <summary>
/// 식생 층 정의(data/plants/*.json). 첫 버전은 저층 먹이 식생 한 층만 쓴다.
/// 수치는 게임 내 하루 기준이며 모두 게임적 단순화다.
/// </summary>
public sealed class PlantDefinition
{
    public string Id { get; init; } = "";
    public string NameKo { get; init; } = "";
    public string NameEn { get; init; } = "";
    public string Description { get; init; } = "";
    public EvidenceLevel Evidence { get; init; } = EvidenceLevel.Simplified;

    /// <summary>최적 조건에서의 로지스틱 성장률(/일).</summary>
    public float GrowthRatePerDay { get; init; } = 1.2f;

    /// <summary>바이오매스가 0이어도 다시 자라게 하는 씨앗·뿌리 몫(수용량 대비).</summary>
    public float SeedFraction { get; init; } = 0.02f;

    /// <summary>수분·온도 스트레스가 최대일 때의 고사율(/일).</summary>
    public float DiebackRatePerDay { get; init; } = 0.25f;

    /// <summary>뜯어 먹을 수 없는 잔여분(수용량 대비). 완전한 황폐화를 막는다.</summary>
    public float ResidualFraction { get; init; } = 0.08f;

    public float MinMoisture { get; init; } = 0.12f;
    public float OptimalMoisture { get; init; } = 0.6f;

    public float MinTemperatureC { get; init; } = 4f;
    public float OptimalLowC { get; init; } = 16f;
    public float OptimalHighC { get; init; } = 30f;
    public float MaxTemperatureC { get; init; } = 42f;
}
