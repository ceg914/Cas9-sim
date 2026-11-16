"""CRISPR-Cas9 편집 과정의 단계 상태기계.

5단계(표적 탐색 → PAM 인식 → 결합/구조 변화 → 절단 → 복구)를 하나의
연속 타임라인 위에 배치한다. 각 단계는 길이 1.0을 차지하므로 전체
타임라인은 ``[0, n]`` 범위다. 재생/일시정지와 실제 타이머 구동은 UI가
담당하고, 이 모듈은 '현재 시각 → 단계/진행률' 매핑만 제공한다.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import IntEnum


class Stage(IntEnum):
    TARGET_SEARCH = 0   # 표적 탐색
    PAM_RECOGNITION = 1 # PAM 인식
    BINDING = 2         # 결합 및 구조 변화
    CLEAVAGE = 3        # DNA 절단
    REPAIR = 4          # DNA 복구


@dataclass(frozen=True)
class StageInfo:
    """한 단계의 표시 정보."""

    stage: Stage
    title: str
    subtitle: str
    description: str
    highlight: str   # 3D에서 강조할 대상 힌트


STAGE_INFO: dict[Stage, StageInfo] = {
    Stage.TARGET_SEARCH: StageInfo(
        Stage.TARGET_SEARCH,
        "표적 탐색",
        "Cas9–gRNA 복합체가 DNA를 훑는다",
        "Cas9 단백질과 가이드 RNA가 결합한 복합체가 DNA를 따라 이동하며 "
        "PAM 서열을 찾는 과정을 개념적으로 표현한다.",
        "complex",
    ),
    Stage.PAM_RECOGNITION: StageInfo(
        Stage.PAM_RECOGNITION,
        "PAM 인식",
        "짧은 PAM 서열을 확인한다",
        "Cas9이 표적 하류(또는 상류)의 PAM을 인식한다. PAM이 없으면 절단이 "
        "일어나지 않는다. 변이체마다 인식하는 PAM이 다르다.",
        "pam",
    ),
    Stage.BINDING: StageInfo(
        Stage.BINDING,
        "결합 및 구조 변화",
        "gRNA가 표적 가닥과 염기쌍을 이룬다",
        "DNA 두 가닥이 풀리고 가이드 RNA가 표적 가닥과 상보 결합(R-loop)을 "
        "형성한다. 일치도가 높을수록 안정적으로 결합한다.",
        "rloop",
    ),
    Stage.CLEAVAGE: StageInfo(
        Stage.CLEAVAGE,
        "DNA 절단",
        "두 가닥 절단(DSB)이 일어난다",
        "Cas9의 두 뉴클레아제 도메인이 각 가닥을 잘라 이중가닥 절단을 "
        "만든다. 절단 위치는 PAM을 기준으로 결정된다.",
        "cut",
    ),
    Stage.REPAIR: StageInfo(
        Stage.REPAIR,
        "DNA 복구",
        "세포가 절단을 복구한다",
        "절단된 DNA는 NHEJ 또는 HDR 경로로 복구된다. 경로와 조건에 따라 "
        "삽입·결실·치환 등 다양한 결과가 나올 수 있다.",
        "repair",
    ),
}

STAGE_ORDER: list[Stage] = list(Stage)


class SimulationTimeline:
    """단계들을 연속 시각으로 다루는 타임라인."""

    def __init__(self) -> None:
        self._time: float = 0.0
        self.n_stages: int = len(STAGE_ORDER)

    # --- 상태 조회 ---
    @property
    def duration(self) -> float:
        """전체 길이(단계 수)."""
        return float(self.n_stages)

    @property
    def time(self) -> float:
        return self._time

    @property
    def current_stage(self) -> Stage:
        idx = min(int(self._time), self.n_stages - 1)
        return STAGE_ORDER[idx]

    @property
    def current_info(self) -> StageInfo:
        return STAGE_INFO[self.current_stage]

    @property
    def stage_progress(self) -> float:
        """현재 단계 내 진행률 0.0~1.0."""
        frac = self._time - int(self._time)
        if int(self._time) >= self.n_stages:
            return 1.0
        return min(max(frac, 0.0), 1.0)

    @property
    def overall_progress(self) -> float:
        """전체 진행률 0.0~1.0."""
        return min(max(self._time / self.duration, 0.0), 1.0)

    @property
    def is_finished(self) -> bool:
        return self._time >= self.duration

    # --- 상태 변경 ---
    def set_time(self, t: float) -> None:
        self._time = min(max(t, 0.0), self.duration)

    def set_overall_progress(self, p: float) -> None:
        self.set_time(min(max(p, 0.0), 1.0) * self.duration)

    def advance(self, dt: float) -> None:
        """dt(단계 단위)만큼 전진. 끝에서 멈춘다."""
        self.set_time(self._time + dt)

    def restart(self) -> None:
        self._time = 0.0

    def go_to_end(self) -> None:
        self._time = self.duration

    def go_to_stage(self, stage: Stage) -> None:
        self.set_time(float(int(stage)))

    def next_stage(self) -> None:
        self.go_to_stage(Stage(min(int(self.current_stage) + 1,
                                   self.n_stages - 1)))

    def prev_stage(self) -> None:
        # 단계 중간이면 현재 단계의 시작으로, 시작 지점이면 이전 단계로
        start = float(int(self.current_stage))
        if self._time - start > 1e-6:
            self.set_time(start)
        else:
            self.go_to_stage(Stage(max(int(self.current_stage) - 1, 0)))
