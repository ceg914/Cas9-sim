"""표적 부위 계산: PAM 위치로부터 프로토스페이서와 절단 지점을 유도.

PAM을 하나 고르면 Cas 변이체의 기하 규칙에 따라 프로토스페이서 구간과
절단 지점(표시 가닥 기준 단일 인덱스)을 돌려준다. 교육용 단순화이며,
돌출 말단 효소의 양쪽 가닥 서로 다른 절단 위치는 하나의 대표 지점으로
표현한다.
"""

from __future__ import annotations

from dataclasses import dataclass

from ..analysis.sequence import clean_sequence, find_pam
from .cas_variants import CasVariant


@dataclass(frozen=True)
class TargetSite:
    """선택된 표적 부위."""

    pam_start: int          # 0-based
    pam_end: int            # 반열림 구간 끝
    protospacer_start: int  # 0-based
    protospacer_end: int    # 반열림 구간 끝
    cut_index: int          # seq[:cut] | seq[cut:]
    cas_name: str

    @property
    def pam_len(self) -> int:
        return self.pam_end - self.pam_start


def derive_site(seq: str, cas: CasVariant, pam_start: int) -> TargetSite:
    """PAM 시작 좌표로부터 표적 부위를 계산."""
    seq = clean_sequence(seq)
    pam_len = len(cas.pam)
    pam_end = pam_start + pam_len

    if cas.pam_side == "3prime":
        proto_end = pam_start
        proto_start = pam_start - cas.spacer_len
        cut_index = pam_start - cas.cut_offset
    else:  # 5prime (예: Cas12a) — PAM 하류에 프로토스페이서
        proto_start = pam_end
        proto_end = pam_end + cas.spacer_len
        cut_index = pam_end + cas.cut_offset

    return TargetSite(
        pam_start=pam_start,
        pam_end=pam_end,
        protospacer_start=max(0, proto_start),
        protospacer_end=min(len(seq), proto_end),
        cut_index=max(0, min(cut_index, len(seq))),
        cas_name=cas.name,
    )


def candidate_sites(seq: str, cas: CasVariant) -> list[TargetSite]:
    """프로토스페이서가 서열 안에 온전히 들어가는 PAM 후보 전부."""
    seq = clean_sequence(seq)
    sites: list[TargetSite] = []
    for p in find_pam(seq, cas.pam):
        site = derive_site(seq, cas, p)
        if (site.protospacer_start >= 0
                and site.protospacer_end <= len(seq)
                and site.protospacer_end - site.protospacer_start
                == cas.spacer_len):
            sites.append(site)
    return sites
