"""표준 유전암호(코돈 표)와 아미노산 메타데이터.

표준 코돈 표(NCBI translation table 1)를 사용한다. 미토콘드리아 등
다른 암호를 쓰는 경우는 1차 범위에서 다루지 않는다.
"""

from __future__ import annotations

# DNA 코돈(센스 가닥, 5'->3') -> 아미노산 1문자 기호. 종결코돈은 "*".
CODON_TABLE: dict[str, str] = {
    "TTT": "F", "TTC": "F", "TTA": "L", "TTG": "L",
    "CTT": "L", "CTC": "L", "CTA": "L", "CTG": "L",
    "ATT": "I", "ATC": "I", "ATA": "I", "ATG": "M",
    "GTT": "V", "GTC": "V", "GTA": "V", "GTG": "V",
    "TCT": "S", "TCC": "S", "TCA": "S", "TCG": "S",
    "CCT": "P", "CCC": "P", "CCA": "P", "CCG": "P",
    "ACT": "T", "ACC": "T", "ACA": "T", "ACG": "T",
    "GCT": "A", "GCC": "A", "GCA": "A", "GCG": "A",
    "TAT": "Y", "TAC": "Y", "TAA": "*", "TAG": "*",
    "CAT": "H", "CAC": "H", "CAA": "Q", "CAG": "Q",
    "AAT": "N", "AAC": "N", "AAA": "K", "AAG": "K",
    "GAT": "D", "GAC": "D", "GAA": "E", "GAG": "E",
    "TGT": "C", "TGC": "C", "TGA": "*", "TGG": "W",
    "CGT": "R", "CGC": "R", "CGA": "R", "CGG": "R",
    "AGT": "S", "AGC": "S", "AGA": "R", "AGG": "R",
    "GGT": "G", "GGC": "G", "GGA": "G", "GGG": "G",
}

START_CODON = "ATG"
STOP_CODONS = frozenset({"TAA", "TAG", "TGA"})

# 아미노산 1문자 -> (3문자, 한글/영문 이름)
AMINO_ACIDS: dict[str, tuple[str, str]] = {
    "A": ("Ala", "알라닌"),
    "R": ("Arg", "아르기닌"),
    "N": ("Asn", "아스파라긴"),
    "D": ("Asp", "아스파트산"),
    "C": ("Cys", "시스테인"),
    "E": ("Glu", "글루탐산"),
    "Q": ("Gln", "글루타민"),
    "G": ("Gly", "글리신"),
    "H": ("His", "히스티딘"),
    "I": ("Ile", "아이소류신"),
    "L": ("Leu", "류신"),
    "K": ("Lys", "라이신"),
    "M": ("Met", "메티오닌"),
    "F": ("Phe", "페닐알라닌"),
    "P": ("Pro", "프롤린"),
    "S": ("Ser", "세린"),
    "T": ("Thr", "트레오닌"),
    "W": ("Trp", "트립토판"),
    "Y": ("Tyr", "타이로신"),
    "V": ("Val", "발린"),
    "*": ("Ter", "종결"),
}


def codon_to_aa(codon: str) -> str:
    """코돈 하나를 아미노산 1문자 기호로. 미지의 코돈은 ``"X"``."""
    return CODON_TABLE.get(codon.upper(), "X")


def aa_three_letter(symbol: str) -> str:
    """1문자 기호를 3문자 표기로. 미지의 기호는 ``"Xaa"``."""
    entry = AMINO_ACIDS.get(symbol.upper())
    return entry[0] if entry else "Xaa"


def aa_full_name(symbol: str) -> str:
    """1문자 기호를 이름(한글)으로. 미지의 기호는 ``"미지"``."""
    entry = AMINO_ACIDS.get(symbol.upper())
    return entry[1] if entry else "미지"
