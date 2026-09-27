# CalaPlayer 汉化 — 重新生成并安装
# 改完 translations.json / 各名称表后运行本脚本，即可重新打包并安装到游戏。
# -NoPause：不在结束/出错时等待回车（供 GUI 无人值守调用）。
# -NoInstall：只打包到 mod_work\_rebuild_pak，不装进游戏（自检用）。
param([switch]$NoPause, [switch]$NoInstall)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$kit    = $PSScriptRoot
$root   = Split-Path $kit -Parent
# 同一份脚本既放在「CalaPlayer_汉化工具」里，也放在仓库 tools\ 下：数据文件先找脚本旁边，再找上一级
function First([string[]]$c) { foreach ($p in $c) { if (Test-Path $p) { return $p } }; return $c[0] }
function DataFile([string]$name) { First @((Join-Path $kit $name), (Join-Path $root $name)) }
$dotnet = 'C:\Users\XG\dotnet10\dotnet.exe'
$tool   = Join-Path $root 'tool\CalaTextTool\bin\Release\net10.0\CalaTextTool.dll'
$usmap  = First @((Join-Path $kit 'mappings\CalaPlayer-UE5.7.usmap'), (Join-Path $root 'tool\mappings\CalaPlayer-UE5.7.usmap'))
$retoc  = Join-Path $root 'tool\retoc\retoc.exe'
$base   = Join-Path $root 'mod_work\base_bp'
$map    = DataFile 'translations.json'
$edited = Join-Path $root 'mod_work\_rebuild_edited'
$out    = Join-Path $root 'mod_work\_rebuild_pak'
$paks   = Join-Path $root 'CalaPlayer\Content\Paks'

function Fail($msg) { Write-Host "[X] $msg" -ForegroundColor Red; if (-not $NoPause) { Read-Host '按回车退出' }; exit 1 }
# 运行外部程序：stderr 也当普通日志输出（PowerShell 5.1 在 Stop 模式下会把原生 stderr 当成异常）
function Native([string]$what, [string]$exe) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $exe @args 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE; $ErrorActionPreference = $old
    if ($code -ne 0) { Fail "$what 失败（退出码 $code）。" }
}
function Tool([string]$what) { Native $what $dotnet $tool @args }
# 没有可翻译文本的控件不会被 apply 输出：注入前从基础资源拷一份
function Ensure([string]$file) {
    if (Get-ChildItem $edited -Recurse -Filter "$file.uasset" -ErrorAction SilentlyContinue) { return }
    $src = Get-ChildItem $base -Recurse -Filter "$file.uasset" | Select-Object -First 1
    if (-not $src) { Fail "基础资源里找不到 $file" }
    $dst = Join-Path $edited ($src.DirectoryName.Substring($base.Length).TrimStart('\'))
    New-Item -ItemType Directory -Force $dst | Out-Null
    foreach ($ext in 'uasset','uexp') { Copy-Item (Join-Path $src.DirectoryName "$file.$ext") $dst -Force }
}

Write-Host '==============================================' -ForegroundColor Cyan
Write-Host '   CalaPlayer 汉化 — 重新生成并安装' -ForegroundColor Cyan
Write-Host '==============================================' -ForegroundColor Cyan
Write-Host ''

if (-not $NoInstall -and (Get-Process -Name CalaPlayer -ErrorAction SilentlyContinue)) { Fail '请先完全关闭游戏 CalaPlayer，再运行本脚本。' }
if (-not (Test-Path (Join-Path $base 'scriptobjects.bin'))) { Fail "缺少基础资源目录：$base" }
if (-not (Test-Path $map)) { Fail "找不到翻译表：$map" }

Write-Host '[1/5] 应用界面翻译表 ...' -ForegroundColor Yellow
if (Test-Path $edited) { Remove-Item $edited -Recurse -Force }
Tool '应用翻译' apply $base $map $edited $usmap
Copy-Item (Join-Path $base 'scriptobjects.bin') (Join-Path $edited 'scriptobjects.bin') -Force

# 以下各类名称都只翻「显示」：在显示处按名称表分支换成中文，底层键（加载/存盘用）保持英文。
# 哪张表不存在就跳过哪一类。
$resmap = DataFile 'resname_map.json'
if (Test-Path $resmap) {
    Write-Host '[2/5] 角色名 ...' -ForegroundColor Yellow
    Ensure 'WBP_DropdownButtonContent'
    Tool '角色名（下拉框）' injectmap $edited $resmap $usmap
    $targets = @(
        @('WBP_CharacterEntry',          'TextBlock_Name'),          # 左侧角色卡片
        @('WBP_TimelineTrackHeaderItem', 'SubtitleText'),            # 时间轴轨道（两条分支）
        @('WBP_TimelineTrackHeaderItem', 'TitleText'),
        @('WBP_ActiveCharacter',         'CharNameText'),            # 激活角色名
        @('WBP_TextContainer',           'CharacterNameText'),       # 播放模式：对话框说话人
        @('WBP_HistoryEntry',            'TextBlock_CharacterName'), # 播放模式：对话历史
        @('WBP_ColorPreset',             'TextBlock_42')             # 台词里的角色名快捷按钮（点了填入中文名）
    )
    foreach ($t in $targets) {
        Ensure $t[0]
        Tool "角色名（$($t[0]) / $($t[1])）" injectrender $edited $resmap "$($t[0]).uasset" $t[1] $usmap
    }
    # 右上「<名字> changes」标题：后缀先被翻译表译过，这里按译后的后缀定位拼接
    $suffix = ' changes'
    $raw = Get-Content $map -Raw -Encoding UTF8
    if ($raw -match '"\x20changes"\s*:\s*"([^"]*)"') { $suffix = $matches[1] }
    Tool '角色名（详情标题）' injectconcat $edited $resmap 'WBP_Editor.uasset' $suffix $usmap
} else {
    Write-Host '[2/5] 未找到 resname_map.json，跳过角色名' -ForegroundColor DarkYellow
}

Write-Host '[3/5] 动作 / 表情 ...' -ForegroundColor Yellow
# 已添加的动作/表情选择器：生成列表项时换显示文字；
# [+] 添加选择器是纯字符串下拉（显示即取值）：加选项前 英→中，选中后 中→英 再交给游戏（要求中文译名唯一）
$animmap = DataFile 'anim_map.json'
if (Test-Path $animmap) {
    Tool '动作（已添加列表）' injectsetprop $edited $animmap 'WBP_AnimationListItem.uasset' $usmap
    Tool '动作（[+] 列表）'   injectvarmap $edited $animmap 'WBP_DetailsPanel.uasset' 'SetCharacterAnimDropdown' 'AddOption' 'CallFunc_Conv_NameToString_ReturnValue' 'fwd' $usmap
    Tool '动作（[+] 选中）'   injectvarmap $edited $animmap 'WBP_DetailsPanel.uasset' 'ExecuteUbergraph_WBP_DetailsPanel' 'SetSelected' 'SelectedItem_4' 'rev' $usmap
} else { Write-Host '      未找到 anim_map.json，跳过动作' -ForegroundColor DarkYellow }
$morphmap = DataFile 'morph_map.json'
if (Test-Path $morphmap) {
    Tool '表情（已添加列表）' injectsetprop $edited $morphmap 'WBP_MorphListItem.uasset' $usmap
    Tool '表情（[+] 列表）'   injectvarmap $edited $morphmap 'WBP_DetailsPanel.uasset' 'SetCharacterMorphDropdown' 'AddOption' 'CallFunc_Array_Get_Item' 'fwd' $usmap
    Tool '表情（[+] 选中）'   injectvarmap $edited $morphmap 'WBP_DetailsPanel.uasset' 'ExecuteUbergraph_WBP_DetailsPanel' 'SetSelectedManually' 'SelectedItem_3' 'rev' $usmap
} else { Write-Host '      未找到 morph_map.json，跳过表情' -ForegroundColor DarkYellow }

Write-Host '[4/5] 背景音乐 / 环境音 / 音效 / 背景 ...' -ForegroundColor Yellow
# 详情面板的三个下拉（列表项和已选项共用同一个生成函数）+ 时间轴上音乐/音效块的文字
$audio = @(
    @('bgm_map.json',     'On_ComboboxKey_BGM_GenerateContentWidget',     'CallFunc_Conv_StringToText_ReturnValue',   '背景音乐'),
    @('ambient_map.json', 'On_ComboboxKey_Ambient_GenerateContentWidget', 'CallFunc_Conv_StringToText_ReturnValue',   '环境音'),
    @('sound_map.json',   'On_ComboboxKey_Sounds_GenerateContentWidget',  'CallFunc_Conv_StringToText_ReturnValue_1', '音效')
)
foreach ($a in $audio) {
    $m = DataFile $a[0]
    if (-not (Test-Path $m)) { Write-Host "      未找到 $($a[0])，跳过$($a[3])" -ForegroundColor DarkYellow; continue }
    Tool "$($a[3])（下拉）" injectsetprop $edited $m 'WBP_DetailsPanel.uasset' $usmap $a[1]
}
Ensure 'WBP_SubslotContent'
foreach ($a in $audio) {
    $m = DataFile $a[0]
    if (-not (Test-Path $m)) { continue }
    Tool "$($a[3])（时间轴）" injectrender $edited $m 'WBP_SubslotContent.uasset' 'TextBlock_26' $usmap $a[2]
}
# 背景：详情面板「背景」按钮上的名字（与角色下拉共用 WBP_DropdownButtonContent，只改渲染层）
$bgmap = DataFile 'background_map.json'
if (Test-Path $bgmap) {
    Ensure 'WBP_DropdownButtonContent'
    Tool '背景名' injectrender $edited $bgmap 'WBP_DropdownButtonContent.uasset' 'TextBlock_25' $usmap
} else { Write-Host '      未找到 background_map.json，跳过背景' -ForegroundColor DarkYellow }

Write-Host '[5/5] 打包' -ForegroundColor Yellow
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
Native '打包' $retoc to-zen --version UE5_7 $edited (Join-Path $out 'CalaPlayer-Windows_P.utoc')

if ($NoInstall) {
    Write-Host ''
    Write-Host "[OK] 已打包到 $out（未安装）" -ForegroundColor Green
} else {
    Write-Host '      安装到游戏 ...' -ForegroundColor Yellow
    foreach ($f in 'CalaPlayer-Windows_P.utoc','CalaPlayer-Windows_P.ucas','CalaPlayer-Windows_P.pak') {
        Copy-Item (Join-Path $out $f) $paks -Force
    }
    Write-Host ''
    Write-Host '[OK] 完成！启动游戏即为最新翻译。' -ForegroundColor Green
}
if (-not $NoPause) { Read-Host '按回车退出' }
