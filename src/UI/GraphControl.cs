using System;
using System.Collections.Generic;
using Godot;

namespace PaleoSphere.UI;

/// <summary>시계열 꺾은선 그래프. 가로축은 경과 일수, 세로축은 자동 범위.</summary>
public partial class GraphControl : Control
{
    public sealed class Series
    {
        public string Name = "";
        public Color Color = Colors.White;
        public readonly List<Vector2> Points = new();
    }

    private readonly List<Series> _series = new();

    public string XLabel { get; set; } = "";
    public string EmptyText { get; set; } = "";

    /// <summary>세로축 최대값을 고정한다. 0 이하면 자동.</summary>
    public float FixedMax { get; set; }

    public GraphControl()
    {
        CustomMinimumSize = new Vector2(220, 120);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        ClipContents = true;
    }

    public Series GetSeries(int index, string name, Color color)
    {
        while (_series.Count <= index) _series.Add(new Series());
        Series s = _series[index];
        s.Name = name;
        s.Color = color;
        return s;
    }

    public void TrimSeries(int count)
    {
        if (_series.Count > count) _series.RemoveRange(count, _series.Count - count);
    }

    public override void _Draw()
    {
        Font font = GetThemeDefaultFont();
        int fs = 12;
        Vector2 size = Size;
        const float left = 46f, right = 10f, top = 22f, bottom = 22f;
        var plot = new Rect2(left, top, Math.Max(10f, size.X - left - right), Math.Max(10f, size.Y - top - bottom));
        DrawRect(plot, new Color(0.04f, 0.05f, 0.06f, 0.75f));

        float minX = float.MaxValue, maxX = float.MinValue, maxY = 0f;
        int points = 0;
        foreach (Series s in _series)
        {
            foreach (Vector2 p in s.Points)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
                points++;
            }
        }
        if (points < 2 || maxX <= minX)
        {
            DrawString(font, plot.Position + new Vector2(8, plot.Size.Y * 0.5f), EmptyText, HorizontalAlignment.Left, plot.Size.X - 16, 13, UiTheme.Muted);
            DrawLegend(font, fs, left);
            return;
        }
        float yMax = FixedMax > 0f ? FixedMax : NiceCeil(maxY * 1.08f);
        if (yMax <= 0f) yMax = 1f;

        for (int g = 0; g <= 4; g++)
        {
            float t = g / 4f;
            float y = plot.Position.Y + plot.Size.Y * (1f - t);
            DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), new Color(1, 1, 1, 0.07f), 1f);
            DrawString(font, new Vector2(2, y + 4), FormatValue(yMax * t), HorizontalAlignment.Right, left - 6, fs, UiTheme.Muted);
        }
        DrawString(font, new Vector2(plot.Position.X, size.Y - 5), minX.ToString("0.0"), HorizontalAlignment.Left, -1, fs, UiTheme.Muted);
        DrawString(font, new Vector2(plot.End.X - 80, size.Y - 5), maxX.ToString("0.0") + "  " + XLabel, HorizontalAlignment.Right, 80, fs, UiTheme.Muted);

        foreach (Series s in _series)
        {
            if (s.Points.Count < 2) continue;
            // 화면 가로 픽셀보다 표본이 많으면 건너뛰며 그린다.
            int stride = Math.Max(1, s.Points.Count / Math.Max(2, (int)plot.Size.X));
            var line = new List<Vector2>(s.Points.Count / stride + 2);
            for (int i = 0; i < s.Points.Count; i += stride) line.Add(Map(s.Points[i], plot, minX, maxX, yMax));
            Vector2 last = Map(s.Points[^1], plot, minX, maxX, yMax);
            if (line[^1] != last) line.Add(last);
            if (line.Count >= 2) DrawPolyline(line.ToArray(), s.Color, 2f, true);
        }
        DrawLegend(font, fs, left);
    }

    private void DrawLegend(Font font, int fs, float left)
    {
        float x = left;
        foreach (Series s in _series)
        {
            DrawRect(new Rect2(x, 6, 10, 10), s.Color);
            string text = s.Points.Count > 0 ? $"{s.Name} {FormatValue(s.Points[^1].Y)}" : s.Name;
            DrawString(font, new Vector2(x + 14, 16), text, HorizontalAlignment.Left, -1, fs, UiTheme.Text);
            x += 24 + font.GetStringSize(text, HorizontalAlignment.Left, -1, fs).X;
        }
    }

    private static Vector2 Map(Vector2 p, Rect2 plot, float minX, float maxX, float yMax) => new(
        plot.Position.X + (p.X - minX) / (maxX - minX) * plot.Size.X,
        plot.Position.Y + plot.Size.Y * (1f - Math.Clamp(p.Y / yMax, 0f, 1f)));

    private static string FormatValue(float v) => v >= 100f ? v.ToString("0") : (v >= 10f ? v.ToString("0.#") : v.ToString("0.##"));

    private static float NiceCeil(float v)
    {
        if (v <= 0f) return 1f;
        float pow = MathF.Pow(10f, MathF.Floor(MathF.Log10(v)));
        float n = v / pow;
        float nice = n <= 1f ? 1f : n <= 2f ? 2f : n <= 2.5f ? 2.5f : n <= 5f ? 5f : 10f;
        return nice * pow;
    }
}

/// <summary>작은 막대그래프(나이 분포 등).</summary>
public partial class MiniBars : Control
{
    private int[] _values = Array.Empty<int>();
    public Color BarColor { get; set; } = UiTheme.Accent;

    public MiniBars()
    {
        CustomMinimumSize = new Vector2(120, 36);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public void SetValues(int[] values)
    {
        _values = values;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.05f, 0.06f, 0.75f));
        if (_values.Length == 0) return;
        int max = 1;
        foreach (int v in _values) max = Math.Max(max, v);
        float w = Size.X / _values.Length;
        for (int i = 0; i < _values.Length; i++)
        {
            float h = (Size.Y - 4f) * _values[i] / max;
            DrawRect(new Rect2(i * w + 1f, Size.Y - 2f - h, w - 2f, h), BarColor);
        }
    }
}
