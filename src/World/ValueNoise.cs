using System;
using PaleoSphere.Core;

namespace PaleoSphere.World;

/// <summary>격자 해시 기반 값 노이즈. 외부 라이브러리 없이 시드만으로 재현된다.</summary>
public static class ValueNoise
{
    /// <summary>격자점 (x, y)의 [0, 1) 해시 값.</summary>
    public static float Hash(ulong seed, int x, int y)
    {
        ulong h = seed;
        h ^= unchecked((ulong)x * 0x9E3779B97F4A7C15UL);
        h = Pcg32.SplitMix64(h);
        h ^= unchecked((ulong)y * 0xC2B2AE3D27D4EB4FUL);
        h = Pcg32.SplitMix64(h);
        return (h >> 40) * (1f / 16777216f);
    }

    public static float Sample(ulong seed, float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
        float a = Hash(seed, x0, y0), b = Hash(seed, x0 + 1, y0);
        float c = Hash(seed, x0, y0 + 1), d = Hash(seed, x0 + 1, y0 + 1);
        return MathUtil.Lerp(MathUtil.Lerp(a, b, sx), MathUtil.Lerp(c, d, sx), sy);
    }

    /// <summary>여러 옥타브를 합친 프랙탈 노이즈. 결과는 대략 0~1.</summary>
    public static float Fbm(ulong seed, float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Sample(seed + (ulong)o * 1013UL, x * freq, y * freq);
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }
        return sum / norm;
    }
}
