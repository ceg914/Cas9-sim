"""렌더링에 최적화된 평면 구조 모델.

원자 속성을 numpy 배열로 모아 glyph 렌더링과 벡터 연산에 바로 쓴다.
사슬·잔기 메타데이터는 선택/정보 표시를 위해 별도로 보관한다.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from . import elements

# 성분 분류용 잔기 집합
AA3 = {
    "ALA", "ARG", "ASN", "ASP", "CYS", "GLU", "GLN", "GLY", "HIS", "ILE",
    "LEU", "LYS", "MET", "PHE", "PRO", "SER", "THR", "TRP", "TYR", "VAL",
    "MSE", "SEC", "PYL",
}
DNA_RES = {"DA", "DT", "DG", "DC", "DI", "DU", "DN"}
RNA_RES = {"A", "U", "G", "C", "I", "N"}
WATER_RES = {"HOH", "WAT", "DOD", "H2O"}

# 성분 코드(정수) — 토글/색상에 사용
COMP_PROTEIN = 0
COMP_DNA = 1
COMP_RNA = 2
COMP_WATER = 3
COMP_HETERO = 4
COMP_NAMES = {
    COMP_PROTEIN: "단백질", COMP_DNA: "DNA", COMP_RNA: "RNA",
    COMP_WATER: "물", COMP_HETERO: "기타(리간드/이온)",
}


def classify_residue(resname: str, is_hetero: bool) -> int:
    name = resname.strip().upper()
    if name in WATER_RES:
        return COMP_WATER
    if name in AA3:
        return COMP_PROTEIN
    if name in DNA_RES:
        return COMP_DNA
    if name in RNA_RES:
        return COMP_RNA
    return COMP_HETERO


@dataclass
class Residue:
    """잔기 하나(선택/정보용)."""

    chain_id: str
    resid: int
    resname: str
    component: int
    insertion: str = ""
    atom_indices: list[int] = field(default_factory=list)

    @property
    def label(self) -> str:
        return f"{self.chain_id}/{self.resname}{self.resid}{self.insertion}".rstrip()


@dataclass
class Chain:
    """사슬 하나."""

    chain_id: str
    component_counts: dict[int, int] = field(default_factory=dict)
    residue_indices: list[int] = field(default_factory=list)

    @property
    def dominant_component(self) -> int:
        if not self.component_counts:
            return COMP_HETERO
        return max(self.component_counts, key=self.component_counts.get)


# 렌더링 편의를 위해 Atom은 가벼운 뷰로만 제공(실데이터는 배열에 있음)
@dataclass
class Atom:
    index: int
    name: str
    element: str
    coord: np.ndarray
    resname: str
    resid: int
    chain_id: str
    component: int


@dataclass
class Structure:
    """파싱된 구조 전체."""

    name: str
    coords: np.ndarray           # (N,3) float32
    elements: list[str]          # 원소 기호
    atom_names: list[str]
    resnames: list[str]
    resids: np.ndarray           # (N,) int32
    chain_ids: list[str]
    components: np.ndarray        # (N,) int8 — COMP_*
    is_hetero: np.ndarray        # (N,) bool
    bonds: np.ndarray            # (M,2) int32
    residues: list[Residue] = field(default_factory=list)
    chains: list[Chain] = field(default_factory=list)
    meta: dict = field(default_factory=dict)

    # --- 파생 캐시 ---
    _vdw: np.ndarray | None = field(default=None, repr=False)
    _cov: np.ndarray | None = field(default=None, repr=False)
    _colors: np.ndarray | None = field(default=None, repr=False)

    @property
    def n_atoms(self) -> int:
        return len(self.coords)

    @property
    def n_bonds(self) -> int:
        return len(self.bonds)

    def vdw_radii(self) -> np.ndarray:
        if self._vdw is None:
            self._vdw = np.array([elements.vdw_radius(e) for e in self.elements],
                                 dtype=np.float32)
        return self._vdw

    def covalent_radii(self) -> np.ndarray:
        if self._cov is None:
            self._cov = np.array([elements.covalent_radius(e) for e in self.elements],
                                 dtype=np.float32)
        return self._cov

    def element_colors(self) -> np.ndarray:
        if self._colors is None:
            self._colors = np.array([elements.cpk_color(e) for e in self.elements],
                                    dtype=np.float32)
        return self._colors

    def center(self) -> np.ndarray:
        return self.coords.mean(axis=0) if self.n_atoms else np.zeros(3)

    def bounding_radius(self) -> float:
        if not self.n_atoms:
            return 1.0
        d = np.linalg.norm(self.coords - self.center(), axis=1)
        return float(d.max())

    def atom(self, index: int) -> Atom:
        return Atom(
            index=index, name=self.atom_names[index], element=self.elements[index],
            coord=self.coords[index], resname=self.resnames[index],
            resid=int(self.resids[index]), chain_id=self.chain_ids[index],
            component=int(self.components[index]),
        )

    def component_mask(self, comps: set[int]) -> np.ndarray:
        """주어진 성분 집합에 속하는 원자 불리언 마스크."""
        return np.isin(self.components, list(comps))

    def summary(self) -> dict:
        comp_counts: dict[str, int] = {}
        for c in self.components:
            name = COMP_NAMES.get(int(c), "기타")
            comp_counts[name] = comp_counts.get(name, 0) + 1
        return {
            "name": self.name,
            "atoms": self.n_atoms,
            "bonds": self.n_bonds,
            "chains": len(self.chains),
            "residues": len(self.residues),
            "components": comp_counts,
        }
