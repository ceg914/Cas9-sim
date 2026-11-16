"""번들된 예시 구조 레지스트리.

로컬 assets/pdb에 보관한 구조를 가리켜 오프라인에서도 로드할 수 있게 한다.
"""

from __future__ import annotations

import sys
from dataclasses import dataclass, field
from pathlib import Path


def assets_dir() -> Path:
    """구조 파일이 있는 디렉터리를 찾는다(개발/패키징 모두 대응)."""
    candidates = []
    # PyInstaller 번들
    if hasattr(sys, "_MEIPASS"):  # pragma: no cover
        candidates.append(Path(sys._MEIPASS) / "assets" / "pdb")
    # 저장소 루트(crispr_studio/bio/structure/examples.py -> parents[3])
    here = Path(__file__).resolve()
    candidates.append(here.parents[3] / "assets" / "pdb")
    candidates.append(Path.cwd() / "assets" / "pdb")
    for c in candidates:
        if c.exists():
            return c
    return candidates[0]


@dataclass
class StructureExample:
    """예시 구조 메타데이터."""

    pdb_id: str
    name: str
    description: str
    filename: str
    default_mode: str = "cartoon"
    # 4OO8용 도메인 색상 등 추가 메타
    domain_hints: dict = field(default_factory=dict)

    def path(self, prefer: str = "pdb") -> Path:
        base = assets_dir()
        stem = Path(self.filename).stem
        # 선호 포맷이 있으면 그걸 우선
        pref = base / f"{stem}.{prefer}"
        if pref.exists():
            return pref
        return base / self.filename


EXAMPLES: list[StructureExample] = [
    StructureExample(
        pdb_id="1BNA",
        name="B-DNA 도데카머 (Drew–Dickerson)",
        description="고전적인 B형 DNA 이중나선 결정구조. 12개 염기쌍, "
                    "실제 당-인산 골격과 염기 원자 좌표를 담고 있다.",
        filename="1BNA.pdb",
        default_mode="ball_stick",
    ),
    StructureExample(
        pdb_id="4OO8",
        name="SpCas9–sgRNA–표적 DNA 복합체",
        description="SpCas9 단백질이 가이드 RNA, 표적 DNA와 결합한 복합체 "
                    "결정구조. 단백질·RNA·DNA가 한 구조에 모두 들어 있다.",
        filename="4OO8.pdb",
        default_mode="cartoon",
    ),
]


def get_example(pdb_id: str) -> StructureExample | None:
    for e in EXAMPLES:
        if e.pdb_id.upper() == pdb_id.upper():
            return e
    return None
