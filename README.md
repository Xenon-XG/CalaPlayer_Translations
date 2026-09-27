# CalaPlayer 简体中文汉化 (Simplified Chinese Translation)

将 **CalaPlayer**（基于 Unreal Engine 5.7 的《卡拉彼丘 / Strinova》剧情演出编辑器）的英文界面汉化为简体中文——主菜单、编辑器、各面板/标签、按钮、下拉框、弹窗、时间轴等界面文字全部中文。

角色名、动作、表情、背景音乐、环境音、音效、背景的名字也显示为中文。这些只改「显示」，剧本里存的仍是英文原名，旧剧本照常可用，卸载汉化后剧本也不受影响。

> 本项目为**粉丝汉化补丁**，仅供学习与个人使用，仅适配CalaPlayer v0.1.0.134。

---

## 🚀 安装（即插即用）

把 [`patch/`](patch/) 里的 3 个文件复制到游戏目录：

```
CalaPlayer\Content\Paks\
    ├─ CalaPlayer-Windows_P.utoc
    ├─ CalaPlayer-Windows_P.ucas
    └─ CalaPlayer-Windows_P.pak
```

启动游戏即为中文。首次启动可能因编译着色器短暂“无响应”，稍等即可，属正常现象。

**卸载 / 还原英文**：删除上面这 3 个 `CalaPlayer-Windows_P.*` 文件即可。

- 不修改任何原始游戏文件，只新增一个高优先级补丁包（`_P`），安全、可逆。
- 适用于对应版本的 CalaPlayer（UE 5.7 构建）。游戏更新导致界面资源改变时可能需要重做。

---

## ✏️ 自己调整 / 贡献翻译

翻译文本都在 [`translations.json`](translations.json)（`"英文" : "中文"` 一行一条）。

改词流程：
1. 编辑 `translations.json`，改冒号右边的中文（左边英文原文别动）。
2. 需要新增词条：照格式加一行，左边英文须与游戏内原文**完全一致**（区分大小写、空格、标点）。游戏内全部可翻译原文见 [`reference/所有可翻译文本.json`](reference/所有可翻译文本.json)。
3. 用 [`tools/`](tools/) 里的脚本重新生成补丁（见下）。

各类名称各有一张表，格式同样是 `"游戏里的英文名" : "中文"`，中文留空即保持英文：

| 文件 | 内容 |
|---|---|
| [`resname_map.json`](resname_map.json) | 角色名 |
| [`anim_map.json`](anim_map.json) · [`morph_map.json`](morph_map.json) | 动作 · 表情（中文名不能重复，[+] 添加菜单要靠它换回英文） |
| [`bgm_map.json`](bgm_map.json) · [`ambient_map.json`](ambient_map.json) · [`sound_map.json`](sound_map.json) | 背景音乐 · 环境音 · 音效 |
| [`background_map.json`](background_map.json) | 背景（按官方剧情中出现的场景命名，依据见 [`reference/背景命名依据.md`](reference/背景命名依据.md)） |

也可以双击 `tools/打开汉化工具.bat`，在浏览器里分页修改以上所有内容，并一键打包安装。

---

## 🛠️ 重新生成补丁（工具）

[`tools/`](tools/) 提供了从翻译表重新打包补丁的脚本与源码：

| 文件 | 说明 |
|---|---|
| `rebuild.ps1` | PowerShell 主脚本：应用翻译和各名称表 → 重新打包 → 安装到游戏 |
| `重新生成并安装.bat` | 双击启动器（用 `-ExecutionPolicy Bypass` 运行上面的 ps1） |
| `打开汉化工具.bat` · `server.py` · `index.html` | 本地网页编辑器（需 Python 3）：分页编辑翻译和名称表，一键打包安装 |
| `CalaTextTool/` | 汉化核心工具源码（.NET）：编辑 cooked UE5.7 资产里的 FText/StrProperty/蓝图字节码文字 |
| `SigFinder/` | 从 PE+PDB 用 dbghelp 定位符号地址、生成 AOB 的小工具 |

**依赖**（自行准备）：[retoc](https://github.com/trumank/retoc)（IoStore 解/打包）、[UAssetAPI](https://github.com/atenfyr/UAssetAPI)（需对 UE5.7 打小补丁）、任一 UE5.7 的 `.usmap`、.NET 10 SDK。

---

## 致谢
- [retoc](https://github.com/trumank/retoc) · [UAssetAPI](https://github.com/atenfyr/UAssetAPI)
