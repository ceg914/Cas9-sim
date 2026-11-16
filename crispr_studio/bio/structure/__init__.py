"""실제 분자 구조(PDB/mmCIF) 처리 계층.

Biopython으로 좌표·원소·잔기·사슬을 파싱하고, 거리 기반으로 공유 결합을
추정해 렌더링에 바로 쓸 수 있는 평면(numpy) 모델로 변환한다.
"""

from .model import Atom, Chain, Residue, Structure  # noqa: F401
from .loader import load_structure, LoadOptions  # noqa: F401
