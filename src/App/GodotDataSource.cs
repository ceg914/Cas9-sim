using System;
using System.Collections.Generic;
using Godot;
using PaleoSphere.Modding;

namespace PaleoSphere.App;

/// <summary>res://data 아래의 JSON을 읽어 코어의 로더에 넘긴다. 내보낸 실행 파일에서는 PCK 안의 파일을 읽는다.</summary>
public sealed class GodotDataSource : IDataSource
{
    private readonly string _root;

    public GodotDataSource(string root = "res://data") => _root = root;

    public IReadOnlyList<DataFile> ReadAll(string category, ValidationReport report)
    {
        var list = new List<DataFile>();
        string dir = $"{_root}/{category}";
        using DirAccess? access = DirAccess.Open(dir);
        if (access == null) return list;

        string[] names = access.GetFiles();
        Array.Sort(names, StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!name.EndsWith(".json", StringComparison.Ordinal)) continue;
            string label = category + "/" + name;
            if (list.Count >= JsonGuard.MaxFilesPerCategory)
            {
                report.Warn(label, "파일이 너무 많아 나머지는 건너뜁니다.");
                break;
            }
            using FileAccess? file = FileAccess.Open($"{dir}/{name}", FileAccess.ModeFlags.Read);
            if (file == null)
            {
                report.Error(label, "파일을 열지 못했습니다: " + FileAccess.GetOpenError());
                continue;
            }
            if (file.GetLength() > JsonGuard.MaxDataFileBytes)
            {
                report.Error(label, $"파일이 너무 큽니다(최대 {JsonGuard.MaxDataFileBytes / 1024} KiB).");
                continue;
            }
            list.Add(new DataFile(label, file.GetAsText()));
        }
        return list;
    }
}
