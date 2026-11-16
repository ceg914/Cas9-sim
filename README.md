# CRISPR Studio

교육용 CRISPR-Cas9 3D 유전자 편집 시뮬레이터. 분자 구조를 3차원으로
관찰하고, DNA 서열 변화와 그 영향을 분석하는 데스크톱 프로그램이다.

두 가지 작업 모드를 하나의 3D 뷰포트에서 제공한다.

- **교육용 시뮬레이션** — 단순화된 모형으로 표적 탐색부터 DNA 복구까지
  편집 과정을 단계별 애니메이션으로 보여주고, 편집 전후 서열·단백질 영향을
  분석한다.
- **실제 구조** — RCSB PDB의 실제 원자 좌표(1BNA, 4OO8)를 불러와 전문
  분자 뷰어 수준으로 렌더링한다.

두 표현은 명확히 구분된다. 애니메이션은 개념 설명용 연출이고, 구조 뷰는
결정구조의 실제 좌표다.

## 주요 기능

### 실제 구조 렌더링

- PDB / mmCIF 파싱(Biopython): 원자 좌표·원소·잔기·사슬
- 원자 간 거리 기반 공유 결합 추정
- 렌더링 모드 5종
  - **Ball-and-Stick** — 원자와 결합
  - **Space-Filling** — 반데르발스 반지름 기반 CPK 모형
  - **Licorice** — 결합 중심 표현
  - **Cartoon** — 단백질/핵산 백본
  - **Ribbon** — 가는 백본 리본
- 색상 체계: 원소(CPK) · 사슬 · 성분 · 2차 구조 · Cas9 도메인
- 단백질 2차 구조(알파 나선·베타 병풍·루프) phi/psi 추정
- 성분별 표시/숨김(단백질·DNA·RNA·물·리간드)
- 원자 클릭 선택 → 원소·잔기·사슬·좌표·도메인 정보
- 어두운/밝은 배경, MSAA 안티앨리어싱, 고해상도 스크린샷
- VTK Glyph 인스턴싱으로 2만 개 이상 원자도 실시간 렌더링
- 대형 구조는 백그라운드 스레드에서 파싱(UI 비차단), 결과 캐싱

### 교육용 시뮬레이션

- 단계별 애니메이션: 표적 탐색 → PAM 인식 → 결합 → 절단 → 복구
- 재생/일시정지, 처음/끝, 이전/다음 단계, 0.25~4배속, 타임라인 스크러빙
- DNA 이중나선 교육용 모형, Cas9·gRNA 표현, 절단 간격 연출
- 사례 라이브러리(코딩/비코딩/Cas12a), Cas 변이체·PAM 표적 선택

### 서열·편집 분석

- FASTA 불러오기, 서열 직접 입력, 입력 검증
- 염기별 색상, 좌표 눈금, 상보 가닥, 표적 구간 강조
- 3D 선택과 서열 뷰어 동기화
- 편집 전후 전역 정렬(Needleman-Wunsch), 삽입·결실·치환 추출
- 단백질 영향 분류: 동의·미스센스·넌센스·프레임시프트·틀유지 indel
- NHEJ / HDR 교육용 복구 결과 예시
- CSV · JSON · 텍스트 보고서 내보내기

### 프로젝트

- 서열·사례·표적·카메라·타임라인 상태 저장/복원(JSON)

## 설치

```bash
pip install -r requirements.txt
```

Linux에서 오프스크린 렌더링에는 시스템 OpenGL 라이브러리가 필요하다.

```bash
sudo apt-get install -y libegl1 libopengl0 libgl1-mesa-dri libglx-mesa0 \
    libxkbcommon0 libxcb-cursor0
```

## 실행

```bash
python run.py
```

## 예시 구조 데이터

오프라인 동작을 위해 두 구조를 `assets/pdb/`에 번들로 포함한다.

| PDB ID | 내용 | 원자 수 |
|---|---|---|
| 1BNA | B-DNA 도데카머(Drew–Dickerson) | 566 |
| 4OO8 | SpCas9–sgRNA–표적 DNA 복합체 | 24,153 |

출처: [RCSB PDB](https://www.rcsb.org/). PDB·mmCIF 두 형식을 모두 포함한다.

## 프로젝트 구조

```
crispr_studio/
├── analysis/        서열 처리·번역·정렬·변이 분류
├── bio/
│   ├── codon_table.py, examples.py   코돈표·교육용 사례
│   └── structure/   PDB/mmCIF 로더·결합탐지·백본·캐시
├── simulation/      단계 상태기계·복구 예시·Cas 변이체·타게팅
├── geometry/        교육용 이중나선 좌표
├── render/          CrisprScene(교육용)·StructureScene(실제구조)·뷰포트
├── ui/              메인 창·패널·타임라인·서열 뷰어·스레드 로더
├── storage/         프로젝트 저장·내보내기
└── app.py           진입점
```

렌더링 계층(`render`, `ui`)과 순수 로직 계층(그 외)을 분리한다. 로직
계층은 Qt·VTK 없이 단위 테스트가 가능하다.

## 테스트

```bash
# 코어·구조 로직(디스플레이 불필요)
pytest

# GUI 스모크(디스플레이/GL 필요)
CRISPR_GUI_TEST=1 xvfb-run -a pytest tests/test_gui_smoke.py
```

## 과학적 한계

- 교육용 애니메이션은 실제 원자 수준 분자동역학이 아니라 개념 설명용
  연출이다.
- 편집 결과 분석은 주어진 두 서열의 결정론적 비교이며, 실제 세포의 편집
  성공률·확률·표현형 예측이 아니다.
- 실제 구조 뷰는 공개 좌표를 그대로 표시한다. 결합은 원자 간 거리로
  추정하므로 일부 비표준 결합은 다를 수 있다.
- 2차 구조·도메인 색상은 DSSP 없이 좌표로 추정한 교육용 근사다.

## 기술 스택

PySide6 · PyVista · VTK · Biopython · NumPy
