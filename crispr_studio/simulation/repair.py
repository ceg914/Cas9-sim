"""교육용 DNA 복구 결과 생성.

절단 부위와 복구 경로가 주어지면, '있을 수 있는' 편집 결과의 예시를
결정론적으로 만들어 낸다. 이는 사례 설명을 위한 것이며, 실제 세포에서
어느 결과가 얼마의 확률로 일어나는지에 대한 예측이 아니다.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

from ..analysis.sequence import clean_sequence


class RepairPathway(str, Enum):
    NHEJ = "NHEJ"   # 비상동 말단 연결
    HDR = "HDR"     # 상동성 기반 복구


@dataclass(frozen=True)
class RepairOutcome:
    """복구 결과 예시 한 건."""

    pathway: RepairPathway
    label: str
    edited: str
    description: str
    donor_template: str | None = None

    @property
    def pathway_full(self) -> str:
        return ("비상동 말단 연결(NHEJ)" if self.pathway is RepairPathway.NHEJ
                else "상동성 기반 복구(HDR)")


# NHEJ에서 흔히 예시로 드는 소규모 indel 카탈로그(교육용).
# (종류, 크기) — 결과 길이 변화를 만들기 위한 결정론적 예시.
_NHEJ_CATALOG: list[tuple[str, int, str]] = [
    ("del", 1, "1bp 결실 — 흔한 프레임시프트 예시"),
    ("del", 2, "2bp 결실 — 프레임시프트 예시"),
    ("del", 3, "3bp 결실 — 틀을 유지하는 결실 예시"),
    ("ins", 1, "1bp 삽입 — 프레임시프트 예시"),
]


def _safe_cut(seq: str, cut_index: int) -> int:
    return max(0, min(cut_index, len(seq)))


def nhej_outcomes(seq: str, cut_index: int) -> list[RepairOutcome]:
    """절단 부위 주변에서 생길 수 있는 NHEJ 예시들."""
    seq = clean_sequence(seq)
    cut = _safe_cut(seq, cut_index)
    outcomes: list[RepairOutcome] = []
    for kind, size, note in _NHEJ_CATALOG:
        if kind == "del":
            # 절단 부위에서 좌우로 걸쳐 size개 제거(절단 상류 우선)
            left = size // 2
            right = size - left
            start = max(0, cut - left)
            end = min(len(seq), cut + right)
            edited = seq[:start] + seq[end:]
            label = f"NHEJ −{size}bp"
        else:  # ins — 절단 부위에 size개 삽입(교육용 고정 염기)
            filler = ("A" * size)
            edited = seq[:cut] + filler + seq[cut:]
            label = f"NHEJ +{size}bp"
        outcomes.append(
            RepairOutcome(RepairPathway.NHEJ, label, edited, note)
        )
    return outcomes


def hdr_outcome(
    seq: str,
    cut_index: int,
    donor_template: str,
    *,
    replace_span: int = 0,
    label: str = "HDR 정밀 편집",
    description: str = "공여 주형을 이용해 설계된 서열을 도입한 예시.",
) -> RepairOutcome:
    """공여 주형(donor)을 절단 부위에 반영한 HDR 예시.

    Parameters
    ----------
    replace_span:
        절단 부위부터 치환할 원본 염기 수. 0이면 순수 삽입.
    """
    seq = clean_sequence(seq)
    cut = _safe_cut(seq, cut_index)
    donor = clean_sequence(donor_template)
    span = max(0, min(replace_span, len(seq) - cut))
    edited = seq[:cut] + donor + seq[cut + span:]
    return RepairOutcome(
        RepairPathway.HDR, label, edited, description, donor_template=donor
    )


def apply_outcome(outcome: RepairOutcome) -> str:
    """결과 서열 반환(편의 함수)."""
    return outcome.edited
