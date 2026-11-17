using System;
using System.Collections.Generic;
using Godot;

namespace PaleoSphere.Presentation;

/// <summary>
/// 코드로 만드는 개체 메시. 단면(타원)을 척추를 따라 이어 붙여 매끈한 몸통을 만들고,
/// 뿔·프릴·눈 같은 부속을 같은 메시에 합친다. 외부 모델 파일 없이 종의 윤곽을 나타내기 위한
/// 중간 단계이며, 리깅된 모델이 준비되면 CreatureLayer에서 이 메시 대신 그 모델을 쓴다.
/// 색은 정점 색으로 넣는다(등은 어둡고 배는 밝게). 실제 피부색은 알려져 있지 않다.
/// </summary>
public sealed class CreatureMeshBuilder
{
    /// <summary>몸통 단면 하나: 중심 위치와 가로·세로 반지름.</summary>
    public readonly record struct Section(float Z, float Y, float HalfWidth, float HalfHeight);

    private readonly List<Vector3> _vertices = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<Color> _colors = new();
    private readonly List<int> _indices = new();

    public Color Back = new(0.4f, 0.35f, 0.25f);
    public Color Belly = new(0.75f, 0.70f, 0.58f);
    public float ShadeLow;
    public float ShadeHigh = 1f;

    private int Add(Vector3 position, Vector3 normal, Color color)
    {
        _vertices.Add(position);
        _normals.Add(normal.LengthSquared() > 1e-10f ? normal.Normalized() : Vector3.Up);
        _colors.Add(color);
        return _vertices.Count - 1;
    }

    /// <summary>삼각형 추가. Godot는 시계 방향이 앞면이라, 바깥쪽 힌트와 어긋나면 순서를 뒤집는다.</summary>
    private void Triangle(int a, int b, int c, Vector3 outward)
    {
        Vector3 n = (_vertices[c] - _vertices[a]).Cross(_vertices[b] - _vertices[a]);
        if (n.LengthSquared() < 1e-12f) return;
        if (n.Dot(outward) < 0f) (b, c) = (c, b);
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    /// <summary>높이에 따라 배에서 등으로 넘어가는 색에 얼룩을 조금 섞는다.</summary>
    private Color BodyColor(Vector3 p)
    {
        float t = Mathf.SmoothStep(ShadeLow, ShadeHigh, p.Y);
        float mottle = 0.90f + 0.16f * Hash(p * 3.1f);
        float stripe = 0.94f + 0.06f * Mathf.Sin(p.Z * 5.5f + p.X * 2f);
        Color c = Belly.Lerp(Back, t);
        float k = mottle * stripe;
        return new Color(c.R * k, c.G * k, c.B * k);
    }

    private static float Hash(Vector3 p)
    {
        float h = Mathf.Sin(p.X * 12.9898f + p.Y * 78.233f + p.Z * 37.719f) * 43758.5453f;
        return h - Mathf.Floor(h);
    }

    private static float CatmullRom(float a, float b, float c, float d, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3);
    }

    /// <summary>
    /// Z축을 따라 놓인 단면들을 부드럽게 이어 몸통을 만든다. 양 끝은 한 점으로 오므린다.
    /// </summary>
    public void AddBody(IReadOnlyList<Section> sections, int sides = 12, int subdivisions = 3, Color? flat = null)
    {
        var rings = new List<Section>();
        for (int i = 0; i < sections.Count - 1; i++)
        {
            Section a = sections[Math.Max(i - 1, 0)], b = sections[i], c = sections[i + 1], d = sections[Math.Min(i + 2, sections.Count - 1)];
            for (int s = 0; s < subdivisions; s++)
            {
                float t = s / (float)subdivisions;
                rings.Add(new Section(
                    Mathf.Lerp(b.Z, c.Z, t),
                    CatmullRom(a.Y, b.Y, c.Y, d.Y, t),
                    Mathf.Max(0.001f, CatmullRom(a.HalfWidth, b.HalfWidth, c.HalfWidth, d.HalfWidth, t)),
                    Mathf.Max(0.001f, CatmullRom(a.HalfHeight, b.HalfHeight, c.HalfHeight, d.HalfHeight, t))));
            }
        }
        rings.Add(sections[^1]);

        int first = _vertices.Count;
        foreach (Section r in rings)
        {
            for (int k = 0; k < sides; k++)
            {
                float angle = k * Mathf.Tau / sides;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                var p = new Vector3(cos * r.HalfWidth, r.Y + sin * r.HalfHeight, r.Z);
                var n = new Vector3(cos / r.HalfWidth, sin / r.HalfHeight, 0f);
                Add(p, n, flat ?? BodyColor(p));
            }
        }
        for (int i = 0; i < rings.Count - 1; i++)
        {
            for (int k = 0; k < sides; k++)
            {
                int a = first + i * sides + k, b = first + i * sides + (k + 1) % sides;
                int c = a + sides, d = b + sides;
                Vector3 outward = _normals[a];
                Triangle(a, b, c, outward);
                Triangle(b, d, c, outward);
            }
        }
        Cap(first, rings[0], sides, new Vector3(0, 0, rings[0].Z < rings[^1].Z ? -1 : 1), flat);
        Cap(first + (rings.Count - 1) * sides, rings[^1], sides, new Vector3(0, 0, rings[0].Z < rings[^1].Z ? 1 : -1), flat);
    }

    private void Cap(int ringStart, Section ring, int sides, Vector3 outward, Color? flat)
    {
        var center = new Vector3(0f, ring.Y, ring.Z);
        int c = Add(center, outward, flat ?? BodyColor(center));
        for (int k = 0; k < sides; k++)
            Triangle(c, ringStart + k, ringStart + (k + 1) % sides, outward);
    }

    /// <summary>from에서 to로 가늘어지는 원뿔대(뿔, 다리). radiusTo가 0이면 끝이 뾰족하다.</summary>
    public void AddCone(Vector3 from, Vector3 to, float radiusFrom, float radiusTo, Color color, int sides = 8, bool shadeAsBody = false)
    {
        Vector3 axis = (to - from).Normalized();
        Vector3 side = axis.Cross(Mathf.Abs(axis.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        Vector3 up = side.Cross(axis);
        int first = _vertices.Count;
        for (int ring = 0; ring < 2; ring++)
        {
            Vector3 center = ring == 0 ? from : to;
            float radius = Mathf.Max(ring == 0 ? radiusFrom : radiusTo, 0.0005f);
            for (int k = 0; k < sides; k++)
            {
                float angle = k * Mathf.Tau / sides;
                Vector3 n = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                Vector3 p = center + n * radius;
                Add(p, n, shadeAsBody ? BodyColor(p) : color);
            }
        }
        for (int k = 0; k < sides; k++)
        {
            int a = first + k, b = first + (k + 1) % sides, c = a + sides, d = b + sides;
            Triangle(a, b, c, _normals[a]);
            Triangle(b, d, c, _normals[a]);
        }
        int capA = Add(from, -axis, shadeAsBody ? BodyColor(from) : color);
        int capB = Add(to, axis, shadeAsBody ? BodyColor(to) : color);
        for (int k = 0; k < sides; k++)
        {
            Triangle(capA, first + k, first + (k + 1) % sides, -axis);
            Triangle(capB, first + sides + k, first + sides + (k + 1) % sides, axis);
        }
    }

    /// <summary>타원체(눈, 발, 프릴). basis로 기울일 수 있다.</summary>
    public void AddEllipsoid(Vector3 center, Vector3 radii, Color color, Basis? basis = null, int segments = 10, int rings = 6, bool shadeAsBody = false)
    {
        Basis b = basis ?? Basis.Identity;
        int first = _vertices.Count;
        for (int r = 0; r <= rings; r++)
        {
            float phi = r * Mathf.Pi / rings;
            for (int s = 0; s < segments; s++)
            {
                float theta = s * Mathf.Tau / segments;
                var unit = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                Vector3 p = center + b * (unit * radii);
                Vector3 n = b * (unit / radii);
                Add(p, n, shadeAsBody ? BodyColor(p) : color);
            }
        }
        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                int a = first + r * segments + s, bb = first + r * segments + (s + 1) % segments;
                int c = a + segments, d = bb + segments;
                Vector3 outward = _vertices[a] + _vertices[c] - center * 2f;
                Triangle(a, bb, c, outward);
                Triangle(bb, d, c, outward);
            }
        }
    }

    public ArrayMesh Build()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
