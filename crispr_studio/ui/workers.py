"""백그라운드 작업 스레드.

대형 구조(4OO8 등)는 파싱·결합 계산에 시간이 걸리므로 UI 스레드에서
처리하지 않는다. QThread에서 로드한 뒤 결과를 신호로 전달한다.
"""

from __future__ import annotations

from pathlib import Path

from PySide6 import QtCore

from ..bio.structure.cache import load_cached
from ..bio.structure.loader import LoadOptions
from ..bio.structure.model import Structure


class StructureLoader(QtCore.QThread):
    """구조 파일을 백그라운드에서 로드."""

    loaded = QtCore.Signal(object)       # Structure
    failed = QtCore.Signal(str)
    progress = QtCore.Signal(str)

    def __init__(self, path: str | Path, name: str | None = None,
                 options: LoadOptions | None = None, parent=None) -> None:
        super().__init__(parent)
        self._path = Path(path)
        self._name = name
        self._options = options

    def run(self) -> None:  # noqa: D401 - QThread 진입점
        try:
            self.progress.emit(f"{self._path.name} 로딩 중…")
            structure: Structure = load_cached(
                self._path, name=self._name, options=self._options)
            self.loaded.emit(structure)
        except Exception as exc:  # pragma: no cover - 런타임 오류 전달
            self.failed.emit(f"{self._path.name} 로드 실패: {exc}")
