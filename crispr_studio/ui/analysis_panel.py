"""편집 결과 분석 패널.

복구 결과 예시를 고르면 편집 전후를 정렬·비교하고, 변이 목록과 단백질
영향을 보여준다. CSV/JSON/보고서로 내보낼 수 있다.
"""

from __future__ import annotations

from PySide6 import QtCore, QtGui, QtWidgets

from ..analysis.mutation import EditAnalysis, analyze_edit
from ..bio.examples import EditingCase
from ..simulation.repair import RepairOutcome
from ..storage import export

_LIMIT = ("※ 아래 결과는 주어진 두 서열을 비교한 결정론적 분석입니다. "
          "실제 세포의 편집 성공률·확률·표현형 예측이 아닙니다.")


class AnalysisPanel(QtWidgets.QWidget):
    """편집 결과 분석 탭."""

    outcome_selected = QtCore.Signal(object)   # RepairOutcome

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self._case: EditingCase | None = None
        self._analysis: EditAnalysis | None = None
        self._build_ui()

    def _build_ui(self) -> None:
        root = QtWidgets.QVBoxLayout(self)

        top = QtWidgets.QHBoxLayout()
        top.addWidget(QtWidgets.QLabel("복구 결과 예시:"))
        self.outcome_combo = QtWidgets.QComboBox()
        self.outcome_combo.currentIndexChanged.connect(self._on_outcome)
        top.addWidget(self.outcome_combo, 1)
        root.addLayout(top)

        self.effect_label = QtWidgets.QLabel()
        self.effect_label.setStyleSheet("font-weight:bold; font-size:14px;")
        root.addWidget(self.effect_label)

        self.summary_label = QtWidgets.QLabel()
        self.summary_label.setWordWrap(True)
        root.addWidget(self.summary_label)

        # 변이 표
        self.table = QtWidgets.QTableWidget(0, 4)
        self.table.setHorizontalHeaderLabels(["종류", "위치", "원본→결과", "설명"])
        self.table.horizontalHeader().setStretchLastSection(True)
        self.table.verticalHeader().setVisible(False)
        self.table.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        root.addWidget(self.table, 1)

        # 정렬 뷰
        root.addWidget(QtWidgets.QLabel("정렬(편집 전 / 일치 / 편집 후):"))
        self.align_view = QtWidgets.QPlainTextEdit()
        self.align_view.setReadOnly(True)
        self.align_view.setFont(QtGui.QFont("DejaVu Sans Mono", 9))
        self.align_view.setMaximumHeight(90)
        root.addWidget(self.align_view)

        # 단백질
        self.protein_view = QtWidgets.QPlainTextEdit()
        self.protein_view.setReadOnly(True)
        self.protein_view.setFont(QtGui.QFont("DejaVu Sans Mono", 9))
        self.protein_view.setMaximumHeight(80)
        root.addWidget(self.protein_view)

        # 내보내기
        btns = QtWidgets.QHBoxLayout()
        self.btn_csv = QtWidgets.QPushButton("CSV 내보내기")
        self.btn_json = QtWidgets.QPushButton("JSON 내보내기")
        self.btn_report = QtWidgets.QPushButton("보고서(txt)")
        self.btn_csv.clicked.connect(lambda: self._export("csv"))
        self.btn_json.clicked.connect(lambda: self._export("json"))
        self.btn_report.clicked.connect(lambda: self._export("report"))
        for b in (self.btn_csv, self.btn_json, self.btn_report):
            btns.addWidget(b)
        root.addLayout(btns)

        limit = QtWidgets.QLabel(_LIMIT)
        limit.setWordWrap(True)
        limit.setStyleSheet("color:#c99; font-size:11px; padding-top:4px;")
        root.addWidget(limit)

    # --- 데이터 ---
    def set_case(self, case: EditingCase) -> None:
        self._case = case
        self.outcome_combo.blockSignals(True)
        self.outcome_combo.clear()
        for o in case.outcomes:
            self.outcome_combo.addItem(o.label, o)
        self.outcome_combo.blockSignals(False)
        if case.outcomes:
            self.outcome_combo.setCurrentIndex(0)
            self._on_outcome(0)

    def current_outcome(self) -> RepairOutcome | None:
        return self.outcome_combo.currentData()

    def _on_outcome(self, idx: int) -> None:
        if self._case is None or idx < 0:
            return
        outcome: RepairOutcome = self.outcome_combo.itemData(idx)
        if outcome is None:
            return
        analysis = analyze_edit(
            self._case.sequence, outcome.edited,
            coding=self._case.is_coding, frame=self._case.frame,
        )
        self._analysis = analysis
        self._show(analysis, outcome)
        self.outcome_selected.emit(outcome)

    def _show(self, a: EditAnalysis, outcome: RepairOutcome) -> None:
        self.effect_label.setText(
            f"[{outcome.pathway_full}] {a.protein_effect.value}")
        self.summary_label.setText(
            f"{outcome.description}\n"
            f"길이 {len(a.original)} → {len(a.edited)} nt "
            f"(순 변화 {a.net_indel:+d}), 변이 {len(a.variants)}건")

        self.table.setRowCount(len(a.variants))
        for r, v in enumerate(a.variants):
            vals = [v.kind.value, str(v.position + 1),
                    f"{v.ref or '-'}→{v.alt or '-'}", v.describe()]
            for c, text in enumerate(vals):
                self.table.setItem(r, c, QtWidgets.QTableWidgetItem(text))

        aln = a.alignment
        self.align_view.setPlainText(
            f"{aln.aligned_a}\n{aln.match_line}\n{aln.aligned_b}")

        if a.protein_before is not None:
            self.protein_view.setPlainText(
                f"편집 전 단백질: {a.protein_before}\n"
                f"편집 후 단백질: {a.protein_after}")
            self.protein_view.show()
        else:
            self.protein_view.setPlainText("(비코딩 서열 — 단백질 분석 없음)")

    def _export(self, kind: str) -> None:
        if self._analysis is None or self._case is None:
            return
        meta = {"사례": self._case.name, "Cas": self._case.cas_name,
                "출처": self._case.source_note}
        filters = {"csv": ("CSV (*.csv)", ".csv"),
                   "json": ("JSON (*.json)", ".json"),
                   "report": ("텍스트 (*.txt)", ".txt")}
        flt, ext = filters[kind]
        path, _ = QtWidgets.QFileDialog.getSaveFileName(
            self, "내보내기", f"분석_결과{ext}", flt)
        if not path:
            return
        if kind == "csv":
            content = export.to_csv(self._analysis)
        elif kind == "json":
            content = export.to_json(self._analysis, meta=meta)
        else:
            content = export.to_report(self._analysis, title="편집 결과 보고서",
                                       meta=meta)
        export.write_text(path, content)
        QtWidgets.QMessageBox.information(self, "완료", f"저장했습니다:\n{path}")
