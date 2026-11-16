using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PaleoSphere.Persistence;

public sealed record SaveSlotInfo(string Slot, string Path, long SizeBytes, DateTime ModifiedUtc, SaveHeader? Header, string? Error);

/// <summary>
/// 세이브 파일 입출력. 임시 파일에 먼저 쓰고 다시 읽어 검증한 뒤 원본과 바꾼다.
/// 바꾸기 전에 기존 파일을 .bak1~.bak3으로 밀어 백업을 세 개 유지한다.
/// </summary>
public sealed class SaveFileStore
{
    public const string Extension = ".psave";
    public const int BackupCount = 3;
    public const string AutosaveSlot = "autosave";

    public string Directory { get; }

    public SaveFileStore(string directory)
    {
        Directory = directory;
    }

    /// <summary>파일 이름으로 안전한 슬롯 이름으로 정리한다. 쓸 수 있는 글자가 없으면 "save".</summary>
    public static string SanitizeSlot(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.Trim())
        {
            if (sb.Length >= 40) break;
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            else if (c == ' ') sb.Append('_');
        }
        return sb.Length == 0 ? "save" : sb.ToString();
    }

    public string PathFor(string slot) => Path.Combine(Directory, SanitizeSlot(slot) + Extension);

    private static string BackupPath(string path, int index) => path + ".bak" + index;

    /// <summary>
    /// 검증 후 원자적으로 교체한다. 검증에 실패하면 원본을 건드리지 않고 예외를 던진다.
    /// </summary>
    public string Write(string slot, byte[] bytes)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = PathFor(slot);
        string tmp = path + ".tmp";

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true);
        }

        byte[] written = File.ReadAllBytes(tmp);
        SaveHeader? header = SaveSerializer.ReadHeader(written, out string? error);
        if (header == null)
        {
            File.Delete(tmp);
            throw new IOException("저장 검증 실패: " + error);
        }

        if (File.Exists(path))
        {
            for (int i = BackupCount; i >= 2; i--)
            {
                string older = BackupPath(path, i - 1);
                if (File.Exists(older)) File.Move(older, BackupPath(path, i), overwrite: true);
            }
            File.Copy(path, BackupPath(path, 1), overwrite: true);
        }

        // 같은 볼륨 안의 이름 바꾸기라 도중에 꺼져도 원본이나 새 파일 중 하나는 온전히 남는다.
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    public byte[] Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("세이브 파일이 없습니다.", path);
        if (info.Length > SaveSerializer.MaxFileBytes)
            throw new IOException($"세이브 파일이 너무 큽니다(최대 {SaveSerializer.MaxFileBytes / (1024 * 1024)} MiB).");
        return File.ReadAllBytes(path);
    }

    /// <summary>원본과 백업을 최신 순으로 돌려준다(존재하는 것만).</summary>
    public List<string> CandidatePaths(string slot)
    {
        var list = new List<string>();
        string path = PathFor(slot);
        if (File.Exists(path)) list.Add(path);
        for (int i = 1; i <= BackupCount; i++)
            if (File.Exists(BackupPath(path, i))) list.Add(BackupPath(path, i));
        return list;
    }

    public List<SaveSlotInfo> List()
    {
        var result = new List<SaveSlotInfo>();
        if (!System.IO.Directory.Exists(Directory)) return result;
        foreach (string path in System.IO.Directory.GetFiles(Directory, "*" + Extension))
        {
            var info = new FileInfo(path);
            SaveHeader? header = null;
            string? error = null;
            try
            {
                header = SaveSerializer.ReadHeader(Read(path), out error);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error = e.Message;
            }
            result.Add(new SaveSlotInfo(Path.GetFileNameWithoutExtension(path), path, info.Length, info.LastWriteTimeUtc, header, error));
        }
        result.Sort((a, b) => b.ModifiedUtc.CompareTo(a.ModifiedUtc));
        return result;
    }

    public bool Delete(string slot)
    {
        string path = PathFor(slot);
        bool any = false;
        foreach (string p in CandidatePaths(slot))
        {
            File.Delete(p);
            any = true;
        }
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        return any;
    }
}
