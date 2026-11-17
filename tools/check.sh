#!/usr/bin/env bash
# 빌드와 자동 점검을 한 번에 돌린다. Godot가 PATH에 없으면 엔진 점검은 건너뛴다.
# 사용법: tools/check.sh [--with-ui]   (--with-ui: 화면이 필요한 UI 점검까지. Linux에서는 xvfb-run 사용)
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== 1. 솔루션 빌드"
dotnet build PaleoSphere.sln -nologo -v q

echo "== 2. 단위·통합 테스트 (Debug, Release)"
dotnet test tests/PaleoSphere.Tests.csproj -nologo -v q
dotnet test tests/PaleoSphere.Tests.csproj -c Release -nologo -v q

GODOT="${GODOT:-godot}"
if ! command -v "$GODOT" >/dev/null 2>&1; then
  echo "== Godot 실행 파일이 없어 엔진 점검은 건너뜁니다 (GODOT 환경 변수로 경로 지정 가능)"
  exit 0
fi

echo "== 3. 엔진 안 셀프테스트 (헤드리스)"
"$GODOT" --headless --path . --import >/dev/null 2>&1 || true
"$GODOT" --headless --path . -- --selftest

if [[ "${1:-}" == "--with-ui" ]]; then
  echo "== 4. UI 통합 점검 (실제 클릭·키 입력)"
  if command -v xvfb-run >/dev/null 2>&1 && [[ -z "${DISPLAY:-}" ]]; then
    xvfb-run -a -s "-screen 0 1600x900x24" "$GODOT" --path . --resolution 1600x900 -- --uitest
  else
    "$GODOT" --path . --resolution 1600x900 -- --uitest
  fi
fi
echo "모든 점검 통과"
