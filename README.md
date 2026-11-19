# OmniToolbox.Displaced

Displaced 的 Omni 在线模块仓库，当前提供 **自动装配面部、时尚配饰 1.2.2**。

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

