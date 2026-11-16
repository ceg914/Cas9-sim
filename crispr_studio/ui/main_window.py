"""CRISPR Studio 메인 창.

하나의 3D 뷰포트를 중심으로 두 작업 모드를 제공한다.

* 교육용 시뮬레이션 — 사례 선택, 단계별 애니메이션, 서열/편집 분석
* 실제 구조 — PDB/mmCIF 구조를 전문 분자 뷰어 수준으로 관찰

두 모드는 좌/우/하단 패널 구성이 다르며, 뷰포트만 공유한다.
"""

from __future__ import annotations

from pathlib import Path

from PySide6 import QtCore, QtGui, QtWidgets

from .. import APP_NAME, __version__
from ..analysis.sequence import SequenceError, validate_sequence
from ..bio.examples import EditingCase, build_outcomes
from ..bio.structure.examples import get_example
from ..bio.structure.model import COMP_NAMES, COMP_PROTEIN, Structure
from ..render.molecule_view import MoleculeView, ViewMode
from ..render.palette import cas9_domain
from ..simulation import cas_variants
from ..simulation.targeting import TargetSite, candidate_sites
from ..storage.project import ProjectState, load_project, save_project
from .analysis_panel import AnalysisPanel
from .sequence_viewer import SequenceViewer
from .side_panels import CasePanel, DisplayPanel
from .structure_panel import StructurePanel
from .timeline import TimelineController
from .workers import StructureLoader


class MainWindow(QtWidgets.QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle(f"{APP_NAME} v{__version__}")
        self.resize(1480, 940)

        self._case: EditingCase | None = None
        self._structure: Structure | None = None
        self._loader: StructureLoader | None = None

        self.view = MoleculeView()
        self.setCentralWidget(self.view)

        self._build_panels()
        self._build_menus()
        self._build_statusbar()
        self._connect()

        # 초기: 교육용 모드 + 첫 사례(신호를 강제 발생시켜 로드)
        self._apply_mode(ViewMode.EDUCATION)
        if self.case_panel.case_list.count():
            self.case_panel.case_list.setCurrentRow(-1)
            self.case_panel.case_list.setCurrentRow(0)

    # ================= 패널 구성 =================
    def _build_panels(self) -> None:
        # 좌측: 교육/구조 스택
        self.case_panel = CasePanel()
        self.structure_panel = StructurePanel()
        self.left_stack = QtWidgets.QStackedWidget()
        self.left_stack.addWidget(self.case_panel)
        self.left_stack.addWidget(self.structure_panel)
        self.left_dock = _dock("탐색기 / 구조", self.left_stack,
                               QtCore.Qt.LeftDockWidgetArea)
        self.addDockWidget(QtCore.Qt.LeftDockWidgetArea, self.left_dock)

        # 우측: 교육용 표시 패널
        self.display_panel = DisplayPanel()
        self.right_dock = _dock("표시 / 정보", self.display_panel,
                                QtCore.Qt.RightDockWidgetArea)
        self.addDockWidget(QtCore.Qt.RightDockWidgetArea, self.right_dock)

        # 하단: 타임라인 + 서열/분석 탭
        self.timeline = TimelineController()
        self.sequence_viewer = SequenceViewer()
        self.analysis_panel = AnalysisPanel()
        self.bottom_tabs = QtWidgets.QTabWidget()
        self.bottom_tabs.addTab(self.sequence_viewer, "DNA 서열 뷰어")
        self.bottom_tabs.addTab(self.analysis_panel, "편집 결과 분석")

        bottom = QtWidgets.QWidget()
        bl = QtWidgets.QVBoxLayout(bottom)
        bl.setContentsMargins(0, 0, 0, 0)
        bl.addWidget(self.timeline)
        bl.addWidget(self.bottom_tabs, 1)
        self.bottom_dock = _dock("시뮬레이션 / 서열 / 분석", bottom,
                                 QtCore.Qt.BottomDockWidgetArea)
        self.bottom_dock.setMinimumHeight(260)
        self.addDockWidget(QtCore.Qt.BottomDockWidgetArea, self.bottom_dock)

    def _build_menus(self) -> None:
        mb = self.menuBar()

        # 모드 전환 툴바
        tb = self.addToolBar("모드")
        tb.setMovable(False)
        tb.addWidget(QtWidgets.QLabel("  작업 모드: "))
        self.mode_combo = QtWidgets.QComboBox()
        self.mode_combo.addItem("교육용 시뮬레이션", ViewMode.EDUCATION)
        self.mode_combo.addItem("실제 구조 (PDB)", ViewMode.STRUCTURE)
        self.mode_combo.currentIndexChanged.connect(
            lambda i: self._apply_mode(self.mode_combo.itemData(i)))
        tb.addWidget(self.mode_combo)

        # 파일
        m_file = mb.addMenu("파일(&F)")
        _act(m_file, "FASTA 불러오기…", self._open_fasta)
        _act(m_file, "서열 직접 입력…", self._enter_sequence)
        _act(m_file, "구조 파일 열기…", lambda: self._apply_mode(ViewMode.STRUCTURE)
             or self.structure_panel._open_file())  # noqa: SLF001
        m_file.addSeparator()
        _act(m_file, "프로젝트 저장…", self._save_project)
        _act(m_file, "프로젝트 불러오기…", self._load_project)
        m_file.addSeparator()
        _act(m_file, "스크린샷 저장…", self._screenshot)
        m_file.addSeparator()
        _act(m_file, "종료", self.close)

        # 시뮬레이션
        m_sim = mb.addMenu("시뮬레이션(&S)")
        _act(m_sim, "재생 / 일시정지", self.timeline.toggle)
        _act(m_sim, "처음으로", self.timeline.restart)
        _act(m_sim, "다음 단계", self.timeline.next_stage)
        _act(m_sim, "이전 단계", self.timeline.prev_stage)

        # 보기
        m_view = mb.addMenu("보기(&V)")
        _act(m_view, "카메라 초기화", self.view.reset_camera)
        _act(m_view, "어두운 배경", lambda: self.view.set_background("dark"))
        _act(m_view, "밝은 배경", lambda: self.view.set_background("light"))
        m_view.addSeparator()
        m_view.addAction(self.left_dock.toggleViewAction())
        m_view.addAction(self.right_dock.toggleViewAction())
        m_view.addAction(self.bottom_dock.toggleViewAction())

        # 도움말
        m_help = mb.addMenu("도움말(&H)")
        _act(m_help, "과학적 한계 안내", self._show_limits)
        _act(m_help, "정보", self._show_about)

    def _build_statusbar(self) -> None:
        self.status = self.statusBar()
        self.status.showMessage("준비됨")

    def _connect(self) -> None:
        # 교육용
        self.case_panel.case_selected.connect(self._on_case_selected)
        self.case_panel.site_selected.connect(self._on_site_selected)
        self.display_panel.options_changed.connect(self.view.set_education_options)
        self.timeline.frame.connect(self._on_frame)
        self.timeline.stage_changed.connect(self._on_stage_changed)
        self.view.base_picked.connect(self._on_base_picked)
        self.sequence_viewer.base_clicked.connect(self._on_seq_clicked)
        self.analysis_panel.outcome_selected.connect(self._on_outcome_selected)

        # 구조
        self.structure_panel.request_example.connect(self._load_example)
        self.structure_panel.request_file.connect(self._load_structure_file)
        self.structure_panel.display_changed.connect(self._on_struct_display)
        self.structure_panel.reset_camera.connect(self.view.reset_camera)
        self.structure_panel.screenshot.connect(self._screenshot)
        self.view.atom_picked.connect(self._on_atom_picked)

    # ================= 모드 전환 =================
    def _apply_mode(self, mode: str) -> None:
        if mode == ViewMode.STRUCTURE:
            self.left_stack.setCurrentWidget(self.structure_panel)
            self.right_dock.hide()
            self.bottom_dock.hide()
            self.mode_combo.setCurrentIndex(1)
            if self._structure is None:
                # 아직 구조가 없으면 교육용 장면을 비우고 안내
                self.status.showMessage("좌측에서 예시 구조를 불러오세요.")
        else:
            self.left_stack.setCurrentWidget(self.case_panel)
            self.right_dock.show()
            self.bottom_dock.show()
            self.mode_combo.setCurrentIndex(0)
            if self._case:
                self._show_education_case()

    # ================= 교육용 로직 =================
    def _on_case_selected(self, case: EditingCase) -> None:
        self._case = case
        self._show_education_case()
        self.analysis_panel.set_case(case)

    def _show_education_case(self) -> None:
        case = self._case
        if case is None:
            return
        self.view.set_education_options(self.display_panel.options())
        self.view.show_education(case.sequence, case.site)
        self.sequence_viewer.set_sequence(case.sequence, case.site)
        self.timeline.restart()
        self.view.set_stage(*(self.timeline.timeline.current_stage, 0.0),
                            reset_camera=True)
        self.status.showMessage(
            f"사례: {case.name} · {case.cas_name} · "
            f"PAM@{case.site.pam_start + 1} · 절단@{case.site.cut_index}")

    def _on_site_selected(self, site: TargetSite | None) -> None:
        if site is None or self._case is None:
            return
        case = self._case
        case.site = site
        case.outcomes = build_outcomes(case.sequence, site.cut_index)
        self.sequence_viewer.set_sequence(case.sequence, site)
        self.view.show_education(case.sequence, site)
        self.analysis_panel.set_case(case)
        self.timeline.restart()
        self.view.set_stage(self.timeline.timeline.current_stage, 0.0,
                            reset_camera=True)

    def _on_frame(self, stage, stage_progress: float, overall: float) -> None:
        self.view.set_stage(stage, stage_progress)

    def _on_stage_changed(self, stage) -> None:
        from ..simulation.engine import STAGE_INFO
        self.display_panel.set_description(STAGE_INFO[stage].description)

    def _on_base_picked(self, index: int, base: str) -> None:
        self.sequence_viewer.highlight_base(index)
        self._describe_base(index, base)

    def _on_seq_clicked(self, index: int) -> None:
        if self._case:
            self.sequence_viewer.highlight_base(index)
            self._describe_base(index, self._case.sequence[index])

    def _describe_base(self, index: int, base: str) -> None:
        case = self._case
        region = "일반 구간"
        if case:
            s = case.site
            if s.pam_start <= index < s.pam_end:
                region = "PAM"
            elif s.protospacer_start <= index < s.protospacer_end:
                region = "프로토스페이서(표적)"
            elif index == s.cut_index:
                region = "절단 위치"
        comp = {"A": "아데닌", "T": "티민", "G": "구아닌", "C": "사이토신"}.get(base, base)
        self.display_panel.set_info(
            f"위치 {index + 1}번 · 염기 {base} ({comp})\n구간: {region}")

    def _on_outcome_selected(self, outcome) -> None:
        self.status.showMessage(
            f"선택한 복구 결과: {outcome.label} — {outcome.pathway_full}")

    # ================= 구조 로직 =================
    def _load_example(self, pdb_id: str) -> None:
        ex = get_example(pdb_id)
        if ex is None:
            return
        path = ex.path()
        if not path.exists():
            QtWidgets.QMessageBox.warning(
                self, "구조 없음",
                f"{pdb_id} 구조 파일을 찾을 수 없습니다:\n{path}")
            return
        self._start_loader(path, pdb_id, default_mode=ex.default_mode)

    def _load_structure_file(self, path: str) -> None:
        self._apply_mode(ViewMode.STRUCTURE)
        self._start_loader(Path(path), Path(path).stem)

    def _start_loader(self, path, name, default_mode: str | None = None) -> None:
        if self._loader and self._loader.isRunning():
            self.status.showMessage("다른 구조를 로딩 중입니다. 잠시 기다려 주세요.")
            return
        self._apply_mode(ViewMode.STRUCTURE)
        self.status.showMessage(f"{name} 로딩 중… (대형 구조는 수 초 걸릴 수 있음)")
        QtWidgets.QApplication.setOverrideCursor(QtCore.Qt.WaitCursor)
        self._pending_mode = default_mode
        self._loader = StructureLoader(path, name=name)
        self._loader.loaded.connect(self._on_structure_loaded)
        self._loader.failed.connect(self._on_structure_failed)
        self._loader.start()

    def _on_structure_loaded(self, structure: Structure) -> None:
        QtWidgets.QApplication.restoreOverrideCursor()
        self._structure = structure
        # 기본 렌더 모드 반영
        if getattr(self, "_pending_mode", None):
            from ..render.structure_scene import RenderMode
            try:
                self.structure_panel.set_mode(RenderMode(self._pending_mode))
            except ValueError:
                pass
        display = self.structure_panel.current_display()
        self.view.show_structure(structure, display)
        self.structure_panel.set_summary(structure)
        s = structure.summary()
        self.status.showMessage(
            f"{s['name']} 로드 완료 · 원자 {s['atoms']:,} · 사슬 {s['chains']}")

    def _on_structure_failed(self, message: str) -> None:
        QtWidgets.QApplication.restoreOverrideCursor()
        QtWidgets.QMessageBox.critical(self, "로드 실패", message)
        self.status.showMessage(message)

    def _on_struct_display(self, display) -> None:
        self.view.set_structure_display(display)

    def _on_atom_picked(self, index: int) -> None:
        s = self._structure
        if s is None:
            return
        atom = s.atom(index)
        lines = [
            f"원자 #{index} · {atom.name} ({atom.element})",
            f"잔기: {atom.resname} {atom.resid}  사슬 {atom.chain_id}",
            f"성분: {COMP_NAMES.get(atom.component, '기타')}",
            f"좌표: ({atom.coord[0]:.1f}, {atom.coord[1]:.1f}, {atom.coord[2]:.1f})",
        ]
        if atom.component == COMP_PROTEIN and s.name.upper().startswith("4OO8"):
            dom, _ = cas9_domain(atom.resid)
            lines.append(f"도메인: {dom}")
        self.structure_panel.set_info("\n".join(lines))
        # 잔기 전체 강조
        res_atoms = self.view.struct_scene.residue_atom_indices(
            atom.chain_id, atom.resid)
        self.view.highlight_atoms(res_atoms or [index])

    # ================= 파일/프로젝트 =================
    def _open_fasta(self) -> None:
        path, _ = QtWidgets.QFileDialog.getOpenFileName(
            self, "FASTA 열기", "", "FASTA (*.fasta *.fa *.fna *.txt);;모든 파일 (*)")
        if not path:
            return
        from ..analysis.sequence import parse_fasta
        try:
            text = Path(path).read_text(encoding="utf-8", errors="ignore")
            recs = parse_fasta(text)
            if not recs:
                raise SequenceError("FASTA 레코드를 찾지 못했습니다.")
            seq = validate_sequence(recs[0].sequence, allow_ambiguous=True)
        except (SequenceError, OSError) as exc:
            QtWidgets.QMessageBox.warning(self, "FASTA 오류", str(exc))
            return
        self._load_custom_sequence(seq, name=recs[0].identifier or Path(path).stem)

    def _enter_sequence(self) -> None:
        text, ok = QtWidgets.QInputDialog.getMultiLineText(
            self, "서열 직접 입력", "DNA 서열(A/T/G/C):", "")
        if not ok or not text.strip():
            return
        try:
            seq = validate_sequence(text, allow_ambiguous=True)
        except SequenceError as exc:
            QtWidgets.QMessageBox.warning(self, "서열 오류", str(exc))
            return
        self._load_custom_sequence(seq, name="사용자 입력")

    def _load_custom_sequence(self, seq: str, name: str) -> None:
        """사용자 서열로 교육용 사례를 즉석 구성."""
        self._apply_mode(ViewMode.EDUCATION)
        cas = cas_variants.SPCAS9
        sites = candidate_sites(seq, cas)
        if not sites:
            QtWidgets.QMessageBox.information(
                self, "표적 없음",
                "이 서열에서 SpCas9 PAM(NGG) 표적을 찾지 못했습니다. "
                "서열은 표시하되 시뮬레이션 표적은 없습니다.")
            site = None
        else:
            site = sites[0]
        case = EditingCase(
            case_id="custom", name=f"사용자 서열: {name}",
            description="불러온 서열로 구성한 사례입니다.",
            sequence=seq, cas_name=cas.name,
            site=site or sites[0] if sites else _dummy_site(),
            outcomes=build_outcomes(seq, site.cut_index) if site else [],
            is_coding=False, source_note="사용자 제공 데이터",
        )
        self._case = case
        self.case_panel.set_sequence_for_targeting(seq, cas.name)
        self._show_education_case()
        self.analysis_panel.set_case(case)

    def _save_project(self) -> None:
        if self._case is None:
            QtWidgets.QMessageBox.information(self, "저장", "저장할 교육용 사례가 없습니다.")
            return
        path, _ = QtWidgets.QFileDialog.getSaveFileName(
            self, "프로젝트 저장", "project.crispr.json", "CRISPR 프로젝트 (*.json)")
        if not path:
            return
        case = self._case
        state = ProjectState(
            name=case.name, sequence=case.sequence, case_id=case.case_id,
            cas_name=case.cas_name, pam_start=case.site.pam_start,
            frame=case.frame, is_coding=case.is_coding,
            selected_outcome=self.analysis_panel.outcome_combo.currentText(),
            timeline_time=self.timeline.timeline.time,
            camera=self.view.get_camera_state(),
            display={"background": self.view.struct_scene.display.background},
        )
        save_project(state, path)
        self.status.showMessage(f"저장됨: {path}")

    def _load_project(self) -> None:
        path, _ = QtWidgets.QFileDialog.getOpenFileName(
            self, "프로젝트 불러오기", "", "CRISPR 프로젝트 (*.json)")
        if not path:
            return
        try:
            state = load_project(path)
        except Exception as exc:
            QtWidgets.QMessageBox.warning(self, "불러오기 오류", str(exc))
            return
        self._apply_mode(ViewMode.EDUCATION)
        if state.case_id and state.case_id != "custom":
            from ..bio.examples import get_case
            case = get_case(state.case_id)
            self._on_case_selected(case)
        elif state.sequence:
            self._load_custom_sequence(state.sequence, name=state.name)
        self.timeline.timeline.set_time(state.timeline_time)
        self.view.set_camera_state(state.camera)
        self.status.showMessage(f"불러옴: {path}")

    def _screenshot(self) -> None:
        path, _ = QtWidgets.QFileDialog.getSaveFileName(
            self, "스크린샷 저장", "screenshot.png", "PNG (*.png)")
        if path:
            self.view.screenshot(path, scale=2)
            self.status.showMessage(f"스크린샷 저장: {path}")

    def _show_limits(self) -> None:
        QtWidgets.QMessageBox.information(
            self, "과학적 한계",
            "· 교육용 시뮬레이션의 분자 애니메이션은 실제 원자 수준 "
            "분자동역학이 아니라 개념 설명용 연출입니다.\n"
            "· 편집 결과 분석은 주어진 두 서열의 결정론적 비교이며, "
            "실제 세포의 편집 성공률·확률·표현형 예측이 아닙니다.\n"
            "· 실제 구조 뷰는 RCSB PDB의 공개 좌표를 그대로 표시하며, "
            "결합은 원자 간 거리로 추정합니다.\n"
            "· 2차 구조·도메인 색상은 교육용 근사입니다.")

    def _show_about(self) -> None:
        QtWidgets.QMessageBox.about(
            self, f"{APP_NAME} 정보",
            f"<b>{APP_NAME}</b> v{__version__}<br>"
            "교육용 CRISPR-Cas9 3D 시뮬레이터<br><br>"
            "PySide6 · PyVista · VTK · Biopython<br>"
            "구조 데이터: RCSB PDB (1BNA, 4OO8)")

    def closeEvent(self, e: QtGui.QCloseEvent) -> None:  # noqa: N802
        self.timeline.pause()
        if self._loader and self._loader.isRunning():
            self._loader.wait(2000)
        super().closeEvent(e)


def _dummy_site() -> TargetSite:
    return TargetSite(0, 0, 0, 0, 0, "SpCas9")


def _dock(title: str, widget: QtWidgets.QWidget, area) -> QtWidgets.QDockWidget:
    dock = QtWidgets.QDockWidget(title)
    dock.setWidget(widget)
    dock.setFeatures(QtWidgets.QDockWidget.DockWidgetMovable
                     | QtWidgets.QDockWidget.DockWidgetFloatable
                     | QtWidgets.QDockWidget.DockWidgetClosable)
    return dock


def _act(menu: QtWidgets.QMenu, text: str, slot) -> QtGui.QAction:
    action = QtGui.QAction(text, menu)
    action.triggered.connect(slot)
    menu.addAction(action)
    return action
