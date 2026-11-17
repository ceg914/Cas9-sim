# Windows에서 실행 파일을 만드는 스크립트.
# 필요: Godot 4.7.2 .NET 에디터(콘솔 버전), 내보내기 템플릿(mono), .NET SDK 8.
# 사용법: powershell -ExecutionPolicy Bypass -File tools\export_windows.ps1 -Godot "C:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe"
param(
    [Parameter(Mandatory = $true)][string]$Godot,
    [ValidateSet("debug", "release")][string]$Mode = "debug"
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
if (Test-Path builds\windows) { Remove-Item builds\windows -Recurse -Force }
New-Item -ItemType Directory -Path builds\windows | Out-Null
& $Godot --headless --path . --import | Out-Null
& $Godot --headless --path . "--export-$Mode" "Windows Desktop" builds\windows\PaleoSphere.exe
if (-not (Test-Path builds\windows\PaleoSphere.exe)) { throw "내보내기에 실패했습니다." }
Write-Host "완료: builds\windows"
Write-Host "점검: builds\windows\PaleoSphere.console.exe --headless -- --selftest"
