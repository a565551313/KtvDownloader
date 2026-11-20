<#
.SYNOPSIS
    本地跑单元测试（Windows）。

.DESCRIPTION
    先构建，再用 vswhere 找到 vstest.console.exe 跑测试。
    /Platform:x64 是必须的：被测工程锁定 x64（libVLC 只发布 x64 原生库），
    32 位测试宿主加载它会直接 BadImageFormatException。

.EXAMPLE
    .\scripts\run-tests.ps1
    .\scripts\run-tests.ps1 -Configuration Debug -Filter "FullyQualifiedName~RetryPolicy"
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # vstest 的过滤器，例如 "FullyQualifiedName~FfmpegArguments"
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# 复用构建脚本，顺便把 docs §坑13 的残留进程处理掉。
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installationPath = & $vswhere -latest -products * `
    -requires Microsoft.Component.MSBuild `
    -property installationPath | Select-Object -First 1

if (-not $installationPath) { throw '找不到 Visual Studio 安装路径。' }

$vstest = Join-Path $installationPath 'Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe'
if (-not (Test-Path $vstest)) { throw "找不到 vstest.console.exe（$vstest）。" }

$testDll = Join-Path $repoRoot "tests\KtvDownloader.Tests\bin\$Configuration\net48\KtvDownloader.Tests.dll"
if (-not (Test-Path $testDll)) { throw "找不到测试程序集：$testDll" }

$arguments = @($testDll, '/Platform:x64', '/Framework:Framework48', '/Logger:console;verbosity=normal')
if ($Filter) { $arguments += "/TestCaseFilter:$Filter" }

& $vstest @arguments
if ($LASTEXITCODE -ne 0) {
    throw "测试失败（vstest 退出码 $LASTEXITCODE）"
}

Write-Host ''
Write-Host '全部测试通过。' -ForegroundColor Green
