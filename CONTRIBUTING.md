# 参与开发

先阅读 [README](README.md) 和[架构说明](docs/development/wpf-workbench.md)。请把改动限制在独立仓库内，使用 `artifacts/` 保存编译结果与诊断，不要直接覆盖正在使用的游戏安装。

## 提交问题

说明 Windows 版本、游戏来源与 build、修改器版本、相关设置、最短复现步骤和预期结果。汽车问题应提供赛道、款式、行驶方向和碰撞对象；声音问题应说明总音量、比赛音乐、比赛音效以及是否已重新开始比赛。

可附相关日志片段，但先检查其中的用户名、绝对路径和其他个人信息。不要上传游戏本体、存档、第三方 EXE 或整份本地备份。

## 修改代码

玩家操作说明位于 `docs/players/`，算法、架构及验证方法位于 `docs/development/`，历次阶段验收位于 `docs/history/`。通用安装与构建入口在根目录 README 中。

本机已有依赖缓存时，可运行 `scripts/build.ps1 -Offline`。名次回归由 `scripts/test.ps1 -GameDirectory 游戏目录` 在测试进程中完成。完整比赛由玩家手动运行并提供帧时间及 CSV。[名次对照步骤](docs/development/rank-batching.md)

1. WPF 界面使用 .NET 10、C# 与 CommunityToolkit.Mvvm；共享的 `src/*.cs` 和兼容桥接仍须保持 Framework 4.x 编译器兼容。运行时补丁算法不应依赖界面对象。
2. 常规修改执行 `scripts/build.ps1`、`scripts/test.ps1` 与 `scripts/test-wpf.ps1`；改动汽车选帧或 MIDI 解析时，加跑带 `-GameDirectory` 的对应集成验证。
3. 修改原生 hook 时检查入口字节、ASLR、寄存器、标志位、栈平衡、跳转目标与分配区边界。签名不匹配应报错，不应强行写入。
4. 用能证明行为的回归测试覆盖修复；UI 文案与文档小改动不必堆叠重复测试。
5. 有配置、使用方式或兼容性变化时同步更新文档与变更记录。新增发布文件需同步 `scripts/package.py` 的显式清单。

提交说明应写清触发条件、改动后的行为、执行过的验证及剩余限制。贡献原创内容时请确认可按本项目 MIT 许可提供；第三方代码或素材需保留来源和自身许可。
