using System;

namespace PaleoSphere.Core;

/// <summary>
/// 결정론적 수학 함수. 운영체제의 수학 라이브러리(sinf, cosf, atan2f, expf)는 CPU·OS에 따라
/// 마지막 비트가 다른 구현을 고르기 때문에, 그대로 쓰면 같은 시드라도 기기마다 결과가 갈린다.
/// 여기서는 IEEE 754가 정확한 결과를 보장하는 사칙연산과 Math.Floor만으로 계산한다.
/// 그래서 어느 기기에서나 비트 단위로 같은 값이 나온다.
/// 내부 계산은 double로 하고 결과만 float로 줄인다(오차는 float 정밀도보다 훨씬 작다).
/// 시뮬레이션 코어는 MathF.Sin/Cos/Atan2/Exp 대신 반드시 이 함수들을 쓴다.
/// </summary>
public static class DetMath
{
    private const double Pi = 3.14159265358979323846;
    private const double PiOver2 = 1.57079632679489661923;
    private const double PiOver6 = 0.52359877559829887308;
    private const double TwoOverPi = 0.63661977236758134308;
    // π/2를 두 조각으로 나눠 빼서 범위 축소의 오차를 줄인다.
    private const double PiOver2Hi = 1.57079632673412561417;
    private const double PiOver2Lo = 6.07710050650619224932e-11;
    private const double Sqrt3 = 1.73205080756887729353;
    private const double TanPiOver12 = 0.26794919243112270647;
    private const double InvLn2 = 1.44269504088896338700;
    private const double Ln2Hi = 6.93147180369123816490e-01;
    private const double Ln2Lo = 1.90821492927058770002e-10;

    private static double SinPoly(double r)
    {
        double r2 = r * r;
        return r + r * r2 * (-1.0 / 6 + r2 * (1.0 / 120 + r2 * (-1.0 / 5040 + r2 * (1.0 / 362880
            + r2 * (-1.0 / 39916800 + r2 * (1.0 / 6227020800))))));
    }

    private static double CosPoly(double r)
    {
        double r2 = r * r;
        return 1.0 + r2 * (-0.5 + r2 * (1.0 / 24 + r2 * (-1.0 / 720 + r2 * (1.0 / 40320
            + r2 * (-1.0 / 3628800 + r2 * (1.0 / 479001600 + r2 * (-1.0 / 87178291200)))))));
    }

    /// <summary>x를 π/2의 배수와 나머지 r(|r| ≤ π/4)로 나눈다. 돌려주는 값은 사분면(0~3).</summary>
    private static int Reduce(double x, out double r)
    {
        double k = Math.Floor(x * TwoOverPi + 0.5);
        r = (x - k * PiOver2Hi) - k * PiOver2Lo;
        return (int)((long)k & 3);
    }

    public static float Sin(float x)
    {
        if (float.IsNaN(x) || float.IsInfinity(x)) return float.NaN;
        int q = Reduce(x, out double r);
        double v = q switch { 0 => SinPoly(r), 1 => CosPoly(r), 2 => -SinPoly(r), _ => -CosPoly(r) };
        return (float)v;
    }

    public static float Cos(float x)
    {
        if (float.IsNaN(x) || float.IsInfinity(x)) return float.NaN;
        int q = Reduce(x, out double r);
        double v = q switch { 0 => CosPoly(r), 1 => -SinPoly(r), 2 => -CosPoly(r), _ => SinPoly(r) };
        return (float)v;
    }

    private static double Atan(double z)
    {
        bool negative = z < 0.0;
        if (negative) z = -z;
        bool inverted = z > 1.0;
        if (inverted) z = 1.0 / z;
        // atan(z) = π/6 + atan((z√3 − 1) / (√3 + z)) 로 인자를 |z| ≤ tan(π/12)까지 줄인다.
        bool shifted = z > TanPiOver12;
        if (shifted) z = (z * Sqrt3 - 1.0) / (Sqrt3 + z);
        double z2 = z * z;
        double p = z * (1.0 + z2 * (-1.0 / 3 + z2 * (1.0 / 5 + z2 * (-1.0 / 7 + z2 * (1.0 / 9
            + z2 * (-1.0 / 11 + z2 * (1.0 / 13 + z2 * (-1.0 / 15 + z2 * (1.0 / 17)))))))));
        if (shifted) p += PiOver6;
        if (inverted) p = PiOver2 - p;
        return negative ? -p : p;
    }

    /// <summary>(x, y) 방향의 각도, (-π, π] 범위.</summary>
    public static float Atan2(float y, float x)
    {
        if (float.IsNaN(x) || float.IsNaN(y)) return float.NaN;
        if (x == 0f) return y > 0f ? (float)PiOver2 : (y < 0f ? (float)-PiOver2 : 0f);
        double a = Atan((double)y / x);
        if (x < 0f) a += y >= 0f ? Pi : -Pi;
        return (float)a;
    }

    public static float Exp(float x)
    {
        if (float.IsNaN(x)) return float.NaN;
        if (x > 88.7f) return float.PositiveInfinity;
        if (x < -103.9f) return 0f;
        double k = Math.Floor(x * InvLn2 + 0.5);
        double r = (x - k * Ln2Hi) - k * Ln2Lo;
        double p = 1.0 + r * (1.0 + r * (1.0 / 2 + r * (1.0 / 6 + r * (1.0 / 24 + r * (1.0 / 120 + r * (1.0 / 720
            + r * (1.0 / 5040 + r * (1.0 / 40320 + r * (1.0 / 362880 + r * (1.0 / 3628800
            + r * (1.0 / 39916800 + r * (1.0 / 479001600))))))))))));
        // 2^k 곱하기: 2와 0.5의 곱셈은 지수만 바꾸므로 정확하다.
        for (int n = (int)k; n > 0; n--) p *= 2.0;
        for (int n = (int)k; n < 0; n++) p *= 0.5;
        return (float)p;
    }
}
