# 启动桌面组件层
# 用法：
#   .\scripts\run-host.ps1 -Demo
#   .\scripts\run-host.ps1 -Home "$env:TEMP\pd-test" -Theme Light
#   .\scripts\run-host.ps1 -Screenshot docs\host-light.png -Theme Light
[CmdletBinding()]
param(
    [switch]$Demo,
    [string]$Home = "",
    [string]$Theme = "",
    [string]$Screenshot = "",
    [switch]$Wait
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$configuration = "Debug"
$exe = Join-Path $root "src\PocketDesk.Host\bin\$configuration\net10.0\PocketDesk.Host.exe"
if (-not (Test-Path $exe)) {
    throw "找不到宿主，请先运行 .\scripts\build.ps1。"
}

if (-not [string]::IsNullOrWhiteSpace($Home)) {
    $env:POCKETDESK_HOME = $Home
}

$arguments = @()
if ($Demo) { $arguments += "--demo" }
if (-not [string]::IsNullOrWhiteSpace($Theme)) { $arguments += @("--theme", $Theme) }
if (-not [string]::IsNullOrWhiteSpace($Screenshot)) {
    $arguments += @("--screenshot", [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Screenshot)))
    Write-Host "截图模式：$Screenshot"
}

$process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
if ($Wait -or -not [string]::IsNullOrWhiteSpace($Screenshot)) {
    $process.WaitForExit()
    Write-Host "宿主已退出，退出码 $($process.ExitCode)"
} else {
    Write-Host "宿主已启动（PID $($process.Id)）。托盘菜单可打开设置或退出。"
}
