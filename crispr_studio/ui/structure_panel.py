"""실제 구조 제어 패널.

구조 선택, 렌더링 모드, 색상 체계, 성분 표시/숨김, 배경, LOD를 조절하고
선택 원자/잔기 정보와 구조 요약을 보여준다.
"""

from __future__ import annotations

from PySide6 import QtCore, QtWidgets

from ..bio.structure.examples import EXAMPLES
from ..bio.structure.model import (
    COMP_DNA,
    COMP_HETERO,
    COMP_PROTEIN,
    COMP_RNA,
    COMP_WATER,
    Structure,
)
from ..render.structure_scene import (
    MODE_LABELS,
    SCHEME_LABELS,
    ColorScheme,
    RenderMode,
    StructureDisplay,
)


class StructurePanel(QtWidgets.QWidget):
    """실제 구조 뷰 제어."""

    request_example = QtCore.Signal(str)      # pdb_id
    request_file = QtCore.Signal(str)         # 파일 경로
    display_changed = QtCore.Signal(object)   # StructureDisplay
    reset_camera = QtCore.Signal()
    screenshot = QtCore.Signal()

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self._display = StructureDisplay()
        self._build_ui()

    def _build_ui(self) -> None:
        root = QtWidgets.QVBoxLayout(self)

        root.addWidget(_hdr("예시 구조"))
        self.example_combo = QtWidgets.QComboBox()
        for ex in EXAMPLES:
            self.example_combo.addItem(f"{ex.pdb_id} — {ex.name}", ex.pdb_id)
        root.addWidget(self.example_combo)
        row = QtWidgets.QHBoxLayout()
        self.btn_load = QtWidgets.QPushButton("불러오기")
        self.btn_open = QtWidgets.QPushButton("파일 열기…")
        self.btn_load.clicked.connect(
            lambda: self.request_example.emit(self.example_combo.currentData()))
        self.btn_open.clicked.connect(self._open_file)
        row.addWidget(self.btn_load)
        row.addWidget(self.btn_open)
        root.addLayout(row)
        self.example_desc = _wrap_label("", "#bcd", 11)
        root.addWidget(self.example_desc)
        self.example_combo.currentIndexChanged.connect(self._update_example_desc)
        self._update_example_desc(0)

        root.addWidget(_hdr("렌더링 모드"))
        self.mode_combo = QtWidgets.QComboBox()
        for mode in RenderMode:
            self.mode_combo.addItem(MODE_LABELS[mode], mode.value)
        self.mode_combo.currentIndexChanged.connect(self._emit)
        root.addWidget(self.mode_combo)

        root.addWidget(_hdr("색상 체계"))
        self.scheme_combo = QtWidgets.QComboBox()
        for scheme in ColorScheme:
            self.scheme_combo.addItem(SCHEME_LABELS[scheme], scheme.value)
        self.scheme_combo.currentIndexChanged.connect(self._emit)
        root.addWidget(self.scheme_combo)

        root.addWidget(_hdr("성분 표시"))
        self.cb_protein = _chk("단백질", True, self._emit)
        self.cb_dna = _chk("DNA", True, self._emit)
        self.cb_rna = _chk("RNA", True, self._emit)
        self.cb_hetero = _chk("리간드/이온", True, self._emit)
        self.cb_water = _chk("물", False, self._emit)
        for c in (self.cb_protein, self.cb_dna, self.cb_rna,
                  self.cb_hetero, self.cb_water):
            root.addWidget(c)

        root.addWidget(_hdr("배경 / 품질"))
        opt = QtWidgets.QHBoxLayout()
        self.bg_combo = QtWidgets.QComboBox()
        self.bg_combo.addItem("어두운 배경", "dark")
        self.bg_combo.addItem("밝은 배경", "light")
        self.bg_combo.currentIndexChanged.connect(self._emit)
        self.lod_combo = QtWidgets.QComboBox()
        self.lod_combo.addItem("자동 LOD", "auto")
        self.lod_combo.addItem("고품질", "high")
        self.lod_combo.addItem("경량", "low")
        self.lod_combo.currentIndexChanged.connect(self._emit)
        opt.addWidget(self.bg_combo)
        opt.addWidget(self.lod_combo)
        root.addLayout(opt)

        row2 = QtWidgets.QHBoxLayout()
        btn_cam = QtWidgets.QPushButton("카메라 초기화")
        btn_shot = QtWidgets.QPushButton("스크린샷")
        btn_cam.clicked.connect(self.reset_camera)
        btn_shot.clicked.connect(self.screenshot)
        row2.addWidget(btn_cam)
        row2.addWidget(btn_shot)
        root.addLayout(row2)

        root.addWidget(_hdr("구조 정보"))
        self.summary_label = _wrap_label("구조를 불러오세요.", "#cde", 11)
        root.addWidget(self.summary_label)

        root.addWidget(_hdr("선택 정보"))
        self.info_label = _wrap_label("원자를 클릭하면 정보가 표시됩니다.", "#dfe", 12)
        root.addWidget(self.info_label)
        root.addStretch(1)

    # --- 상태 ---
    def current_display(self) -> StructureDisplay:
        comps = set()
        if self.cb_protein.isChecked():
            comps.add(COMP_PROTEIN)
        if self.cb_dna.isChecked():
            comps.add(COMP_DNA)
        if self.cb_rna.isChecked():
            comps.add(COMP_RNA)
        if self.cb_hetero.isChecked():
            comps.add(COMP_HETERO)
        self._display = StructureDisplay(
            mode=RenderMode(self.mode_combo.currentData()),
            color_scheme=ColorScheme(self.scheme_combo.currentData()),
            show_components=comps,
            show_water=self.cb_water.isChecked(),
            background=self.bg_combo.currentData(),
            lod=self.lod_combo.currentData(),
        )
        return self._display

    def set_mode(self, mode: RenderMode) -> None:
        idx = self.mode_combo.findData(mode.value)
        if idx >= 0:
            self.mode_combo.blockSignals(True)
            self.mode_combo.setCurrentIndex(idx)
            self.mode_combo.blockSignals(False)

    def set_summary(self, structure: Structure) -> None:
        s = structure.summary()
        comps = ", ".join(f"{k} {v}" for k, v in s["components"].items())
        self.summary_label.setText(
            f"{s['name']} · 원자 {s['atoms']:,} · 결합 {s['bonds']:,}\n"
            f"사슬 {s['chains']} · 잔기 {s['residues']:,}\n성분: {comps}")

    def set_info(self, text: str) -> None:
        self.info_label.setText(text)

    def _emit(self, *_) -> None:
        self.display_changed.emit(self.current_display())

    def _update_example_desc(self, idx: int) -> None:
        if 0 <= idx < len(EXAMPLES):
            self.example_desc.setText(EXAMPLES[idx].description)

    def _open_file(self) -> None:
        path, _ = QtWidgets.QFileDialog.getOpenFileName(
            self, "구조 파일 열기", "",
            "구조 파일 (*.pdb *.cif *.mmcif *.ent);;모든 파일 (*)")
        if path:
            self.request_file.emit(path)


def _hdr(text: str) -> QtWidgets.QLabel:
    lbl = QtWidgets.QLabel(text)
    lbl.setStyleSheet("font-weight:bold; padding-top:6px; color:#def;")
    return lbl


def _wrap_label(text: str, color: str, size: int) -> QtWidgets.QLabel:
    lbl = QtWidgets.QLabel(text)
    lbl.setWordWrap(True)
    lbl.setStyleSheet(f"color:{color}; font-size:{size}px;")
    return lbl


def _chk(text: str, checked: bool, slot) -> QtWidgets.QCheckBox:
    cb = QtWidgets.QCheckBox(text)
    cb.setChecked(checked)
    cb.stateChanged.connect(slot)
    return cb
