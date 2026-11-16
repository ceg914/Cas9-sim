"""pytest 루트 설정: 저장소 루트를 import 경로에 추가."""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
