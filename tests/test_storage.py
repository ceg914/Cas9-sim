"""프로젝트 저장/복원과 내보내기 테스트."""

from crispr_studio.analysis.mutation import analyze_edit
from crispr_studio.storage import export
from crispr_studio.storage.project import (
    ProjectState,
    load_project,
    save_project,
)


def test_project_roundtrip(tmp_path):
    state = ProjectState(
        name="테스트", sequence="ATGGCT", case_id="coding_spcas9",
        pam_start=42, timeline_time=2.0,
        camera={"position": [1, 2, 3]}, display={"dna": True},
    )
    path = tmp_path / "proj.crispr.json"
    save_project(state, path)
    loaded = load_project(path)
    assert loaded.name == "테스트"
    assert loaded.sequence == "ATGGCT"
    assert loaded.pam_start == 42
    assert loaded.timeline_time == 2.0
    assert loaded.camera["position"] == [1, 2, 3]


def test_project_ignores_unknown_keys(tmp_path):
    path = tmp_path / "p.json"
    path.write_text('{"name":"x","sequence":"AC","미래필드":123}', encoding="utf-8")
    loaded = load_project(path)
    assert loaded.name == "x" and loaded.sequence == "AC"


def test_export_formats():
    a = analyze_edit("ATGCAAGCT", "ATGTAAGCT", coding=True)
    j = export.to_json(a, meta={"사례": "테스트"})
    assert "protein_effect" in j and "limitation" in j
    csv_text = export.to_csv(a)
    assert "종류" in csv_text
    report = export.to_report(a, title="보고서", meta={"사례": "테스트"})
    assert "한계" in report and "정렬" in report
