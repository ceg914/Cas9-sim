"""교육용 편집 사례 라이브러리.

각 사례는 표시 가닥(센스) 서열, Cas 변이체, 선택된 표적 부위, 그리고
있을 수 있는 복구 결과 예시를 담는다. 모든 서열은 개념 설명을 위해
합성한 교육용 데이터이며 특정 실존 유전자나 실험 결과를 나타내지 않는다.
"""

from __future__ import annotations

from dataclasses import dataclass, field

from ..simulation import cas_variants
from ..simulation.repair import (
    RepairOutcome,
    RepairPathway,
    hdr_outcome,
    nhej_outcomes,
)
from ..simulation.targeting import TargetSite, derive_site


@dataclass
class EditingCase:
    """편집 사례 하나."""

    case_id: str
    name: str
    description: str
    sequence: str
    cas_name: str
    site: TargetSite
    outcomes: list[RepairOutcome]
    is_coding: bool = True
    frame: int = 0
    source_note: str = "합성 교육용 데이터 (실존 유전자·실험 결과 아님)"
    tags: list[str] = field(default_factory=list)

    @property
    def protospacer(self) -> str:
        return self.sequence[self.site.protospacer_start:self.site.protospacer_end]

    @property
    def pam(self) -> str:
        return self.sequence[self.site.pam_start:self.site.pam_end]


# --- 사례 1: 코딩 표적, SpCas9 ---------------------------------------------
# frame 0에서 내부 종결코돈이 없는 30코돈(90nt) 합성 코딩 서열.
_CODING_SEQ = (
    "ATGGCTAAGCTGGATCCGGGAACCGAATTCCGTGACAGCCACTGGCGC"
    "GGAACCCTGAAGGACTTCTGGCATGCAGTTGAACGCTTCTAA"
)


def _coding_case() -> EditingCase:
    cas = cas_variants.SPCAS9
    site = derive_site(_CODING_SEQ, cas, pam_start=42)  # PAM=TGG
    outcomes = nhej_outcomes(_CODING_SEQ, site.cut_index)
    # HDR: 절단 부위(코돈 경계)에 3bp 삽입 — 틀을 유지하는 정밀 삽입 예시
    outcomes.append(
        hdr_outcome(
            _CODING_SEQ, site.cut_index, donor_template="GGC",
            replace_span=0, label="HDR +3bp(틀유지)",
            description="공여 주형으로 코돈 경계에 3bp를 정밀 삽입한 예시. "
                        "틀이 유지되어 아미노산 하나가 추가된다.",
        )
    )
    return EditingCase(
        case_id="coding_spcas9",
        name="코딩 유전자 넉아웃/정밀편집 (SpCas9)",
        description="코딩 서열 중앙을 SpCas9으로 표적한다. NHEJ 결과에 따라 "
                    "프레임시프트로 기능이 손상될 수도, 틀이 유지될 수도 있다. "
                    "HDR 예시는 설계된 정밀 삽입을 보여준다.",
        sequence=_CODING_SEQ,
        cas_name=cas.name,
        site=site,
        outcomes=outcomes,
        is_coding=True,
        frame=0,
        tags=["코딩", "SpCas9", "NHEJ", "HDR"],
    )


# --- 사례 2: 비코딩 조절부위, SpCas9 ---------------------------------------
_NONCODING_SEQ = (
    "TTGACAGCTAGCTCAGTCCTAGGTATAATGCTAGCACCGTTCGAATGG"
    "CCATAGCATTACGTGACCAGT"
)


def _noncoding_case() -> EditingCase:
    cas = cas_variants.SPCAS9
    site = derive_site(_NONCODING_SEQ, cas, pam_start=45)  # PAM=TGG
    outcomes = nhej_outcomes(_NONCODING_SEQ, site.cut_index)
    return EditingCase(
        case_id="noncoding_spcas9",
        name="조절부위 교란 (SpCas9, 비코딩)",
        description="프로모터 유사 비코딩 구간을 표적한다. 단백질 서열이 아닌 "
                    "DNA 수준 변화만 관찰한다. 단백질 영향 분석은 적용하지 않는다.",
        sequence=_NONCODING_SEQ,
        cas_name=cas.name,
        site=site,
        outcomes=outcomes,
        is_coding=False,
        tags=["비코딩", "SpCas9", "NHEJ"],
    )


# --- 사례 3: Cas12a, 5' PAM -------------------------------------------------
_CAS12A_SEQ = (
    "ATGCTTTAGCTTTCGACCTGAAGCATTCGGATCCGTTACGCAGTTAAC"
    "GGTACCGATTTCATGGAACTG"
)


def _cas12a_case() -> EditingCase:
    cas = cas_variants.CAS12A
    # TTTV(5' PAM) 후보 중 프로토스페이서가 온전한 첫 자리를 찾는다.
    from ..simulation.targeting import candidate_sites
    sites = candidate_sites(_CAS12A_SEQ, cas)
    site = sites[0] if sites else derive_site(_CAS12A_SEQ, cas, 2)
    outcomes = nhej_outcomes(_CAS12A_SEQ, site.cut_index)
    return EditingCase(
        case_id="cas12a_demo",
        name="Cas12a 돌출말단 절단 (5' PAM)",
        description="Cas12a는 상류 T-rich PAM을 인식하고 돌출 말단을 만든다. "
                    "Cas9과 PAM 방향·절단 방식이 다름을 비교하는 사례다.",
        sequence=_CAS12A_SEQ,
        cas_name=cas.name,
        site=site,
        outcomes=outcomes,
        is_coding=False,
        tags=["Cas12a", "5'PAM", "돌출말단"],
    )


def build_outcomes(sequence: str, cut_index: int) -> list[RepairOutcome]:
    """임의 서열·절단 위치에 대한 교육용 복구 결과 예시를 생성."""
    outs = nhej_outcomes(sequence, cut_index)
    outs.append(
        hdr_outcome(
            sequence, cut_index, donor_template="GGC", replace_span=0,
            label="HDR +3bp(예시)",
            description="공여 주형으로 절단 부위에 3bp를 정밀 삽입한 예시.",
        )
    )
    return outs


def default_cases() -> list[EditingCase]:
    """기본 사례 라이브러리."""
    return [_coding_case(), _noncoding_case(), _cas12a_case()]


def get_case(case_id: str) -> EditingCase:
    """ID로 사례 조회. 없으면 첫 사례."""
    cases = default_cases()
    for c in cases:
        if c.case_id == case_id:
            return c
    return cases[0]
