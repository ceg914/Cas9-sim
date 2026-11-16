"""변이 분류 테스트."""

from crispr_studio.analysis.mutation import (
    ProteinEffect,
    VariantKind,
    analyze_edit,
)


def test_substitution_synonymous():
    # CTT(L) -> CTC(L): 동의 변이
    a = analyze_edit("ATGCTT", "ATGCTC", coding=True)
    assert a.protein_effect is ProteinEffect.SYNONYMOUS
    assert a.net_indel == 0


def test_substitution_missense():
    # GCT(A) -> GAT(D)
    a = analyze_edit("ATGGCT", "ATGGAT", coding=True)
    assert a.protein_effect is ProteinEffect.MISSENSE


def test_substitution_nonsense():
    # 두 번째 코돈을 종결코돈으로: CAA(Q) -> TAA(*)
    a = analyze_edit("ATGCAAGCT", "ATGTAAGCT", coding=True)
    assert a.protein_effect is ProteinEffect.NONSENSE


def test_frameshift_deletion():
    a = analyze_edit("ATGGCTGCTGCT", "ATGGCTGCTGC", coding=True)
    assert a.net_indel == -1
    assert a.protein_effect is ProteinEffect.FRAMESHIFT


def test_inframe_deletion():
    # 한 코돈(3bp) 결실, 조기 종결 없음
    a = analyze_edit("ATGGCTGGCGCTTTT", "ATGGCTGCTTTT", coding=True)
    assert a.net_indel == -3
    assert a.protein_effect is ProteinEffect.INFRAME_INDEL


def test_insertion_detected():
    a = analyze_edit("ATGGCT", "ATGAAAGCT", coding=True)
    kinds = {v.kind for v in a.variants}
    assert VariantKind.INSERTION in kinds
    assert a.net_indel == 3


def test_no_change():
    a = analyze_edit("ATGGCT", "ATGGCT", coding=True)
    assert a.protein_effect is ProteinEffect.NONE
    assert not a.variants
