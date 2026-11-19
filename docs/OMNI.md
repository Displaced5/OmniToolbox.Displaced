# Omni 在线模块

## 安装

要求国服 Dalamud API 15 与 **Omni 1.2.0.6 或更新版本**。在 Omni 的“插件设置 → 在线模块”填写：

```text
https://github.com/Displaced5/OmniToolbox.Displaced
```

刷新并安装“自动装配面部、时尚配饰”，然后在“树树妙妙屋 → 在线”中手动启用。模块内部名为 `ClothingPairingOnline`。设置中可以分别开启时尚配饰、面部配饰及各恢复触发条件。

本仓库专门发布 Omni 在线版，当前模块版本为 **1.2.2**。已有旧仓库来源时，请在 Omni 中改用本仓库，避免同时保留两个同名模块来源。模块内部名、命令及记忆路径保持一致。

请先关闭独立版 Clothing pairing；在线模块检测到独立插件加载时暂停操作。在线模块与独立插件使用独立配置和配饰记忆，不自动读取或改写另一方的配置。首次使用需手动佩戴一次希望记录的配饰，登录、重载或重新启用不自动执行旧请求。

## 行为

沿用 B 方案：分别记录一件时尚配饰和一件面部配饰，完整保存面部款式与颜色。默认出本后恢复；面部默认在切换职业、切换套装造成缺失后恢复。可选下坐骑、脱战、普通切区触发。

手动摘除后暂停对应项，新套装或玩家已经佩戴另一件时采用当前选择。实际保留着的配饰不重复装备。恢复须等待状态稳定，有限重试并核对实际结果，不修改游戏套装、解锁状态或强制模型。

设置通过 Omni 公共控件绘制，全部界面及状态文本经 `ModuleLocalization` 与 `OmniLoc` 获取。提供简体词库，由宿主生成完整繁体；其他语言暂不提供，按 Omni 的语言完整性规则处理。

## 命令

| 命令 | 功能 |
|---|---|
| `/omni ClothingPairingOnline pause` | 暂停两类配饰恢复 |
| `/omni ClothingPairingOnline restore` | 请求恢复两类记忆 |
| `/omni ClothingPairingOnline face pause` | 只暂停面部配饰 |
| `/omni ClothingPairingOnline face restore` | 只恢复面部配饰 |
| `/omni ClothingPairingOnline fashion pause` | 只暂停时尚配饰 |
| `/omni ClothingPairingOnline fashion restore` | 只恢复时尚配饰 |

`resume` 等同 `restore`，`clear` 清除对应记忆。命令和窗口操作仍受模块、分类开关及游戏可用性约束。停用模块后不接受命令。

## 配置与生命周期

嵌套的 `Settings` 通过名为 `config` 的成员交由 Omni 绑定，偏好开关只在实际变更时报告保存。配饰记忆和暂停另存于 Omni 配置目录下的 `ClothingPairingOnline/memories.json`，避免记忆保存依赖设置面板是否打开。使用临时文件原子替换，写入失败保留旧文件并显示提示。

构造函数只创建元数据与普通状态。`OnStart` 载入当前配置和记忆，创建观察基线并订阅框架、切区和登出事件。解绑回调先登记到 `FeatureLifetime.OnStopping`；停用后停止接收工作、保存记忆并清空本轮请求。重新启用建立新基线，不继承旧恢复任务。所有原生访问仅在框架采样/提交路径执行。

## 构建与发布

适配依据 [Omni 官方在线模块规范](https://github.com/YouShux/OmniToolbox.TreeHouseOnline)，采用单文件 `.cs` 交付。仓库根目录 `TreeHouseModules.json` 的 `File` 指向 `Modules/ClothingPairingOnline.cs`，文件名、可实例化模块类型和 `InternalName` 一致。

```powershell
.\build-online.ps1 -OmniDir '<Omni 程序集目录>' -DalamudDir '<Dalamud 程序集目录>' -DotnetPath 'dotnet'
```

脚本从 `src/` 与 `omni/` 源码合成单文件，使用真实宿主引用编译，运行逻辑和宿主契约检查，再输出摘要清单和交付副本。本仓库不需要独立 Dalamud 插件工程。所有 DLL、obj、bin 和核验记录位于项目外的“插件/OmniToolbox.Displaced”目录；宿主依赖不随仓库或模块分发。不要手工修改生成文件后保留旧摘要，更新应重新运行脚本并提高模块版本。

公开仓库清单即可接入自定义在线模块，不需要修改 Omni 官方仓库。Omni 仍会按自身规则验证仓库公开状态、摘要、版本及语言完整性。

当前验证：0 警告、0 错误，76 项恢复逻辑检查与 27 项 Omni 集成检查通过；真实 Common 接受模块类型和配置，简繁词库完整，交付文件摘要匹配。逻辑检查不再包含独立插件的编译身份检查。**尚未在运行中的 Omni 里安装、启用或验证实际佩戴效果。**
