"""GUI 조립 스모크 테스트(디스플레이/GL 필요).

기본 pytest에서는 건너뛴다. 디스플레이가 있는 환경에서
``CRISPR_GUI_TEST=1`` 로 실행하면 메인 창 구성·모드 전환·구조 로딩이
예외 없이 되는지 확인한다.

    CRISPR_GUI_TEST=1 xvfb-run -a python -m pytest tests/test_gui_smoke.py
"""

import os

import pytest

pytestmark = pytest.mark.skipif(
    os.environ.get("CRISPR_GUI_TEST") != "1",
    reason="GUI 스모크는 CRISPR_GUI_TEST=1 에서만 실행",
)


def test_mainwindow_smoke():
    import pyvista as pv
    pv.OFF_SCREEN = True
    from crispr_studio.app import build_application
    from crispr_studio.bio.structure.examples import get_example
    from crispr_studio.bio.structure.loader import load_structure
    from crispr_studio.render.molecule_view import ViewMode
    from crispr_studio.ui.main_window import MainWindow

    app = build_application([])
    w = MainWindow()
    w.show()
    app.processEvents()

    # 교육용 사례가 로드됐는가
    assert w._case is not None
    assert w.view.mode == ViewMode.EDUCATION

    # 구조 모드 전환 + 1BNA 동기 로드
    w._apply_mode(ViewMode.STRUCTURE)
    s = load_structure(get_example("1BNA").path())
    w._on_structure_loaded(s)
    app.processEvents()
    assert w._structure is not None
    assert w.view.mode == ViewMode.STRUCTURE

    # 원자 피킹 핸들러 동작
    w._on_atom_picked(0)
    assert "원자 #0" in w.structure_panel.info_label.text()

    w.close()
