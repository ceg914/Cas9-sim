"""PDB/mmCIF 파일을 평면 Structure 모델로 로드.

Biopython으로 파싱한 뒤 성분을 분류하고 결합을 추정한다. 좌표가 없는
원자는 만들어 내지 않는다(파일에 있는 원자만 표시).
"""

from __future__ import annotations

import warnings
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from . import bonds as bond_mod
from .model import (
    COMP_WATER,
    Chain,
    Residue,
    Structure,
    classify_residue,
)


@dataclass
class LoadOptions:
    """로드 옵션."""

    detect_bonds: bool = True
    bond_water: bool = False       # 물 분자도 결합 탐지에 포함할지
    keep_hydrogens: bool = True
    first_model_only: bool = True


def _guess_element(name: str, raw_element: str) -> str:
    el = (raw_element or "").strip()
    if el:
        return el.upper()
    # 원자 이름에서 추정(예: "CA" -> C, "N1" -> N)
    letters = "".join(ch for ch in name if ch.isalpha())
    if not letters:
        return "X"
    # 두 글자 원소(예: FE, NA, CL)는 드물게만; 기본은 첫 글자
    return letters[0].upper()


def _parser_for(path: Path):
    from Bio.PDB import MMCIFParser, PDBParser

    suffix = path.suffix.lower()
    if suffix in (".cif", ".mmcif"):
        return MMCIFParser(QUIET=True)
    return PDBParser(QUIET=True)


def load_structure(
    path: str | Path,
    *,
    name: str | None = None,
    options: LoadOptions | None = None,
) -> Structure:
    """구조 파일을 로드해 Structure 반환."""
    path = Path(path)
    options = options or LoadOptions()
    if not path.exists():
        raise FileNotFoundError(f"구조 파일이 없습니다: {path}")

    parser = _parser_for(path)
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        biostruct = parser.get_structure(name or path.stem, str(path))

    models = list(biostruct)
    chosen = models[:1] if options.first_model_only else models

    coords: list[tuple[float, float, float]] = []
    elements: list[str] = []
    atom_names: list[str] = []
    resnames: list[str] = []
    resids: list[int] = []
    chain_ids: list[str] = []
    components: list[int] = []
    is_hetero: list[bool] = []

    residues: list[Residue] = []
    chains_map: dict[str, Chain] = {}

    atom_idx = 0
    for model in chosen:
        for chain in model:
            cid = chain.id
            chain_obj = chains_map.get(cid)
            if chain_obj is None:
                chain_obj = Chain(chain_id=cid)
                chains_map[cid] = chain_obj
            for residue in chain:
                hetfield, seqid, icode = residue.id
                het = hetfield.strip() != ""
                resname = residue.resname.strip()
                comp = classify_residue(resname, het)

                res_ref = Residue(
                    chain_id=cid, resid=int(seqid), resname=resname,
                    component=comp, insertion=icode.strip(),
                )
                ridx = len(residues)
                chain_obj.residue_indices.append(ridx)
                chain_obj.component_counts[comp] = (
                    chain_obj.component_counts.get(comp, 0) + 1)

                for atom in residue:
                    # 교란(altloc) 원자는 대표 1개만
                    if atom.is_disordered():
                        try:
                            atom = atom.disordered_get()
                        except Exception:
                            pass
                    element = _guess_element(atom.get_name(), atom.element)
                    if not options.keep_hydrogens and element == "H":
                        continue
                    x, y, z = atom.get_coord()
                    coords.append((float(x), float(y), float(z)))
                    elements.append(element)
                    atom_names.append(atom.get_name().strip())
                    resnames.append(resname)
                    resids.append(int(seqid))
                    chain_ids.append(cid)
                    components.append(comp)
                    is_hetero.append(het)
                    res_ref.atom_indices.append(atom_idx)
                    atom_idx += 1
                residues.append(res_ref)

    coords_arr = np.asarray(coords, dtype=np.float32) if coords else np.zeros((0, 3), np.float32)
    comp_arr = np.asarray(components, dtype=np.int8)

    structure = Structure(
        name=name or path.stem.upper(),
        coords=coords_arr,
        elements=elements,
        atom_names=atom_names,
        resnames=resnames,
        resids=np.asarray(resids, dtype=np.int32),
        chain_ids=chain_ids,
        components=comp_arr,
        is_hetero=np.asarray(is_hetero, dtype=bool),
        bonds=np.empty((0, 2), dtype=np.int32),
        residues=residues,
        chains=list(chains_map.values()),
        meta={"path": str(path), "format": path.suffix.lower().lstrip(".")},
    )

    if options.detect_bonds and structure.n_atoms > 1:
        exclude = None
        if not options.bond_water:
            exclude = comp_arr == COMP_WATER
        structure.bonds = bond_mod.detect_bonds(
            structure.coords, structure.covalent_radii(), exclude=exclude
        )

    return structure
