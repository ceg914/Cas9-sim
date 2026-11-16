using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using PaleoSphere.Core;

namespace PaleoSphere.Persistence;

/// <summary>
/// 구버전 세이브를 현재 스키마로 올리는 변환 사슬. 버전 n → n+1 변환을 차례로 적용한다.
/// 스키마 1이 첫 버전이라 등록된 변환은 아직 없다. 형식을 바꿀 때 여기에 추가하고
/// tests/SaveCompatibility에 이전 버전 샘플을 남긴다.
/// </summary>
public static class SaveMigrations
{
    private static readonly Dictionary<int, Func<JsonObject, JsonObject>> Steps = new();

    /// <summary>fromVersion → fromVersion + 1 변환을 등록한다.</summary>
    public static void Register(int fromVersion, Func<JsonObject, JsonObject> step) => Steps[fromVersion] = step;

    internal static void Unregister(int fromVersion) => Steps.Remove(fromVersion);

    public static bool CanMigrate(int fromVersion)
    {
        for (int v = fromVersion; v < SimVersion.SaveSchema; v++)
            if (!Steps.ContainsKey(v)) return false;
        return true;
    }

    /// <summary>state 객체를 현재 스키마까지 올린다. 적용한 단계를 log에 적는다.</summary>
    public static JsonObject Upgrade(JsonObject state, int fromVersion, List<string> log)
    {
        JsonObject current = state;
        for (int v = fromVersion; v < SimVersion.SaveSchema; v++)
        {
            current = Steps[v](current);
            log.Add($"세이브 스키마 {v} → {v + 1} 변환을 적용했습니다.");
        }
        return current;
    }
}
