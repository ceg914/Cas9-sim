using System;
using System.Diagnostics;
using PaleoSphere.Core;

namespace PaleoSphere.Simulation;

/// <summary>
/// 렌더링 프레임 시간을 고정 틱으로 바꾸는 누산기. 프레임률이 달라도 틱 하나하나의 계산은 같다.
/// 한 프레임에 쓸 수 있는 시간·틱 수를 넘으면 밀린 시간을 버리고, 실제로 낸 속도를 EffectiveSpeed로 알려 준다.
/// </summary>
public sealed class FixedTimestepDriver
{
    private double _accumulator;
    private double _windowReal;
    private double _windowSim;

    /// <summary>요청 배속. 0이면 멈춤.</summary>
    public float TimeScale { get; set; } = 1f;
    public bool Paused { get; set; }

    /// <summary>한 프레임에 시뮬레이션에 쓸 수 있는 시간(ms). 넘으면 그 프레임의 진행을 끊는다.</summary>
    public double MaxFrameMilliseconds { get; set; } = 12.0;
    public int MaxStepsPerFrame { get; set; } = 240;

    /// <summary>최근 약 1초 동안 실제로 낸 배속.</summary>
    public double EffectiveSpeed { get; private set; }

    /// <summary>처리 한도 때문에 버린 시뮬레이션 시간(초) 누계.</summary>
    public double DroppedSeconds { get; private set; }

    /// <summary>이전 틱과 현재 틱 사이의 보간 계수(0~1). 렌더러가 위치를 부드럽게 잇는 데 쓴다.</summary>
    public float Alpha => Paused ? 1f : (float)Math.Clamp(_accumulator / SimClock.TickSeconds, 0.0, 1.0);

    /// <summary>실제 경과 시간만큼 시뮬레이션을 진행하고 실행한 틱 수를 돌려준다.</summary>
    public int Advance(double realDeltaSeconds, SimulationCore sim)
    {
        if (realDeltaSeconds < 0 || double.IsNaN(realDeltaSeconds)) realDeltaSeconds = 0;
        int steps = 0;
        if (!Paused && TimeScale > 0f)
        {
            double dt = SimClock.TickSeconds;
            _accumulator += realDeltaSeconds * TimeScale;
            long started = Stopwatch.GetTimestamp();
            double budgetTicks = MaxFrameMilliseconds / 1000.0 * Stopwatch.Frequency;
            while (_accumulator >= dt && steps < MaxStepsPerFrame)
            {
                sim.Step();
                _accumulator -= dt;
                steps++;
                if (Stopwatch.GetTimestamp() - started > budgetTicks) break;
            }
            if (_accumulator > dt)
            {
                // 따라잡지 못한 시간은 쌓아 두지 않는다. 쌓으면 다음 프레임이 더 느려진다.
                DroppedSeconds += _accumulator - dt;
                _accumulator = dt;
            }
        }

        _windowReal += realDeltaSeconds;
        _windowSim += steps * (double)SimClock.TickSeconds;
        if (_windowReal >= 1.0)
        {
            EffectiveSpeed = _windowSim / _windowReal;
            _windowReal = 0;
            _windowSim = 0;
        }
        return steps;
    }

    public void Reset()
    {
        _accumulator = 0;
        _windowReal = 0;
        _windowSim = 0;
        EffectiveSpeed = 0;
    }
}
