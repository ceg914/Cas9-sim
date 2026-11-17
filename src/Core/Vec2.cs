using System;

namespace PaleoSphere.Core;

/// <summary>지면 평면(X, Z) 좌표. 높이는 지형에서 조회한다.</summary>
public readonly struct Vec2 : IEquatable<Vec2>
{
    public readonly float X;
    public readonly float Z;

    public Vec2(float x, float z)
    {
        X = x;
        Z = z;
    }

    public static readonly Vec2 Zero = new(0f, 0f);

    public float Length => MathF.Sqrt(X * X + Z * Z);
    public float LengthSquared => X * X + Z * Z;

    public Vec2 Normalized()
    {
        float len = Length;
        return len > 1e-6f ? new Vec2(X / len, Z / len) : Zero;
    }

    public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared;
    public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;

    /// <summary>heading(라디안) 방향의 단위 벡터. heading 0은 +X, 양의 방향은 +Z 쪽 회전.</summary>
    public static Vec2 FromHeading(float heading) => new(DetMath.Cos(heading), DetMath.Sin(heading));

    public float Heading => DetMath.Atan2(Z, X);

    public Vec2 Rotated(float radians)
    {
        float c = DetMath.Cos(radians), s = DetMath.Sin(radians);
        return new Vec2(X * c - Z * s, X * s + Z * c);
    }

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Z + b.Z);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Z - b.Z);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Z * s);
    public static Vec2 operator /(Vec2 a, float s) => new(a.X / s, a.Z / s);

    public bool Equals(Vec2 other) => X.Equals(other.X) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Vec2 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Z);
    public override string ToString() => FormattableString.Invariant($"({X:0.0}, {Z:0.0})");
}
