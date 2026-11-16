"""간단한 전역 정렬(Needleman-Wunsch).

편집 전후 짧은 교육용 서열의 삽입·결실·치환을 눈으로 비교하기 위한
용도다. 수백 bp 이하 서열을 가정하며, 대규모 서열 정렬용이 아니다.
"""

from __future__ import annotations

from dataclasses import dataclass

GAP = "-"


@dataclass(frozen=True)
class Alignment:
    """정렬 결과."""

    aligned_a: str
    aligned_b: str
    score: int

    @property
    def match_line(self) -> str:
        """일치 "|", 치환 ".", 갭 " "로 이뤄진 가운데 줄."""
        out: list[str] = []
        for x, y in zip(self.aligned_a, self.aligned_b):
            if x == GAP or y == GAP:
                out.append(" ")
            elif x == y:
                out.append("|")
            else:
                out.append(".")
        return "".join(out)

    @property
    def identity(self) -> float:
        """갭을 제외한 정렬 열 중 일치 비율."""
        cols = [(x, y) for x, y in zip(self.aligned_a, self.aligned_b)
                if x != GAP and y != GAP]
        if not cols:
            return 0.0
        same = sum(1 for x, y in cols if x == y)
        return same / len(cols)


def needleman_wunsch(
    a: str,
    b: str,
    *,
    match: int = 1,
    mismatch: int = -1,
    gap: int = -2,
) -> Alignment:
    """두 서열의 전역 정렬.

    선형 갭 패널티를 쓰는 표준 동적계획법. 되추적은 진단적으로
    대각선(치환/일치)을 우선한다.
    """
    n, m = len(a), len(b)
    # 점수 행렬
    score = [[0] * (m + 1) for _ in range(n + 1)]
    for i in range(1, n + 1):
        score[i][0] = i * gap
    for j in range(1, m + 1):
        score[0][j] = j * gap
    for i in range(1, n + 1):
        ai = a[i - 1]
        for j in range(1, m + 1):
            diag = score[i - 1][j - 1] + (match if ai == b[j - 1] else mismatch)
            up = score[i - 1][j] + gap
            left = score[i][j - 1] + gap
            score[i][j] = max(diag, up, left)

    # 되추적
    ax: list[str] = []
    bx: list[str] = []
    i, j = n, m
    while i > 0 and j > 0:
        cur = score[i][j]
        diag = score[i - 1][j - 1] + (match if a[i - 1] == b[j - 1] else mismatch)
        if cur == diag:
            ax.append(a[i - 1]); bx.append(b[j - 1]); i -= 1; j -= 1
        elif cur == score[i - 1][j] + gap:
            ax.append(a[i - 1]); bx.append(GAP); i -= 1
        else:
            ax.append(GAP); bx.append(b[j - 1]); j -= 1
    while i > 0:
        ax.append(a[i - 1]); bx.append(GAP); i -= 1
    while j > 0:
        ax.append(GAP); bx.append(b[j - 1]); j -= 1

    return Alignment("".join(reversed(ax)), "".join(reversed(bx)), score[n][m])
