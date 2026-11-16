namespace PaleoSphere.Core;

/// <summary>
/// 데이터 항목의 근거 수준. UI와 데이터 파일에서 항상 함께 표시한다.
/// </summary>
public enum EvidenceLevel
{
    /// <summary>화석 기록으로 직접 확인된 내용.</summary>
    Confirmed = 0,

    /// <summary>화석·현생 유사종에서 추론한 학술적 추정 또는 가설.</summary>
    Inferred = 1,

    /// <summary>시뮬레이션을 위해 임의로 정한 게임적 단순화. 실제 생물의 수치가 아니다.</summary>
    Simplified = 2,
}

public static class EvidenceLevelNames
{
    public static bool TryParse(string? text, out EvidenceLevel level)
    {
        switch (text)
        {
            case "confirmed": level = EvidenceLevel.Confirmed; return true;
            case "inferred": level = EvidenceLevel.Inferred; return true;
            case "simplified": level = EvidenceLevel.Simplified; return true;
            default: level = EvidenceLevel.Simplified; return false;
        }
    }

    public static string ToId(EvidenceLevel level) => level switch
    {
        EvidenceLevel.Confirmed => "confirmed",
        EvidenceLevel.Inferred => "inferred",
        _ => "simplified",
    };
}
