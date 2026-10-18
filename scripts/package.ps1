# 构建 PocketDesk-New MSI 安装包
# 用法：
#   .\scripts\package.ps1                         # 构建 ARM64 + x64
#   .\scripts\package.ps1 -Architectures ARM64    # 只构建当前架构
#   .\scripts\package.ps1 -Version 1.0.1
[CmdletBinding()]
param(
    [ValidateSet("ARM64", "x64")]
    [string[]]$Architectures = @("ARM64", "x64"),
    [string]$Version = "1.0.1",
    [string]$Output = "C:\Users\Admin1\Desktop\PocketDesk\Pack-new"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $root "installer"
$license = Join-Path $installer "License.rtf"
$wxl = Join-Path $installer "WixUI_zh-CN.wxl"
$wxs = Join-Path $installer "PocketDeskNew.wxs"

if (-not (Test-Path $license)) { throw "缺少许可证文件：$license" }
if (-not (Test-Path $wxl)) { throw "缺少中文 WiX UI 本地化文件：$wxl" }
if (-not (Test-Path $wxs)) { throw "缺少 WiX 源文件：$wxs" }

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$built = @()

function Write-InstallNotes([string]$path, [string]$arch) {
    $text = @"
PocketDesk-New $Version（$arch）
=====================================

安装位置（当前用户，无需管理员权限）：
  %LOCALAPPDATA%\Programs\PocketDesk-New

快捷方式：
  · 开始菜单 → PocketDesk-New（桌面组件层）
  · 开始菜单 → PocketDesk 设置
  · 桌面快捷方式（可在安装向导中取消勾选）

启动后：
  · 组件层会常驻在壁纸之上、普通应用窗口之下；
  · 系统托盘图标可打开设置、新建收纳盒、显示/隐藏全部组件、退出；
  · 设置界面是独立的 WinUI 3 进程，通过命名管道与宿主同步；
  · 组件插件位于安装目录的 Widgets 子目录。

用户数据（卸载默认保留）：
  %LOCALAPPDATA%\PocketDesk-New
  ├─ pocketdesk.db
  ├─ Storage\
  ├─ Logs\
  └─ backup\

卸载：
  设置 → 应用 → 已安装的应用 → PocketDesk-New → 卸载
  安装包包含的卸载自定义动作会先结束正在运行的宿主/设置进程。

说明：
  本安装包未做代码签名，SmartScreen 可能提示，请选择“更多信息 → 仍要运行”。
"@
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($true)))
}

foreach ($arch in $Architectures) {
    $platform = $arch
    $rid = if ($arch -eq "ARM64") { "win-arm64" } else { "win-x64" }
    $wixArch = if ($arch -eq "ARM64") { "arm64" } else { "x64" }
    $publishRoot = Join-Path $root "artifacts\msi\$rid\publish"

    Write-Host "== 发布 PocketDesk-New ($arch / $rid) ==" -ForegroundColor Cyan
    if (Test-Path $publishRoot) { Remove-Item -Recurse -Force $publishRoot }
    New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

    dotnet publish (Join-Path $root "src\PocketDesk.Host\PocketDesk.Host.csproj") `
        -c Release -r $rid --self-contained true -p:Platform=$platform `
        -p:DebugType=None -p:DebugSymbols=false -o $publishRoot
    if ($LASTEXITCODE -ne 0) { throw "宿主发布失败（$arch）" }

    dotnet publish (Join-Path $root "src\PocketDesk.Settings\PocketDesk.Settings.csproj") `
        -c Release -p:Platform=$platform -r $rid --self-contained true `
        -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $publishRoot "Settings")
    if ($LASTEXITCODE -ne 0) { throw "设置界面发布失败（$arch）" }

    $plugin = Join-Path $publishRoot "Widgets\PocketDesk.Widgets.Weather.dll"
    if (-not (Test-Path $plugin)) {
        Write-Host "  未找到发布时的天气插件，手动补齐..." -ForegroundColor Yellow
        dotnet build (Join-Path $root "src\PocketDesk.Widgets.Weather\PocketDesk.Widgets.Weather.csproj") -c Release | Out-Null
        $widgetDir = Join-Path $publishRoot "Widgets"
        New-Item -ItemType Directory -Force -Path $widgetDir | Out-Null
        Copy-Item (Join-Path $root "src\PocketDesk.Widgets.Weather\bin\Release\net10.0\PocketDesk.Widgets.Weather.dll") $widgetDir -Force
    }

    Get-ChildItem $publishRoot -Recurse -File -Include *.pdb,*.wixpdb | Remove-Item -Force -ErrorAction SilentlyContinue
    Write-InstallNotes (Join-Path $publishRoot "安装说明.txt") $arch

    if (-not (Test-Path (Join-Path $publishRoot "PocketDesk.Host.exe"))) { throw "发布目录缺少 PocketDesk.Host.exe" }
    if (-not (Test-Path (Join-Path $publishRoot "Settings\PocketDesk.Settings.exe"))) { throw "发布目录缺少 Settings\PocketDesk.Settings.exe" }
    if (-not (Test-Path $plugin)) { throw "发布目录缺少 Widgets\PocketDesk.Widgets.Weather.dll" }
    if (-not (Test-Path (Join-Path $publishRoot "Assets\AppIcon.ico"))) { throw "发布目录缺少 Assets\AppIcon.ico" }

    $msiName = "PocketDesk-New-$Version-$arch.msi"
    $msiPath = Join-Path $Output $msiName
    Write-Host "== 生成 MSI：$msiName ==" -ForegroundColor Cyan

    wix build $wxs -ext WixToolset.UI.wixext `
        -arch $wixArch `
        -loc $wxl `
        -d "PublishDir=$publishRoot" `
        -d "LicenseFile=$license" `
        -d "ProductVersion=$Version" `
        -pdbtype none `
        -o $msiPath
    if ($LASTEXITCODE -ne 0) { throw "WiX 生成 MSI 失败（$arch）" }

    Write-InstallNotes (Join-Path $Output "安装说明-$arch.txt") $arch
    $hash = (Get-FileHash $msiPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $built += [pscustomobject]@{ Arch = $arch; File = $msiName; Sha256 = $hash; SizeMB = [math]::Round((Get-Item $msiPath).Length / 1MB, 1) }
}

$sumLines = $built | ForEach-Object { "$($_.Sha256)  $($_.File)" }
[System.IO.File]::WriteAllLines((Join-Path $Output "SHA256SUMS.txt"), $sumLines, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "打包完成：" -ForegroundColor Green
$built | Format-Table Arch, File, SizeMB, Sha256 -AutoSize
Write-Host "输出目录：$Output"
