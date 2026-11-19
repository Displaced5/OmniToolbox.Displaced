# OmniToolbox.Displaced

Displaced 的 Omni 在线模块仓库，当前提供 **自动装配面部、时尚配饰 1.2.2**。

此名称仅用于 Omni 在线模块；独立 Dalamud 插件仍为 **Clothing pairing**。

分别记忆一件时尚配饰和一件面部配饰，保存面部款式及颜色。沿用 B 方案：出本后恢复，面部配饰在切换职业、套装造成缺失后恢复；手动摘除后暂停，当前佩戴选择优先。

## 安装

要求国服 Dalamud API 15、**Omni 1.2.0.6 或更新版本**。

在 Omni 的“插件设置 → 在线模块”添加：

```text
https://github.com/Displaced5/OmniToolbox.Displaced
```

刷新并安装 **自动装配面部、时尚配饰**，然后在“树树妙妙屋 → 在线”手动启用。先关闭独立版 Clothing pairing，首次使用手动佩戴一次希望记录的配饰。

如果已使用旧仓库中的同名在线模块，请在 Omni 中改用本仓库，避免同时保留两个来源。模块内部名 `ClothingPairingOnline`、命令和记忆路径保持一致。

设置可分别控制两类配饰及恢复触发条件。命令示例：

```text
/omni ClothingPairingOnline pause
/omni ClothingPairingOnline restore
/omni ClothingPairingOnline face restore
/omni ClothingPairingOnline fashion restore
```

详细行为、命令、配置和接口说明见 [Omni 文档](docs/OMNI.md)。

## 开发

源码与编译产物分别存放：

```text
工作目录/
  源码/
    OmniToolbox.Displaced/
      Modules/                 Omni 下载的单文件源码
      src/                     配饰恢复逻辑和游戏接口
      omni/                    Omni 入口、服务和本地化
      tests/                   使用合成数据的逻辑检查
      omni-tests/              实际宿主契约检查
      TreeHouseModules.json    在线模块清单
      build-online.ps1
  插件/
    OmniToolbox.Displaced/
      .build/                  编译中间文件
      1.2.2/                   交付副本和本地核验记录
```

安装 .NET SDK 10，使用匹配的 Dalamud 与 Omni 程序集：

```powershell
.\build-online.ps1 -OmniDir '<Omni 程序集目录>' -DalamudDir '<Dalamud 程序集目录>' -DotnetPath 'dotnet'
```

构建脚本生成单一模块源码、编译并运行检查、更新清单 SHA256。只分发模块源码和清单，不包含宿主 DLL、PDB、玩家配置、日志或研究快照。

当前构建为 0 警告、0 错误，76 项恢复逻辑检查、27 项 Omni 集成检查通过。尚未完成游戏内加载、界面和实际佩戴验证。

参考 [Omni 在线模块规范](https://github.com/YouShux/OmniToolbox.TreeHouseOnline)。
