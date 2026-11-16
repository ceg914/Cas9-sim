using System;

namespace PaleoSphere.Diagnostics;

public enum LogLevel
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// 코어에서 쓰는 로그 출구. 기본은 아무것도 하지 않고, 앱이 시작할 때 Godot 콘솔·파일로 연결한다.
/// </summary>
public static class SimLog
{
    public static Action<LogLevel, string>? Sink { get; set; }

    public static void Info(string message) => Sink?.Invoke(LogLevel.Info, message);
    public static void Warn(string message) => Sink?.Invoke(LogLevel.Warning, message);
    public static void Error(string message) => Sink?.Invoke(LogLevel.Error, message);
}
