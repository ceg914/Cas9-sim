using Godot;
using PaleoSphere.Core;
using PaleoSphere.World;

namespace PaleoSphere.Presentation;

/// <summary>
/// 궤도 카메라. 지면의 한 점(pivot)을 바라보며 WASD·방향키로 pivot을 옮기고,
/// 우클릭 드래그로 돌리고, 휠로 거리를 바꾼다. 추적 중에는 pivot이 선택 개체를 따라간다.
/// </summary>
public partial class CameraRig : Node3D
{
    public Camera3D Camera { get; private set; } = null!;

    private Vector3 _pivot;
    private float _yaw = Mathf.DegToRad(35f);
    private float _pitch = Mathf.DegToRad(-42f);
    private float _distance = 120f;
    private float _targetDistance = 120f;
    private TerrainGrid? _terrain;

    public bool Following { get; set; }
    public Vector3 Pivot => _pivot;

    public override void _Ready()
    {
        Camera = new Camera3D { Near = 0.5f, Far = 4000f, Fov = 55f, Current = true };
        AddChild(Camera);
        Apply();
    }

    public void SetTerrain(TerrainGrid terrain)
    {
        _terrain = terrain;
        float half = terrain.WorldSize * 0.5f;
        _pivot = new Vector3(half, terrain.HeightAt(new Vec2(half, half)), half);
        _targetDistance = _distance = Mathf.Clamp(terrain.WorldSize * 0.34f, 60f, 520f);
        Following = false;
        Apply();
    }

    public void FocusOn(Vector3 point, bool zoomIn)
    {
        _pivot = point;
        if (zoomIn) _targetDistance = Mathf.Min(_targetDistance, 55f);
    }

    public void SetDistance(float distance)
    {
        _targetDistance = _distance = Mathf.Clamp(distance, 6f, 2000f);
    }

    public void SetFollowPoint(Vector3 point)
    {
        if (Following) _pivot = _pivot.Lerp(point, 0.25f);
    }

    public void Rotate(Vector2 relative)
    {
        _yaw -= relative.X * 0.005f;
        _pitch = Mathf.Clamp(_pitch - relative.Y * 0.004f, Mathf.DegToRad(-88f), Mathf.DegToRad(-6f));
    }

    public void Zoom(float factor)
    {
        float max = _terrain != null ? _terrain.WorldSize * 1.6f : 900f;
        _targetDistance = Mathf.Clamp(_targetDistance * factor, 6f, max);
    }

    /// <summary>키보드 이동. move.X는 좌우, move.Y는 앞뒤.</summary>
    public void Pan(Vector2 move, float delta)
    {
        if (move == Vector2.Zero) return;
        Following = false;
        float speed = Mathf.Max(8f, _distance * 0.9f);
        var forward = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
        var right = new Vector3(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        _pivot += (forward * move.Y + right * move.X) * speed * delta;
    }

    public override void _Process(double delta)
    {
        if (_terrain != null)
        {
            float size = _terrain.WorldSize;
            _pivot.X = Mathf.Clamp(_pivot.X, 0f, size);
            _pivot.Z = Mathf.Clamp(_pivot.Z, 0f, size);
            float ground = Mathf.Max(_terrain.HeightAt(new Vec2(_pivot.X, _pivot.Z)), _terrain.WaterLevel);
            _pivot.Y = Mathf.Lerp(_pivot.Y, ground, Mathf.Min(1f, (float)delta * 6f));
        }
        _distance = Mathf.Lerp(_distance, _targetDistance, Mathf.Min(1f, (float)delta * 10f));
        Apply();
    }

    private void Apply()
    {
        if (Camera == null) return;
        var basis = Basis.FromEuler(new Vector3(_pitch, _yaw, 0f));
        Vector3 position = _pivot + basis * new Vector3(0f, 0f, _distance);
        if (_terrain != null)
        {
            // 카메라가 땅속으로 들어가지 않게 한다.
            float ground = Mathf.Max(_terrain.HeightAt(new Vec2(position.X, position.Z)), _terrain.WaterLevel) + 1.5f;
            if (position.Y < ground) position.Y = ground;
        }
        Camera.Position = position;
        Camera.LookAt(_pivot + new Vector3(0f, 1f, 0f), Vector3.Up);
    }
}
