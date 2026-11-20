<#
.SYNOPSIS
    本地构建脚本（Windows）。替代 docs 里那些写死在作者机器上的 msbuild 命令行。

.DESCRIPTION
    做三件事：
      1. 结束可能残留的 KtvDownloader 进程 —— 否则 exe 被占用，
         MSB3021/MSB3027「无法复制文件」，这是 docs §坑13 说的那个问题；
      2. 用 vswhere 找到本机 MSBuild，不写死 Visual Studio 的安装路径；
      3. restore + build，并把版本号打进产物（-Version 可选）。

.EXAMPLE
    .\scripts\build.ps1
    .\scripts\build.ps1 -Configuration Debug
    .\scripts\build.ps1 -Version 1.2.3
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # 传给 MSBuild 的 -p:Version，会同时决定 AssemblyVersion / FileVersion，
    # 启动日志和「关于」对话框显示的就是它。留空则用 csproj 里的值。
    [string]$Version = '',

    [switch]$NoKill
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not $NoKill) {
    # ── docs §坑13：构建前必须结束残留进程 ──
    # 上一次调试留下的 KtvDownloader.exe 还占着 bin 目录，
    # MSBuild 会报 "无法将文件复制到输出目录" 而不是 "文件被占用"，很好误判。
    $stale = Get-Process -Name 'KtvDownloader' -ErrorAction SilentlyContinue
    if ($stale) {
        Write-Host "发现 $($stale.Count) 个残留的 KtvDownloader 进程，正在结束..." -ForegroundColor Yellow
        $stale | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
}

# ── 定位 MSBuild ──
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "找不到 vswhere.exe（$vswhere）。请安装 Visual Studio 2022 或更高，或安装 Build Tools。"
}

$msbuild = & $vswhere -latest -products * `
    -requires Microsoft.Component.MSBuild `
    -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1

if (-not $msbuild) { throw '找不到 MSBuild.exe。' }
Write-Host "MSBuild: $msbuild" -ForegroundColor Cyan

$arguments = @(
    'KtvDownloader.sln',
    '-t:restore;build',
    "-p:Configuration=$Configuration",
    '-p:Platform="Any CPU"',
    '-m',
    '-v:minimal',
    '-nologo'
)

if ($Version) {
    $arguments += "-p:Version=$Version"
    $arguments += "-p:AssemblyVersion=$Version.0"
    $arguments += "-p:FileVersion=$Version.0"
    Write-Host "版本: $Version" -ForegroundColor Cyan
}

& $msbuild @arguments
if ($LASTEXITCODE -ne 0) {
    throw "构建失败（MSBuild 退出码 $LASTEXITCODE）"
}

$exe = Join-Path $repoRoot "src\KtvDownloader\bin\$Configuration\net48\KtvDownloader.exe"
Write-Host ''
Write-Host "构建成功：$exe" -ForegroundColor Green
Write-Host '提示：仓库里没有 tools\ffmpeg.exe（已在 .gitignore 中）。' -ForegroundColor Yellow
Write-Host '      缺它也能构建与搜索，但转码/音轨分离不可用 —— 把 ffmpeg.exe 放进 tools\ 或在设置里指定路径即可。' -ForegroundColor Yellow
