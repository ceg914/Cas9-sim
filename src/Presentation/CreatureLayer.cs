using System;
using System.Collections.Generic;
using Godot;
using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Simulation;
using PaleoSphere.World;

namespace PaleoSphere.Presentation;

/// <summary>
/// 개체의 임시 도형 프록시. 리깅 모델이 들어오기 전까지 캡슐·상자·원뿔을 조합해 종을 나타내고,
/// 동작(서기·걷기·달리기·먹기·마시기·쉬기)에 따라 절차적으로 움직인다.
/// 위치는 스냅샷의 이전·현재 값을 보간하므로 틱 속도와 프레임률이 달라도 부드럽다.
/// </summary>
public partial class CreatureLayer : Node3D
{
    private sealed class Proxy
    {
        public Node3D Root = null!;
        public Node3D Body = null!;
        public Node3D Head = null!;
        public Node3D Details = null!;
        public Node3D[] Legs = Array.Empty<Node3D>();
        public Node3D Tail = null!;
        public int SpeciesIndex;
        public float Phase;
        public float HeadPitch;
        public float Crouch;
        public bool Seen;
    }

    private sealed class Corpse
    {
        public MeshInstance3D Node = null!;
        public double Age;
    }

    private readonly Dictionary<int, Proxy> _proxies = new();
    private readonly Dictionary<int, Stack<Proxy>> _pool = new();
    private readonly Dictionary<int, StandardMaterial3D[]> _materials = new();
    private readonly List<Corpse> _corpses = new();
    private readonly List<int> _stale = new();
    private MeshInstance3D _selectionRing = null!;

    private static readonly Mesh Capsule = new CapsuleMesh { Radius = 0.5f, Height = 2f, RadialSegments = 10, Rings = 4 };
    private static readonly Mesh Box = new BoxMesh { Size = Vector3.One };
    private static readonly Mesh Cone = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 };
    private static readonly Mesh Cylinder = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 };
    private static readonly Mesh Disc = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 14, Rings = 1 };

    public int SelectedId { get; set; } = -1;

    /// <summary>선택한 개체의 보간된 위치(추적 카메라용). 선택이 없으면 null.</summary>
    public Vector3? SelectedPosition { get; private set; }

    public const float DetailDistance = 260f;
    private const double CorpseSeconds = 25.0;

    public override void _Ready()
    {
        _selectionRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.86f, OuterRadius = 1f, Rings = 32, RingSegments = 6 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.86f, 0.25f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = false,
            },
        };
        AddChild(_selectionRing);
    }

    public void Clear()
    {
        foreach (Proxy p in _proxies.Values) p.Root.QueueFree();
        _proxies.Clear();
        foreach (Stack<Proxy> stack in _pool.Values)
            foreach (Proxy p in stack) p.Root.QueueFree();
        _pool.Clear();
        foreach (Corpse c in _corpses) c.Node.QueueFree();
        _corpses.Clear();
        _materials.Clear();
        SelectedId = -1;
        SelectedPosition = null;
        _selectionRing.Visible = false;
    }

    private StandardMaterial3D[] MaterialsFor(int speciesIndex, SpeciesDefinition sp)
    {
        if (_materials.TryGetValue(speciesIndex, out StandardMaterial3D[]? m)) return m;
        var baseColor = new Color(sp.ProxyColorR, sp.ProxyColorG, sp.ProxyColorB);
        m = new[]
        {
            new StandardMaterial3D { AlbedoColor = baseColor, Roughness = 0.85f },
            new StandardMaterial3D { AlbedoColor = baseColor.Darkened(0.25f), Roughness = 0.9f },
            new StandardMaterial3D { AlbedoColor = new Color(0.90f, 0.86f, 0.74f), Roughness = 0.6f },
        };
        _materials[speciesIndex] = m;
        return m;
    }

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3 scale, Vector3 rotationDeg)
    {
        var part = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            Position = position,
            Scale = scale,
            RotationDegrees = rotationDeg,
        };
        parent.AddChild(part);
        return part;
    }

    /// <summary>몸길이 L인 네발 동물 프록시. 모델의 앞은 -Z.</summary>
    private Proxy Build(int speciesIndex, SpeciesDefinition sp)
    {
        StandardMaterial3D[] mat = MaterialsFor(speciesIndex, sp);
        float L = sp.BodyLengthM;
        bool ceratopsian = sp.ProxyShape == "ceratopsian";
        var proxy = new Proxy { SpeciesIndex = speciesIndex, Root = new Node3D() };

        float legHeight = 0.17f * L;
        float bodyRadius = 0.12f * L;
        float bodyY = legHeight + bodyRadius * 0.85f;

        proxy.Body = new Node3D { Position = new Vector3(0f, bodyY, 0f) };
        proxy.Root.AddChild(proxy.Body);
        // 몸통: 눕힌 캡슐.
        Part(proxy.Body, Capsule, mat[0], Vector3.Zero, new Vector3(bodyRadius * 2f, 0.26f * L, bodyRadius * 2f), new Vector3(90f, 0f, 0f));

        proxy.Details = new Node3D();
        proxy.Body.AddChild(proxy.Details);

        // 꼬리: 뒤로 가늘어지는 원뿔.
        proxy.Tail = new Node3D { Position = new Vector3(0f, 0f, 0.24f * L) };
        proxy.Details.AddChild(proxy.Tail);
        Part(proxy.Tail, Cone, mat[1], new Vector3(0f, -0.01f * L, 0.14f * L), new Vector3(0.15f * L, 0.30f * L, 0.15f * L), new Vector3(90f, 0f, 0f));

        // 머리: 목 관절에서 숙였다 든다.
        proxy.Head = new Node3D { Position = new Vector3(0f, 0.01f * L, -0.26f * L) };
        proxy.Body.AddChild(proxy.Head);
        Part(proxy.Head, Box, mat[0], new Vector3(0f, -0.01f * L, -0.10f * L), new Vector3(0.13f * L, 0.13f * L, 0.22f * L), Vector3.Zero);
        if (ceratopsian)
        {
            // 프릴(목 방패), 눈 위 뿔 둘, 코 뿔 하나. 화석으로 확인되는 윤곽만 흉내 낸다.
            Part(proxy.Head, Disc, mat[1], new Vector3(0f, 0.07f * L, 0.02f * L), new Vector3(0.30f * L, 0.02f * L, 0.26f * L), new Vector3(68f, 0f, 0f));
            var horns = new Node3D();
            proxy.Head.AddChild(horns);
            Part(horns, Cone, mat[2], new Vector3(-0.04f * L, 0.08f * L, -0.14f * L), new Vector3(0.022f * L, 0.13f * L, 0.022f * L), new Vector3(-52f, 0f, 0f));
            Part(horns, Cone, mat[2], new Vector3(0.04f * L, 0.08f * L, -0.14f * L), new Vector3(0.022f * L, 0.13f * L, 0.022f * L), new Vector3(-52f, 0f, 0f));
            Part(horns, Cone, mat[2], new Vector3(0f, 0.06f * L, -0.20f * L), new Vector3(0.02f * L, 0.05f * L, 0.02f * L), new Vector3(-20f, 0f, 0f));
        }

        // 다리 넷: 어깨·엉덩이 관절에서 앞뒤로 흔든다.
        proxy.Legs = new Node3D[4];
        float lx = bodyRadius * 0.72f, lzFront = -0.17f * L, lzBack = 0.15f * L;
        var offsets = new[] { new Vector3(-lx, 0f, lzFront), new Vector3(lx, 0f, lzFront), new Vector3(-lx, 0f, lzBack), new Vector3(lx, 0f, lzBack) };
        for (int i = 0; i < 4; i++)
        {
            var joint = new Node3D { Position = offsets[i] + new Vector3(0f, legHeight, 0f) };
            proxy.Root.AddChild(joint);
            Part(joint, Cylinder, mat[1], new Vector3(0f, -legHeight * 0.5f, 0f), new Vector3(0.055f * L, legHeight, 0.055f * L), Vector3.Zero);
            proxy.Legs[i] = joint;
        }
        return proxy;
    }

    private Proxy Acquire(int speciesIndex, SpeciesDefinition sp)
    {
        if (_pool.TryGetValue(speciesIndex, out Stack<Proxy>? stack) && stack.Count > 0)
        {
            Proxy reused = stack.Pop();
            reused.Root.Visible = true;
            return reused;
        }
        Proxy p = Build(speciesIndex, sp);
        AddChild(p.Root);
        return p;
    }

    private void Release(Proxy p)
    {
        p.Root.Visible = false;
        if (!_pool.TryGetValue(p.SpeciesIndex, out Stack<Proxy>? stack))
        {
            stack = new Stack<Proxy>();
            _pool[p.SpeciesIndex] = stack;
        }
        stack.Push(p);
    }

    /// <summary>사망 사건이 났을 때 그 자리에 잠깐 남는 표식. 사체 자원(분해·영양 환원)은 아직 구현되지 않았다.</summary>
    public void AddCorpseMarker(Vec2 position, TerrainGrid terrain, SpeciesDefinition sp)
    {
        float L = sp.BodyLengthM;
        var node = new MeshInstance3D
        {
            Mesh = Capsule,
            Scale = new Vector3(0.22f * L, 0.26f * L, 0.14f * L),
            RotationDegrees = new Vector3(90f, (position.X * 37f + position.Z * 11f) % 360f, 0f),
            Position = new Vector3(position.X, Mathf.Max(terrain.HeightAt(position), terrain.WaterLevel) + 0.07f * L, position.Z),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.20f, 0.17f, 0.14f),
                Roughness = 1f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        AddChild(node);
        _corpses.Add(new Corpse { Node = node });
    }

    /// <summary>스냅샷을 화면에 반영한다. alpha는 이전 틱과 현재 틱 사이의 보간 계수.</summary>
    public void UpdateFrame(SimSnapshot snapshot, SpeciesRegistry species, TerrainGrid terrain, float alpha, double delta, Vector3 cameraPosition)
    {
        foreach (Proxy p in _proxies.Values) p.Seen = false;
        SelectedPosition = null;
        float dt = (float)delta;

        for (int i = 0; i < snapshot.Count; i++)
        {
            CreatureSnapshot c = snapshot.Creatures[i];
            SpeciesDefinition sp = species[c.SpeciesIndex];
            if (!_proxies.TryGetValue(c.Id, out Proxy? p))
            {
                p = Acquire(c.SpeciesIndex, sp);
                p.Phase = (c.Id * 0.618f) % 1f * Mathf.Tau;
                _proxies[c.Id] = p;
            }
            p.Seen = true;

            float x = Mathf.Lerp(c.PrevPosition.X, c.Position.X, alpha);
            float z = Mathf.Lerp(c.PrevPosition.Z, c.Position.Z, alpha);
            float heading = Mathf.LerpAngle(c.PrevHeading, c.Heading, alpha);
            float y = terrain.HeightAt(new Vec2(x, z));
            var position = new Vector3(x, y, z);
            float L = sp.BodyLengthM;

            // 걸음: 속도에 맞춰 다리를 흔들고 몸을 살짝 출렁인다.
            bool moving = c.Speed > 0.05f && (c.Activity == ActivityKind.Walk || c.Activity == ActivityKind.Run);
            float stride = L * 0.42f;
            if (moving) p.Phase += c.Speed / stride * Mathf.Tau * dt;
            else p.Phase += dt * 0.9f;
            float swing = moving ? (c.Activity == ActivityKind.Run ? 0.75f : 0.45f) : 0f;
            float bob = moving ? Mathf.Abs(Mathf.Sin(p.Phase)) * 0.012f * L * (c.Activity == ActivityKind.Run ? 2.2f : 1f) : Mathf.Sin(p.Phase) * 0.003f * L;

            float targetCrouch = c.Activity == ActivityKind.Rest ? 1f : 0f;
            float targetPitch = c.Activity switch
            {
                ActivityKind.Eat => 0.75f + 0.12f * Mathf.Sin(p.Phase * 3f),
                ActivityKind.Drink => 0.85f,
                ActivityKind.Rest => 0.35f,
                ActivityKind.Run => -0.12f,
                _ => 0.05f,
            };
            float blend = Mathf.Min(1f, dt * 5f);
            p.Crouch = Mathf.Lerp(p.Crouch, targetCrouch, blend);
            p.HeadPitch = Mathf.Lerp(p.HeadPitch, targetPitch, blend);

            float legHeight = 0.17f * L;
            p.Root.Position = position + new Vector3(0f, bob - p.Crouch * legHeight * 0.85f, 0f);
            p.Root.Rotation = new Vector3(0f, -heading - Mathf.Pi * 0.5f, 0f);
            p.Root.Scale = Vector3.One * c.BodySize;
            p.Head.Rotation = new Vector3(-p.HeadPitch, 0f, 0f);
            p.Tail.Rotation = new Vector3(0f, Mathf.Sin(p.Phase * 0.5f) * (moving ? 0.18f : 0.06f), 0f);

            bool detailed = position.DistanceSquaredTo(cameraPosition) < DetailDistance * DetailDistance;
            p.Details.Visible = detailed;
            for (int l = 0; l < 4; l++)
            {
                Node3D leg = p.Legs[l];
                leg.Visible = detailed && p.Crouch < 0.6f;
                // 대각선 다리끼리 같은 위상.
                float sign = (l == 0 || l == 3) ? 1f : -1f;
                leg.Rotation = new Vector3(Mathf.Sin(p.Phase) * swing * sign, 0f, 0f);
            }

            if (c.Id == SelectedId)
            {
                SelectedPosition = position;
                float r = L * 0.62f * c.BodySize;
                _selectionRing.Visible = true;
                _selectionRing.Position = new Vector3(x, Mathf.Max(y, terrain.WaterLevel) + 0.25f, z);
                _selectionRing.Scale = new Vector3(r, r, r);
            }
        }

        _stale.Clear();
        foreach (KeyValuePair<int, Proxy> kv in _proxies)
            if (!kv.Value.Seen) _stale.Add(kv.Key);
        foreach (int id in _stale)
        {
            Release(_proxies[id]);
            _proxies.Remove(id);
        }
        if (SelectedPosition == null) _selectionRing.Visible = false;

        for (int i = _corpses.Count - 1; i >= 0; i--)
        {
            Corpse corpse = _corpses[i];
            corpse.Age += delta;
            if (corpse.Age >= CorpseSeconds)
            {
                corpse.Node.QueueFree();
                _corpses.RemoveAt(i);
                continue;
            }
            if (corpse.Node.MaterialOverride is StandardMaterial3D m)
            {
                float fade = (float)Math.Clamp((CorpseSeconds - corpse.Age) / 6.0, 0.0, 1.0);
                m.AlbedoColor = new Color(m.AlbedoColor.R, m.AlbedoColor.G, m.AlbedoColor.B, fade);
            }
        }
    }
}
