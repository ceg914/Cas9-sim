using System;

namespace PaleoSphere.Core;

public static class MathUtil
{
    public const float Pi = MathF.PI;
    public const float TwoPi = MathF.PI * 2f;

    public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>각도를 (-π, π] 범위로 접는다.</summary>
    public static float WrapAngle(float a)
    {
        while (a > Pi) a -= TwoPi;
        while (a <= -Pi) a += TwoPi;
        return a;
    }

    /// <summary>current에서 target 쪽으로 최대 maxDelta만큼 회전한 각도.</summary>
    public static float MoveTowardAngle(float current, float target, float maxDelta)
    {
        float diff = WrapAngle(target - current);
        if (MathF.Abs(diff) <= maxDelta) return WrapAngle(target);
        return WrapAngle(current + MathF.Sign(diff) * maxDelta);
    }

    public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
