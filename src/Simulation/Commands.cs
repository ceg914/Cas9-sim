using PaleoSphere.Core;

namespace PaleoSphere.Simulation;

/// <summary>
/// 세계를 바꾸는 요청. UI는 상태를 직접 고치지 않고 명령을 큐에 넣으며,
/// 코어가 틱 경계에서 검증한 뒤 순서대로 적용한다.
/// </summary>
public interface ISimCommand
{
    string Name { get; }
}

/// <summary>center 주변 radius 안의 마른 땅에 개체를 배치한다.</summary>
public sealed record SpawnCreaturesCommand(string SpeciesId, int Count, Vec2 Center, float Radius) : ISimCommand
{
    public string Name => "spawn";
}

public sealed record RemoveCreatureCommand(int CreatureId) : ISimCommand
{
    public string Name => "remove";
}

/// <summary>위협 자극을 만든다. 반경 안의 개체가 달아난다.</summary>
public sealed record DisturbanceCommand(Vec2 Position, float Radius, float Intensity) : ISimCommand
{
    public string Name => "disturbance";
}

/// <summary>명령 처리 결과. MessageKey는 UI가 현지화해 보여 준다.</summary>
public readonly record struct CommandResult(long Tick, string CommandName, bool Ok, int Count, string MessageKey);
