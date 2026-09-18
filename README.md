# CalaPlayer 简体中文汉化 (Simplified Chinese Translation)

将 **CalaPlayer**（基于 Unreal Engine 5.7 的《卡拉彼丘 / Strinova》剧情演出编辑器）的英文界面汉化为简体中文——主菜单、编辑器、各面板/标签、按钮、下拉框、弹窗、时间轴等界面文字全部中文。

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

---

## 🛠️ 重新生成补丁（工具）

[`tools/`](tools/) 提供了从翻译表重新打包补丁的脚本与源码：

| 文件 | 说明 |
|---|---|
| `rebuild.ps1` | PowerShell 主脚本：应用翻译 → 重新打包 → 安装到游戏 |
| `重新生成并安装.bat` | 双击启动器（用 `-ExecutionPolicy Bypass` 运行上面的 ps1） |
| `CalaTextTool/` | 汉化核心工具源码（.NET）：编辑 cooked UE5.7 资产里的 FText/StrProperty/蓝图字节码文字 |
| `SigFinder/` | 从 PE+PDB 用 dbghelp 定位符号地址、生成 AOB 的小工具 |

**依赖**（自行准备）：[retoc](https://github.com/trumank/retoc)（IoStore 解/打包）、[UAssetAPI](https://github.com/atenfyr/UAssetAPI)（需对 UE5.7 打小补丁）、任一 UE5.7 的 `.usmap`、.NET 10 SDK。

---

## 致谢
- [retoc](https://github.com/trumank/retoc) · [UAssetAPI](https://github.com/atenfyr/UAssetAPI)
