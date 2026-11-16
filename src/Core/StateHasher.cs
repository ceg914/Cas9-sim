using System;

namespace PaleoSphere.Core;

/// <summary>재현성 검증용 상태 해시(FNV-1a 64비트). float는 비트 패턴 그대로 넣는다.</summary>
public struct StateHasher
{
    private ulong _hash;

    public static StateHasher Create() => new() { _hash = 14695981039346656037UL };

    public readonly ulong Value => _hash;

    private void AddByte(byte b)
    {
        _hash ^= b;
        _hash = unchecked(_hash * 1099511628211UL);
    }

    public void Add(uint v)
    {
        AddByte((byte)v);
        AddByte((byte)(v >> 8));
        AddByte((byte)(v >> 16));
        AddByte((byte)(v >> 24));
    }

    public void Add(int v) => Add(unchecked((uint)v));

    public void Add(ulong v)
    {
        Add((uint)v);
        Add((uint)(v >> 32));
    }

    public void Add(long v) => Add(unchecked((ulong)v));
    public void Add(float v) => Add(BitConverter.SingleToUInt32Bits(v));
    public void Add(double v) => Add(BitConverter.DoubleToUInt64Bits(v));
    public void Add(bool v) => AddByte(v ? (byte)1 : (byte)0);
    public void Add(Vec2 v) { Add(v.X); Add(v.Z); }

    public void Add(string? s)
    {
        if (s == null) { Add(-1); return; }
        Add(s.Length);
        foreach (char c in s) { AddByte((byte)c); AddByte((byte)(c >> 8)); }
    }

    public void Add(ReadOnlySpan<float> values)
    {
        Add(values.Length);
        foreach (float f in values) Add(f);
    }

    public void Add(ReadOnlySpan<byte> values)
    {
        Add(values.Length);
        foreach (byte b in values) AddByte(b);
    }

    public static string ToHex(ulong hash) => hash.ToString("x16");
}
