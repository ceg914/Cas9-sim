"""프로젝트 상태 직렬화(JSON).

카메라 위치, 분자 표시 설정, 선택한 서열·사례·표적 부위, 타임라인 위치
등을 하나의 JSON으로 저장/복원한다. GUI에 의존하지 않으므로 테스트가
가능하다.
"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path

PROJECT_VERSION = 1


@dataclass
class ProjectState:
    """저장 가능한 프로젝트 상태."""

    name: str = "제목 없는 프로젝트"
    version: int = PROJECT_VERSION
    sequence: str = ""
    case_id: str | None = None
    cas_name: str = "SpCas9"
    pam_start: int | None = None
    frame: int = 0
    is_coding: bool = True
    selected_outcome: str | None = None
    timeline_time: float = 0.0
    camera: dict | None = None          # {position, focal_point, up}
    display: dict = field(default_factory=dict)  # 표시/투명도/색상 설정
    notes: str = ""

    def to_dict(self) -> dict:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict) -> "ProjectState":
        known = {f for f in cls.__dataclass_fields__}  # type: ignore[attr-defined]
        filtered = {k: v for k, v in data.items() if k in known}
        return cls(**filtered)


class ProjectError(Exception):
    """프로젝트 입출력 오류."""


def save_project(state: ProjectState, path: str | Path) -> None:
    """프로젝트를 JSON 파일로 저장."""
    path = Path(path)
    try:
        path.write_text(
            json.dumps(state.to_dict(), ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
    except OSError as exc:  # pragma: no cover - 환경 의존
        raise ProjectError(f"저장 실패: {exc}") from exc


def load_project(path: str | Path) -> ProjectState:
    """JSON 파일에서 프로젝트 복원."""
    path = Path(path)
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ProjectError(f"불러오기 실패: {exc}") from exc
    if not isinstance(data, dict):
        raise ProjectError("프로젝트 파일 형식이 올바르지 않습니다.")
    version = data.get("version", 0)
    if version > PROJECT_VERSION:
        raise ProjectError(
            f"이 파일은 더 새로운 버전(v{version})입니다. "
            f"현재 지원 버전은 v{PROJECT_VERSION}입니다."
        )
    return ProjectState.from_dict(data)
