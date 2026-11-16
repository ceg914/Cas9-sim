"""시뮬레이션 타임라인 컨트롤러 위젯.

재생/일시정지, 처음/끝, 이전/다음 단계, 속도 조절, 스크러빙을 제공하고
매 프레임 현재 단계·진행률을 신호로 내보낸다. 실제 시간 구동은 내부
QTimer가 담당한다.
"""

from __future__ import annotations

from PySide6 import QtCore, QtWidgets

from ..simulation.engine import STAGE_INFO, SimulationTimeline, Stage

_FPS = 30
_SECONDS_PER_STAGE = 2.5          # 1배속에서 한 단계 재생 시간
_SPEEDS = [0.25, 0.5, 1.0, 2.0, 4.0]


class TimelineController(QtWidgets.QWidget):
    """타임라인 UI + 재생 엔진."""

    frame = QtCore.Signal(object, float, float)   # (Stage, stage_progress, overall)
    stage_changed = QtCore.Signal(object)         # Stage

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.timeline = SimulationTimeline()
        self._speed = 1.0
        self._last_stage: Stage | None = None

        self._timer = QtCore.QTimer(self)
        self._timer.setInterval(int(1000 / _FPS))
        self._timer.timeout.connect(self._tick)

        self._build_ui()
        self._emit()

    # --- UI ---
    def _build_ui(self) -> None:
        root = QtWidgets.QVBoxLayout(self)
        root.setContentsMargins(8, 6, 8, 6)

        # 상단: 단계 제목/부제
        info = QtWidgets.QHBoxLayout()
        self.stage_label = QtWidgets.QLabel()
        self.stage_label.setStyleSheet("font-weight:bold; font-size:14px;")
        self.subtitle_label = QtWidgets.QLabel()
        self.subtitle_label.setStyleSheet("color:#aab;")
        info.addWidget(self.stage_label)
        info.addWidget(self.subtitle_label, 1)
        root.addLayout(info)

        # 스크러버
        self.slider = QtWidgets.QSlider(QtCore.Qt.Horizontal)
        self.slider.setRange(0, 1000)
        self.slider.sliderMoved.connect(self._on_scrub)
        self.slider.sliderPressed.connect(self.pause)
        root.addWidget(self.slider)

        # 컨트롤 버튼들
        ctl = QtWidgets.QHBoxLayout()
        self.btn_restart = QtWidgets.QToolButton(text="⏮")
        self.btn_prev = QtWidgets.QToolButton(text="◀◀")
        self.btn_play = QtWidgets.QToolButton(text="▶")
        self.btn_next = QtWidgets.QToolButton(text="▶▶")
        self.btn_end = QtWidgets.QToolButton(text="⏭")
        for b in (self.btn_restart, self.btn_prev, self.btn_play,
                  self.btn_next, self.btn_end):
            b.setFixedWidth(42)
            ctl.addWidget(b)
        self.btn_restart.clicked.connect(self.restart)
        self.btn_prev.clicked.connect(self.prev_stage)
        self.btn_play.clicked.connect(self.toggle)
        self.btn_next.clicked.connect(self.next_stage)
        self.btn_end.clicked.connect(self.to_end)

        ctl.addStretch(1)
        ctl.addWidget(QtWidgets.QLabel("속도"))
        self.speed_combo = QtWidgets.QComboBox()
        for s in _SPEEDS:
            self.speed_combo.addItem(f"{s:g}×", s)
        self.speed_combo.setCurrentIndex(_SPEEDS.index(1.0))
        self.speed_combo.currentIndexChanged.connect(self._on_speed)
        ctl.addWidget(self.speed_combo)
        root.addLayout(ctl)

        # 단계 설명
        self.desc_label = QtWidgets.QLabel()
        self.desc_label.setWordWrap(True)
        self.desc_label.setStyleSheet("color:#ccd; padding-top:4px;")
        root.addWidget(self.desc_label)

    # --- 재생 제어 ---
    def toggle(self) -> None:
        if self._timer.isActive():
            self.pause()
        else:
            self.play()

    def play(self) -> None:
        if self.timeline.is_finished:
            self.timeline.restart()
        self._timer.start()
        self.btn_play.setText("⏸")

    def pause(self) -> None:
        self._timer.stop()
        self.btn_play.setText("▶")

    def restart(self) -> None:
        self.pause()
        self.timeline.restart()
        self._emit()

    def to_end(self) -> None:
        self.pause()
        self.timeline.go_to_end()
        self._emit()

    def next_stage(self) -> None:
        self.pause()
        self.timeline.next_stage()
        self._emit()

    def prev_stage(self) -> None:
        self.pause()
        self.timeline.prev_stage()
        self._emit()

    def set_speed(self, speed: float) -> None:
        self._speed = speed
        idx = min(range(len(_SPEEDS)), key=lambda i: abs(_SPEEDS[i] - speed))
        self.speed_combo.setCurrentIndex(idx)

    # --- 내부 콜백 ---
    def _on_speed(self, idx: int) -> None:
        self._speed = self.speed_combo.itemData(idx)

    def _on_scrub(self, value: int) -> None:
        self.timeline.set_overall_progress(value / 1000)
        self._emit(update_slider=False)

    def _tick(self) -> None:
        dt = (1.0 / _FPS) * self._speed / _SECONDS_PER_STAGE
        self.timeline.advance(dt)
        self._emit()
        if self.timeline.is_finished:
            self.pause()

    def _emit(self, *, update_slider: bool = True) -> None:
        tl = self.timeline
        stage = tl.current_stage
        info = STAGE_INFO[stage]
        self.stage_label.setText(f"{int(stage) + 1}. {info.title}")
        self.subtitle_label.setText(info.subtitle)
        self.desc_label.setText(info.description)
        if update_slider:
            self.slider.blockSignals(True)
            self.slider.setValue(int(tl.overall_progress * 1000))
            self.slider.blockSignals(False)
        if stage != self._last_stage:
            self._last_stage = stage
            self.stage_changed.emit(stage)
        self.frame.emit(stage, tl.stage_progress, tl.overall_progress)
