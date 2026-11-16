using System.Collections.Generic;

namespace PaleoSphere.Modding;

/// <summary>데이터 검증 결과. 오류가 있어도 예외를 던지지 않고 여기에 모은다.</summary>
public sealed class ValidationReport
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool Ok => Errors.Count == 0;

    public void Error(string source, string message) => Errors.Add($"[{source}] {message}");
    public void Warn(string source, string message) => Warnings.Add($"[{source}] {message}");

    public void Merge(ValidationReport other)
    {
        Errors.AddRange(other.Errors);
        Warnings.AddRange(other.Warnings);
    }
}
