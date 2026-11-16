"""구조 렌더링용 색상 팔레트와 배경 프리셋."""

from __future__ import annotations

from ..bio.structure.model import (
    COMP_DNA,
    COMP_HETERO,
    COMP_PROTEIN,
    COMP_RNA,
    COMP_WATER,
)

# 배경 프리셋
BACKGROUNDS = {
    "dark": (0.09, 0.10, 0.14),
    "light": (0.96, 0.97, 0.99),
}

# 성분별 색상
COMPONENT_COLORS = {
    COMP_PROTEIN: (0.45, 0.70, 0.95),
    COMP_DNA: (0.95, 0.65, 0.35),
    COMP_RNA: (0.60, 0.90, 0.55),
    COMP_WATER: (0.60, 0.75, 0.95),
    COMP_HETERO: (0.85, 0.55, 0.85),
}

# 사슬별 색상(순환)
CHAIN_PALETTE = [
    (0.40, 0.76, 0.95), (0.95, 0.60, 0.35), (0.60, 0.90, 0.55),
    (0.90, 0.55, 0.80), (0.95, 0.85, 0.40), (0.55, 0.65, 0.95),
    (0.80, 0.45, 0.45), (0.50, 0.85, 0.80),
]

# 2차 구조 색상
SSE_COLORS = {
    "H": (0.92, 0.35, 0.45),   # 알파 나선 — 빨강
    "E": (0.95, 0.82, 0.35),   # 베타 병풍 — 노랑
    "C": (0.70, 0.72, 0.78),   # 루프/코일 — 회색
}

# SpCas9(4OO8 A/D 사슬) 기능 도메인 — 교육용 근사 경계
CAS9_DOMAINS = [
    ("RuvC", (0.55, 0.60, 0.70), [(1, 59), (718, 774), (909, 1098)]),
    ("Bridge helix", (0.95, 0.70, 0.30), [(60, 93)]),
    ("REC lobe", (0.40, 0.72, 0.95), [(94, 717)]),
    ("HNH", (0.90, 0.40, 0.45), [(775, 908)]),
    ("PAM-interacting", (0.60, 0.88, 0.55), [(1099, 1400)]),
]
_DOMAIN_DEFAULT = (0.70, 0.72, 0.78)


def chain_color(chain_index: int) -> tuple[float, float, float]:
    return CHAIN_PALETTE[chain_index % len(CHAIN_PALETTE)]


def component_color(component: int) -> tuple[float, float, float]:
    return COMPONENT_COLORS.get(component, _DOMAIN_DEFAULT)


def sse_color(code: str) -> tuple[float, float, float]:
    return SSE_COLORS.get(code, SSE_COLORS["C"])


def cas9_domain(resid: int) -> tuple[str, tuple[float, float, float]]:
    """잔기 번호 -> (도메인 이름, 색)."""
    for name, color, ranges in CAS9_DOMAINS:
        for lo, hi in ranges:
            if lo <= resid <= hi:
                return name, color
    return "기타", _DOMAIN_DEFAULT
