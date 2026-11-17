using Godot;
using PaleoSphere.Core;

namespace PaleoSphere.UI;

/// <summary>UI 색과 글꼴. 글꼴 파일을 싣지 않고 운영체제의 한글 글꼴을 쓴다(Windows는 맑은 고딕).</summary>
public static class UiTheme
{
    public static readonly Color Panel = new(0.09f, 0.11f, 0.12f, 0.93f);
    public static readonly Color PanelLight = new(0.15f, 0.18f, 0.19f, 0.96f);
    public static readonly Color Border = new(0.26f, 0.31f, 0.31f);
    public static readonly Color Text = new(0.90f, 0.92f, 0.90f);
    public static readonly Color Muted = new(0.62f, 0.67f, 0.66f);
    public static readonly Color Accent = new(0.55f, 0.80f, 0.45f);
    public static readonly Color Warn = new(0.95f, 0.72f, 0.30f);
    public static readonly Color Bad = new(0.90f, 0.42f, 0.36f);

    public static readonly Color[] Series =
    {
        new(0.55f, 0.80f, 0.45f), new(0.40f, 0.70f, 0.95f), new(0.95f, 0.72f, 0.30f),
        new(0.90f, 0.42f, 0.36f), new(0.75f, 0.55f, 0.90f), new(0.45f, 0.85f, 0.80f),
    };

    public static Color EvidenceColor(EvidenceLevel e) => e switch
    {
        EvidenceLevel.Confirmed => new Color(0.50f, 0.82f, 0.50f),
        EvidenceLevel.Inferred => new Color(0.95f, 0.76f, 0.35f),
        _ => new Color(0.60f, 0.72f, 0.88f),
    };

    public static StyleBoxFlat Box(Color color, int margin = 8, int radius = 4, bool border = true)
    {
        var sb = new StyleBoxFlat { BgColor = color };
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(margin);
        if (border)
        {
            sb.SetBorderWidthAll(1);
            sb.BorderColor = Border;
        }
        return sb;
    }

    public static Theme Build()
    {
        var font = new SystemFont
        {
            FontNames = new[] { "Malgun Gothic", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Apple SD Gothic Neo", "sans-serif" },
            AllowSystemFallback = true,
            Antialiasing = TextServer.FontAntialiasing.Gray,
        };
        var theme = new Theme { DefaultFont = font, DefaultFontSize = 14 };

        theme.SetStylebox("panel", "PanelContainer", Box(Panel));
        theme.SetStylebox("panel", "Panel", Box(Panel));
        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("default_color", "RichTextLabel", Text);

        theme.SetStylebox("normal", "Button", Box(PanelLight, 6));
        theme.SetStylebox("hover", "Button", Box(PanelLight.Lightened(0.12f), 6));
        theme.SetStylebox("pressed", "Button", Box(new Color(0.22f, 0.36f, 0.22f, 1f), 6));
        theme.SetStylebox("disabled", "Button", Box(new Color(0.12f, 0.13f, 0.14f, 0.9f), 6));
        theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), 6));
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        theme.SetColor("font_disabled_color", "Button", Muted);

        theme.SetStylebox("background", "ProgressBar", Box(new Color(0.05f, 0.06f, 0.07f, 1f), 0, 3));
        theme.SetStylebox("fill", "ProgressBar", Box(Accent, 0, 3, border: false));

        theme.SetStylebox("panel", "TabContainer", Box(Panel));
        theme.SetStylebox("tab_selected", "TabContainer", Box(PanelLight, 8, 3));
        theme.SetStylebox("tab_unselected", "TabContainer", Box(new Color(0.07f, 0.08f, 0.09f, 0.9f), 8, 3));
        theme.SetStylebox("tab_hovered", "TabContainer", Box(PanelLight.Darkened(0.15f), 8, 3));

        theme.SetStylebox("normal", "LineEdit", Box(new Color(0.05f, 0.06f, 0.07f, 1f), 6));
        theme.SetStylebox("focus", "LineEdit", Box(new Color(0.05f, 0.06f, 0.07f, 1f), 6));
        theme.SetStylebox("panel", "PopupMenu", Box(PanelLight));
        theme.SetStylebox("panel", "AcceptDialog", Box(Panel, 12));
        theme.SetStylebox("embedded_border", "Window", Box(Panel, 12));
        theme.SetStylebox("panel", "ItemList", Box(new Color(0.05f, 0.06f, 0.07f, 1f), 4));
        return theme;
    }

    public static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", Accent);
        label.AddThemeFontSizeOverride("font_size", 15);
        return label;
    }

    /// <summary>줄바꿈 없는 작은 보조 문구(표의 숫자 칸 등).</summary>
    public static Label SmallLabel(string text = "")
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", Muted);
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }

    public static Label MutedLabel(string text = "")
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", Muted);
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }
}
