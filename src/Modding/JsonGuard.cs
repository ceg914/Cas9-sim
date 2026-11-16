using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using PaleoSphere.Core;

namespace PaleoSphere.Modding;

/// <summary>
/// 신뢰할 수 없는 JSON(데이터 파일, 모드)을 읽을 때의 한도와 안전한 파싱.
/// </summary>
public static class JsonGuard
{
    /// <summary>정의 파일 한 개의 최대 크기.</summary>
    public const int MaxDataFileBytes = 256 * 1024;
    public const int MaxDepth = 16;
    public const int MaxFilesPerCategory = 512;

    public static JsonDocument? Parse(string text, string source, ValidationReport report, int maxBytes = MaxDataFileBytes)
    {
        if (text == null)
        {
            report.Error(source, "내용이 없습니다.");
            return null;
        }
        if (Encoding.UTF8.GetByteCount(text) > maxBytes)
        {
            report.Error(source, $"파일이 너무 큽니다(최대 {maxBytes / 1024} KiB).");
            return null;
        }
        try
        {
            var options = new JsonDocumentOptions
            {
                MaxDepth = MaxDepth,
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            JsonDocument doc = JsonDocument.Parse(text, options);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                report.Error(source, "최상위 값이 객체가 아닙니다.");
                doc.Dispose();
                return null;
            }
            return doc;
        }
        catch (JsonException e)
        {
            report.Error(source, "JSON 구문 오류: " + e.Message);
            return null;
        }
    }
}

/// <summary>JSON 객체에서 범위를 검사하며 값을 꺼내는 도우미. 문제는 report에 쌓는다.</summary>
internal readonly struct JsonFields
{
    private readonly JsonElement _obj;
    private readonly string _source;
    private readonly string _path;
    private readonly ValidationReport _report;

    public JsonFields(JsonElement obj, string source, ValidationReport report, string path = "")
    {
        _obj = obj;
        _source = source;
        _report = report;
        _path = path;
    }

    private string Name(string field) => _path.Length == 0 ? field : _path + "." + field;

    public bool Has(string field) => _obj.TryGetProperty(field, out _);

    public string Str(string field, int maxLength = 200, bool required = true, string fallback = "")
    {
        if (!_obj.TryGetProperty(field, out JsonElement e))
        {
            if (required) _report.Error(_source, $"'{Name(field)}' 항목이 없습니다.");
            return fallback;
        }
        if (e.ValueKind != JsonValueKind.String)
        {
            _report.Error(_source, $"'{Name(field)}'은(는) 문자열이어야 합니다.");
            return fallback;
        }
        string s = e.GetString() ?? "";
        if (s.Length > maxLength)
        {
            _report.Error(_source, $"'{Name(field)}'이(가) 너무 깁니다(최대 {maxLength}자).");
            return fallback;
        }
        if (required && s.Length == 0)
        {
            _report.Error(_source, $"'{Name(field)}'이(가) 비어 있습니다.");
            return fallback;
        }
        return s;
    }

    /// <summary>소문자 영문·숫자·밑줄로 된 식별자.</summary>
    public string Id(string field)
    {
        string s = Str(field, 64);
        foreach (char c in s)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') continue;
            _report.Error(_source, $"'{Name(field)}'에는 소문자 영문, 숫자, 밑줄만 쓸 수 있습니다: \"{s}\"");
            return "";
        }
        return s;
    }

    public float Num(string field, float min, float max, float? fallback = null)
    {
        if (!_obj.TryGetProperty(field, out JsonElement e))
        {
            if (fallback.HasValue) return fallback.Value;
            _report.Error(_source, $"'{Name(field)}' 항목이 없습니다.");
            return min;
        }
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetDouble(out double d) || double.IsNaN(d) || double.IsInfinity(d))
        {
            _report.Error(_source, $"'{Name(field)}'은(는) 유한한 숫자여야 합니다.");
            return fallback ?? min;
        }
        if (d < min || d > max)
        {
            _report.Error(_source, FormattableString.Invariant($"'{Name(field)}' 값 {d}이(가) 허용 범위 {min}~{max}를 벗어났습니다."));
            return fallback ?? min;
        }
        return (float)d;
    }

    public int Int(string field, int min, int max, int? fallback = null)
    {
        if (!_obj.TryGetProperty(field, out JsonElement e))
        {
            if (fallback.HasValue) return fallback.Value;
            _report.Error(_source, $"'{Name(field)}' 항목이 없습니다.");
            return min;
        }
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out int v))
        {
            _report.Error(_source, $"'{Name(field)}'은(는) 정수여야 합니다.");
            return fallback ?? min;
        }
        if (v < min || v > max)
        {
            _report.Error(_source, $"'{Name(field)}' 값 {v}이(가) 허용 범위 {min}~{max}를 벗어났습니다.");
            return fallback ?? min;
        }
        return v;
    }

    public bool Bool(string field, bool fallback)
    {
        if (!_obj.TryGetProperty(field, out JsonElement e)) return fallback;
        if (e.ValueKind == JsonValueKind.True) return true;
        if (e.ValueKind == JsonValueKind.False) return false;
        _report.Error(_source, $"'{Name(field)}'은(는) true 또는 false여야 합니다.");
        return fallback;
    }

    public bool TryObj(string field, out JsonFields child, bool required = true)
    {
        child = default;
        if (!_obj.TryGetProperty(field, out JsonElement e))
        {
            if (required) _report.Error(_source, $"'{Name(field)}' 항목이 없습니다.");
            return false;
        }
        if (e.ValueKind != JsonValueKind.Object)
        {
            _report.Error(_source, $"'{Name(field)}'은(는) 객체여야 합니다.");
            return false;
        }
        child = new JsonFields(e, _source, _report, Name(field));
        return true;
    }

    public List<JsonElement> Array(string field, int maxCount, bool required = false)
    {
        var list = new List<JsonElement>();
        if (!_obj.TryGetProperty(field, out JsonElement e))
        {
            if (required) _report.Error(_source, $"'{Name(field)}' 항목이 없습니다.");
            return list;
        }
        if (e.ValueKind != JsonValueKind.Array)
        {
            _report.Error(_source, $"'{Name(field)}'은(는) 배열이어야 합니다.");
            return list;
        }
        if (e.GetArrayLength() > maxCount)
        {
            _report.Error(_source, $"'{Name(field)}' 항목이 너무 많습니다(최대 {maxCount}개).");
            return list;
        }
        foreach (JsonElement item in e.EnumerateArray()) list.Add(item);
        return list;
    }

    public JsonFields Child(JsonElement element, string field, int index) =>
        new(element, _source, _report, $"{Name(field)}[{index}]");

    public EvidenceLevel Evidence(string field = "evidence")
    {
        string s = Str(field, 16);
        if (EvidenceLevelNames.TryParse(s, out EvidenceLevel level)) return level;
        if (s.Length > 0)
            _report.Error(_source, $"'{Name(field)}'은(는) confirmed, inferred, simplified 중 하나여야 합니다: \"{s}\"");
        return EvidenceLevel.Simplified;
    }

    /// <summary>0~1 범위의 RGB 세 값.</summary>
    public float[] Color(string field, float[] fallback)
    {
        if (!_obj.TryGetProperty(field, out JsonElement e)) return fallback;
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
        {
            _report.Error(_source, $"'{Name(field)}'은(는) 숫자 3개의 배열이어야 합니다.");
            return fallback;
        }
        var rgb = new float[3];
        int i = 0;
        foreach (JsonElement c in e.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Number || !c.TryGetDouble(out double d) || d < 0 || d > 1)
            {
                _report.Error(_source, $"'{Name(field)}'의 값은 0~1 범위의 숫자여야 합니다.");
                return fallback;
            }
            rgb[i++] = (float)d;
        }
        return rgb;
    }

    public void RequireSchemaVersion()
    {
        int v = Int("schema_version", 1, 1000);
        if (v != SimVersion.DataSchema && v >= 1)
            _report.Error(_source, $"schema_version {v}은(는) 지원하지 않습니다(지원: {SimVersion.DataSchema}).");
    }

    /// <summary>{"ko": "...", "en": "..."} 형태의 이름.</summary>
    public void Names(string field, out string ko, out string en)
    {
        ko = "";
        en = "";
        if (!TryObj(field, out JsonFields name)) return;
        ko = name.Str("ko", 80);
        en = name.Str("en", 80, required: false, fallback: ko);
    }
}
