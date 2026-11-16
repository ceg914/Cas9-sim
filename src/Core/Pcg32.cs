namespace PaleoSphere.Core;

/// <summary>
/// 결정론적 난수(PCG32, XSH-RR). System.Random은 런타임 구현에 따라 수열이 달라질 수 있어 쓰지 않는다.
/// 상태 두 값(State, Inc)만 저장하면 수열을 그대로 이어 갈 수 있다.
/// 가변 구조체이므로 반드시 필드에 두고 직접 호출한다(복사본에서 호출하면 상태가 전진하지 않는다).
/// </summary>
public struct Pcg32
{
    public ulong State;
    public ulong Inc;

    private const ulong Multiplier = 6364136223846793005UL;

    /// <summary>시드와 스트림 번호로 독립적인 수열을 만든다. 개체마다 고유한 streamId를 준다.</summary>
    public static Pcg32 Create(ulong seed, ulong streamId)
    {
        var rng = new Pcg32
        {
            State = 0UL,
            Inc = (SplitMix64(streamId ^ 0x9E3779B97F4A7C15UL) << 1) | 1UL,
        };
        rng.NextUInt();
        rng.State += SplitMix64(seed);
        rng.NextUInt();
        return rng;
    }

    public static ulong SplitMix64(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    public uint NextUInt()
    {
        ulong old = State;
        State = unchecked(old * Multiplier + Inc);
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    /// <summary>[0, 1) 범위. 24비트 정밀도라 float로 정확히 표현된다.</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>[0, maxExclusive) 정수. 편향 없는 거부 표본 추출.</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 1) return 0;
        uint bound = (uint)maxExclusive;
        uint threshold = (uint)(-(int)bound) % bound;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    /// <summary>평균 0, 표준편차 1에 가까운 값(균등 난수 4개의 합, 범위 약 ±3.46).</summary>
    public float NextGaussianApprox()
    {
        float sum = NextFloat() + NextFloat() + NextFloat() + NextFloat();
        return (sum - 2f) * 1.7320508f;
    }
}
