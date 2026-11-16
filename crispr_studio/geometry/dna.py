"""B-DNA 이중나선의 교육용 3D 좌표 생성.

실제 원자 좌표가 아니라, 이해를 돕기 위한 단순화된 모형이다. 나선 축은
z축을 따르고, 두 당-인산 골격은 반대 위상의 나선으로, 염기쌍은 두 골격을
잇는 막대로 표현한다. 단위는 교육용 임의 단위(대략 옹스트롬 스케일)다.

PyVista/VTK에 의존하지 않으므로 좌표 생성 로직만 단위 테스트할 수 있다.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from ..analysis.sequence import clean_sequence, complement

# B-DNA 근사 파라미터(교육용)
RISE_PER_BP = 3.4          # bp당 축방향 상승
TWIST_PER_BP = 34.3        # bp당 회전(도) ≈ 10.5 bp/turn
HELIX_RADIUS = 10.0        # 골격 반지름
BASE_COLORS = {
    "A": (0.90, 0.30, 0.30),   # 빨강
    "T": (0.95, 0.80, 0.25),   # 노랑
    "G": (0.30, 0.65, 0.95),   # 파랑
    "C": (0.35, 0.80, 0.45),   # 초록
    "N": (0.6, 0.6, 0.6),
}


@dataclass
class HelixModel:
    """이중나선 기하 데이터."""

    bases: str                      # 센스 가닥 서열
    strand_a: np.ndarray            # (N,3) 센스 골격 점
    strand_b: np.ndarray            # (N,3) 상보 골격 점
    axis: np.ndarray                # (N,3) 나선 축 위의 점(염기쌍 중심)
    rise: float = RISE_PER_BP
    twist: float = TWIST_PER_BP
    radius: float = HELIX_RADIUS
    meta: dict = field(default_factory=dict)

    @property
    def n_bases(self) -> int:
        return len(self.bases)

    @property
    def complement_bases(self) -> str:
        return complement(self.bases)

    def axis_point(self, index: float) -> np.ndarray:
        """정수 또는 소수 인덱스에서 축 위 점(선형 보간)."""
        if self.n_bases == 0:
            return np.zeros(3)
        lo = int(np.floor(index))
        hi = min(lo + 1, self.n_bases - 1)
        lo = max(0, min(lo, self.n_bases - 1))
        frac = index - lo
        return self.axis[lo] * (1 - frac) + self.axis[hi] * frac

    def base_color(self, index: int) -> tuple[float, float, float]:
        return BASE_COLORS.get(self.bases[index], BASE_COLORS["N"])

    def total_height(self) -> float:
        return self.rise * max(self.n_bases - 1, 0)


def build_helix(
    sequence: str,
    *,
    rise: float = RISE_PER_BP,
    twist: float = TWIST_PER_BP,
    radius: float = HELIX_RADIUS,
    center: bool = True,
) -> HelixModel:
    """서열로부터 이중나선 좌표를 만든다."""
    bases = clean_sequence(sequence)
    n = len(bases)
    if n == 0:
        z = np.zeros((0, 3))
        return HelixModel(bases, z, z, z, rise, twist, radius)

    idx = np.arange(n)
    theta = np.deg2rad(twist) * idx
    z = rise * idx
    if center:
        z = z - z.mean()

    # 센스 골격
    ax = radius * np.cos(theta)
    ay = radius * np.sin(theta)
    strand_a = np.column_stack([ax, ay, z])

    # 상보 골격 — 약 (minor/major groove를 흉내내려 180도 대신 ~144도 위상차를
    # 줄 수도 있으나, 교육용 단순화를 위해 반대편 180도로 둔다)
    bx = radius * np.cos(theta + np.pi)
    by = radius * np.sin(theta + np.pi)
    strand_b = np.column_stack([bx, by, z])

    axis = np.column_stack([np.zeros(n), np.zeros(n), z])

    return HelixModel(bases, strand_a, strand_b, axis, rise, twist, radius)


def base_pair_segments(model: HelixModel) -> list[tuple[np.ndarray, np.ndarray]]:
    """각 염기쌍의 (센스 골격점, 상보 골격점) 쌍 목록."""
    return [(model.strand_a[i], model.strand_b[i]) for i in range(model.n_bases)]


def region_axis_span(
    model: HelixModel, start: int, end: int
) -> tuple[np.ndarray, np.ndarray]:
    """구간 [start,end)의 축 시작·끝 점."""
    start = max(0, min(start, model.n_bases - 1))
    end = max(0, min(end, model.n_bases))
    return model.axis[start], model.axis[max(end - 1, start)]


def cut_gap_offsets(
    model: HelixModel, cut_index: int, gap: float
) -> np.ndarray:
    """절단 애니메이션용: 각 염기의 축방향 이동량(절단 상/하류 분리).

    절단 상류(인덱스 < cut)는 -gap/2, 하류는 +gap/2 만큼 z로 벌어진다.
    """
    offs = np.zeros((model.n_bases, 3))
    for i in range(model.n_bases):
        offs[i, 2] = -gap / 2 if i < cut_index else gap / 2
    return offs
