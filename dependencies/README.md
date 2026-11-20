# 外部工具

**构建和基础测试不需要这些工具；应用游戏设置需要增强器。**

修改器通过反射读取 `RoadRash30thEnhancement-v1.1.0.exe` 中的选项和补丁接口。应用设置时使用与下方摘要匹配的 v1.1.0 文件。

| 文件 | 用途 | 获取说明 |
| --- | --- | --- |
| `RoadRash30thEnhancement-v1.1.0.exe` | 必需；设置读取、磁盘补丁、备份与还原；当前版本含高清与汉化资源 | 从该增强器原发布者处取得，或使用自己已有、摘要一致的文件。独立公开下载地址尚未确认，缺少时请向本项目维护者询问原始来源 |
| `RoadRash_HD_Setup.exe` | 可选，单独安装或维护高清补丁 | [上游仓库与 Releases](https://github.com/OBLPlife/RoadRash30th-HD) |
| `RoadRash_ZH_CN_Setup.exe` | 可选，单独安装或维护汉化补丁 | [上游仓库与 Releases](https://github.com/OBLPlife/RoadRash30th-Chinese) |

验证过的本地文件大小与 SHA-256 记录在 [verified-files.json](verified-files.json)，用于辨认测试所用版本，不代表下载地址、数字签名或对未知文件的安全背书。独立安装器的新版本不等同于已经验证的组合。

安装目录：

```text
游戏目录/
  RoadRash95.exe                       # 自己安装的游戏
  暴力摩托增强修改器.exe
  launcher_settings.ini
  Tools/
    RoadRash30thEnhancement-v1.1.0.exe   # 必需外部依赖
    player_car.png                     # 仅用于旧版叠加方案
```

可选的独立安装器也可放在 `Tools/`。修改器调用增强器内置的安装逻辑。还原时使用创建相应备份的工具。

可以用下面的命令查看已有文件摘要：

```powershell
Get-FileHash "D:\Games\RoadRash\Tools\RoadRash30thEnhancement-v1.1.0.exe" -Algorithm SHA256
```

源码包与独立修改器包仅附依赖清单。玩家版沿用经摘要核对的 v1.1.0 增强器，高清与汉化的独立安装器由玩家按需获取。[第三方说明](../THIRD_PARTY.md)
