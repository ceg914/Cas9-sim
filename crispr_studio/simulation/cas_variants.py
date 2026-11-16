"""교육용 Cas 단백질 변이체 정보.

PAM 서열과 절단 방식은 널리 알려진 대표값을 교육용으로 단순화한 것이다.
실제 효소의 활성·특이성은 조건에 따라 달라지며, 여기 수치를 실험 설계의
근거로 삼아서는 안 된다.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class CasVariant:
    """Cas 효소 변이체 한 종."""

    name: str
    pam: str            # IUPAC 패턴
    pam_side: str       # "3prime"(프로토스페이서 하류) 또는 "5prime"(상류)
    spacer_len: int     # 전형적 가이드 길이
    cut_offset: int     # PAM 쪽 끝에서 절단까지의 거리(bp)
    blunt: bool         # True=평활 말단, False=돌출(sticky) 말단
    note: str

    def describe(self) -> str:
        side = "3' 하류" if self.pam_side == "3prime" else "5' 상류"
        end = "평활 말단" if self.blunt else "돌출 말단"
        return (f"{self.name}: PAM {self.pam}({side}), "
                f"가이드 {self.spacer_len}nt, {end}")


SPCAS9 = CasVariant(
    name="SpCas9",
    pam="NGG", pam_side="3prime", spacer_len=20, cut_offset=3, blunt=True,
    note="가장 널리 쓰이는 Cas9. 프로토스페이서 하류 NGG를 인식하고 "
         "PAM에서 3bp 상류를 평활 절단한다(개념적 설명).",
)

SACAS9 = CasVariant(
    name="SaCas9",
    pam="NNGRRT", pam_side="3prime", spacer_len=21, cut_offset=3, blunt=True,
    note="Staphylococcus aureus 유래. 더 긴 PAM을 인식해 크기가 작아 "
         "전달이 쉬운 편으로 소개된다(개념적 설명).",
)

CAS12A = CasVariant(
    name="Cas12a(Cpf1)",
    pam="TTTV", pam_side="5prime", spacer_len=20, cut_offset=18, blunt=False,
    note="프로토스페이서 상류 T-rich PAM을 인식하고 돌출(sticky) 말단을 "
         "만든다. Cas9과 절단 방식이 다르다(개념적 설명).",
)

REGISTRY: dict[str, CasVariant] = {
    v.name: v for v in (SPCAS9, SACAS9, CAS12A)
}


def get(name: str) -> CasVariant:
    """이름으로 변이체 조회. 없으면 SpCas9."""
    return REGISTRY.get(name, SPCAS9)
