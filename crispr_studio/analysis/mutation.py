"""편집 전후 서열 비교와 단백질 수준 영향 분류.

정렬 결과로부터 삽입·결실·치환을 추출하고, (코딩 구간이 주어지면)
동의/미스센스/넌센스/프레임시프트 등 교육용 분류를 제공한다.

주의: 여기서 내놓는 분류는 '주어진 두 서열'을 비교한 결정론적 결과이며,
실제 세포에서 어떤 편집이 일어날지에 대한 예측이 아니다.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

from .alignment import GAP, Alignment, needleman_wunsch
from .translation import translate


class VariantKind(str, Enum):
    """자리별 변이 종류."""

    SUBSTITUTION = "치환"
    INSERTION = "삽입"
    DELETION = "결실"


@dataclass(frozen=True)
class Variant:
    """원본 좌표 기준 변이 한 건(연속 구간은 하나로 묶음)."""

    kind: VariantKind
    position: int       # 원본 서열 기준 0-기반 좌표
    ref: str            # 원본 염기(삽입이면 "")
    alt: str            # 결과 염기(결실이면 "")

    def describe(self) -> str:
        p = self.position + 1  # 1-기반 표기
        if self.kind is VariantKind.SUBSTITUTION:
            return f"{p}번째 {self.ref}→{self.alt} 치환"
        if self.kind is VariantKind.INSERTION:
            return f"{p}번째 뒤 {self.alt} 삽입(+{len(self.alt)})"
        return f"{p}번째부터 {self.ref} 결실(-{len(self.ref)})"


class ProteinEffect(str, Enum):
    """단백질 수준 영향(교육용 분류)."""

    NONE = "변화 없음"
    SYNONYMOUS = "동의(침묵) 변이"
    MISSENSE = "미스센스 변이"
    NONSENSE = "넌센스 변이"
    FRAMESHIFT = "프레임시프트 변이"
    INFRAME_INDEL = "틀유지 삽입/결실"
    UNKNOWN = "판정 불가"


def extract_variants(alignment: Alignment) -> list[Variant]:
    """정렬에서 변이 목록을 뽑는다(연속 indel은 병합).

    좌표는 원본(aligned_a)의 갭을 제외한 위치를 기준으로 한다.
    """
    a, b = alignment.aligned_a, alignment.aligned_b
    variants: list[Variant] = []
    ref_pos = 0  # 원본에서의 현재 좌표(갭 아닌 문자 수)

    i = 0
    while i < len(a):
        ca, cb = a[i], b[i]
        if ca != GAP and cb != GAP:
            if ca != cb:
                variants.append(
                    Variant(VariantKind.SUBSTITUTION, ref_pos, ca, cb)
                )
            ref_pos += 1
            i += 1
        elif ca == GAP and cb != GAP:
            # 원본에 갭 => 삽입. 연속 병합.
            ins = []
            while i < len(a) and a[i] == GAP and b[i] != GAP:
                ins.append(b[i]); i += 1
            variants.append(
                Variant(VariantKind.INSERTION, ref_pos - 1 if ref_pos else 0,
                        "", "".join(ins))
            )
        elif ca != GAP and cb == GAP:
            # 결과에 갭 => 결실. 연속 병합.
            dele = []
            start = ref_pos
            while i < len(a) and a[i] != GAP and b[i] == GAP:
                dele.append(a[i]); ref_pos += 1; i += 1
            variants.append(
                Variant(VariantKind.DELETION, start, "".join(dele), "")
            )
        else:  # 양쪽 갭 — 발생하지 않지만 방어적으로
            i += 1
    return variants


def net_length_change(variants: list[Variant]) -> int:
    """삽입(+)·결실(-)의 순 길이 변화."""
    delta = 0
    for v in variants:
        if v.kind is VariantKind.INSERTION:
            delta += len(v.alt)
        elif v.kind is VariantKind.DELETION:
            delta -= len(v.ref)
    return delta


@dataclass
class EditAnalysis:
    """편집 전후 종합 분석 결과."""

    original: str
    edited: str
    alignment: Alignment
    variants: list[Variant]
    net_indel: int
    protein_effect: ProteinEffect
    protein_before: str | None = None
    protein_after: str | None = None

    def summary_lines(self) -> list[str]:
        lines = [
            f"원본 길이: {len(self.original)} nt",
            f"결과 길이: {len(self.edited)} nt (순 변화 {self.net_indel:+d})",
            f"변이 건수: {len(self.variants)}",
            f"단백질 영향: {self.protein_effect.value}",
        ]
        for v in self.variants:
            lines.append("  · " + v.describe())
        return lines


def _classify_protein(
    variants: list[Variant],
    net_indel: int,
    protein_before: str | None,
    protein_after: str | None,
) -> ProteinEffect:
    if not variants:
        return ProteinEffect.NONE

    has_indel = any(v.kind is not VariantKind.SUBSTITUTION for v in variants)

    if has_indel:
        if net_indel % 3 != 0:
            return ProteinEffect.FRAMESHIFT
        # 틀유지 indel: 삽입/결실된 코돈 수만큼만 길이가 변해야 정상.
        # 그보다 더 짧으면 새 조기 종결코돈이 생긴 것 → 넌센스로 본다.
        if protein_before is not None and protein_after is not None:
            len_before = _len_to_stop(protein_before)
            len_after = _len_to_stop(protein_after)
            expected = len_before + net_indel // 3
            if len_after < expected:
                return ProteinEffect.NONSENSE
        return ProteinEffect.INFRAME_INDEL

    # 치환만 있는 경우 — 단백질 비교 필요
    if protein_before is None or protein_after is None:
        return ProteinEffect.UNKNOWN

    stop_before = _first_stop(protein_before)
    stop_after = _first_stop(protein_after)
    # 원래보다 앞선 위치에 새 종결코돈 생성 → 넌센스
    if stop_after != -1 and (stop_before == -1 or stop_after < stop_before):
        return ProteinEffect.NONSENSE
    if protein_before == protein_after:
        return ProteinEffect.SYNONYMOUS
    return ProteinEffect.MISSENSE


def _first_stop(protein: str) -> int:
    """첫 종결("*") 위치. 없으면 -1."""
    return protein.find("*")


def _len_to_stop(protein: str) -> int:
    """첫 종결코돈 앞까지의 아미노산 수(종결이 없으면 전체 길이)."""
    s = _first_stop(protein)
    return len(protein) if s == -1 else s


def analyze_edit(
    original: str,
    edited: str,
    *,
    coding: bool = False,
    frame: int = 0,
) -> EditAnalysis:
    """편집 전후 서열을 종합 분석.

    Parameters
    ----------
    coding:
        True면 ``frame`` 기준으로 단백질을 번역해 영향을 분류한다.
    """
    aln = needleman_wunsch(original, edited)
    variants = extract_variants(aln)
    net = net_length_change(variants)

    protein_before = protein_after = None
    if coding:
        protein_before = translate(original, frame, to_stop_only=True)
        protein_after = translate(edited, frame, to_stop_only=True)

    effect = _classify_protein(variants, net, protein_before, protein_after)

    return EditAnalysis(
        original=original,
        edited=edited,
        alignment=aln,
        variants=variants,
        net_indel=net,
        protein_effect=effect,
        protein_before=protein_before,
        protein_after=protein_after,
    )
