"""CRISPR Studio — 교육용 CRISPR-Cas9 3D 유전자 편집 시뮬레이터.

이 패키지는 두 종류의 코드로 나뉜다.

* 순수 로직 계층 (``analysis``, ``bio``, ``simulation``, ``geometry``, ``storage``)
  — Qt/VTK 없이도 동작하며 단위 테스트가 가능하다.
* 표현 계층 (``render``, ``ui``) — PySide6 + PyVista 위에서 동작한다.

교육용 프로그램이라는 성격상, 모든 분석·시뮬레이션 결과는 "실제 생체 내
실험 결과가 아니라 개념 설명을 위한 예시"라는 점을 전제로 한다.
"""

__version__ = "0.1.0"
APP_NAME = "CRISPR Studio"

__all__ = ["__version__", "APP_NAME"]
