"""백본 추출·2차 구조 추정·팔레트 테스트."""

import numpy as np
import pytest

from crispr_studio.bio.structure.backbone import extract_traces
from crispr_studio.bio.structure.examples import get_example
from crispr_studio.bio.structure.loader import load_structure
from crispr_studio.bio.structure.model import COMP_DNA, COMP_PROTEIN
from crispr_studio.render import palette


def _available(pdb_id):
    ex = get_example(pdb_id)
    return ex is not None and ex.path().exists()


def test_palette_domains():
    # REC lobe / HNH / PI 경계가 서로 다른 색을 주는지
    assert palette.cas9_domain(300)[0] == "REC lobe"
    assert palette.cas9_domain(800)[0] == "HNH"
    assert palette.cas9_domain(1200)[0] == "PAM-interacting"
    assert palette.cas9_domain(30)[0] == "RuvC"


def test_palette_helpers():
    assert palette.sse_color("H") != palette.sse_color("E")
    assert len(palette.chain_color(0)) == 3
    assert palette.component_color(COMP_DNA) != palette.component_color(COMP_PROTEIN)


@pytest.mark.skipif(not _available("1BNA"), reason="1BNA 자산 없음")
def test_nucleic_traces_1bna():
    s = load_structure(get_example("1BNA").path())
    traces = extract_traces(s)
    # 두 DNA 사슬
    dna_traces = [t for t in traces if t.component == COMP_DNA]
    assert len(dna_traces) == 2
    for t in dna_traces:
        assert t.coords.shape[0] >= 10          # 12 뉴클레오타이드
        assert t.coords.shape[1] == 3


@pytest.mark.skipif(not _available("4OO8"), reason="4OO8 자산 없음")
def test_protein_traces_and_sse_4oo8():
    s = load_structure(get_example("4OO8").path())
    traces = extract_traces(s)
    prot = [t for t in traces if t.component == COMP_PROTEIN]
    assert prot, "단백질 백본 추출 실패"
    big = max(prot, key=lambda t: t.coords.shape[0])
    assert big.coords.shape[0] > 1000
    # 2차 구조에 나선/병풍이 일부라도 잡혀야 한다
    labels = set(big.sse)
    assert "H" in labels            # 알파 나선 존재
    assert len(big.sse) == big.coords.shape[0]
