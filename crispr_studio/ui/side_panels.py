"""좌측 사례/표적 패널과 우측 표시/정보 패널."""

from __future__ import annotations

from PySide6 import QtCore, QtWidgets

from ..bio.examples import EditingCase, default_cases
from ..render.scene import DisplayOptions
from ..simulation import cas_variants
from ..simulation.targeting import TargetSite, candidate_sites


class CasePanel(QtWidgets.QWidget):
    """사례 라이브러리 + Cas 변이체 + 표적 부위 선택."""

    case_selected = QtCore.Signal(object)   # EditingCase
    site_selected = QtCore.Signal(object)   # TargetSite
    cas_changed = QtCore.Signal(str)

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self._sequence = ""
        self._build_ui()
        self._load_cases()

    def _build_ui(self) -> None:
        root = QtWidgets.QVBoxLayout(self)

        root.addWidget(_section("사례 라이브러리"))
        self.case_list = QtWidgets.QListWidget()
        self.case_list.currentRowChanged.connect(self._on_case_row)
        root.addWidget(self.case_list, 1)

        self.case_desc = QtWidgets.QLabel()
        self.case_desc.setWordWrap(True)
        self.case_desc.setStyleSheet("color:#bcd; font-size:11px;")
        root.addWidget(self.case_desc)

        root.addWidget(_section("Cas 변이체"))
        self.cas_combo = QtWidgets.QComboBox()
        for name in cas_variants.REGISTRY:
            self.cas_combo.addItem(name)
        self.cas_combo.currentTextChanged.connect(self._on_cas)
        root.addWidget(self.cas_combo)
        self.cas_note = QtWidgets.QLabel()
        self.cas_note.setWordWrap(True)
        self.cas_note.setStyleSheet("color:#9ab; font-size:11px;")
        root.addWidget(self.cas_note)

        root.addWidget(_section("표적 부위 (PAM)"))
        self.site_combo = QtWidgets.QComboBox()
        self.site_combo.currentIndexChanged.connect(self._on_site)
        root.addWidget(self.site_combo)

    def _load_cases(self) -> None:
        self._cases = default_cases()
        for c in self._cases:
            self.case_list.addItem(c.name)
        if self._cases:
            self.case_list.setCurrentRow(0)

    # --- 외부에서 서열 갱신(FASTA 등) ---
    def set_sequence_for_targeting(self, sequence: str, cas_name: str) -> None:
        self._sequence = sequence
        self.cas_combo.blockSignals(True)
        self.cas_combo.setCurrentText(cas_name)
        self.cas_combo.blockSignals(False)
        self._refresh_sites(emit=False)

    def _on_case_row(self, row: int) -> None:
        if row < 0 or row >= len(self._cases):
            return
        case = self._cases[row]
        self._sequence = case.sequence
        self.case_desc.setText(case.description + f"\n출처: {case.source_note}")
        self.cas_combo.blockSignals(True)
        self.cas_combo.setCurrentText(case.cas_name)
        self.cas_combo.blockSignals(False)
        self._update_cas_note(case.cas_name)
        self.case_selected.emit(case)
        # 사례의 기본 표적 부위를 목록에 반영
        self._refresh_sites(emit=False, preferred=case.site)

    def _on_cas(self, name: str) -> None:
        self._update_cas_note(name)
        self.cas_changed.emit(name)
        self._refresh_sites(emit=True)

    def _update_cas_note(self, name: str) -> None:
        self.cas_note.setText(cas_variants.get(name).note)

    def _refresh_sites(self, *, emit: bool, preferred: TargetSite | None = None) -> None:
        cas = cas_variants.get(self.cas_combo.currentText())
        sites = candidate_sites(self._sequence, cas)
        self._sites = sites
        self.site_combo.blockSignals(True)
        self.site_combo.clear()
        sel = 0
        for i, s in enumerate(sites):
            proto = self._sequence[s.protospacer_start:s.protospacer_end]
            self.site_combo.addItem(
                f"PAM@{s.pam_start + 1} "
                f"{self._sequence[s.pam_start:s.pam_end]} · {proto[:8]}…", s)
            if preferred and s.pam_start == preferred.pam_start:
                sel = i
        self.site_combo.blockSignals(False)
        if sites:
            self.site_combo.setCurrentIndex(sel)
            if emit:
                self.site_selected.emit(sites[sel])
        elif emit:
            self.site_selected.emit(None)

    def _on_site(self, idx: int) -> None:
        site = self.site_combo.itemData(idx)
        if site is not None:
            self.site_selected.emit(site)


class DisplayPanel(QtWidgets.QWidget):
    """표시 옵션 + 선택 분자 정보 + 설명."""

    options_changed = QtCore.Signal(object)   # DisplayOptions

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self._options = DisplayOptions()
        self._build_ui()

    def _build_ui(self) -> None:
        root = QtWidgets.QVBoxLayout(self)

        root.addWidget(_section("표시 / 숨김"))
        self.cb_dna = _check("DNA 이중나선", True, self._changed)
        self.cb_bases = _check("염기쌍", True, self._changed)
        self.cb_cas9 = _check("Cas9 단백질", True, self._changed)
        self.cb_grna = _check("가이드 RNA", True, self._changed)
        self.cb_hl = _check("표적 구간 강조", True, self._changed)
        for c in (self.cb_dna, self.cb_bases, self.cb_cas9, self.cb_grna, self.cb_hl):
            root.addWidget(c)

        root.addWidget(_section("투명도"))
        self.sl_dna = _slider(100, self._changed)
        self.sl_cas9 = _slider(45, self._changed)
        root.addWidget(QtWidgets.QLabel("DNA"))
        root.addWidget(self.sl_dna)
        root.addWidget(QtWidgets.QLabel("Cas9"))
        root.addWidget(self.sl_cas9)

        root.addWidget(_section("선택 분자 정보"))
        self.info_label = QtWidgets.QLabel("분자나 염기를 클릭하세요.")
        self.info_label.setWordWrap(True)
        self.info_label.setStyleSheet("color:#cde;")
        root.addWidget(self.info_label)

        root.addWidget(_section("구조 설명"))
        self.desc_label = QtWidgets.QLabel()
        self.desc_label.setWordWrap(True)
        self.desc_label.setStyleSheet("color:#bcd; font-size:11px;")
        root.addWidget(self.desc_label)
        root.addStretch(1)

    def _changed(self, *_) -> None:
        o = self._options
        o.show_dna = self.cb_dna.isChecked()
        o.show_bases = self.cb_bases.isChecked()
        o.show_cas9 = self.cb_cas9.isChecked()
        o.show_grna = self.cb_grna.isChecked()
        o.highlight_target = self.cb_hl.isChecked()
        o.dna_opacity = self.sl_dna.value() / 100
        o.cas9_opacity = self.sl_cas9.value() / 100
        self.options_changed.emit(o)

    def options(self) -> DisplayOptions:
        return self._options

    def set_info(self, text: str) -> None:
        self.info_label.setText(text)

    def set_description(self, text: str) -> None:
        self.desc_label.setText(text)


# --- 작은 헬퍼 ---
def _section(title: str) -> QtWidgets.QLabel:
    lbl = QtWidgets.QLabel(title)
    lbl.setStyleSheet("font-weight:bold; padding-top:6px; color:#def;")
    return lbl


def _check(text: str, checked: bool, slot) -> QtWidgets.QCheckBox:
    cb = QtWidgets.QCheckBox(text)
    cb.setChecked(checked)
    cb.stateChanged.connect(slot)
    return cb


def _slider(value: int, slot) -> QtWidgets.QSlider:
    s = QtWidgets.QSlider(QtCore.Qt.Horizontal)
    s.setRange(10, 100)
    s.setValue(value)
    s.valueChanged.connect(slot)
    return s
