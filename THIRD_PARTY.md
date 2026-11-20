# 第三方组件与素材

根目录 `LICENSE` 仅覆盖本项目原创部分。以下内容保留各自权利与许可，不被重新授权为 MIT。

| 内容 | 来源 / 用途 | 本仓库处理方式 |
| --- | --- | --- |
| Road Rash 游戏程序、场景、车辆 sprite、音乐、字体和商标 | 用户自行安装的游戏 | 不包含游戏 EXE、DLL、资源包、音乐或存档 |
| `docs/images/gameplay.png` | 项目维护者提供的实机截图 | 用于演示功能；其中游戏画面的权利归原权利人，不属于 MIT 授权范围 |
| `RoadRash30thEnhancement-v1.1.0.exe` | 修改器调用的外部增强器 | 玩家版沿用已核验的 v1.1.0 原文件并保留第三方许可。源码包与独立修改器包仅附文件摘要，见 `dependencies/` |
| 经典 Dota 2 英文播报 | Valve，玩家本机安装的 Dota 2 | 玩家包提供本地导入脚本。语音录音和生成的 `KnockdownVoice.bank` 仅保存在玩家本机 |
| `RoadRash_HD_Setup.exe` / 高清渲染组件 | [OBLPlife / RoadRash30th-HD](https://github.com/OBLPlife/RoadRash30th-HD) | 不包含二进制；上游采用 PolyForm Noncommercial License 1.0.0 |
| `RoadRash_ZH_CN_Setup.exe` / 汉化组件 | [OBLPlife / RoadRash30th-Chinese](https://github.com/OBLPlife/RoadRash30th-Chinese) | 不包含二进制；上游采用 PolyForm Noncommercial License 1.0.0 |
| .NET Framework、Windows API | 微软系统组件 | 使用系统安装版本，不随仓库分发 |
| .NET 10 / WPF 运行库 | Microsoft / .NET 开源项目 | 自包含 EXE 内分发运行库，许可证与第三方声明随包放在 `Tools/licenses/` |
| CommunityToolkit.Mvvm 8.4.0 | .NET Community Toolkit | MIT，许可及第三方声明随包放在 `Tools/licenses/`；源码项目通过 NuGet 引用 |

`third_party/licenses/Chinese-LICENSE.txt` 与 `HD-LICENSE.txt` 从当前验证使用的增强器内嵌资源原样提取，保留作为依赖许可记录。它们说明对应汉化与高清资源的许可，不能据此推定增强器其他部分的完整授权；增强器本身的独立发布地址和完整授权尚未在本仓库确认。

第三方工具的获取和再分发应遵循其发布者说明。发布本项目时不要把第三方 EXE 放进 MIT 源码树，也不要以本仓库的 MIT 许可替代它们的许可。

`assets/player_car.png` 是 `scripts/make-car.ps1` 用几何形状绘制的旧版透明叠加示例，属于本项目原创部分；当前游戏内汽车模式直接读取玩家安装的游戏资源，不使用该 PNG。

历史开发目录中的 `launcher.ico` 来源未单独确认，因此独立构建不嵌入该文件。运行时可从玩家已安装的游戏 `RASHICON.DLL` 读取窗口图标，仓库不分发此 DLL。

WPF 版使用本项目以几何图形绘制的原创 `assets/workbench.ico`，与界面中的橙色旗帜标识一致，采用本项目 MIT 许可。
