# PocketDesk-New 构建脚本
# 用法：
#   .\scripts\build.ps1                 # Debug，自动选择 ARM64/x64
#   .\scripts\build.ps1 -Configuration Release
#   .\scripts\build.ps1 -Platform x64
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Platform = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Platform)) {
    $osArch = try { (Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).OSArchitecture } catch { $env:PROCESSOR_ARCHITECTURE };
    $Platform = if ($osArch -like "*ARM*") { "ARM64" } else { "x64" }
}

Write-Host "== 构建 PocketDesk-New ($Configuration, 检测到 $Platform) ==" -ForegroundColor Cyan
dotnet build (Join-Path $root "PocketDesk.slnx") -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== 运行数据层测试 ==" -ForegroundColor Cyan
dotnet test (Join-Path $root "tests\PocketDesk.Core.Tests\PocketDesk.Core.Tests.csproj") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$hostExe = Join-Path $root "src\PocketDesk.Host\bin\$Configuration\net10.0\PocketDesk.Host.exe"
$settingsExe = Get-ChildItem (Join-Path $root "src\PocketDesk.Settings\bin") -Recurse -Filter PocketDesk.Settings.exe -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
$plugin = Join-Path $root "src\PocketDesk.Host\bin\$Configuration\net10.0\Widgets\PocketDesk.Widgets.Weather.dll"

Write-Host ""
Write-Host "构建完成：" -ForegroundColor Green
Write-Host "  宿主      : $hostExe"
Write-Host "  设置界面  : $settingsExe"
Write-Host "  天气插件  : $plugin"
Write-Host ""
Write-Host "启动：.\scripts\run-host.ps1 -Demo"
Write-Host "验收：.\scripts\verify.ps1"
