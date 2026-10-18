# 发布打包（框架依赖 .NET 10）
# 用法：.\scripts\publish.ps1 -Platform ARM64
# 输出：artifacts\publish\win-arm64\
#   PocketDesk.Host.exe
#   Settings\PocketDesk.Settings.exe
#   Widgets\PocketDesk.Widgets.Weather.dll
[CmdletBinding()]
param(
    [string]$Platform = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Platform)) {
    $osArch = try { (Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).OSArchitecture } catch { $env:PROCESSOR_ARCHITECTURE };
    $Platform = if ($osArch -like "*ARM*") { "ARM64" } else { "x64" }
}

$rid = if ($Platform -eq "ARM64") { "win-arm64" } else { "win-x64" }
$out = Join-Path $root "artifacts\publish\$rid"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host "== 发布宿主 ==" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\PocketDesk.Host\PocketDesk.Host.csproj") `
    -c $Configuration -r $rid --self-contained false -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== 发布 WinUI 3 设置界面 ==" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\PocketDesk.Settings\PocketDesk.Settings.csproj") `
    -c $Configuration -p:Platform=$Platform -r $rid --self-contained false -o (Join-Path $out "Settings")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$plugin = Get-ChildItem (Join-Path $root "src\PocketDesk.Widgets.Weather\bin\$Configuration") -Recurse -Filter "PocketDesk.Widgets.Weather.dll" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
$widgets = Join-Path $out "Widgets"
New-Item -ItemType Directory -Force -Path $widgets | Out-Null
Copy-Item $plugin $widgets -Force

Write-Host ""
Write-Host "发布完成：$out" -ForegroundColor Green
Write-Host "运行：$out\PocketDesk.Host.exe --demo"
Write-Host "说明：目标机需要安装 .NET 10 Desktop Runtime。"
