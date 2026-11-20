# 参与开发

先阅读 [README](README.md) 和[架构说明](docs/development/wpf-workbench.md)。请把改动限制在独立仓库内，使用 `artifacts/` 保存编译结果与诊断，不要直接覆盖正在使用的游戏安装。

## 提交问题

说明 Windows 版本、游戏来源与 build、修改器版本、相关设置、最短复现步骤和预期结果。汽车问题应提供赛道、款式、行驶方向和碰撞对象；声音问题应说明总音量、比赛音乐、比赛音效以及是否已重新开始比赛。

可附相关日志片段，但先检查其中的用户名、绝对路径和其他个人信息。不要上传游戏本体、存档、第三方 EXE 或整份本地备份。

## 修改代码

玩家指南位于 `docs/players/`，每个正式版的发布说明位于 `docs/releases/`，架构与验证方法位于 `docs/development/`，历史验收记录位于 `docs/history/`。

1. WPF 界面使用 .NET 10、C# 与 CommunityToolkit.Mvvm；共享的 `src/*.cs` 和兼容桥接仍须保持 Framework 4.x 编译器兼容。运行时补丁算法不应依赖界面对象。
2. 常规修改执行 `scripts/build.ps1`、`scripts/test.ps1` 与 `scripts/test-wpf.ps1`；改动汽车选帧或 MIDI 解析时，加跑带 `-GameDirectory` 的对应集成验证。
3. 修改原生 hook 时检查入口字节、ASLR、寄存器、标志位、栈平衡、跳转目标与分配区边界。签名不匹配应报错，不应强行写入。
4. 用能证明行为的回归测试覆盖修复；UI 文案与文档小改动不必堆叠重复测试。
5. 有配置、使用方式或兼容性变化时同步更新文档与变更记录。新增发布文件需同步 `scripts/package.py` 的显式清单。

提交说明应写清触发条件、改动后的行为、执行过的验证及剩余限制。贡献原创内容时请确认可按本项目 MIT 许可提供；第三方代码或素材需保留来源和自身许可。

## 构建与离线验证

在 Windows 上使用 .NET 10 SDK 和 .NET Framework 4.x 编译器。从源码根目录运行以下命令，将游戏路径改为本机安装位置。首次构建用 `.\scripts\build.ps1` 还原依赖。本机已有项目依赖缓存时可用离线模式。

```powershell
$gameDir = 'D:\Games\RoadRash'
.\scripts\build.ps1 -Offline
.\scripts\test.ps1 -GameDirectory $gameDir
.\scripts\test-wpf.ps1 -RenderUi -GameDirectory $gameDir
```

构建产物在 `artifacts/bin/`。名次测试在测试进程中对照万人车阵的返回值。WPF 测试在离屏窗口检查默认值、旧设置迁移和两个中文入口，并使用游戏目录中已有的增强器备份创建隔离副本，核对应用与还原结果。实机比赛由玩家手动进行。[名次测试与实测记录](docs/development/rank-batching.md)

## 准备发布附件

确认 `VERSION`、变更记录与 `docs/releases/v<版本号>.md` 对应，并将新增文档加入 `scripts/package.py` 的源码清单。使用 Python 3.9+ 从已构建的文件一次生成附件。

```powershell
$version = (Get-Content .\VERSION -Raw).Trim()
$out = ".\artifacts\release\v$version"
py -3 .\scripts\package.py --release-set "$gameDir\Tools\RoadRash30thEnhancement-v1.1.0.exe" --output-dir $out
```

输出目录内有玩家版（`-player.zip`）、需自备增强器的独立修改器版（`-standalone.zip`）、源码（`-source.zip`），以及每份 ZIP 的 SHA-256 校验文件。`发布说明.md` 可直接用作 GitHub Release 正文。玩家版只接受大小和摘要均匹配的 v1.1.0 增强器。有关该文件的发布范围见[第三方说明](THIRD_PARTY.md)。

将要发布的玩家版解压到新的空目录，把该路径传给 `scripts/test-wpf.ps1 -BinaryDirectory <解压目录> -RenderUi -GameDirectory $gameDir`，即可从附件复核两个入口及还原结果。校验 ZIP 可用 `Get-FileHash -Algorithm SHA256` 对照同名的 `.sha256.txt`。

单独打包时，`--source-only` 仅生成源码，省略所有模式参数生成独立修改器和源码。`--player-enhancer <增强器路径>` 保留原有的中文玩家版包名。
