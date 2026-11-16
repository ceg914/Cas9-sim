using System.Collections.Generic;
using PaleoSphere.Core;

namespace PaleoSphere.World;

/// <summary>세계 생성·기후 설정. 같은 설정과 시드는 같은 세계를 만든다.</summary>
public sealed class WorldConfig
{
    public ulong Seed { get; set; } = 20261116UL;

    /// <summary>한 변의 셀 수. 세계 크기 = SizeCells × CellSize (m).</summary>
    public int SizeCells { get; set; } = 256;
    public float CellSize { get; set; } = 2f;

    /// <summary>지형 높낮이 진폭(m).</summary>
    public float Relief { get; set; } = 14f;

    /// <summary>수면 아래로 잠기는 셀의 비율(호수 면적).</summary>
    public float WaterFraction { get; set; } = 0.07f;
    public bool River { get; set; } = true;

    /// <summary>초기 식생량(수용량 대비 0~1).</summary>
    public float InitialVegetation { get; set; } = 0.7f;
    public float VegetationCapacityScale { get; set; } = 1f;
    public float VegetationGrowthScale { get; set; } = 1f;

    /// <summary>기후 습윤도 보정(-0.4 건조 ~ +0.4 습윤).</summary>
    public float MoistureBias { get; set; }

    public float BaseTemperatureC { get; set; } = 22f;
    public float SeasonalAmplitudeC { get; set; } = 5f;
    public float DiurnalAmplitudeC { get; set; } = 4f;

    /// <summary>하루를 몇 틱으로 압축할지. 6000틱 = 1배속에서 10분.</summary>
    public int TicksPerDay { get; set; } = 6000;
    public int DaysPerSeason { get; set; } = 10;
    public float StartHour { get; set; } = 8f;

    public float WorldSize => SizeCells * CellSize;

    public WorldConfig Clone() => (WorldConfig)MemberwiseClone();

    /// <summary>범위를 벗어난 값을 찾아 오류 목록으로 돌려준다. 비어 있으면 유효.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (SizeCells < 32 || SizeCells > 1024) errors.Add("size_cells는 32~1024 범위여야 합니다.");
        if (!(CellSize >= 1f && CellSize <= 8f)) errors.Add("cell_size는 1~8 m 범위여야 합니다.");
        if (!(Relief >= 0f && Relief <= 80f)) errors.Add("relief는 0~80 m 범위여야 합니다.");
        if (!(WaterFraction >= 0f && WaterFraction <= 0.5f)) errors.Add("water_fraction은 0~0.5 범위여야 합니다.");
        if (!(InitialVegetation >= 0f && InitialVegetation <= 1f)) errors.Add("initial_vegetation은 0~1 범위여야 합니다.");
        if (!(VegetationCapacityScale >= 0f && VegetationCapacityScale <= 5f)) errors.Add("vegetation_capacity_scale은 0~5 범위여야 합니다.");
        if (!(VegetationGrowthScale >= 0f && VegetationGrowthScale <= 5f)) errors.Add("vegetation_growth_scale은 0~5 범위여야 합니다.");
        if (!(MoistureBias >= -0.6f && MoistureBias <= 0.6f)) errors.Add("moisture_bias는 -0.6~0.6 범위여야 합니다.");
        if (!(BaseTemperatureC >= -10f && BaseTemperatureC <= 45f)) errors.Add("base_temperature_c는 -10~45 범위여야 합니다.");
        if (!(SeasonalAmplitudeC >= 0f && SeasonalAmplitudeC <= 25f)) errors.Add("seasonal_amplitude_c는 0~25 범위여야 합니다.");
        if (!(DiurnalAmplitudeC >= 0f && DiurnalAmplitudeC <= 20f)) errors.Add("diurnal_amplitude_c는 0~20 범위여야 합니다.");
        if (TicksPerDay < 600 || TicksPerDay > 864000) errors.Add("ticks_per_day는 600~864000 범위여야 합니다.");
        if (DaysPerSeason < 1 || DaysPerSeason > 365) errors.Add("days_per_season은 1~365 범위여야 합니다.");
        if (!(StartHour >= 0f && StartHour < 24f)) errors.Add("start_hour는 0 이상 24 미만이어야 합니다.");
        return errors;
    }

    public void AddToHash(ref StateHasher h)
    {
        h.Add(Seed); h.Add(SizeCells); h.Add(CellSize); h.Add(Relief); h.Add(WaterFraction); h.Add(River);
        h.Add(InitialVegetation); h.Add(VegetationCapacityScale); h.Add(VegetationGrowthScale); h.Add(MoistureBias);
        h.Add(BaseTemperatureC); h.Add(SeasonalAmplitudeC); h.Add(DiurnalAmplitudeC);
        h.Add(TicksPerDay); h.Add(DaysPerSeason); h.Add(StartHour);
    }
}
