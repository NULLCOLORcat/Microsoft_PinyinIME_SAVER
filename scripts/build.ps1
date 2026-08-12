# =============================================================================
# WinSpaceImeToggle 构建脚本
# -----------------------------------------------------------------------------
# 职责：
#   1. 编译契约程序集（Contracts），供主程序与各模块共同引用；
#   2. 编译主程序（不引用任何具体模块，只引用契约）；
#   3. 编译各功能模块（src/WinSpaceImeToggle.Modules 下每个子文件夹一个 DLL）；
#   4. 可选：编译命令行调试版（-Test）；
#   5. 可选：刷新 Export 发布包并压缩为 zip（-Package）。
#
# 用法：
#   .\scripts\build.ps1              # 仅构建（契约 + 主程序 + 模块）
#   .\scripts\build.ps1 -Test        # 额外编译 WinSpaceImeToggle.test.exe
#   .\scripts\build.ps1 -Package     # 构建并打包 Export\WinSpaceImeToggle.zip
#
# 目录约定（GitHub 风格布局）：
#   scripts\                         本构建脚本
#   src\WinSpaceImeToggle\           主程序源码（WinSpaceImeToggle 命名空间）
#   src\WinSpaceImeToggle.Contracts\ 模块契约源码
#   src\WinSpaceImeToggle.Modules\   各功能模块源码（Ime、ProxyFix ...）
#   assets\                          运行时资源（字体、图标），构建时复制进 artifacts
#   Languages\                       多语言文件（语言代码命名 .ini），构建时复制进 artifacts
#   artifacts\                       构建产物（exe / dll / modules / assets）
#   Export\                          打包输出目录
# =============================================================================

param(
    [switch]$Test,      # 是否编译命令行调试版（WinSpaceImeToggle.test.exe）
    [switch]$Package    # 是否打包 Export\WinSpaceImeToggle.zip
)
$ErrorActionPreference = "Stop"

# 项目根目录：脚本位于 scripts\ 下，上一级即项目根
$root = Split-Path $PSScriptRoot -Parent

# ---------------------------------------------------------------------------
# 工具链：定位 .NET Framework 4.x 自带的 csc.exe（32/64 位任选其一）
# ---------------------------------------------------------------------------
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "csc.exe (.NET Framework 4.x) not found" }

# 应用图标：若存在则通过 /win32icon 嵌入 exe 资源
$icon = Join-Path $root "assets\icon.ico"
$iconArg = ""
if (Test-Path $icon) { $iconArg = "/win32icon:$icon" }

# 构建产物统一输出到 artifacts\（便于 .gitignore 忽略与整体分发）
$outDir = Join-Path $root "artifacts"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# 源码路径（GitHub 风格：src 下按程序集/功能分子目录）
$contractSrc = Join-Path $root "src\WinSpaceImeToggle.Contracts\Contracts.cs"
$contractDll = Join-Path $outDir "WinSpaceImeToggle.Contracts.dll"

$mainSrcs = @(
    (Join-Path $root "src\WinSpaceImeToggle\Program.cs"),
    (Join-Path $root "src\WinSpaceImeToggle\Localization.cs"),
    (Join-Path $root "src\WinSpaceImeToggle\MainConfig.cs"),
    (Join-Path $root "src\WinSpaceImeToggle\ModuleManager.cs"),
    (Join-Path $root "src\WinSpaceImeToggle\KeyRecorder.cs"),
    (Join-Path $root "src\WinSpaceImeToggle\UiAssets.cs"),
    (Join-Path $root "src\WinSpaceImeToggle\TrayApplicationContext.cs"),
    (Join-Path $root "src\WinSpaceImeToggle\SettingsForm.cs")
)

# ---------------------------------------------------------------------------
# 1) 契约程序集（library）：主程序与模块共同引用的模板类型（IModule 等）
# ---------------------------------------------------------------------------
& $csc /nologo /target:library /optimize+ /platform:anycpu /codepage:65001 "/out:$contractDll" $contractSrc
if ($LASTEXITCODE -ne 0) { throw "contract build failed" }
Write-Host "OK: $contractDll"

# ---------------------------------------------------------------------------
# 2) 主程序（winexe）：不引用任何具体模块，仅引用契约程序集
# ---------------------------------------------------------------------------
$out = Join-Path $outDir "WinSpaceImeToggle.exe"
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 $iconArg "/r:$contractDll" "/out:$out" $mainSrcs
if ($LASTEXITCODE -ne 0) { throw "main build failed" }
Write-Host "OK: $out"

# ---------------------------------------------------------------------------
# 3) 模块（library）：src\WinSpaceImeToggle.Modules 下每个子文件夹的 .cs
#    编译为一个 DLL，并以功能文件夹名命名（Ime -> Ime.dll, ProxyFix -> ...）
# ---------------------------------------------------------------------------
$modDir = Join-Path $outDir "modules"
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
# 清理旧模块产物，避免删除源码文件夹后残留过期 DLL
Get-ChildItem -Path $modDir -Filter *.dll | Remove-Item -Force
Get-ChildItem -Path (Join-Path $root "src\WinSpaceImeToggle.Modules") -Recurse -Filter *.cs | ForEach-Object {
    $dll = Join-Path $modDir ($_.Directory.Name + ".dll")
    & $csc /nologo /target:library /optimize+ /platform:anycpu /codepage:65001 "/r:$contractDll" "/out:$dll" $_.FullName
    if ($LASTEXITCODE -ne 0) { throw "module build failed: $($_.FullName)" }
    Write-Host "OK: $dll"
}

# 运行时资源（字体、图标）复制进 artifacts，保证 exe 可直接双击运行
$assetOut = Join-Path $outDir "assets"
if (Test-Path -LiteralPath (Join-Path $root "assets")) {
    if (Test-Path -LiteralPath $assetOut) { Remove-Item -LiteralPath $assetOut -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $root "assets") -Destination $assetOut -Recurse
    Write-Host "OK: $assetOut"
}

# 语言文件复制进 artifacts，保证运行时按当前语言加载（缺失时回退中文）
$langOut = Join-Path $outDir "Languages"
if (Test-Path -LiteralPath (Join-Path $root "Languages")) {
    if (Test-Path -LiteralPath $langOut) { Remove-Item -LiteralPath $langOut -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $root "Languages") -Destination $langOut -Recurse
    Write-Host "OK: $langOut"
}

# ---------------------------------------------------------------------------
# 4) 命令行调试版（可选）：console 入口，便于 --config / --settings-smoke 等调试
# ---------------------------------------------------------------------------
if ($Test) {
    $testOut = Join-Path $outDir "WinSpaceImeToggle.test.exe"
    & $csc /nologo /target:exe /optimize+ /platform:anycpu /codepage:65001 $iconArg "/r:$contractDll" "/out:$testOut" $mainSrcs
    if ($LASTEXITCODE -ne 0) { throw "test build failed" }
    Write-Host "OK: $testOut"
}

# ---------------------------------------------------------------------------
# 5) 打包 Export（可选）：把运行所需文件（exe、契约、modules、assets、README）
#    汇总到 Export\WinSpaceImeToggle\ 并压缩为 zip，便于拷到其他设备
# ---------------------------------------------------------------------------
if ($Package) {
    $exportRoot = Join-Path $root "Export\WinSpaceImeToggle"
    $exportRootFull = [System.IO.Path]::GetFullPath($exportRoot)
    $rootFull = [System.IO.Path]::GetFullPath($root)
    # 安全校验：拒绝把打包输出写到工作区之外
    if (-not $exportRootFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing to package outside workspace: $exportRootFull"
    }
    if (Test-Path -LiteralPath $exportRoot) { Remove-Item -LiteralPath $exportRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $exportRoot | Out-Null

    Copy-Item -LiteralPath $out -Destination $exportRoot
    Copy-Item -LiteralPath $contractDll -Destination $exportRoot
    Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $exportRoot
    if (Test-Path -LiteralPath $assetOut) {
        Copy-Item -LiteralPath $assetOut -Destination $exportRoot -Recurse
    }
    if (Test-Path -LiteralPath $langOut) {
        Copy-Item -LiteralPath $langOut -Destination $exportRoot -Recurse
    }
    $exportModDir = Join-Path $exportRoot "modules"
    New-Item -ItemType Directory -Force -Path $exportModDir | Out-Null
    Get-ChildItem -Path $modDir -Filter *.dll | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $exportModDir
    }

    $zip = Join-Path $root "Export\WinSpaceImeToggle.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path $exportRoot -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "OK: $zip"
}