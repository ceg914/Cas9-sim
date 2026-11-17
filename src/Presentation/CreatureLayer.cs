using System;
using System.Collections.Generic;
using Godot;
using PaleoSphere.Core;
using PaleoSphere.Creatures;
using PaleoSphere.Simulation;
using PaleoSphere.World;
using Section = PaleoSphere.Presentation.CreatureMeshBuilder.Section;

namespace PaleoSphere.Presentation;

/// <summary>
/// 개체의 3D 표시. 리깅 모델이 들어오기 전까지는 코드로 만든 메시(CreatureMeshBuilder)를 쓴다.
/// 몸통·머리·꼬리·다리를 관절 노드로 나눠 동작(서기·걷기·달리기·먹기·마시기·쉬기)에 따라 움직인다.
/// 위치는 스냅샷의 이전·현재 값을 보간하므로 틱 속도와 프레임률이 달라도 부드럽다.
/// 메시는 종마다 한 번 만들어 모든 개체가 함께 쓴다.
/// </summary>
public partial class CreatureLayer : Node3D
{
    private sealed class SpeciesMeshes
    {
        public ArrayMesh Torso = null!, Head = null!, Tail = null!;
        public ArrayMesh UpperFront = null!, LowerFront = null!, UpperHind = null!, LowerHind = null!;
        public Vector3 HeadPivot, TailPivot;
        public Vector3[] HipPositions = new Vector3[4];
        public float UpperFrontLength, UpperHindLength, BellyHeight;
        public StandardMaterial3D MarkerMaterial = null!;
    }

    private sealed class Proxy
    {
        public Node3D Root = null!;
        public Node3D Body = null!;
        public Node3D Head = null!;
        public Node3D Tail = null!;
        public readonly Node3D[] Hips = new Node3D[4];
        public readonly Node3D[] Knees = new Node3D[4];
        public MeshInstance3D Marker = null!;
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
    private readonly Dictionary<int, SpeciesMeshes> _meshes = new();
    private readonly List<Corpse> _corpses = new();
    private readonly List<int> _stale = new();
    private MeshInstance3D _selectionRing = null!;

    // 정점 색은 sRGB 값으로 넣었으므로 그렇게 해석하게 한다(안 하면 색이 하얗게 뜬다).
    private static readonly StandardMaterial3D SkinMaterial = new() { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.9f };
    private static readonly Mesh MarkerMesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 };

    public int SelectedId { get; set; } = -1;

    /// <summary>선택한 개체의 보간된 위치(추적 카메라용). 선택이 없으면 null.</summary>
    public Vector3? SelectedPosition { get; private set; }

    /// <summary>이 거리보다 멀면 다리를 그리지 않는다.</summary>
    public const float DetailDistance = 260f;

    /// <summary>이 거리보다 멀면 개체 위에 위치 표식을 띄운다(멀리서도 찾을 수 있게).</summary>
    public const float MarkerDistance = 90f;
    private const double CorpseSeconds = 25.0;

    public override void _Ready()
    {
        _selectionRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1f, Rings = 48, RingSegments = 6 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.86f, 0.25f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
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
        _meshes.Clear();
        SelectedId = -1;
        SelectedPosition = null;
        _selectionRing.Visible = false;
    }

    // ------------------------------------------------------------ 메시

    private static Section[] Scale(float L, params (float Z, float Y, float W, float H)[] s)
    {
        var result = new Section[s.Length];
        for (int i = 0; i < s.Length; i++) result[i] = new Section(s[i].Z * L, s[i].Y * L, s[i].W * L, s[i].H * L);
        return result;
    }

    /// <summary>
    /// 몸길이 L인 네발 동물의 메시를 만든다. 수치는 몸길이에 대한 비율이고 모델의 앞은 -Z다.
    /// ceratopsian이면 프릴과 뿔 세 개를 붙인다(화석으로 확인되는 윤곽만 흉내 낸 것).
    /// </summary>
    private SpeciesMeshes MeshesFor(int speciesIndex, SpeciesDefinition sp)
    {
        if (_meshes.TryGetValue(speciesIndex, out SpeciesMeshes? cached)) return cached;
        float L = sp.BodyLengthM;
        bool ceratopsian = sp.ProxyShape == "ceratopsian";
        var skin = new Color(sp.ProxyColorR, sp.ProxyColorG, sp.ProxyColorB);
        Color back = skin.Darkened(0.18f);
        Color belly = skin.Lerp(new Color(0.86f, 0.82f, 0.70f), 0.6f);
        var bone = new Color(0.90f, 0.86f, 0.74f);
        var m = new SpeciesMeshes
        {
            HeadPivot = new Vector3(0f, 0.255f, -0.20f) * L,
            TailPivot = new Vector3(0f, 0.250f, 0.21f) * L,
            UpperFrontLength = 0.100f * L,
            UpperHindLength = 0.110f * L,
            BellyHeight = 0.15f * L,
            MarkerMaterial = new StandardMaterial3D { AlbedoColor = skin.Lightened(0.55f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };

        CreatureMeshBuilder NewBuilder(float low, float high) => new() { Back = back, Belly = belly, ShadeLow = low * L, ShadeHigh = high * L };

        // 몸통: 목 뒤에서 꼬리 밑동까지.
        CreatureMeshBuilder torso = NewBuilder(0.17f, 0.34f);
        torso.AddBody(Scale(L,
            (-0.22f, 0.250f, 0.040f, 0.050f), (-0.16f, 0.262f, 0.085f, 0.100f), (-0.07f, 0.275f, 0.115f, 0.125f),
            (0.03f, 0.280f, 0.120f, 0.130f), (0.12f, 0.275f, 0.100f, 0.112f), (0.20f, 0.258f, 0.060f, 0.072f),
            (0.25f, 0.245f, 0.042f, 0.050f)), sides: 14);
        m.Torso = torso.Build();

        // 꼬리: 밑동 관절 기준 좌표.
        CreatureMeshBuilder tail = NewBuilder(-0.09f, 0.05f);
        tail.AddBody(Scale(L,
            (0.00f, 0.000f, 0.052f, 0.062f), (0.07f, -0.012f, 0.036f, 0.042f), (0.15f, -0.030f, 0.022f, 0.026f),
            (0.23f, -0.050f, 0.010f, 0.012f), (0.29f, -0.065f, 0.003f, 0.004f)), sides: 10);
        m.Tail = tail.Build();

        // 머리: 목 관절 기준 좌표.
        CreatureMeshBuilder head = NewBuilder(-0.07f, 0.06f);
        head.AddBody(Scale(L,
            (0.03f, 0.000f, 0.050f, 0.060f), (-0.04f, 0.000f, 0.060f, 0.068f), (-0.10f, -0.008f, 0.050f, 0.058f),
            (-0.17f, -0.022f, 0.034f, 0.042f), (-0.22f, -0.036f, 0.018f, 0.026f), (-0.245f, -0.048f, 0.004f, 0.006f)), sides: 12);
        var eye = new Color(0.04f, 0.04f, 0.04f);
        head.AddEllipsoid(new Vector3(-0.047f, 0.012f, -0.105f) * L, Vector3.One * 0.008f * L, eye, segments: 6, rings: 4);
        head.AddEllipsoid(new Vector3(0.047f, 0.012f, -0.105f) * L, Vector3.One * 0.008f * L, eye, segments: 6, rings: 4);
        if (ceratopsian)
        {
            // 프릴: 뒤로 젖힌 얇은 방패.
            head.ShadeLow = -0.02f * L;
            head.ShadeHigh = 0.20f * L;
            head.AddEllipsoid(new Vector3(0f, 0.085f, 0.040f) * L, new Vector3(0.150f, 0.125f, 0.018f) * L, back,
                new Basis(Vector3.Right, Mathf.DegToRad(-24f)), segments: 14, rings: 8, shadeAsBody: true);
            // 눈 위 뿔 둘과 코 뿔.
            head.AddCone(new Vector3(-0.036f, 0.045f, -0.085f) * L, new Vector3(-0.052f, 0.140f, -0.205f) * L, 0.015f * L, 0f, bone);
            head.AddCone(new Vector3(0.036f, 0.045f, -0.085f) * L, new Vector3(0.052f, 0.140f, -0.205f) * L, 0.015f * L, 0f, bone);
            head.AddCone(new Vector3(0f, 0.012f, -0.185f) * L, new Vector3(0f, 0.058f, -0.202f) * L, 0.012f * L, 0f, bone, sides: 6);
        }
        m.Head = head.Build();

        // 다리: 위·아래 두 마디와 발. 엉덩이·무릎 관절 기준 좌표.
        ArrayMesh Upper(float length, float top, float bottom)
        {
            CreatureMeshBuilder b = NewBuilder(-0.14f, 0.02f);
            b.AddCone(new Vector3(0f, 0.012f * L, 0f), new Vector3(0f, -length, 0f), top * L, bottom * L, back, 9, shadeAsBody: true);
            return b.Build();
        }
        ArrayMesh Lower(float length, float top, float bottom)
        {
            CreatureMeshBuilder b = NewBuilder(-0.02f, 0.14f);
            b.AddCone(new Vector3(0f, 0.008f * L, 0f), new Vector3(0f, -length + 0.012f * L, 0f), top * L, bottom * L, back, 9, shadeAsBody: true);
            b.AddEllipsoid(new Vector3(0f, -length + 0.014f * L, -0.010f * L), new Vector3(0.040f, 0.016f, 0.046f) * L, belly.Darkened(0.25f), segments: 8, rings: 4);
            return b.Build();
        }
        float frontTotal = 0.215f * L, hindTotal = 0.235f * L;
        m.UpperFront = Upper(m.UpperFrontLength, 0.048f, 0.036f);
        m.LowerFront = Lower(frontTotal - m.UpperFrontLength, 0.036f, 0.031f);
        m.UpperHind = Upper(m.UpperHindLength, 0.060f, 0.042f);
        m.LowerHind = Lower(hindTotal - m.UpperHindLength, 0.042f, 0.034f);
        m.HipPositions[0] = new Vector3(-0.085f * L, frontTotal, -0.135f * L);
        m.HipPositions[1] = new Vector3(0.085f * L, frontTotal, -0.135f * L);
        m.HipPositions[2] = new Vector3(-0.085f * L, hindTotal, 0.115f * L);
        m.HipPositions[3] = new Vector3(0.085f * L, hindTotal, 0.115f * L);

        _meshes[speciesIndex] = m;
        return m;
    }

    private static MeshInstance3D Instance(Node3D parent, Mesh mesh)
    {
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = SkinMaterial };
        parent.AddChild(node);
        return node;
    }

    private Proxy Build(int speciesIndex, SpeciesDefinition sp)
    {
        SpeciesMeshes m = MeshesFor(speciesIndex, sp);
        var proxy = new Proxy { SpeciesIndex = speciesIndex, Root = new Node3D() };

        proxy.Body = new Node3D();
        proxy.Root.AddChild(proxy.Body);
        Instance(proxy.Body, m.Torso);

        proxy.Head = new Node3D { Position = m.HeadPivot };
        proxy.Body.AddChild(proxy.Head);
        Instance(proxy.Head, m.Head);

        proxy.Tail = new Node3D { Position = m.TailPivot };
        proxy.Body.AddChild(proxy.Tail);
        Instance(proxy.Tail, m.Tail);

        for (int i = 0; i < 4; i++)
        {
            bool front = i < 2;
            var hip = new Node3D { Position = m.HipPositions[i] };
            proxy.Root.AddChild(hip);
            Instance(hip, front ? m.UpperFront : m.UpperHind);
            var knee = new Node3D { Position = new Vector3(0f, -(front ? m.UpperFrontLength : m.UpperHindLength), 0f) };
            hip.AddChild(knee);
            Instance(knee, front ? m.LowerFront : m.LowerHind);
            proxy.Hips[i] = hip;
            proxy.Knees[i] = knee;
        }

        proxy.Marker = new MeshInstance3D
        {
            Mesh = MarkerMesh,
            MaterialOverride = m.MarkerMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        proxy.Root.AddChild(proxy.Marker);
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
    public void AddCorpseMarker(Vec2 position, TerrainGrid terrain, SpeciesDefinition sp, int speciesIndex)
    {
        SpeciesMeshes m = MeshesFor(speciesIndex, sp);
        float L = sp.BodyLengthM;
        var node = new MeshInstance3D
        {
            Mesh = m.Torso,
            // 옆으로 쓰러진 몸통.
            RotationDegrees = new Vector3(0f, (position.X * 37f + position.Z * 11f) % 360f, 82f),
            Position = new Vector3(position.X, Mathf.Max(terrain.HeightAt(position), terrain.WaterLevel) + 0.10f * L, position.Z),
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

    // ------------------------------------------------------------ 프레임

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
            SpeciesMeshes m = _meshes[c.SpeciesIndex];

            float x = Mathf.Lerp(c.PrevPosition.X, c.Position.X, alpha);
            float z = Mathf.Lerp(c.PrevPosition.Z, c.Position.Z, alpha);
            float heading = Mathf.LerpAngle(c.PrevHeading, c.Heading, alpha);
            float y = terrain.HeightAt(new Vec2(x, z));
            var position = new Vector3(x, y, z);
            float L = sp.BodyLengthM;

            // 걸음: 한 걸음 길이에 맞춰 위상을 돌린다. 발이 미끄러져 보이지 않게 속도에 비례시킨다.
            bool moving = c.Speed > 0.05f && (c.Activity == ActivityKind.Walk || c.Activity == ActivityKind.Run);
            bool running = c.Activity == ActivityKind.Run;
            float stride = L * 0.42f;
            if (moving) p.Phase += c.Speed / stride * Mathf.Tau * dt;
            else p.Phase += dt * 0.9f;
            float swing = moving ? (running ? 0.62f : 0.36f) : 0f;
            float bob = moving ? Mathf.Abs(Mathf.Sin(p.Phase)) * 0.010f * L * (running ? 2.2f : 1f) : Mathf.Sin(p.Phase) * 0.003f * L;

            float targetCrouch = c.Activity == ActivityKind.Rest ? 1f : 0f;
            float targetPitch = c.Activity switch
            {
                ActivityKind.Eat => 0.62f + 0.10f * Mathf.Sin(p.Phase * 3f),
                ActivityKind.Drink => 0.72f,
                ActivityKind.Rest => 0.30f,
                ActivityKind.Run => -0.10f,
                _ => 0.04f + (moving ? 0.03f * Mathf.Sin(p.Phase * 2f) : 0f),
            };
            float blend = Mathf.Min(1f, dt * 5f);
            p.Crouch = Mathf.Lerp(p.Crouch, targetCrouch, blend);
            p.HeadPitch = Mathf.Lerp(p.HeadPitch, targetPitch, blend);

            p.Root.Position = position + new Vector3(0f, bob - p.Crouch * m.BellyHeight * 0.92f, 0f);
            p.Root.Rotation = new Vector3(0f, -heading - Mathf.Pi * 0.5f, 0f);
            p.Root.Scale = Vector3.One * c.BodySize;
            // 걸을 때 몸통이 좌우로 조금 흔들린다.
            p.Body.Rotation = new Vector3(0f, 0f, moving ? Mathf.Sin(p.Phase) * 0.025f : 0f);
            p.Head.Rotation = new Vector3(-p.HeadPitch, moving ? Mathf.Sin(p.Phase) * 0.04f : 0f, 0f);
            p.Tail.Rotation = new Vector3(0f, Mathf.Sin(p.Phase * (moving ? 1f : 0.5f)) * (moving ? 0.20f : 0.07f), 0f);

            float cameraDistance = position.DistanceTo(cameraPosition);
            bool legsVisible = cameraDistance < DetailDistance && p.Crouch < 0.6f;
            p.Marker.Visible = cameraDistance > MarkerDistance;
            if (p.Marker.Visible)
            {
                // 화면에서 늘 비슷한 크기로 보이도록 거리에 비례해 키운다.
                float markerSize = cameraDistance * 0.011f / c.BodySize;
                p.Marker.Scale = new Vector3(markerSize, markerSize, markerSize);
                p.Marker.Position = new Vector3(0f, 0.55f * L + markerSize, 0f);
            }
            for (int l = 0; l < 4; l++)
            {
                Node3D hip = p.Hips[l];
                hip.Visible = legsVisible;
                if (!legsVisible) continue;
                // 대각선 다리끼리 같은 위상. 다리가 앞으로 나갈 때 무릎을 굽혀 발을 든다.
                float legPhase = p.Phase + ((l == 0 || l == 3) ? 0f : Mathf.Pi);
                float lift = moving ? Mathf.Max(0f, Mathf.Cos(legPhase)) : 0f;
                hip.Rotation = new Vector3(Mathf.Sin(legPhase) * swing + lift * swing * 0.35f, 0f, 0f);
                p.Knees[l].Rotation = new Vector3(-lift * swing * 1.7f, 0f, 0f);
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
            if (corpse.Node.MaterialOverride is StandardMaterial3D material)
            {
                float fade = (float)Math.Clamp((CorpseSeconds - corpse.Age) / 6.0, 0.0, 1.0);
                material.AlbedoColor = new Color(material.AlbedoColor.R, material.AlbedoColor.G, material.AlbedoColor.B, fade);
            }
        }
    }
}
