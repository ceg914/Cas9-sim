"""CRISPR 분자 장면 구성(플로터 비의존).

PyVista의 어떤 플로터(QtInteractor 또는 off_screen Plotter)에도 붙을 수
있도록, 장면 구성·갱신 로직만 담는다. 덕분에 헤드리스에서 스크린샷으로
렌더링을 검증할 수 있다.

표현은 모두 단순화된 교육용 모형이다. 원자 수준 구조가 아니다.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np
import pyvista as pv

from ..geometry import dna
from ..simulation.engine import Stage
from ..simulation.targeting import TargetSite

# 색상 팔레트
COLOR_BACKBONE_A = (0.95, 0.55, 0.20)   # 센스 골격(주황)
COLOR_BACKBONE_B = (0.55, 0.75, 0.95)   # 상보 골격(하늘)
COLOR_CAS9 = (0.60, 0.40, 0.75)         # Cas9(보라)
COLOR_GRNA = (0.95, 0.35, 0.65)         # gRNA(분홍)
COLOR_PAM = (1.0, 0.95, 0.3)            # PAM 강조(노랑)
COLOR_PROTO = (0.4, 1.0, 0.6)           # 프로토스페이서 강조(연두)
COLOR_CUT = (1.0, 0.2, 0.2)             # 절단 표시(빨강)
COLOR_BG = (0.09, 0.10, 0.14)


@dataclass
class DisplayOptions:
    """표시 토글과 투명도."""

    show_dna: bool = True
    show_cas9: bool = True
    show_grna: bool = True
    show_bases: bool = True
    dna_opacity: float = 1.0
    cas9_opacity: float = 0.45
    highlight_target: bool = True


@dataclass
class SceneState:
    """장면이 표현하는 논리 상태."""

    model: dna.HelixModel
    site: TargetSite | None = None
    options: DisplayOptions = field(default_factory=DisplayOptions)


class CrisprScene:
    """플로터 위에 CRISPR 장면을 그리고 단계별로 갱신한다."""

    def __init__(self, plotter: "pv.BasePlotter") -> None:
        self.plotter = plotter
        self.state: SceneState | None = None
        self._actors: list = []
        try:
            self.plotter.set_background(COLOR_BG)
        except Exception:  # pragma: no cover - 일부 백엔드
            pass

    # --- 장면 설정 ---
    def set_scene(self, state: SceneState) -> None:
        self.state = state
        self.render_state(Stage.TARGET_SEARCH, 0.0, reset_camera=True)

    def set_from_sequence(
        self, sequence: str, site: TargetSite | None = None,
        options: DisplayOptions | None = None,
    ) -> None:
        model = dna.build_helix(sequence)
        self.set_scene(SceneState(model, site, options or DisplayOptions()))

    # --- 내부 유틸 ---
    def _clear(self) -> None:
        for a in self._actors:
            try:
                self.plotter.remove_actor(a, render=False)
            except Exception:  # pragma: no cover
                pass
        self._actors = []

    def _add(self, mesh, **kw):
        actor = self.plotter.add_mesh(mesh, **kw)
        self._actors.append(actor)
        return actor

    # --- 단계별 렌더 ---
    def render_state(
        self, stage: Stage, progress: float, *, reset_camera: bool = False
    ) -> None:
        if self.state is None:
            return
        self._clear()
        model = self.state.model
        opt = self.state.options
        if model.n_bases == 0:
            if reset_camera:
                self._safe_reset_camera()
            return

        cut_index = self.state.site.cut_index if self.state.site else model.n_bases // 2

        # 절단/복구 단계에서 두 가닥을 벌린다
        gap = self._cut_gap(stage, progress, model)
        offsets = dna.cut_gap_offsets(model, cut_index, gap) if gap > 0 else None

        if opt.show_dna:
            self._draw_backbone(model, offsets, opt)
            if opt.show_bases:
                self._draw_base_pairs(model, offsets, opt, stage)

        if opt.show_cas9:
            self._draw_cas9(model, stage, progress)
        if opt.show_grna and self.state.site:
            self._draw_grna(model, stage, progress)

        if gap > 0:
            self._draw_cut_marker(model, cut_index)

        if reset_camera:
            self._safe_reset_camera()
        try:
            self.plotter.render()
        except Exception:  # pragma: no cover
            pass

    def _safe_reset_camera(self) -> None:
        try:
            self.plotter.reset_camera()
            self.plotter.camera.azimuth = 20
            self.plotter.camera.elevation = 15
        except Exception:  # pragma: no cover
            pass

    @staticmethod
    def _cut_gap(stage: Stage, progress: float, model: dna.HelixModel) -> float:
        """단계별 절단 간격(축 단위)."""
        full = model.rise * 3.0
        if stage is Stage.CLEAVAGE:
            return full * progress
        if stage is Stage.REPAIR:
            # 복구가 진행되며 다시 닫힘
            return full * (1.0 - progress)
        return 0.0

    def _points_with_offset(self, pts: np.ndarray, offsets) -> np.ndarray:
        return pts if offsets is None else pts + offsets

    def _draw_backbone(self, model, offsets, opt: DisplayOptions) -> None:
        a = self._points_with_offset(model.strand_a, offsets)
        b = self._points_with_offset(model.strand_b, offsets)
        r = max(model.radius * 0.08, 0.6)
        for pts, color in ((a, COLOR_BACKBONE_A), (b, COLOR_BACKBONE_B)):
            if len(pts) >= 2:
                tube = pv.Spline(pts, max(len(pts) * 4, 8)).tube(radius=r)
                self._add(tube, color=color, opacity=opt.dna_opacity,
                          smooth_shading=True)

    def _draw_base_pairs(self, model, offsets, opt, stage) -> None:
        a = self._points_with_offset(model.strand_a, offsets)
        b = self._points_with_offset(model.strand_b, offsets)
        n = model.n_bases
        points = np.empty((n * 2, 3))
        points[0::2] = a
        points[1::2] = b
        lines = np.hstack([[2, 2 * i, 2 * i + 1] for i in range(n)]).astype(np.int64)
        rgb = np.empty((n * 2, 3))
        site = self.state.site
        for i in range(n):
            color = model.base_color(i)
            # 표적 구간 강조
            if site and opt.highlight_target:
                if site.pam_start <= i < site.pam_end:
                    color = COLOR_PAM
                elif site.protospacer_start <= i < site.protospacer_end:
                    color = _blend(color, COLOR_PROTO, 0.5)
            rgb[2 * i] = color
            rgb[2 * i + 1] = _blend(color, (1, 1, 1), 0.25)
        mesh = pv.PolyData(points, lines=lines)
        mesh.point_data["rgb"] = (rgb * 255).astype(np.uint8)
        self._add(mesh, scalars="rgb", rgb=True, line_width=7,
                  render_lines_as_tubes=True, opacity=opt.dna_opacity)

    def _target_center(self, model: dna.HelixModel) -> np.ndarray:
        site = self.state.site
        if site:
            return model.axis_point((site.protospacer_start + site.protospacer_end) / 2)
        return model.axis_point(model.n_bases / 2)

    def _draw_cas9(self, model, stage: Stage, progress: float) -> None:
        center = self._target_center(model)
        radius = model.radius * 2.2
        # 표적 탐색 단계에서는 멀리서 접근
        approach = 1.0
        if stage is Stage.TARGET_SEARCH:
            approach = progress
        far = center + np.array([0, 0, model.total_height() * 0.6 + radius])
        pos = far * (1 - approach) + (center + np.array([radius * 0.9, 0, 0])) * approach

        # 두 갈래(bilobed) 단순 표현
        lobe1 = pv.Sphere(radius=radius * 0.8, center=pos + np.array([0, 0, radius * 0.4]))
        lobe2 = pv.Sphere(radius=radius * 0.65, center=pos - np.array([0, 0, radius * 0.4]))
        op = self.state.options.cas9_opacity
        self._add(lobe1, color=COLOR_CAS9, opacity=op, smooth_shading=True)
        self._add(lobe2, color=_blend(COLOR_CAS9, (1, 1, 1), 0.2),
                  opacity=op, smooth_shading=True)

    def _draw_grna(self, model, stage: Stage, progress: float) -> None:
        site = self.state.site
        if site is None:
            return
        # 결합 이전에는 gRNA를 그리지 않는다
        if stage in (Stage.TARGET_SEARCH, Stage.PAM_RECOGNITION):
            return
        s, e = site.protospacer_start, site.protospacer_end
        extent = e
        if stage is Stage.BINDING:
            # 결합이 진행되며 gRNA가 프로토스페이서를 따라 확장
            extent = s + max(1, int((e - s) * progress))
        idx = np.arange(s, max(s + 2, extent))
        idx = idx[idx < model.n_bases]
        if len(idx) < 2:
            return
        # 축에서 살짝 안쪽(센스 골격 쪽)으로 치우친 경로
        pts = (model.axis[idx] + model.strand_a[idx]) / 2
        tube = pv.Spline(pts, len(pts) * 4).tube(radius=max(model.radius * 0.05, 0.4))
        self._add(tube, color=COLOR_GRNA, smooth_shading=True)

    def _draw_cut_marker(self, model, cut_index: int) -> None:
        p = model.axis_point(cut_index)
        marker = pv.Disc(center=p, inner=0, outer=model.radius * 1.4,
                         normal=(0, 0, 1), c_res=24)
        self._add(marker, color=COLOR_CUT, opacity=0.35)


def _blend(c1, c2, t: float) -> tuple[float, float, float]:
    """선형 색상 혼합."""
    return tuple(c1[i] * (1 - t) + c2[i] * t for i in range(3))
