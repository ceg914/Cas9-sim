"""분석 결과 내보내기: CSV, JSON, 텍스트 보고서.

모든 출력에는 교육용 데이터이며 실제 실험 결과·확률 예측이 아니라는
한계를 함께 표기한다.
"""

from __future__ import annotations

import csv
import io
import json
from pathlib import Path

from ..analysis.mutation import EditAnalysis

LIMITATION_NOTE = (
    "이 결과는 주어진 두 서열을 비교한 결정론적 분석이며, 실제 세포에서의 "
    "편집 성공률·확률·표현형 예측이 아닙니다. 교육용 참고 자료입니다."
)


def analysis_to_rows(analysis: EditAnalysis) -> list[dict]:
    """변이별 행 목록(표/CSV용)."""
    rows: list[dict] = []
    for v in analysis.variants:
        rows.append({
            "종류": v.kind.value,
            "위치(1-기반)": v.position + 1,
            "원본": v.ref or "-",
            "결과": v.alt or "-",
            "설명": v.describe(),
        })
    return rows


def to_json(analysis: EditAnalysis, *, meta: dict | None = None) -> str:
    """분석 결과를 JSON 문자열로."""
    payload = {
        "meta": meta or {},
        "original": analysis.original,
        "edited": analysis.edited,
        "net_indel": analysis.net_indel,
        "protein_effect": analysis.protein_effect.value,
        "protein_before": analysis.protein_before,
        "protein_after": analysis.protein_after,
        "variants": analysis_to_rows(analysis),
        "alignment": {
            "a": analysis.alignment.aligned_a,
            "b": analysis.alignment.aligned_b,
            "identity": round(analysis.alignment.identity, 4),
        },
        "limitation": LIMITATION_NOTE,
    }
    return json.dumps(payload, ensure_ascii=False, indent=2)


def to_csv(analysis: EditAnalysis) -> str:
    """변이 표를 CSV 문자열로."""
    rows = analysis_to_rows(analysis)
    buf = io.StringIO()
    fieldnames = ["종류", "위치(1-기반)", "원본", "결과", "설명"]
    writer = csv.DictWriter(buf, fieldnames=fieldnames)
    writer.writeheader()
    for r in rows:
        writer.writerow(r)
    return buf.getvalue()


def to_report(analysis: EditAnalysis, *, title: str = "편집 결과 보고서",
              meta: dict | None = None) -> str:
    """사람이 읽는 텍스트 보고서."""
    lines = [f"# {title}", ""]
    if meta:
        for k, v in meta.items():
            lines.append(f"- {k}: {v}")
        lines.append("")
    lines += analysis.summary_lines()
    lines.append("")
    lines.append("## 정렬")
    lines.append(analysis.alignment.aligned_a)
    lines.append(analysis.alignment.match_line)
    lines.append(analysis.alignment.aligned_b)
    if analysis.protein_before is not None:
        lines.append("")
        lines.append("## 단백질 번역")
        lines.append(f"편집 전: {analysis.protein_before}")
        lines.append(f"편집 후: {analysis.protein_after}")
    lines.append("")
    lines.append("## 한계")
    lines.append(LIMITATION_NOTE)
    return "\n".join(lines)


def write_text(path: str | Path, text: str) -> None:
    Path(path).write_text(text, encoding="utf-8")
