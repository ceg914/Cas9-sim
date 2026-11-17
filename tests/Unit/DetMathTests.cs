using System;
using PaleoSphere.Core;
using Xunit;

namespace PaleoSphere.Tests.Unit;

/// <summary>
/// 결정론적 수학 함수. 기준 비트 값은 어느 OS·CPU에서나 같아야 한다.
/// CI가 Windows와 Linux에서 이 테스트를 돌려 기기 간 재현성을 확인한다.
/// </summary>
public class DetMathTests
{
    private static readonly float[] Angles =
        { 0f, 0.1f, 0.5f, 1f, 1.5707964f, 2f, 3f, 3.1415927f, 4.5f, 6.2831855f, -0.3f, -2.5f, 10f, 100.25f, -77.7f };

    private static readonly uint[] SinBits =
    {
        0x00000000u, 0x3dcc7577u, 0x3ef57744u, 0x3f576aa4u, 0x3f800000u, 0x3f68c7b7u, 0x3e1081c3u, 0xb3bbbd2eu,
        0xbf7a3f6au, 0x343bbd2eu, 0xbe974e6du, 0xbf193578u, 0xbf0b44f8u, 0xbe8df805u, 0xbf3e9908u,
    };

    private static readonly uint[] CosBits =
    {
        0x3f800000u, 0x3f7eb898u, 0x3f60a940u, 0x3f0a5140u, 0xb33bbd2eu, 0xbed51133u, 0xbf7d7026u, 0xbf800000u,
        0xbe57dadbu, 0x3f800000u, 0x3f7490efu, 0xbf4d17bfu, 0xbf56cd64u, 0x3f75f639u, 0xbf2ae7a8u,
    };

    [Fact]
    public void SinCos_AreBitIdenticalEverywhere()
    {
        for (int i = 0; i < Angles.Length; i++)
        {
            Assert.Equal(SinBits[i], BitConverter.SingleToUInt32Bits(DetMath.Sin(Angles[i])));
            Assert.Equal(CosBits[i], BitConverter.SingleToUInt32Bits(DetMath.Cos(Angles[i])));
        }
    }

    [Fact]
    public void Exp_IsBitIdenticalEverywhere()
    {
        float[] xs = { 0f, 1f, -1f, 0.5f, -0.25f, -3.3f, -10f, 5f, 20f, -40f };
        uint[] bits = { 0x3f800000u, 0x402df854u, 0x3ebc5ab2u, 0x3fd3094cu, 0x3f475f7du, 0x3d1712ceu, 0x383e6bceu, 0x431469c5u, 0x4de75844u, 0x229cbc92u };
        for (int i = 0; i < xs.Length; i++)
            Assert.Equal(bits[i], BitConverter.SingleToUInt32Bits(DetMath.Exp(xs[i])));
    }

    [Fact]
    public void Atan2_IsBitIdenticalEverywhere()
    {
        (float Y, float X)[] ps =
        {
            (0f, 1f), (1f, 1f), (1f, 0f), (1f, -1f), (0f, -1f), (-1f, -1f), (-1f, 0f), (-1f, 1f),
            (0.3f, 2.5f), (-7.5f, 0.2f), (123f, -0.001f), (1e-6f, 1e6f),
        };
        uint[] bits =
        {
            0x00000000u, 0x3f490fdbu, 0x3fc90fdbu, 0x4016cbe4u, 0x40490fdbu, 0xc016cbe4u, 0xbfc90fdbu, 0xbf490fdbu,
            0x3df49728u, 0xbfc5a63fu, 0x3fc9101fu, 0x2b8cbcccu,
        };
        for (int i = 0; i < ps.Length; i++)
            Assert.Equal(bits[i], BitConverter.SingleToUInt32Bits(DetMath.Atan2(ps[i].Y, ps[i].X)));
    }

    [Fact]
    public void Results_AreAccurateToFloatPrecision()
    {
        for (int i = -20000; i <= 20000; i++)
        {
            float x = i * 0.005f;
            Assert.True(Math.Abs(DetMath.Sin(x) - Math.Sin(x)) < 2e-7, $"sin({x})");
            Assert.True(Math.Abs(DetMath.Cos(x) - Math.Cos(x)) < 2e-7, $"cos({x})");
        }
        for (int i = -4000; i <= 2000; i++)
        {
            float x = i * 0.01f;
            double e = Math.Exp(x);
            Assert.True(Math.Abs(DetMath.Exp(x) - e) / e < 3e-7, $"exp({x})");
        }
        Pcg32 rng = Pcg32.Create(1, 2);
        for (int i = 0; i < 20000; i++)
        {
            float y = rng.Range(-50f, 50f), x = rng.Range(-50f, 50f);
            Assert.True(Math.Abs(DetMath.Atan2(y, x) - Math.Atan2(y, x)) < 5e-7, $"atan2({y}, {x})");
        }
    }

    [Fact]
    public void EdgeCases()
    {
        Assert.True(float.IsNaN(DetMath.Sin(float.NaN)));
        Assert.True(float.IsNaN(DetMath.Cos(float.PositiveInfinity)));
        Assert.True(float.IsNaN(DetMath.Atan2(float.NaN, 1f)));
        Assert.Equal(0f, DetMath.Atan2(0f, 0f));
        Assert.Equal(0f, DetMath.Exp(-500f));
        Assert.True(float.IsPositiveInfinity(DetMath.Exp(500f)));
        Assert.Equal(1f, DetMath.Exp(0f));
    }

    [Fact]
    public void Vec2_HeadingRoundTrips()
    {
        for (int deg = -179; deg <= 180; deg += 7)
        {
            float h = deg * MathUtil.Pi / 180f;
            Vec2 v = Vec2.FromHeading(h);
            Assert.Equal(1f, v.Length, 5);
            Assert.Equal(h, v.Heading, 4);
            Vec2 r = v.Rotated(0.5f);
            Assert.Equal(MathUtil.WrapAngle(h + 0.5f), r.Heading, 4);
        }
    }
}
