# RoadRash30th-EnhancedMod · 暴力摩托增强修改器

用于 Windows 10/11 x64 上的 Steam《Road Rash：30 周年纪念版》，目标游戏 build **26105967**。可调整比赛人数、AI、车流与战斗规则，也支持长跑、击倒仪表和硬核比赛。

**0.5.1 的「减少大型比赛卡顿」默认开启。** 在单人比赛起跑、许多车手同时更新名次时生效，玩家自己的名次照常更新。[0.5.1 发布说明](docs/releases/v0.5.1.md)

## 下载与安装

1. 在发布页的 Assets 中选择修改器。文件名以 `-player.zip` 结尾的是玩家版，内含增强器 v1.1.0。已有这份增强器的玩家也可以选 `-standalone.zip`，沿用游戏目录下的 `Tools/RoadRash30thEnhancement-v1.1.0.exe`。两种包均自带 .NET 桌面运行库。[前往 Releases 下载](https://github.com/noobdawn/RoadRash30th-EnhancedMod/releases)
2. 退出游戏和修改器，将所选 ZIP 的全部内容解压到 `RoadRash95.exe` 所在目录，允许覆盖包内同名文件。升级时可沿用个人设置、语音库和旧快捷方式。
3. 打开 **暴力摩托增强修改器.exe**，选好玩法，点击 **应用并启动**。需要调整大型比赛优化时，在「Fix 修复」中切换。击倒仪表、警察玩法和语音的设置步骤见[玩家指南](docs/players/guide.txt)。

## 主要功能

| 功能 | 本版行为 |
| --- | --- |
| 大型比赛 | 「Fix 修复 → 减少大型比赛卡顿」默认开启，减少大批电脑车手同时更新名次时的停顿。 |
| 击倒与连击 | 五位液晶表统计本场击倒。默认在最后一次命中或接触后 2 秒内归因，18 秒内连续击倒显示火焰标语。 |
| 英文播报 | 默认每次有效击倒触发。Holy Shit 与 Rampage 只在通道空闲时播放。 |
| 硬核模式 | 玩家首次摔车触地 3 秒后以撞毁出局，暂停期间停表。 |
| 我是警察 | 玩家使用警用摩托与警棍，硬核模式锁定。倒地的参赛 AI 退出本场。 |
| 其他玩法 | 支持骑行、车辆、泥头车和长跑，可调整比赛规模、赛道、车流、武器、AI 和音效。 |

![游戏内汽车外观与车群](docs/images/gameplay.png)

## 文档导航

| 查找内容 | 文档 |
| --- | --- |
| 安装、玩法、语音、设置还原及日志 | [玩家指南](docs/players/guide.txt) |
| 万人比赛名次优化的比较规则、实测结果及开发诊断 | [大型比赛名次优化](docs/development/rank-batching.md) |
| 连击火焰、游戏调色板、DirectDraw 合成与离屏复核 | [击倒仪表绘制](docs/development/knockdown-sprites.md) |
| 长跑范围、选线、恢复上车、车辆耐久和下车保护 | [长跑与比赛设置](docs/development/long-run.md) · [车手功能](docs/development/character-features.md) |
| 各版变化与历史兼容性记录 | [变更记录](CHANGELOG.md) · [2026-11-17 兼容性记录](docs/history/compatibility-2026-11-17.md) |
| 第三方工具、原始文件校验与许可 | [依赖清单](dependencies/README.md) · [第三方说明](THIRD_PARTY.md) |
| 构建、离线测试、打包与源码许可 | [开发与发布指南](CONTRIBUTING.md) · [工作台架构](docs/development/wpf-workbench.md) · [MIT 许可证](LICENSE) |

## 适用范围

单人比赛可配置最多 10000 人，属于实验规模，表现取决于硬件和赛道。车辆外观使用原有交通资源和摩托物理。赛程覆盖使用已有道路，公里数按实测比例换算。其他游戏版本、Wine、Proton 和 Steam Deck 尚未验证。
