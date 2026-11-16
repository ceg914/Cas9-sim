"""실제 원자 구조 렌더러(PyVista/VTK).

전문 분자 뷰어(PyMOL/ChimeraX/Mol*)를 참고한 표현을 목표로 한다.

- 원자: vtkGlyph3DMapper로 GPU 인스턴싱(수만 개도 빠르게)
- 결합: GPU 라인-튜브(양 끝 원자 색으로 분할)
- 모드: Ball-and-Stick / Space-Filling / Licorice / Cartoon / Ribbon
- 색상: 원소(CPK) / 사슬 / 성분 / 2차구조 / 도메인
- 성분 표시/숨김, 명·암 배경, MSAA, LOD, 원자 피킹, 선택 강조
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum

import numpy as np
import pyvista as pv
import vtk
from vtk.util import numpy_support as ns

from ..bio.structure import backbone as bb
from ..bio.structure.model import (
    COMP_DNA,
    COMP_HETERO,
    COMP_PROTEIN,
    COMP_RNA,
    COMP_WATER,
    Structure,
)
from . import palette


class RenderMode(str, Enum):
    BALL_STICK = "ball_stick"
    SPACE_FILLING = "space_filling"
    LICORICE = "licorice"
    CARTOON = "cartoon"
    RIBBON = "ribbon"


MODE_LABELS = {
    RenderMode.BALL_STICK: "Ball-and-Stick",
    RenderMode.SPACE_FILLING: "Space-Filling",
    RenderMode.LICORICE: "Licorice",
    RenderMode.CARTOON: "Cartoon",
    RenderMode.RIBBON: "Ribbon",
}


class ColorScheme(str, Enum):
    ELEMENT = "element"
    CHAIN = "chain"
    COMPONENT = "component"
    SSE = "sse"
    DOMAIN = "domain"


SCHEME_LABELS = {
    ColorScheme.ELEMENT: "원소 (CPK)",
    ColorScheme.CHAIN: "사슬",
    ColorScheme.COMPONENT: "성분",
    ColorScheme.SSE: "2차 구조",
    ColorScheme.DOMAIN: "도메인(Cas9)",
}

_ALL_COMPONENTS = {COMP_PROTEIN, COMP_DNA, COMP_RNA, COMP_HETERO}


@dataclass
class StructureDisplay:
    """구조 표시 설정."""

    mode: RenderMode = RenderMode.BALL_STICK
    color_scheme: ColorScheme = ColorScheme.ELEMENT
    show_components: set[int] = field(
        default_factory=lambda: set(_ALL_COMPONENTS))
    show_water: bool = False
    background: str = "dark"
    lod: str = "auto"              # auto/high/low


class StructureScene:
    """플로터 위에 실제 구조를 그린다."""

    def __init__(self, plotter: "pv.BasePlotter") -> None:
        self.plotter = plotter
        self.structure: Structure | None = None
        self.display = StructureDisplay()
        self._actors: list = []
        self._highlight_actors: list = []
        self._traces: list[bb.ChainTrace] | None = None
        self._chain_index: dict[str, int] = {}
        self._visible_idx = np.empty(0, dtype=np.int64)
        self._enable_quality()

    # --- 품질 설정(1회) ---
    def _enable_quality(self) -> None:
        try:
            self.plotter.enable_anti_aliasing("msaa", multi_samples=8)
        except Exception:  # pragma: no cover
            try:
                self.plotter.enable_anti_aliasing()
            except Exception:
                pass
        try:
            self.plotter.enable_depth_peeling(number_of_peels=4)
        except Exception:  # pragma: no cover
            pass

    # --- 구조 설정 ---
    def set_structure(self, structure: Structure,
                      display: StructureDisplay | None = None) -> None:
        self.structure = structure
        self._traces = None
        self._chain_index = {ch.chain_id: i
                             for i, ch in enumerate(structure.chains)}
        if display is not None:
            self.display = display
        self.rebuild(reset_camera=True)

    def set_display(self, display: StructureDisplay) -> None:
        self.display = display
        self.set_background(display.background)
        self.rebuild(reset_camera=False)

    def set_background(self, name: str) -> None:
        self.display.background = name
        try:
            self.plotter.set_background(palette.BACKGROUNDS.get(name,
                                        palette.BACKGROUNDS["dark"]))
        except Exception:  # pragma: no cover
            pass

    def clear(self) -> None:
        for a in self._actors + self._highlight_actors:
            try:
                self.plotter.remove_actor(a, render=False)
            except Exception:  # pragma: no cover
                pass
        self._actors = []
        self._highlight_actors = []

    # --- 메인 빌드 ---
    def rebuild(self, *, reset_camera: bool = False) -> None:
        if self.structure is None:
            return
        self.clear()
        self.set_background(self.display.background)

        mode = self.display.mode
        if mode in (RenderMode.CARTOON, RenderMode.RIBBON):
            self._build_cartoon(mode)
            # 카툰에서도 리간드/이온은 막대로 함께 표시
            self._build_atoms_bonds(
                only_components={COMP_HETERO}, mode=RenderMode.BALL_STICK)
        else:
            self._build_atoms_bonds(mode=mode)

        if reset_camera:
            self.reset_camera()
        self._render()

    def _visible_mask(self, only_components: set[int] | None = None) -> np.ndarray:
        s = self.structure
        comps = set(self.display.show_components)
        if self.display.show_water:
            comps = comps | {COMP_WATER}
        if only_components is not None:
            comps = comps & only_components
        mask = np.isin(s.components, list(comps)) if comps else np.zeros(s.n_atoms, bool)
        return mask

    def _sphere_res(self, n: int) -> int:
        if self.display.lod == "high":
            return 24
        if self.display.lod == "low":
            return 8
        # auto
        if n > 15000:
            return 8
        if n > 5000:
            return 12
        if n > 1000:
            return 16
        return 24

    # --- 올-원자 모드 ---
    def _build_atoms_bonds(self, *, mode: RenderMode,
                           only_components: set[int] | None = None) -> None:
        s = self.structure
        mask = self._visible_mask(only_components)
        idx = np.where(mask)[0]
        if len(idx) == 0:
            return
        self._visible_idx = idx if only_components is None else self._visible_idx

        colors = self._atom_colors(idx)

        if mode is RenderMode.SPACE_FILLING:
            radii = s.vdw_radii()[idx]
            self._add_atom_glyph(s.coords[idx], radii, colors,
                                 self._sphere_res(len(idx)))
            return

        # Ball-and-Stick / Licorice
        if mode is RenderMode.LICORICE:
            atom_scale, stick_w = 0.28, 9
        else:  # BALL_STICK
            atom_scale = 0.32
            stick_w = 5
        radii = np.full(len(idx), atom_scale, np.float32)
        # 원소별 약간의 크기 차(교육적 가독성)
        radii = s.covalent_radii()[idx] * (0.9 if mode is RenderMode.LICORICE else 0.42)
        self._add_atom_glyph(s.coords[idx], radii, colors,
                             self._sphere_res(len(idx)))
        self._add_bonds(mask, stick_w)

    def _add_atom_glyph(self, coords, radii, colors_rgb, res) -> None:
        poly = vtk.vtkPolyData()
        pts = vtk.vtkPoints()
        pts.SetData(ns.numpy_to_vtk(np.ascontiguousarray(coords, np.float32)))
        poly.SetPoints(pts)
        r = ns.numpy_to_vtk(np.ascontiguousarray(radii, np.float32))
        r.SetName("radii")
        poly.GetPointData().AddArray(r)
        c = ns.numpy_to_vtk(
            np.ascontiguousarray((colors_rgb * 255).astype(np.uint8)))
        c.SetName("colors")
        poly.GetPointData().AddArray(c)

        sph = vtk.vtkSphereSource()
        sph.SetThetaResolution(res)
        sph.SetPhiResolution(res)
        sph.SetRadius(1.0)

        mapper = vtk.vtkGlyph3DMapper()
        mapper.SetInputData(poly)
        mapper.SetSourceConnection(sph.GetOutputPort())
        mapper.SetScaleArray("radii")
        mapper.SetScaleModeToScaleByMagnitude()
        mapper.SetScaleFactor(1.0)
        mapper.SetScalarModeToUsePointFieldData()
        mapper.SelectColorArray("colors")
        mapper.SetColorModeToDirectScalars()

        actor = vtk.vtkActor()
        actor.SetMapper(mapper)
        prop = actor.GetProperty()
        prop.SetInterpolationToPhong()
        prop.SetAmbient(0.22)
        prop.SetDiffuse(0.85)
        prop.SetSpecular(0.35)
        prop.SetSpecularPower(28)
        self.plotter.add_actor(actor)
        self._actors.append(actor)

    def _add_bonds(self, atom_mask: np.ndarray, width: int) -> None:
        s = self.structure
        b = s.bonds
        if len(b) == 0:
            return
        both = atom_mask[b[:, 0]] & atom_mask[b[:, 1]]
        b = b[both]
        if len(b) == 0:
            return
        npts = len(b) * 2
        pts = np.empty((npts, 3), np.float32)
        pts[0::2] = s.coords[b[:, 0]]
        pts[1::2] = s.coords[b[:, 1]]
        col = np.empty((npts, 3), np.float32)
        atom_cols = self._atom_colors(np.arange(s.n_atoms))
        col[0::2] = atom_cols[b[:, 0]]
        col[1::2] = atom_cols[b[:, 1]]
        lines = np.hstack(
            [np.full((len(b), 1), 2, np.int64),
             np.arange(npts).reshape(-1, 2)]).ravel()
        mesh = pv.PolyData(pts, lines=lines)
        mesh.point_data["rgb"] = (col * 255).astype(np.uint8)
        actor = self.plotter.add_mesh(
            mesh, scalars="rgb", rgb=True, line_width=width,
            render_lines_as_tubes=True, lighting=True)
        self._actors.append(actor)

    # --- 색상 계산 ---
    def _atom_colors(self, idx: np.ndarray) -> np.ndarray:
        s = self.structure
        scheme = self.display.color_scheme
        if scheme is ColorScheme.ELEMENT:
            return s.element_colors()[idx]
        out = np.empty((len(idx), 3), np.float32)
        if scheme is ColorScheme.CHAIN:
            for k, ai in enumerate(idx):
                out[k] = palette.chain_color(
                    self._chain_index.get(s.chain_ids[ai], 0))
        elif scheme is ColorScheme.COMPONENT:
            for k, ai in enumerate(idx):
                out[k] = palette.component_color(int(s.components[ai]))
        elif scheme is ColorScheme.DOMAIN:
            for k, ai in enumerate(idx):
                if s.components[ai] == COMP_PROTEIN:
                    out[k] = palette.cas9_domain(int(s.resids[ai]))[1]
                else:
                    out[k] = palette.component_color(int(s.components[ai]))
        else:  # SSE — 올원자에서는 사슬색으로 대체
            for k, ai in enumerate(idx):
                out[k] = palette.chain_color(
                    self._chain_index.get(s.chain_ids[ai], 0))
        return out

    # --- 카툰/리본 ---
    def _get_traces(self) -> list[bb.ChainTrace]:
        if self._traces is None:
            self._traces = bb.extract_traces(self.structure)
        return self._traces

    def _build_cartoon(self, mode: RenderMode) -> None:
        comps = set(self.display.show_components)
        radius = 0.45 if mode is RenderMode.CARTOON else 0.28
        for trace in self._get_traces():
            if trace.component not in comps or trace.coords.shape[0] < 2:
                continue
            colors = self._trace_colors(trace)
            self._add_backbone_tube(trace.coords, colors, radius, mode)

    def _trace_colors(self, trace: bb.ChainTrace) -> np.ndarray:
        scheme = self.display.color_scheme
        n = trace.coords.shape[0]
        out = np.empty((n, 3), np.float32)
        ci = self._chain_index.get(trace.chain_id, 0)
        for i in range(n):
            if scheme is ColorScheme.SSE and trace.component == COMP_PROTEIN and trace.sse:
                out[i] = palette.sse_color(trace.sse[i])
            elif scheme is ColorScheme.DOMAIN and trace.component == COMP_PROTEIN:
                out[i] = palette.cas9_domain(trace.resids[i])[1]
            elif scheme is ColorScheme.COMPONENT:
                out[i] = palette.component_color(trace.component)
            elif scheme is ColorScheme.ELEMENT:
                # 원소 색은 백본에 의미가 약함 → 성분색 사용
                out[i] = palette.component_color(trace.component)
            else:
                out[i] = palette.chain_color(ci)
        return out

    def _add_backbone_tube(self, coords, colors, radius, mode) -> None:
        n = coords.shape[0]
        if n < 2:
            return
        # 부드러운 스플라인으로 업샘플, 색은 최근접 잔기에서
        n_spline = min(max(n * 6, 8), 6000)
        spline = pv.Spline(coords, n_spline)
        sp = spline.points
        # 각 스플라인 점 -> 원본 잔기 비율로 색 인덱스
        ratio = np.linspace(0, n - 1, len(sp))
        ci = np.clip(np.round(ratio).astype(int), 0, n - 1)
        rgb = (colors[ci] * 255).astype(np.uint8)
        spline.point_data["rgb"] = rgb
        tube = spline.tube(radius=radius, n_sides=12)
        actor = self.plotter.add_mesh(
            tube, scalars="rgb", rgb=True, smooth_shading=True,
            specular=0.3, specular_power=20)
        self._actors.append(actor)

    # --- 피킹/선택 ---
    def pick_atom(self, point) -> int | None:
        s = self.structure
        if s is None or point is None or s.n_atoms == 0:
            return None
        p = np.asarray(point, dtype=np.float32)
        # 보이는 원자 우선
        idx = self._visible_idx if len(self._visible_idx) else np.arange(s.n_atoms)
        d = np.linalg.norm(s.coords[idx] - p, axis=1)
        return int(idx[int(d.argmin())])

    def highlight_atoms(self, indices: list[int]) -> None:
        for a in self._highlight_actors:
            try:
                self.plotter.remove_actor(a, render=False)
            except Exception:  # pragma: no cover
                pass
        self._highlight_actors = []
        s = self.structure
        if s is None or not indices:
            self._render()
            return
        pts = s.coords[np.asarray(indices)]
        radii = s.vdw_radii()[np.asarray(indices)] * 1.15
        poly = pv.PolyData(pts)
        poly["r"] = radii
        glyph = poly.glyph(geom=pv.Sphere(theta_resolution=16, phi_resolution=16),
                           scale="r", factor=1.0, orient=False)
        actor = self.plotter.add_mesh(glyph, color=(1.0, 1.0, 0.3),
                                      opacity=0.45, smooth_shading=True)
        self._highlight_actors.append(actor)
        self._render()

    def residue_atom_indices(self, chain_id: str, resid: int) -> list[int]:
        s = self.structure
        if s is None:
            return []
        for res in s.residues:
            if res.chain_id == chain_id and res.resid == resid:
                return list(res.atom_indices)
        return []

    # --- 카메라/출력 ---
    def reset_camera(self) -> None:
        """주성분(PCA)으로 길쭉한 분자를 보기 좋게 프레이밍."""
        s = self.structure
        try:
            if s is not None and s.n_atoms >= 3:
                c = s.center()
                cov = np.cov((s.coords - c).T)
                evals, evecs = np.linalg.eigh(cov)
                long_axis = evecs[:, 2]      # 분산 최대 축
                view_axis = evecs[:, 0]      # 분산 최소 축(정면)
                cam = self.plotter.camera
                cam.focal_point = tuple(c)
                cam.up = tuple(long_axis)
                cam.position = tuple(c + view_axis)
            self.plotter.reset_camera()
            self.plotter.camera.azimuth = 15
            self.plotter.reset_camera()
        except Exception:  # pragma: no cover
            try:
                self.plotter.reset_camera()
            except Exception:
                pass

    def screenshot(self, path: str, scale: int = 2) -> None:
        try:
            self.plotter.screenshot(path, scale=scale)
        except TypeError:  # 일부 백엔드는 scale 미지원
            self.plotter.screenshot(path)

    def _render(self) -> None:
        try:
            self.plotter.render()
        except Exception:  # pragma: no cover
            pass
