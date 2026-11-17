#!/usr/bin/env bash
# Windows x64 실행 파일을 builds/windows/ 에 만든다.
# 필요: Godot 4.7.2 .NET 에디터, 같은 버전의 내보내기 템플릿(mono), .NET SDK 8.
# 사용법: tools/export_windows.sh [debug|release]
set -euo pipefail
cd "$(dirname "$0")/.."
MODE="${1:-debug}"
GODOT="${GODOT:-godot}"
rm -rf builds/windows
mkdir -p builds/windows
"$GODOT" --headless --path . --import >/dev/null 2>&1 || true
"$GODOT" --headless --path . "--export-$MODE" "Windows Desktop" builds/windows/PaleoSphere.exe
test -f builds/windows/PaleoSphere.exe
test -f builds/windows/PaleoSphere.pck
test -f builds/windows/data_PaleoSphere_windows_x86_64/PaleoSphere.dll
echo "완료: builds/windows/ ($(du -sh builds/windows | cut -f1))"
echo "배포할 때는 builds/windows 폴더 전체를 함께 옮긴다(EXE, PCK, data_ 폴더)."
