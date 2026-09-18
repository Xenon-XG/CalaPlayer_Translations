# CalaPlayer 汉化 — 重新生成并安装
# 改完 translations.json 后运行本脚本，即可重新打包并安装到游戏。
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$kit    = $PSScriptRoot
$root   = Split-Path $kit -Parent
$dotnet = 'C:\Users\XG\dotnet10\dotnet.exe'
$tool   = Join-Path $root 'tool\CalaTextTool\bin\Release\net10.0\CalaTextTool.dll'
$usmap  = Join-Path $root 'tool\src\UAssetAPI\UAssetAPI.Tests\TestAssets\TestUE5_7\FarFarWest\FarFarWest.usmap'
$retoc  = Join-Path $root 'tool\retoc\retoc.exe'
$base   = Join-Path $root 'mod_work\base_bp'
$map    = Join-Path $kit  'translations.json'
$edited = Join-Path $root 'mod_work\_rebuild_edited'
$out    = Join-Path $root 'mod_work\_rebuild_pak'
$paks   = Join-Path $root 'CalaPlayer\Content\Paks'

function Fail($msg) { Write-Host "[X] $msg" -ForegroundColor Red; Read-Host '按回车退出'; exit 1 }

Write-Host '==============================================' -ForegroundColor Cyan
Write-Host '   CalaPlayer 汉化 — 重新生成并安装' -ForegroundColor Cyan
Write-Host '==============================================' -ForegroundColor Cyan
Write-Host ''

if (Get-Process -Name CalaPlayer -ErrorAction SilentlyContinue) { Fail '请先完全关闭游戏 CalaPlayer，再运行本脚本。' }
if (-not (Test-Path (Join-Path $base 'scriptobjects.bin'))) { Fail "缺少基础资源目录：$base" }
if (-not (Test-Path $map)) { Fail "找不到翻译表：$map" }

Write-Host '[1/3] 应用翻译表 ...' -ForegroundColor Yellow
if (Test-Path $edited) { Remove-Item $edited -Recurse -Force }
& $dotnet $tool apply $base $map $edited $usmap
if ($LASTEXITCODE -ne 0) { Fail '应用翻译失败。' }
Copy-Item (Join-Path $base 'scriptobjects.bin') (Join-Path $edited 'scriptobjects.bin') -Force

Write-Host '[2/3] 重新打包 mod ...' -ForegroundColor Yellow
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
& $retoc to-zen --version UE5_7 $edited (Join-Path $out 'CalaPlayer-Windows_P.utoc')
if ($LASTEXITCODE -ne 0) { Fail '打包失败。' }

Write-Host '[3/3] 安装到游戏 ...' -ForegroundColor Yellow
foreach ($f in 'CalaPlayer-Windows_P.utoc','CalaPlayer-Windows_P.ucas','CalaPlayer-Windows_P.pak') {
    Copy-Item (Join-Path $out $f) $paks -Force
}

Write-Host ''
Write-Host '[OK] 完成！启动游戏即为最新翻译。' -ForegroundColor Green
Read-Host '按回车退出'
