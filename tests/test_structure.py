"""실제 구조 로딩·결합 탐지·성분 분류 테스트."""

import numpy as np
import pytest

from crispr_studio.bio.structure import bonds as bond_mod
from crispr_studio.bio.structure.examples import get_example
from crispr_studio.bio.structure.loader import LoadOptions, load_structure
from crispr_studio.bio.structure.model import (
    COMP_DNA,
    COMP_PROTEIN,
    COMP_RNA,
    COMP_WATER,
    classify_residue,
)


def test_classify_residue():
    assert classify_residue("ALA", False) == COMP_PROTEIN
    assert classify_residue("DA", False) == COMP_DNA
    assert classify_residue("U", False) == COMP_RNA
    assert classify_residue("HOH", True) == COMP_WATER


def test_detect_bonds_small():
    # 물 분자 형태: O-H, O-H (각 ~0.96Å). 공유 반지름 O=0.66,H=0.31
    coords = np.array([[0, 0, 0], [0.96, 0, 0], [-0.24, 0.93, 0]], dtype=float)
    radii = np.array([0.66, 0.31, 0.31])
    b = bond_mod.detect_bonds(coords, radii)
    # O(0)-H(1), O(0)-H(2) 두 결합, H-H는 멀어서 없음
    pairs = {tuple(p) for p in b}
    assert (0, 1) in pairs and (0, 2) in pairs
    assert (1, 2) not in pairs


def test_detect_bonds_no_self_or_dup():
    coords = np.array([[0, 0, 0], [1.5, 0, 0], [3.0, 0, 0]], dtype=float)
    radii = np.array([0.76, 0.76, 0.76])
    b = bond_mod.detect_bonds(coords, radii)
    assert np.all(b[:, 0] < b[:, 1])
    assert len(np.unique(b, axis=0)) == len(b)


def _assets_available(pdb_id):
    ex = get_example(pdb_id)
    return ex is not None and ex.path().exists()


@pytest.mark.skipif(not _assets_available("1BNA"), reason="1BNA 자산 없음")
def test_load_1bna():
    s = load_structure(get_example("1BNA").path())
    assert s.n_atoms == 566
    assert len(s.chains) == 2
    assert s.n_bonds > 400
    # DNA 성분이 다수
    assert (s.components == COMP_DNA).sum() > 400
    # 결합 길이 타당성
    d = np.linalg.norm(s.coords[s.bonds[:, 0]] - s.coords[s.bonds[:, 1]], axis=1)
    assert 1.2 < d.mean() < 1.7


@pytest.mark.skipif(not _assets_available("4OO8"), reason="4OO8 자산 없음")
def test_load_4oo8_counts():
    s = load_structure(get_example("4OO8").path())
    assert s.n_atoms == 24153
    assert len(s.chains) == 6
    comps = {int(c) for c in np.unique(s.components)}
    assert {COMP_PROTEIN, COMP_DNA, COMP_RNA} <= comps
    # 결합 인덱스 무결성
    assert s.bonds.min() >= 0 and s.bonds.max() < s.n_atoms


@pytest.mark.skipif(not _assets_available("1BNA"), reason="1BNA 자산 없음")
def test_pdb_cif_equivalent():
    ex = get_example("1BNA")
    s_pdb = load_structure(ex.path(prefer="pdb"))
    s_cif = load_structure(ex.path(prefer="cif"))
    assert s_pdb.n_atoms == s_cif.n_atoms
    assert s_pdb.n_bonds == s_cif.n_bonds


@pytest.mark.skipif(not _assets_available("1BNA"), reason="1BNA 자산 없음")
def test_drop_water_option():
    ex = get_example("1BNA")
    s = load_structure(ex.path(), options=LoadOptions(bond_water=False))
    # 물은 원자로는 남지만 결합은 없어야(격리된 산소)
    water_atoms = np.where(s.components == COMP_WATER)[0]
    if len(water_atoms):
        bonded = set(s.bonds.flatten().tolist())
        assert not (set(water_atoms.tolist()) & bonded)
