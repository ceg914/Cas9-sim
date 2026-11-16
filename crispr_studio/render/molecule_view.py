"""Qt 3D 뷰포트 위젯.

하나의 QtInteractor 위에 두 장면을 번갈아 띄운다.

* 교육용 시뮬레이션 장면(``CrisprScene``) — 단순화된 모형 애니메이션
* 실제 구조 장면(``StructureScene``) — PDB 원자 좌표 기반 렌더링

두 표현은 명확히 구분되며, 같은 뷰포트에서 전환해 사용한다.
"""

from __future__ import annotations

from PySide6 import QtCore, QtWidgets

from ..bio.structure.model import Structure
from ..geometry import dna
from ..simulation.engine import Stage
from ..simulation.targeting import TargetSite
from .scene import CrisprScene, DisplayOptions, SceneState
from .structure_scene import StructureDisplay, StructureScene


class ViewMode:
    EDUCATION = "education"
    STRUCTURE = "structure"


class MoleculeView(QtWidgets.QWidget):
    """3D 뷰포트(교육용/실제구조 공용)."""

    base_picked = QtCore.Signal(int, str)       # 교육용: (염기 인덱스, 문자)
    atom_picked = QtCore.Signal(int)            # 실제구조: 원자 인덱스

    def __init__(self, parent: QtWidgets.QWidget | None = None) -> None:
        super().__init__(parent)
        from pyvistaqt import QtInteractor

        layout = QtWidgets.QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        self.interactor = QtInteractor(self)
        layout.addWidget(self.interactor)

        self.edu_scene = CrisprScene(self.interactor)
        self.struct_scene = StructureScene(self.interactor)
        self._mode = ViewMode.EDUCATION
        self._edu_options = DisplayOptions()
        self._stage = Stage.TARGET_SEARCH
        self._progress = 0.0
        self._picking_enabled = False
        self._enable_picking()

    @property
    def mode(self) -> str:
        return self._mode

    # ===== 교육용 =====
    def show_education(self, sequence: str, site: TargetSite | None = None) -> None:
        self.struct_scene.clear()
        self._mode = ViewMode.EDUCATION
        model = dna.build_helix(sequence)
        self.edu_scene.set_scene(SceneState(model, site, self._edu_options))
        self._stage = Stage.TARGET_SEARCH
        self._progress = 0.0

    def set_stage(self, stage: Stage, progress: float,
                  *, reset_camera: bool = False) -> None:
        if self._mode != ViewMode.EDUCATION:
            return
        self._stage, self._progress = stage, progress
        self.edu_scene.render_state(stage, progress, reset_camera=reset_camera)

    def set_education_options(self, options: DisplayOptions) -> None:
        self._edu_options = options
        if self.edu_scene.state:
            self.edu_scene.state.options = options
        if self._mode == ViewMode.EDUCATION:
            self.edu_scene.render_state(self._stage, self._progress)

    # ===== 실제 구조 =====
    def show_structure(self, structure: Structure,
                       display: StructureDisplay | None = None) -> None:
        self.edu_scene._clear()  # noqa: SLF001
        self._mode = ViewMode.STRUCTURE
        self.struct_scene.set_structure(structure, display)

    def set_structure_display(self, display: StructureDisplay) -> None:
        if self._mode == ViewMode.STRUCTURE:
            self.struct_scene.set_display(display)

    def highlight_atoms(self, indices: list[int]) -> None:
        if self._mode == ViewMode.STRUCTURE:
            self.struct_scene.highlight_atoms(indices)

    # ===== 공용 =====
    def reset_camera(self) -> None:
        if self._mode == ViewMode.STRUCTURE:
            self.struct_scene.reset_camera()
        else:
            self.edu_scene.render_state(self._stage, self._progress,
                                        reset_camera=True)

    def set_background(self, name: str) -> None:
        self.struct_scene.set_background(name)

    def screenshot(self, path: str, scale: int = 2) -> None:
        try:
            self.interactor.screenshot(path, scale=scale)
        except TypeError:
            self.interactor.screenshot(path)

    def get_camera_state(self) -> dict:
        cam = self.interactor.camera
        return {"position": list(cam.position),
                "focal_point": list(cam.focal_point), "up": list(cam.up)}

    def set_camera_state(self, state: dict | None) -> None:
        if not state:
            return
        cam = self.interactor.camera
        try:
            cam.position = state["position"]
            cam.focal_point = state["focal_point"]
            cam.up = state["up"]
            self.interactor.render()
        except Exception:  # pragma: no cover
            pass

    # ===== 피킹 =====
    def _enable_picking(self) -> None:
        if self._picking_enabled:
            return
        try:
            self.interactor.enable_point_picking(
                callback=self._on_pick, show_message=False,
                show_point=False, left_clicking=True, use_picker=True)
            self._picking_enabled = True
        except Exception:  # pragma: no cover
            pass

    def _on_pick(self, point, *args) -> None:
        if point is None:
            return
        if self._mode == ViewMode.STRUCTURE:
            idx = self.struct_scene.pick_atom(point)
            if idx is not None:
                self.atom_picked.emit(idx)
        else:
            state = self.edu_scene.state
            if state is None or state.model.n_bases == 0:
                return
            import numpy as np
            d = np.linalg.norm(state.model.axis - np.asarray(point), axis=1)
            i = int(d.argmin())
            self.base_picked.emit(i, state.model.bases[i])
