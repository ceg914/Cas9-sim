"""2D DNA 서열 뷰어(직접 그리는 위젯).

염기별 색상, 좌표 눈금, 상보 가닥, 표적 구간 강조, 선택/검색 강조,
편집 전후 정렬 비교를 지원한다. 클릭하면 해당 염기 인덱스를 알린다.
"""

from __future__ import annotations

from PySide6 import QtCore, QtGui, QtWidgets

from ..analysis.sequence import complement
from ..geometry.dna import BASE_COLORS
from ..simulation.targeting import TargetSite

_CELL_W = 20
_CELL_H = 22
_RULER_H = 18
_MARGIN = 10
_GAP_ROWS = 14  # 가닥 사이 간격


def _qcolor(rgb: tuple[float, float, float], a: int = 255) -> QtGui.QColor:
    return QtGui.QColor(int(rgb[0] * 255), int(rgb[1] * 255), int(rgb[2] * 255), a)


class _SequenceCanvas(QtWidgets.QWidget):
    """실제 그리기를 담당하는 내부 캔버스."""

    base_clicked = QtCore.Signal(int)

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setMouseTracking(True)
        self.setFont(QtGui.QFont("DejaVu Sans Mono", 10))
        self._seq = ""
        self._comp = ""
        self._site: TargetSite | None = None
        self._per_row = 30
        self._selected: set[int] = set()
        self._search_hits: set[int] = set()
        self._cut_index: int | None = None
        self.setMinimumHeight(120)

    def set_sequence(self, seq: str, site: TargetSite | None = None) -> None:
        self._seq = seq
        self._comp = complement(seq)
        self._site = site
        self._cut_index = site.cut_index if site else None
        self._selected.clear()
        self._search_hits.clear()
        self._relayout()

    def set_selection(self, indices: set[int]) -> None:
        self._selected = {i for i in indices if 0 <= i < len(self._seq)}
        self.update()

    def set_search_hits(self, indices: set[int]) -> None:
        self._search_hits = indices
        self.update()

    def _rows(self) -> int:
        if not self._seq:
            return 0
        return (len(self._seq) + self._per_row - 1) // self._per_row

    def _row_block_h(self) -> int:
        return _RULER_H + _CELL_H * 2 + _GAP_ROWS + 18

    def _relayout(self) -> None:
        w = max(self.width(), 200)
        avail = w - 2 * _MARGIN - 48  # 좌측 좌표 여백
        self._per_row = max(10, avail // _CELL_W)
        self.setMinimumHeight(_MARGIN * 2 + self._rows() * self._row_block_h())
        self.updateGeometry()
        self.update()

    def resizeEvent(self, e: QtGui.QResizeEvent) -> None:  # noqa: N802
        self._relayout()
        super().resizeEvent(e)

    def _cell_rect(self, index: int) -> QtCore.QRect | None:
        if not self._seq or index < 0 or index >= len(self._seq):
            return None
        row = index // self._per_row
        col = index % self._per_row
        x = _MARGIN + 48 + col * _CELL_W
        y = _MARGIN + row * self._row_block_h() + _RULER_H
        return QtCore.QRect(x, y, _CELL_W, _CELL_H)

    def mousePressEvent(self, e: QtGui.QMouseEvent) -> None:  # noqa: N802
        idx = self._index_at(e.position().toPoint())
        if idx is not None:
            self.base_clicked.emit(idx)

    def _index_at(self, pos: QtCore.QPoint) -> int | None:
        if not self._seq:
            return None
        y = pos.y() - _MARGIN
        block = self._row_block_h()
        row = y // block
        if row < 0 or row >= self._rows():
            return None
        within = y - row * block
        if not (_RULER_H <= within <= _RULER_H + _CELL_H):
            return None
        col = (pos.x() - _MARGIN - 48) // _CELL_W
        if col < 0 or col >= self._per_row:
            return None
        idx = int(row * self._per_row + col)
        return idx if idx < len(self._seq) else None

    def paintEvent(self, e: QtGui.QPaintEvent) -> None:  # noqa: N802
        p = QtGui.QPainter(self)
        p.fillRect(self.rect(), QtGui.QColor(28, 30, 38))
        if not self._seq:
            p.setPen(QtGui.QColor(150, 150, 160))
            p.drawText(self.rect(), QtCore.Qt.AlignCenter,
                       "서열이 없습니다. FASTA를 불러오거나 사례를 선택하세요.")
            return

        n = len(self._seq)
        for i in range(n):
            self._draw_base(p, i)

        # 절단 위치 표시선
        if self._cut_index is not None and 0 < self._cut_index < n:
            self._draw_cut_line(p, self._cut_index)

    def _draw_cut_line(self, p: QtGui.QPainter, cut: int) -> None:
        # 절단은 염기 cut-1과 cut 사이
        left = self._cell_rect(cut)
        if left is None:
            # 줄 끝이면 이전 염기 오른쪽
            prev = self._cell_rect(cut - 1)
            if prev is None:
                return
            x = prev.right()
            y = prev.top()
        else:
            x = left.left()
            y = left.top()
        pen = QtGui.QPen(QtGui.QColor(255, 60, 60), 2, QtCore.Qt.DashLine)
        p.setPen(pen)
        p.drawLine(x, y - 3, x, y + _CELL_H * 2 + 3)

    def _draw_base(self, p: QtGui.QPainter, i: int) -> None:
        rect = self._cell_rect(i)
        if rect is None:
            return
        col = i % self._per_row
        row = i // self._per_row

        # 좌표 눈금(행 시작 + 10단위)
        if col == 0:
            p.setPen(QtGui.QColor(160, 165, 180))
            p.setFont(QtGui.QFont("DejaVu Sans Mono", 8))
            p.drawText(QtCore.QRect(_MARGIN, rect.top(), 44, _CELL_H),
                       QtCore.Qt.AlignRight | QtCore.Qt.AlignVCenter,
                       str(i + 1))
        tick_pos = i + 1
        if tick_pos % 10 == 0:
            p.setPen(QtGui.QColor(120, 125, 140))
            p.setFont(QtGui.QFont("DejaVu Sans Mono", 7))
            p.drawText(QtCore.QRect(rect.left() - 6, rect.top() - _RULER_H,
                                    _CELL_W + 12, _RULER_H),
                       QtCore.Qt.AlignCenter, str(tick_pos))

        base = self._seq[i]
        base_rgb = BASE_COLORS.get(base, BASE_COLORS["N"])

        # 배경 강조(표적/선택/검색)
        bg = None
        if self._site:
            if self._site.pam_start <= i < self._site.pam_end:
                bg = QtGui.QColor(255, 240, 80, 110)
            elif self._site.protospacer_start <= i < self._site.protospacer_end:
                bg = QtGui.QColor(90, 230, 130, 70)
        if i in self._search_hits:
            bg = QtGui.QColor(90, 160, 255, 130)
        if i in self._selected:
            bg = QtGui.QColor(255, 255, 255, 150)
        if bg:
            p.fillRect(rect.adjusted(0, 0, 0, _CELL_H), bg)

        # 센스 가닥 염기
        p.setFont(QtGui.QFont("DejaVu Sans Mono", 11, QtGui.QFont.Bold))
        p.setPen(_qcolor(base_rgb))
        p.drawText(rect, QtCore.Qt.AlignCenter, base)

        # 상보 가닥 염기
        crect = QtCore.QRect(rect.left(), rect.top() + _CELL_H, _CELL_W, _CELL_H)
        comp = self._comp[i]
        p.setPen(_qcolor(BASE_COLORS.get(comp, BASE_COLORS["N"]), 170))
        p.drawText(crect, QtCore.Qt.AlignCenter, comp)


class SequenceViewer(QtWidgets.QScrollArea):
    """스크롤 가능한 서열 뷰어."""

    base_clicked = QtCore.Signal(int)

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.canvas = _SequenceCanvas()
        self.setWidget(self.canvas)
        self.setWidgetResizable(True)
        self.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.canvas.base_clicked.connect(self.base_clicked)

    def set_sequence(self, seq: str, site: TargetSite | None = None) -> None:
        self.canvas.set_sequence(seq, site)

    def highlight_base(self, index: int) -> None:
        self.canvas.set_selection({index})
        rect = self.canvas._cell_rect(index)  # noqa: SLF001
        if rect:
            self.ensureVisible(rect.center().x(), rect.center().y(), 40, 60)

    def set_search_hits(self, indices: list[int]) -> None:
        self.canvas.set_search_hits(set(indices))
