"""DNA 서열 처리: 검증, 통계, 상보 가닥, FASTA, 검색.

외부 의존성 없이 동작하도록 표준 라이브러리만 사용한다. (Biopython이
설치돼 있으면 FASTA 로더가 선택적으로 이를 활용할 수 있으나, 코어 로직은
Biopython 없이도 완전히 동작한다.)
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

# 표준 4종 염기
DNA_BASES = frozenset("ACGT")

# IUPAC 모호 염기 코드 -> 매칭되는 염기 집합. PAM 등 패턴 매칭에 쓴다.
IUPAC_CODES: dict[str, frozenset[str]] = {
    "A": frozenset("A"), "C": frozenset("C"),
    "G": frozenset("G"), "T": frozenset("T"),
    "R": frozenset("AG"), "Y": frozenset("CT"),
    "S": frozenset("GC"), "W": frozenset("AT"),
    "K": frozenset("GT"), "M": frozenset("AC"),
    "B": frozenset("CGT"), "D": frozenset("AGT"),
    "H": frozenset("ACT"), "V": frozenset("ACG"),
    "N": frozenset("ACGT"),
}

_COMPLEMENT = str.maketrans("ACGTacgt", "TGCAtgca")


class SequenceError(ValueError):
    """서열 입력이 잘못됐을 때 발생."""


def clean_sequence(raw: str) -> str:
    """공백·숫자·개행을 제거하고 대문자로. 염기 검증은 하지 않는다."""
    return re.sub(r"[\s\d]", "", raw).upper()


def validate_sequence(seq: str, *, allow_ambiguous: bool = False) -> str:
    """정제된 서열을 돌려주되, 허용되지 않는 문자가 있으면 예외.

    Parameters
    ----------
    allow_ambiguous:
        True면 IUPAC 모호 코드(N, R, Y ...)도 허용한다.
    """
    cleaned = clean_sequence(seq)
    allowed = set(IUPAC_CODES) if allow_ambiguous else set(DNA_BASES)
    bad = sorted({c for c in cleaned if c not in allowed})
    if bad:
        raise SequenceError(
            f"허용되지 않는 문자가 있습니다: {', '.join(bad)} "
            f"(허용: {''.join(sorted(allowed))})"
        )
    return cleaned


def complement(seq: str) -> str:
    """상보 가닥(5'->3' 방향 유지 안 함, 자리별 상보)."""
    return seq.translate(_COMPLEMENT)


def reverse_complement(seq: str) -> str:
    """역상보 가닥(3'->5'를 뒤집어 5'->3'로)."""
    return seq.translate(_COMPLEMENT)[::-1]


def gc_content(seq: str) -> float:
    """GC 함량(0.0~1.0). 빈 서열은 0.0."""
    cleaned = clean_sequence(seq)
    if not cleaned:
        return 0.0
    gc = sum(1 for c in cleaned if c in "GC")
    return gc / len(cleaned)


def base_counts(seq: str) -> dict[str, int]:
    """A/C/G/T 및 기타(N 등) 개수."""
    cleaned = clean_sequence(seq)
    counts = {b: 0 for b in "ACGT"}
    counts["기타"] = 0
    for c in cleaned:
        if c in counts:
            counts[c] += 1
        else:
            counts["기타"] += 1
    return counts


def find_all(seq: str, pattern: str, *, overlapping: bool = True) -> list[int]:
    """``pattern``(정확 문자열)이 나타나는 0-기반 시작 좌표 목록."""
    seq_u = clean_sequence(seq)
    pat_u = clean_sequence(pattern)
    if not pat_u:
        return []
    hits: list[int] = []
    start = 0
    step = 1 if overlapping else len(pat_u)
    while True:
        idx = seq_u.find(pat_u, start)
        if idx == -1:
            break
        hits.append(idx)
        start = idx + step
    return hits


def _iupac_match(seq_window: str, pattern: str) -> bool:
    """``pattern``의 각 IUPAC 코드가 ``seq_window``와 일치하는지."""
    if len(seq_window) != len(pattern):
        return False
    for base, code in zip(seq_window, pattern):
        allowed = IUPAC_CODES.get(code)
        if allowed is None or base not in allowed:
            return False
    return True


def find_pam(seq: str, pam: str = "NGG") -> list[int]:
    """IUPAC PAM 패턴(예: SpCas9의 ``NGG``)이 나타나는 시작 좌표 목록."""
    seq_u = clean_sequence(seq)
    pam_u = pam.upper()
    n = len(pam_u)
    return [
        i for i in range(len(seq_u) - n + 1)
        if _iupac_match(seq_u[i : i + n], pam_u)
    ]


@dataclass(frozen=True)
class FastaRecord:
    """FASTA 레코드 하나."""

    header: str
    sequence: str

    @property
    def identifier(self) -> str:
        """헤더의 첫 토큰(> 제외)을 식별자로."""
        return self.header.split()[0] if self.header else ""


def parse_fasta(text: str) -> list[FastaRecord]:
    """FASTA 텍스트를 레코드 목록으로. 서열 검증은 하지 않는다.

    ``>`` 헤더가 전혀 없으면 전체를 단일 무명 서열로 간주한다.
    """
    records: list[FastaRecord] = []
    header: str | None = None
    chunks: list[str] = []

    def flush() -> None:
        if header is not None or chunks:
            records.append(
                FastaRecord(header or "", clean_sequence("".join(chunks)))
            )

    for line in text.splitlines():
        if line.startswith(">"):
            flush()
            header = line[1:].strip()
            chunks = []
        else:
            chunks.append(line.strip())
    flush()
    # 헤더 없는 순수 서열도 허용
    return [r for r in records if r.sequence or r.header]


def to_fasta(records: list[FastaRecord], *, width: int = 70) -> str:
    """레코드 목록을 FASTA 문자열로(줄바꿈 width 기준)."""
    lines: list[str] = []
    for rec in records:
        lines.append(f">{rec.header}")
        seq = rec.sequence
        for i in range(0, len(seq), width):
            lines.append(seq[i : i + width])
    return "\n".join(lines) + ("\n" if lines else "")


@dataclass
class SequenceStats:
    """서열 기초 통계."""

    length: int
    gc: float
    counts: dict[str, int] = field(default_factory=dict)

    def as_dict(self) -> dict[str, object]:
        return {"length": self.length, "gc": self.gc, "counts": self.counts}


def summarize(seq: str) -> SequenceStats:
    """길이, GC 함량, 염기 개수를 한 번에."""
    cleaned = clean_sequence(seq)
    return SequenceStats(
        length=len(cleaned),
        gc=gc_content(cleaned),
        counts=base_counts(cleaned),
    )
