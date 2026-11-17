# 라이선스 기록

## 이 저장소의 코드와 데이터

저장소 소유자가 아직 라이선스를 정하지 않았습니다. 공개 배포 전에 정해야 합니다.

## 외부 에셋

**없습니다.** 3D 모델, 텍스처, 애니메이션, 음향, 글꼴 파일을 하나도 싣지 않았습니다.

| 항목 | 출처 | 비고 |
|---|---|---|
| 개체·나무 도형 | 엔진 기본 도형(캡슐, 원뿔, 원기둥, 상자)을 코드로 조합 | `src/Presentation/` |
| 지형 색·식생 표시 | 직접 작성한 셰이더 | `assets/shaders/terrain.gdshader` |
| 아이콘 | 직접 작성한 SVG | `icon.svg` |
| 글꼴 | 운영체제에 설치된 글꼴을 실행 중에 불러 씀(Windows: 맑은 고딕). 파일을 배포하지 않음 | `src/UI/UiTheme.cs` |

모델·음향을 추가할 때는 아래 표에 원본 링크, 제작자, 라이선스, 수정 여부를 적고, 사용 권리가 확인되지 않은 자산은 넣지 않습니다.

| 파일 | 원본 링크 | 제작자 | 라이선스 | 수정 여부 |
|---|---|---|---|---|
| (아직 없음) | | | | |

## 엔진과 라이브러리

| 이름 | 버전 | 라이선스 | 쓰임 |
|---|---|---|---|
| Godot Engine (.NET) | 4.7.2 stable | MIT | 엔진, 내보내기 템플릿 |
| GodotSharp / Godot.NET.Sdk | 4.7.2 | MIT | C# 바인딩(NuGet) |
| .NET 런타임 | 8.0 | MIT | 실행 파일에 함께 들어감(self-contained) |
| xunit | 2.9.3 | Apache-2.0 | 테스트 전용 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 | 테스트 전용 |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT | 테스트 전용 |

Godot로 만든 실행 파일을 배포할 때는 Godot의 라이선스 고지와 엔진이 포함한 제3자 구성 요소의 고지를 함께 실어야 합니다(<https://docs.godotengine.org/en/stable/about/complying_with_licenses.html>). 현재 빌드에는 이 고지 화면이 없으므로 **배포 전에 추가해야 합니다.**

## 종 정보의 출처

종 정의 파일(`data/species/*.json`)의 `sources` 항목에 적습니다. 체격 수치처럼 2차 자료를 종합한 값은 그 사실을 함께 적었습니다.
