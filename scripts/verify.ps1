# 端到端验收：真实启动宿主，检查窗口样式、命中测试、焦点策略、IPC 与插件扩展。
[CmdletBinding()]
param(
    [string]$Platform = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Platform)) {
    $osArch = try { (Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).OSArchitecture } catch { $env:PROCESSOR_ARCHITECTURE };
    $Platform = if ($osArch -like "*ARM*") { "ARM64" } else { "x64" }
}

dotnet build (Join-Path $root "src\PocketDesk.Host\PocketDesk.Host.csproj") -c Debug --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build (Join-Path $root "tests\PocketDesk.Verification\PocketDesk.Verification.csproj") -c Debug --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$hostExe = Join-Path $root "src\PocketDesk.Host\bin\Debug\net10.0\PocketDesk.Host.exe"
dotnet run --project (Join-Path $root "tests\PocketDesk.Verification\PocketDesk.Verification.csproj") --no-build -- $hostExe
exit $LASTEXITCODE
