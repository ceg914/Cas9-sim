using System;
using PaleoSphere.Core;

namespace PaleoSphere.World;

/// <summary>
/// 시계와 기후 설정에서 계산하는 환경 값. 시계의 순수 함수라 따로 저장할 상태가 없다.
/// 낮 길이 12시간 고정, 사인 곡선 기온은 게임적 단순화다.
/// </summary>
public struct EnvironmentState
{
    /// <summary>일조량 0~1. 06시에 뜨고 18시에 진다.</summary>
    public float Light;
    public float TemperatureC;
    public bool IsNight;

    public static EnvironmentState Compute(SimClock clock, WorldConfig config)
    {
        float h = clock.TimeOfDayHours;
        float sun = MathF.Sin(MathUtil.Pi * (h - 6f) / 12f);
        float light = sun > 0f ? sun : 0f;
        float seasonal = config.SeasonalAmplitudeC * MathF.Sin(MathUtil.TwoPi * (clock.YearFraction - 0.125f));
        float diurnal = config.DiurnalAmplitudeC * MathF.Cos(MathUtil.TwoPi * (h - 15f) / 24f);
        return new EnvironmentState
        {
            Light = light,
            TemperatureC = config.BaseTemperatureC + seasonal + diurnal,
            IsNight = h < 5.5f || h >= 19f,
        };
    }
}
