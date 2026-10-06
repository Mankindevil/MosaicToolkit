# Mosaic Toolkit

Windows 图形化 Unity Renderer 检查与规则工具。通过游戏内的 BepInEx 扫描插件查看对象、材质和 Shader，测试隐藏指定 Renderer 或材质槽，并保存、导出自动执行规则。

**当前版本：0.1.10。支持 BepInEx 5 / Unity Mono / .NET 4；IL2CPP 目前仅识别，不支持生成插件。**

0.1.10 修复 Unity 6000.4 中 `GetInstanceID()` 过时警告被当成错误、导致插件构建失败的问题。

## 下载与使用

从 [Releases](https://github.com/Mankindevil/MosaicToolkit/releases/latest) 下载：

- `MosaicToolkit-0.1.10.exe`：单文件桌面程序，内嵌插件源码。
- `MosaicToolkit-v0.1.10.zip`：完整包，包含程序、源码、说明、预设和验证记录。
- `SHA256SUMS.txt`：下载文件校验值。

1. 先为目标游戏安装适合的 BepInEx 5，然后退出游戏。
2. 打开 Mosaic Toolkit，选择真正的游戏 EXE，点击“安装扫描插件”。
3. 启动游戏并进入目标场景，等待连接和扫描完成。
4. 在候选对象或全部 Renderer 中检查目标；可临时隐藏后观察效果，也可点击“生成并启用默认规则”。
5. 混合材质对象：选中一个 Renderer，点击“材质详情”或双击对象行，按槽编号勾选并测试。

**遇到新版 Unity 的编译失败时，请退出游戏，用 0.1.10 重新安装扫描插件。已经正常运行的旧插件不会因桌面程序升级而自动替换。**

## 功能

- 按对象名、材质名、Shader 搜索候选，默认关键词为 `mosaic`、`モザ`、`censor`。
- 全部 Renderer 列表支持文字、组件和状态组合筛选，以及状态、名称、Shader 等字段排序。
- 列表增量更新，保留仍可见对象的选择和滚动位置。
- 独立材质详情窗口固定绑定对象，支持重复材质名按槽分别隐藏、多选恢复、保存为限定完整路径的规则。
- 默认规则区分独立 Renderer 和混合材质槽；开启自动补充后，新场景和新对象会追加规则。
- 关闭桌面程序后，游戏内已启用规则和临时控制继续生效；“停止全部并恢复”可撤销本工具接管的状态。
- 操作前备份默认关闭，可在“保存的规则”页开启。导出补丁 ZIP 可在对应游戏中独立使用。

按槽隐藏会隐藏该槽绘制的全部内容，不能拆分同一槽内已经合并的背景与遮罩。关键词命中也不等于用途已确认，实际效果需在游戏中观察。

## 保存位置

```text
游戏目录/
├─ MosaicToolkit/
│  ├─ profile.json       当前规则副本
│  ├─ output/            生成的插件、源码和补丁 ZIP
│  └─ backups/           开启备份后才生成
└─ BepInEx/plugins/MosaicToolkit/
   ├─ MosaicToolkit.Scanner.dll
   ├─ profile.json       插件实际读取的规则
   └─ session/           本地连接与操作文件
```

请通过界面保存或导入规则，避免手动修改错误副本。更多操作和故障排查见 [使用说明](使用说明.txt)。

## 从源码构建

需要 Windows 和 .NET Framework 4.x 编译器。无需下载游戏程序集即可构建桌面程序和执行模拟测试。

```powershell
.\build.ps1
.\test.ps1
```

也可以指定输出文件名：

```powershell
.\build.ps1 -OutputName 'MosaicToolkit-0.1.10.exe'
.\test.ps1 -ExecutableName 'MosaicToolkit-0.1.10.exe'
```

桌面程序在生成扫描插件时，使用用户所选游戏目录中的 Unity/BepInEx 程序集作为编译引用。仓库和发行包不包含这些第三方程序集。

## 验证

0.1.10 已通过 49 项桌面/协议检查、55 项 WinForms 检查、62 项运行时模拟检查，共 166 项；另在带过时标记的 API 模拟环境中重复通过 62 项运行时检查，并保留“警告视为错误”编译选项。

已使用《国家の敵 ～女囚たちの告白～》（Unity 6000.4.7f1）、Dungeon of Meat DLsite v1.05 和 PainRein 0.103 的程序集成功编译扫描插件。本版界面功能沿用 0.1.9。

模拟测试不执行 Unity。本版本未进行真实游戏画面或帧率验证，详情见 [验证记录](验证记录.json)。
