# RoadRash30th-EnhancedMod · 暴力摩托增强修改器

用于 Windows 10/11 x64 上的 Steam《Road Rash：30 周年纪念版》，目标游戏 build **26105967**。可调整比赛人数、AI、车流与战斗规则，也支持长跑、击倒仪表和硬核比赛。

## 安装与使用

1. 退出游戏和修改器，将玩家版 ZIP 解压到 `RoadRash95.exe` 所在目录。允许覆盖包内同名程序和工具文件。旧快捷方式指向的 `暴力摩托增强启动器.exe` 会打开同目录的 `暴力摩托增强修改器.exe`。个人设置和语音库可继续使用。
2. 打开修改器，选好玩法，点击 **应用并启动**。玩家包自带 .NET 桌面运行库和增强器 v1.1.0。
3. 在「画面设置 → 击倒仪表」调整归因时间、连击时间和播报间隔。在「比赛设置」开启硬核模式或选择「我是警察」。可选语音的安装命令见[玩家指南](docs/players/guide.txt)。

| 功能 | 本版行为 |
| --- | --- |
| 击倒与连击 | 五位液晶表统计本场击倒。默认在最后一次命中或接触后 2 秒内归因，18 秒内连续击倒显示火焰标语。 |
| 英文播报 | 默认每次有效击倒触发。Holy Shit 与 Rampage 只在通道空闲时播放。 |
| 硬核模式 | 玩家首次摔车触地 3 秒后以撞毁出局，暂停期间停表。 |
| 我是警察 | 玩家使用警用摩托与警棍，硬核模式锁定。倒地的参赛 AI 退出本场。 |
| 其他玩法 | 支持骑行、车辆、泥头车和长跑，可调整比赛规模、赛道、车流、武器、AI 和音效。 |

![游戏内汽车外观与车群](docs/images/gameplay.png)

## 开发与验证

在 Windows 上准备 .NET 10 SDK 和系统自带的 .NET Framework 4.x 编译器。首次构建需要还原 NuGet 依赖。以下命令从源码根目录运行，将示例游戏路径替换为本机路径。

```powershell
$gameDir = 'D:\Games\RoadRash'
.\scripts\build.ps1
.\scripts\test.ps1 -GameDirectory $gameDir
.\scripts\test-wpf.ps1 -RenderUi -GameDirectory $gameDir
```

构建产物位于 `artifacts/bin/`。WPF 测试使用离屏界面；带游戏目录的桥接测试从已有增强器备份基线创建隔离副本，验证读取、应用与还原。界面、补丁与兼容进程的职责见[工作台架构](docs/development/wpf-workbench.md)。

当前预览版 **0.5.1-preview.2** 的万人起跑优化位于「Fix 修复 → AI 名次刷新」。在「比赛设置」把参赛人数设为 10000，再在同一赛道对照原版和 50 毫秒合并两档。[实测步骤](docs/development/rank-batching.md)

## 生成分发包

打包需要 Python 3.9+。在构建完成后运行以下命令生成玩家版、源码 ZIP 及各自的 SHA-256 校验文件。

```powershell
py -3 .\scripts\package.py --player-enhancer "$gameDir\Tools\RoadRash30thEnhancement-v1.1.0.exe"
```

脚本核对增强器摘要后将输出写入 `artifacts/release/`。省略 `--player-enhancer` 可生成需另备增强器的独立修改器包。使用 `--source-only` 可只打包源码。玩家包随附语音导入脚本，玩家自己的音库、存档和配置保存在本机。

## 文档导航

| 查找内容 | 文档 |
| --- | --- |
| 安装、玩法、语音、设置还原及日志 | [玩家指南](docs/players/guide.txt) |
| 万人比赛的起跑名次、离线验证和玩家对照步骤 | [名次刷新实验](docs/development/rank-batching.md) |
| 连击火焰、游戏调色板、DirectDraw 合成与离屏复核 | [击倒仪表绘制](docs/development/knockdown-sprites.md) |
| 长跑范围、选线、恢复上车、车辆耐久和下车保护 | [长跑与比赛设置](docs/development/long-run.md) · [车手功能](docs/development/character-features.md) |
| 历次功能、修复与兼容性记录 | [变更记录](CHANGELOG.md) · [2026-11-17 兼容性记录](docs/history/compatibility-2026-11-17.md) |
| 第三方工具、原始文件校验与许可 | [依赖清单](dependencies/README.md) · [第三方说明](THIRD_PARTY.md) |
| 源码许可与参与开发 | [MIT 许可证](LICENSE) · [贡献指南](CONTRIBUTING.md) |

## 适用范围

单人比赛可配置最多 10000 人，属于实验规模，表现取决于硬件和赛道。车辆外观使用原有交通资源和摩托物理。赛程覆盖使用已有道路，公里数按实测比例换算。其他游戏版本、Wine、Proton 和 Steam Deck 尚未验证。
