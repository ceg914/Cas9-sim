"""시뮬레이션 엔진·타게팅·복구·예시 테스트."""

from crispr_studio.analysis.sequence import find_pam
from crispr_studio.analysis.translation import translate
from crispr_studio.bio.examples import default_cases, get_case
from crispr_studio.simulation import cas_variants
from crispr_studio.simulation.engine import SimulationTimeline, Stage
from crispr_studio.simulation.repair import (
    RepairPathway,
    hdr_outcome,
    nhej_outcomes,
)
from crispr_studio.simulation.targeting import candidate_sites, derive_site


def test_timeline_stage_mapping():
    tl = SimulationTimeline()
    assert tl.current_stage is Stage.TARGET_SEARCH
    assert tl.stage_progress == 0.0
    tl.set_time(2.5)
    assert tl.current_stage is Stage.BINDING
    assert abs(tl.stage_progress - 0.5) < 1e-9
    tl.go_to_end()
    assert tl.is_finished
    assert tl.current_stage is Stage.REPAIR


def test_timeline_navigation():
    tl = SimulationTimeline()
    tl.next_stage()
    assert tl.current_stage is Stage.PAM_RECOGNITION
    tl.set_time(1.5)          # 단계 중간
    tl.prev_stage()           # 현재 단계 시작으로
    assert abs(tl.time - 1.0) < 1e-9
    tl.prev_stage()           # 이전 단계로
    assert tl.current_stage is Stage.TARGET_SEARCH
    tl.advance(100)
    assert tl.time == tl.duration


def test_nhej_outcomes():
    seq = "ATGGCTGCTGCTGCTGCT"
    outs = nhej_outcomes(seq, 9)
    assert len(outs) == 4
    for o in outs:
        assert o.pathway is RepairPathway.NHEJ
        assert o.edited != seq
    # 길이 변화 확인
    lengths = sorted(len(o.edited) for o in outs)
    assert lengths == [len(seq) - 3, len(seq) - 2, len(seq) - 1, len(seq) + 1]


def test_hdr_outcome():
    seq = "ATGGCTGCTGCT"
    o = hdr_outcome(seq, 6, "GGG", replace_span=0)
    assert o.pathway is RepairPathway.HDR
    assert o.edited == "ATGGCT" + "GGG" + "GCTGCT"
    # 치환형
    o2 = hdr_outcome(seq, 6, "AAA", replace_span=3)
    assert len(o2.edited) == len(seq)


def test_derive_site_spcas9():
    seq = "N" * 25 + "AGG" + "N" * 10
    seq = seq.replace("N", "A")
    # PAM을 명시적으로 심은 위치
    s = "A" * 25 + "AGG" + "A" * 10
    site = derive_site(s, cas_variants.SPCAS9, 25)
    assert site.protospacer_start == 5
    assert site.protospacer_end == 25
    assert site.cut_index == 22


def test_candidate_sites_respect_pam():
    for case in default_cases():
        cas = cas_variants.get(case.cas_name)
        # 선택된 PAM이 실제로 그 변이체의 PAM 규칙과 일치해야 한다
        assert case.site.pam_start in find_pam(case.sequence, cas.pam)


def test_cases_consistent():
    for case in default_cases():
        # 프로토스페이서 길이
        cas = cas_variants.get(case.cas_name)
        assert len(case.protospacer) == cas.spacer_len
        # 코딩 사례는 내부 조기 종결이 없어야(원본이 온전한 ORF)
        if case.is_coding:
            prot = translate(case.sequence, case.frame, to_stop_only=True)
            assert "*" not in prot[:-1]
        assert case.outcomes


def test_get_case_fallback():
    assert get_case("coding_spcas9").case_id == "coding_spcas9"
    assert get_case("없는ID").case_id  # 첫 사례로 폴백
