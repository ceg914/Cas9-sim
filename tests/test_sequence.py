"""서열 처리·번역·정렬 테스트."""

import pytest

from crispr_studio.analysis import sequence as seq
from crispr_studio.analysis import translation as tr
from crispr_studio.analysis.alignment import needleman_wunsch


def test_clean_and_validate():
    assert seq.clean_sequence("ac gt\n12") == "ACGT"
    assert seq.validate_sequence("acgt") == "ACGT"
    with pytest.raises(seq.SequenceError):
        seq.validate_sequence("ACGTX")
    # 모호 코드 허용 옵션
    assert seq.validate_sequence("ACGTN", allow_ambiguous=True) == "ACGTN"


def test_complement_rc():
    assert seq.complement("ACGT") == "TGCA"
    assert seq.reverse_complement("ACGT") == "ACGT"
    assert seq.reverse_complement("AAAC") == "GTTT"


def test_gc_and_counts():
    assert seq.gc_content("GCGC") == 1.0
    assert seq.gc_content("ATAT") == 0.0
    assert seq.gc_content("") == 0.0
    c = seq.base_counts("AACGTN")
    assert c["A"] == 2 and c["기타"] == 1


def test_find_and_pam():
    assert seq.find_all("AAAA", "AA") == [0, 1, 2]
    assert seq.find_all("AAAA", "AA", overlapping=False) == [0, 2]
    # NGG PAM: index 2(AGG), 6(CGG)
    assert seq.find_pam("TTAGGTCGG", "NGG") == [2, 6]
    # IUPAC: TTTV (V=A/C/G)
    assert 0 in seq.find_pam("TTTAAA", "TTTV")


def test_fasta_roundtrip():
    text = ">seq1 desc\nACGT\nACGT\n>seq2\nTTTT\n"
    recs = seq.parse_fasta(text)
    assert len(recs) == 2
    assert recs[0].identifier == "seq1"
    assert recs[0].sequence == "ACGTACGT"
    out = seq.to_fasta(recs, width=4)
    assert ">seq1 desc" in out and "TTTT" in out
    # 헤더 없는 순수 서열
    assert seq.parse_fasta("ACGT")[0].sequence == "ACGT"


def test_translate():
    assert tr.translate("ATGGCTTAA", to_stop_only=True) == "MA*"
    assert tr.translate("ATGGCTTAA", stop_at_stop=True) == "MA"
    # 프레임
    assert tr.translate("AATGGCT", frame=1) == "MA"
    assert tr.split_codons("ATGGC") == ["ATG"]  # 불완전 코돈 버림


def test_find_orfs():
    s = "AAATGGCTGGCTAATTT"  # ATG ... TAA
    orfs = tr.find_orfs(s)
    assert orfs
    assert orfs[0].protein.startswith("M")


def test_needleman_wunsch():
    a = needleman_wunsch("ACGTACGT", "ACGTACGT")
    assert a.identity == 1.0
    assert a.aligned_a == a.aligned_b
    # 단일 결실
    b = needleman_wunsch("ACGTACGT", "ACGTCGT")
    assert "-" in b.aligned_b
