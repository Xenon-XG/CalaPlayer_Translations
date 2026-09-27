# CalaPlayer 汉化 — 重新生成并安装
# 改完 translations.json 后运行本脚本，即可重新打包并安装到游戏。
# -NoPause：不在结束/出错时等待回车（供 GUI 无人值守调用）。
param([switch]$NoPause)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$kit    = $PSScriptRoot
$root   = Split-Path $kit -Parent
# 同一份脚本既放在「CalaPlayer_汉化工具」里，也放在仓库 tools\ 下：数据文件先找脚本旁边，再找上一级
function First([string[]]$c) { foreach ($p in $c) { if (Test-Path $p) { return $p } }; return $c[0] }
$dotnet = 'C:\Users\XG\dotnet10\dotnet.exe'
$tool   = Join-Path $root 'tool\CalaTextTool\bin\Release\net10.0\CalaTextTool.dll'
$usmap  = First @((Join-Path $kit 'mappings\CalaPlayer-UE5.7.usmap'), (Join-Path $root 'tool\mappings\CalaPlayer-UE5.7.usmap'))
$retoc  = Join-Path $root 'tool\retoc\retoc.exe'
$base   = Join-Path $root 'mod_work\base_bp'
$map    = First @((Join-Path $kit 'translations.json'), (Join-Path $root 'translations.json'))
$resmap = First @((Join-Path $kit 'resname_map.json'), (Join-Path $root 'resname_map.json'))
$edited = Join-Path $root 'mod_work\_rebuild_edited'
$out    = Join-Path $root 'mod_work\_rebuild_pak'
$paks   = Join-Path $root 'CalaPlayer\Content\Paks'

function Fail($msg) { Write-Host "[X] $msg" -ForegroundColor Red; if (-not $NoPause) { Read-Host '按回车退出' }; exit 1 }

Write-Host '==============================================' -ForegroundColor Cyan
Write-Host '   CalaPlayer 汉化 — 重新生成并安装' -ForegroundColor Cyan
Write-Host '==============================================' -ForegroundColor Cyan
Write-Host ''

if (Get-Process -Name CalaPlayer -ErrorAction SilentlyContinue) { Fail '请先完全关闭游戏 CalaPlayer，再运行本脚本。' }
if (-not (Test-Path (Join-Path $base 'scriptobjects.bin'))) { Fail "缺少基础资源目录：$base" }
if (-not (Test-Path $map)) { Fail "找不到翻译表：$map" }

Write-Host '[1/4] 应用翻译表 ...' -ForegroundColor Yellow
if (Test-Path $edited) { Remove-Item $edited -Recurse -Force }
& $dotnet $tool apply $base $map $edited $usmap
if ($LASTEXITCODE -ne 0) { Fail '应用翻译失败。' }
Copy-Item (Join-Path $base 'scriptobjects.bin') (Join-Path $edited 'scriptobjects.bin') -Force

# 角色名只翻「显示」：在各处 SetText 前按 resname_map.json 分支换成中文，底层键（加载/存盘用）保持英文
if (Test-Path $resmap) {
    Write-Host '[2/4] 角色名显示汉化 ...' -ForegroundColor Yellow
    $wdir = 'CalaPlayer\Content\CalaPlayer\UI\EditorUI\Widgets'
    # 下拉项控件本身没有可翻译文本，apply 不会输出它，从基础资源拷一份再注入
    New-Item -ItemType Directory -Force (Join-Path $edited $wdir) | Out-Null
    foreach ($ext in 'uasset','uexp') {
        Copy-Item (Join-Path $base "$wdir\WBP_DropdownButtonContent.$ext") (Join-Path $edited $wdir) -Force
    }
    & $dotnet $tool injectmap $edited $resmap $usmap
    if ($LASTEXITCODE -ne 0) { Fail '角色名注入失败（下拉框）。' }
    $targets = @(
        @('WBP_CharacterEntry.uasset',          'TextBlock_Name'),   # 左侧角色卡片
        @('WBP_TimelineTrackHeaderItem.uasset', 'SubtitleText'),     # 时间轴轨道（两条分支）
        @('WBP_TimelineTrackHeaderItem.uasset', 'TitleText'),
        @('WBP_ActiveCharacter.uasset',         'CharNameText')      # 激活角色名
    )
    foreach ($t in $targets) {
        & $dotnet $tool injectrender $edited $resmap $t[0] $t[1] $usmap
        if ($LASTEXITCODE -ne 0) { Fail "角色名注入失败（$($t[0]) / $($t[1])）。" }
    }
    # 右上「<名字> changes」标题：后缀先被翻译表译过，这里按译后的后缀定位拼接
    $suffix = ' changes'
    $raw = Get-Content $map -Raw -Encoding UTF8
    if ($raw -match '"\x20changes"\s*:\s*"([^"]*)"') { $suffix = $matches[1] }
    & $dotnet $tool injectconcat $edited $resmap 'WBP_Editor.uasset' $suffix $usmap
    if ($LASTEXITCODE -ne 0) { Fail '角色名注入失败（详情标题）。' }
} else {
    Write-Host '[2/4] 未找到 resname_map.json，跳过角色名汉化' -ForegroundColor DarkYellow
}

Write-Host '[3/4] 重新打包 mod ...' -ForegroundColor Yellow
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
& $retoc to-zen --version UE5_7 $edited (Join-Path $out 'CalaPlayer-Windows_P.utoc')
if ($LASTEXITCODE -ne 0) { Fail '打包失败。' }

Write-Host '[4/4] 安装到游戏 ...' -ForegroundColor Yellow
foreach ($f in 'CalaPlayer-Windows_P.utoc','CalaPlayer-Windows_P.ucas','CalaPlayer-Windows_P.pak') {
    Copy-Item (Join-Path $out $f) $paks -Force
}

Write-Host ''
Write-Host '[OK] 完成！启动游戏即为最新翻译。' -ForegroundColor Green
if (-not $NoPause) { Read-Host '按回车退出' }
