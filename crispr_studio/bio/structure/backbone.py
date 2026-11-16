"""백본 추출과 2차 구조 추정(Cartoon/Ribbon용).

- 단백질: 사슬별로 N–CA–C를 잔기 순서대로 모으고, phi/psi로 알파나선·
  베타병풍·루프를 추정한다. DSSP 바이너리 없이 좌표만으로 근사한다.
- 핵산: 사슬별로 인(P) 또는 C1' 좌표를 이어 골격 경로를 만든다.

추정 2차 구조는 교육용 근사이며 DSSP 수준의 정밀도는 아니다.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .model import COMP_DNA, COMP_PROTEIN, COMP_RNA, Structure

# 핵산 골격 원자 우선순위
_NUCLEIC_BACKBONE = ("P", "C1'", "C1*", "O5'", "C4'")


@dataclass
class ChainTrace:
    """한 사슬의 백본 경로."""

    chain_id: str
    component: int
    coords: np.ndarray                 # (R,3) 백본 대표 원자
    resids: list[int] = field(default_factory=list)
    resnames: list[str] = field(default_factory=list)
    sse: list[str] = field(default_factory=list)   # 'H'/'E'/'C' (단백질만)


def _residue_atom(structure: Structure, atom_indices, name: str):
    for ai in atom_indices:
        if structure.atom_names[ai] == name:
            return structure.coords[ai]
    return None


def _dihedral(p0, p1, p2, p3) -> float:
    b0 = p0 - p1
    b1 = p2 - p1
    b2 = p3 - p2
    b1n = b1 / (np.linalg.norm(b1) + 1e-9)
    v = b0 - np.dot(b0, b1n) * b1n
    w = b2 - np.dot(b2, b1n) * b1n
    x = np.dot(v, w)
    y = np.dot(np.cross(b1n, v), w)
    return float(np.degrees(np.arctan2(y, x)))


def _assign_sse(n_list, ca_list, c_list) -> list[str]:
    """phi/psi 기반 2차 구조 추정."""
    r = len(ca_list)
    sse = ["C"] * r
    phi = [None] * r
    psi = [None] * r
    for i in range(r):
        if i > 0 and c_list[i - 1] is not None and n_list[i] is not None \
                and ca_list[i] is not None and c_list[i] is not None:
            phi[i] = _dihedral(c_list[i - 1], n_list[i], ca_list[i], c_list[i])
        if i < r - 1 and n_list[i] is not None and ca_list[i] is not None \
                and c_list[i] is not None and n_list[i + 1] is not None:
            psi[i] = _dihedral(n_list[i], ca_list[i], c_list[i], n_list[i + 1])

    raw = ["C"] * r
    for i in range(r):
        p, s = phi[i], psi[i]
        if p is None or s is None:
            continue
        if -145 <= p <= -35 and -75 <= s <= 10:
            raw[i] = "H"
        elif -180 <= p <= -40 and (90 <= s <= 180 or -180 <= s <= -160):
            raw[i] = "E"

    # 런렝스 평활화: 짧은 조각 제거(헬릭스 최소 4, 시트 최소 3)
    sse = _smooth_runs(raw, {"H": 4, "E": 3})
    return sse


def _smooth_runs(labels: list[str], min_len: dict[str, int]) -> list[str]:
    out = labels[:]
    n = len(out)
    i = 0
    while i < n:
        j = i
        while j < n and out[j] == out[i]:
            j += 1
        lab = out[i]
        if lab in min_len and (j - i) < min_len[lab]:
            for k in range(i, j):
                out[k] = "C"
        i = j
    return out


def _iter_chain_residues(structure: Structure):
    """사슬 id -> 잔기 Reference 목록(파일 순서)."""
    chains: dict[str, list] = {}
    for res in structure.residues:
        chains.setdefault(res.chain_id, []).append(res)
    return chains


def extract_traces(structure: Structure) -> list[ChainTrace]:
    """모든 사슬의 백본 경로를 추출한다."""
    traces: list[ChainTrace] = []
    for cid, residues in _iter_chain_residues(structure).items():
        protein = [r for r in residues if r.component == COMP_PROTEIN]
        nucleic = [r for r in residues
                   if r.component in (COMP_DNA, COMP_RNA)]
        if len(protein) >= 4:
            traces.append(_protein_trace(structure, cid, protein))
        if len(nucleic) >= 2:
            traces.append(_nucleic_trace(structure, cid, nucleic))
    return traces


def _protein_trace(structure, cid, residues) -> ChainTrace:
    ca_list, n_list, c_list = [], [], []
    resids, resnames = [], []
    for r in residues:
        ca = _residue_atom(structure, r.atom_indices, "CA")
        if ca is None:
            continue
        ca_list.append(ca)
        n_list.append(_residue_atom(structure, r.atom_indices, "N"))
        c_list.append(_residue_atom(structure, r.atom_indices, "C"))
        resids.append(r.resid)
        resnames.append(r.resname)
    coords = np.asarray(ca_list, dtype=np.float32) if ca_list else np.zeros((0, 3), np.float32)
    sse = _assign_sse(n_list, ca_list, c_list) if ca_list else []
    return ChainTrace(cid, COMP_PROTEIN, coords, resids, resnames, sse)


def _nucleic_trace(structure, cid, residues) -> ChainTrace:
    coords = []
    resids, resnames = [], []
    comp = residues[0].component
    for r in residues:
        pt = None
        for name in _NUCLEIC_BACKBONE:
            pt = _residue_atom(structure, r.atom_indices, name)
            if pt is not None:
                break
        if pt is None:
            continue
        coords.append(pt)
        resids.append(r.resid)
        resnames.append(r.resname)
    arr = np.asarray(coords, dtype=np.float32) if coords else np.zeros((0, 3), np.float32)
    return ChainTrace(cid, comp, arr, resids, resnames, [])
