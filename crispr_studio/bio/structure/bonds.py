"""거리 기반 공유 결합 추정.

균일 격자(cell list)로 이웃 후보를 추려 원자 쌍의 거리를 공유 반지름 합과
비교한다. 수만 개 원자도 numpy 브로드캐스팅으로 빠르게 처리한다.
Biopython에 의존하지 않으므로 좌표만으로 테스트할 수 있다.
"""

from __future__ import annotations

import numpy as np

BOND_TOLERANCE = 0.45   # 공유 반지름 합에 더하는 여유(Å)
_MIN_DIST = 0.4         # 이보다 가까우면 중복/오류로 간주


def detect_bonds(
    coords: np.ndarray,
    covalent_radii: np.ndarray,
    *,
    tolerance: float = BOND_TOLERANCE,
    exclude: np.ndarray | None = None,
) -> np.ndarray:
    """공유 결합 쌍 (M,2) 반환(i<j).

    Parameters
    ----------
    exclude:
        True인 원자는 결합 탐지에서 제외(예: 물 분자).
    """
    n = len(coords)
    if n < 2:
        return np.empty((0, 2), dtype=np.int32)

    coords = np.ascontiguousarray(coords, dtype=np.float64)
    radii = np.asarray(covalent_radii, dtype=np.float64)

    keep = np.ones(n, dtype=bool)
    if exclude is not None:
        keep &= ~exclude
    active = np.where(keep)[0]
    if len(active) < 2:
        return np.empty((0, 2), dtype=np.int32)

    pts = coords[active]
    rad = radii[active]
    cutoff = float(rad.max() * 2 + tolerance)
    cell = max(cutoff, 1.0)

    mins = pts.min(axis=0)
    cells = np.floor((pts - mins) / cell).astype(np.int64)

    # 셀 해시 -> 원자(active 지역 인덱스) 목록
    buckets: dict[tuple, list[int]] = {}
    for local_i, c in enumerate(map(tuple, cells)):
        buckets.setdefault(c, []).append(local_i)

    neigh = [(-1, 0, 1)] * 3
    offsets = [(dx, dy, dz)
               for dx in neigh[0] for dy in neigh[1] for dz in neigh[2]]

    pairs: list[np.ndarray] = []
    for c, members in buckets.items():
        members = np.asarray(members)
        # 후보: 이웃 셀 전체
        cand: list[int] = []
        for off in offsets:
            key = (c[0] + off[0], c[1] + off[1], c[2] + off[2])
            b = buckets.get(key)
            if b:
                cand.extend(b)
        cand = np.asarray(cand)
        if cand.size == 0:
            continue
        # members(행) x cand(열) 거리
        d = np.linalg.norm(pts[members][:, None, :] - pts[cand][None, :, :], axis=2)
        rsum = rad[members][:, None] + rad[cand][None, :] + tolerance
        hit = (d <= rsum) & (d > _MIN_DIST)
        mi, ci = np.where(hit)
        if mi.size == 0:
            continue
        gi = active[members[mi]]
        gj = active[cand[ci]]
        # i<j 만 유지(중복 제거)
        lo = np.minimum(gi, gj)
        hi = np.maximum(gi, gj)
        mask = lo < hi
        pairs.append(np.column_stack([lo[mask], hi[mask]]))

    if not pairs:
        return np.empty((0, 2), dtype=np.int32)
    allp = np.vstack(pairs)
    # 전역 중복 제거
    allp = np.unique(allp, axis=0)
    return allp.astype(np.int32)
