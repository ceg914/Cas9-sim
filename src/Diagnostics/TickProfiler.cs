using System.Diagnostics;

namespace PaleoSphere.Diagnostics;

/// <summary>틱 처리 시간 계측. 측정값은 시뮬레이션 상태에 영향을 주지 않는다.</summary>
public sealed class TickProfiler
{
    public enum Section
    {
        Vegetation = 0,
        Brain = 1,
        Movement = 2,
        Count = 3,
    }

    private readonly Stopwatch _tick = new();
    private readonly Stopwatch _section = new();
    private readonly double[] _sectionAvgMs = new double[(int)Section.Count];
    private const double Smoothing = 0.05;

    public double AverageTickMs { get; private set; }
    public double MaxTickMs { get; private set; }
    public double LastTickMs { get; private set; }
    public long TicksMeasured { get; private set; }

    public double SectionAverageMs(Section s) => _sectionAvgMs[(int)s];

    public void BeginTick() => _tick.Restart();

    public void BeginSection() => _section.Restart();

    public void EndSection(Section s)
    {
        double ms = _section.Elapsed.TotalMilliseconds;
        int i = (int)s;
        _sectionAvgMs[i] = TicksMeasured == 0 ? ms : _sectionAvgMs[i] + (ms - _sectionAvgMs[i]) * Smoothing;
    }

    public void EndTick()
    {
        LastTickMs = _tick.Elapsed.TotalMilliseconds;
        AverageTickMs = TicksMeasured == 0 ? LastTickMs : AverageTickMs + (LastTickMs - AverageTickMs) * Smoothing;
        if (LastTickMs > MaxTickMs) MaxTickMs = LastTickMs;
        TicksMeasured++;
    }

    public void ResetMax() => MaxTickMs = 0;
}
