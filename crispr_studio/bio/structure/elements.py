"""원소 속성: 공유 결합 반지름, 반데르발스 반지름, CPK 색상.

공유 반지름은 Cordero 등(2008), 반데르발스 반지름은 Bondi(1964)를 기반으로
한 대표값이다. 색상은 널리 쓰이는 CPK/Jmol 계열 팔레트를 따른다.
"""

from __future__ import annotations

# 공유 결합 반지름(Å)
COVALENT_RADII: dict[str, float] = {
    "H": 0.31, "C": 0.76, "N": 0.71, "O": 0.66, "P": 1.07, "S": 1.05,
    "F": 0.57, "CL": 1.02, "BR": 1.20, "I": 1.39,
    "NA": 1.66, "MG": 1.41, "K": 2.03, "CA": 1.76, "MN": 1.39,
    "FE": 1.32, "ZN": 1.22, "CU": 1.32, "CO": 1.26, "NI": 1.24,
    "SE": 1.20, "B": 0.84,
}
_DEFAULT_COVALENT = 0.77

# 반데르발스 반지름(Å) — Space-Filling(CPK)용
VDW_RADII: dict[str, float] = {
    "H": 1.10, "C": 1.70, "N": 1.55, "O": 1.52, "P": 1.80, "S": 1.80,
    "F": 1.47, "CL": 1.75, "BR": 1.85, "I": 1.98,
    "NA": 2.27, "MG": 1.73, "K": 2.75, "CA": 2.31, "MN": 2.05,
    "FE": 2.05, "ZN": 2.10, "CU": 2.00, "CO": 2.00, "NI": 2.00,
    "SE": 1.90, "B": 1.92,
}
_DEFAULT_VDW = 1.70

# CPK/Jmol 색상(0~1 RGB)
CPK_COLORS: dict[str, tuple[float, float, float]] = {
    "H": (0.90, 0.90, 0.90),
    "C": (0.34, 0.34, 0.37),
    "N": (0.19, 0.31, 0.97),
    "O": (0.90, 0.16, 0.16),
    "P": (1.00, 0.50, 0.00),
    "S": (0.90, 0.78, 0.20),
    "F": (0.56, 0.88, 0.31),
    "CL": (0.12, 0.94, 0.12),
    "BR": (0.65, 0.16, 0.16),
    "I": (0.58, 0.00, 0.58),
    "NA": (0.67, 0.36, 0.95),
    "MG": (0.54, 1.00, 0.00),
    "K": (0.56, 0.25, 0.83),
    "CA": (0.24, 1.00, 0.00),
    "MN": (0.61, 0.48, 0.78),
    "FE": (0.88, 0.40, 0.20),
    "ZN": (0.49, 0.50, 0.69),
    "SE": (1.00, 0.63, 0.00),
}
_DEFAULT_COLOR = (0.85, 0.40, 0.85)   # 미지 원소 — 분홍


def normalize(symbol: str) -> str:
    """원소 기호를 대문자로 정규화(예: 'Cl' -> 'CL')."""
    return symbol.strip().upper()


def covalent_radius(symbol: str) -> float:
    return COVALENT_RADII.get(normalize(symbol), _DEFAULT_COVALENT)


def vdw_radius(symbol: str) -> float:
    return VDW_RADII.get(normalize(symbol), _DEFAULT_VDW)


def cpk_color(symbol: str) -> tuple[float, float, float]:
    return CPK_COLORS.get(normalize(symbol), _DEFAULT_COLOR)
