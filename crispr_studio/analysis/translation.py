"""코돈 번역과 ORF 관련 유틸.

종결코돈에서 번역을 멈추는 것은 선택 사항으로 둔다(교육용 비교에서는
종결 이후 서열도 보여주는 편이 이해에 도움이 되므로 기본은 '계속'이다).
"""

from __future__ import annotations

from dataclasses import dataclass

from ..bio.codon_table import START_CODON, STOP_CODONS, codon_to_aa
from .sequence import clean_sequence


@dataclass(frozen=True)
class Codon:
    """번역 단위 하나."""

    index: int          # 단백질 내 0-기반 위치
    nucleotides: str    # 3염기
    amino_acid: str     # 1문자 기호("*" = 종결, "X" = 불완전/미지)
    start: int          # 원본 서열에서의 0-기반 시작 좌표


def split_codons(seq: str, frame: int = 0) -> list[str]:
    """프레임을 적용해 3염기씩 끊는다. 끝의 불완전 코돈은 버린다."""
    cleaned = clean_sequence(seq)[frame:]
    return [cleaned[i : i + 3] for i in range(0, len(cleaned) - 2, 3)]


def translate(
    seq: str,
    frame: int = 0,
    *,
    stop_at_stop: bool = False,
    to_stop_only: bool = False,
) -> str:
    """서열을 아미노산 1문자 문자열로 번역.

    Parameters
    ----------
    stop_at_stop:
        True면 첫 종결코돈에서 멈춘다(종결 기호 "*"는 포함하지 않음).
    to_stop_only:
        True면 종결코돈까지 포함해 번역하고 멈춘다("*" 포함).
    """
    out: list[str] = []
    for codon in split_codons(seq, frame):
        aa = codon_to_aa(codon)
        if aa == "*":
            if to_stop_only:
                out.append(aa)
            if stop_at_stop or to_stop_only:
                break
            out.append(aa)
            continue
        out.append(aa)
    return "".join(out)


def translate_detailed(seq: str, frame: int = 0) -> list[Codon]:
    """코돈별 상세 정보 목록."""
    cleaned = clean_sequence(seq)
    result: list[Codon] = []
    protein_idx = 0
    for start in range(frame, len(cleaned) - 2, 3):
        nts = cleaned[start : start + 3]
        result.append(
            Codon(
                index=protein_idx,
                nucleotides=nts,
                amino_acid=codon_to_aa(nts),
                start=start,
            )
        )
        protein_idx += 1
    return result


def find_start_codons(seq: str) -> list[int]:
    """ATG 시작 좌표 목록."""
    cleaned = clean_sequence(seq)
    return [i for i in range(len(cleaned) - 2) if cleaned[i : i + 3] == START_CODON]


@dataclass(frozen=True)
class ORF:
    """열린 해독틀."""

    start: int      # ATG의 0-기반 시작 좌표
    end: int        # 종결코돈 끝 다음 좌표(반열림 구간)
    frame: int
    protein: str    # 종결 기호 제외 단백질 서열


def find_orfs(seq: str, *, min_aa: int = 1) -> list[ORF]:
    """단일 가닥에서 ATG로 시작해 종결코돈으로 끝나는 ORF를 찾는다.

    교육용이므로 양쪽 가닥·중첩 ORF 전수조사는 하지 않고, 센스 가닥에서
    각 프레임별로 ATG->종결 구간을 수집한다.
    """
    cleaned = clean_sequence(seq)
    orfs: list[ORF] = []
    for frame in range(3):
        i = frame
        while i < len(cleaned) - 2:
            if cleaned[i : i + 3] == START_CODON:
                j = i
                protein: list[str] = []
                closed = False
                while j < len(cleaned) - 2:
                    codon = cleaned[j : j + 3]
                    if codon in STOP_CODONS:
                        closed = True
                        break
                    protein.append(codon_to_aa(codon))
                    j += 3
                if closed and len(protein) >= min_aa:
                    orfs.append(
                        ORF(start=i, end=j + 3, frame=frame,
                            protein="".join(protein))
                    )
                    i = j + 3
                    continue
            i += 3
    orfs.sort(key=lambda o: (-len(o.protein), o.start))
    return orfs
