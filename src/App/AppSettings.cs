using System;
using System.Text.Json;
using Godot;

namespace PaleoSphere.App;

/// <summary>사용자 설정(user://settings.json). 파일이 없거나 깨져 있으면 기본값을 쓴다.</summary>
public sealed class AppSettings
{
    private const string Path = "user://settings.json";

    public string Language { get; set; } = "ko";
    public float UiScale { get; set; } = 1f;
    public int AutosaveMinutes { get; set; } = 5;
    public float SimBudgetMs { get; set; } = 12f;
    public bool ShowDock { get; set; } = true;
    public float LeftPanelWidth { get; set; } = 270f;
    public float RightPanelWidth { get; set; } = 320f;
    public int DockTab { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (!FileAccess.FileExists(Path)) return new AppSettings();
            using FileAccess? f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            if (f == null || f.GetLength() > 64 * 1024) return new AppSettings();
            AppSettings s = JsonSerializer.Deserialize<AppSettings>(f.GetAsText()) ?? new AppSettings();
            s.Clamp();
            return s;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            GD.PushWarning("설정 파일을 읽지 못해 기본값을 씁니다: " + e.Message);
            return new AppSettings();
        }
    }

    public void Clamp()
    {
        if (Language != "ko" && Language != "en") Language = "ko";
        UiScale = float.IsFinite(UiScale) ? Mathf.Clamp(UiScale, 0.75f, 2f) : 1f;
        AutosaveMinutes = Math.Clamp(AutosaveMinutes, 0, 120);
        SimBudgetMs = float.IsFinite(SimBudgetMs) ? Mathf.Clamp(SimBudgetMs, 2f, 30f) : 12f;
        LeftPanelWidth = float.IsFinite(LeftPanelWidth) ? Mathf.Clamp(LeftPanelWidth, 200f, 600f) : 270f;
        RightPanelWidth = float.IsFinite(RightPanelWidth) ? Mathf.Clamp(RightPanelWidth, 240f, 700f) : 320f;
        DockTab = Math.Clamp(DockTab, 0, 5);
    }

    public void Save()
    {
        Clamp();
        using FileAccess? f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (f == null)
        {
            GD.PushWarning("설정을 저장하지 못했습니다: " + FileAccess.GetOpenError());
            return;
        }
        f.StoreString(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
