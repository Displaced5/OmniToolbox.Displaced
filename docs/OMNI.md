# Omni 在线模块

## 安装

要求国服 Dalamud API 15 与 **Omni 1.2.0.6 或更新版本**。在 Omni 的“插件设置 → 在线模块”填写：

```text
https://github.com/Displaced5/OmniToolbox.Displaced
```

刷新并安装“自动装配面部、时尚配饰”，然后在“树树妙妙屋 → 在线”中手动启用。模块内部名为 `ClothingPairingOnline`。设置中可以分别开启时尚配饰、面部配饰及各恢复触发条件。

本仓库专门发布 Omni 在线版，当前模块版本为 **1.2.3**。已有旧仓库来源时，请在 Omni 中改用本仓库，避免同时保留两个同名模块来源。模块内部名、命令及记忆路径保持一致。

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

构造函数只创建元数据与普通状态。`OnStart` 重置本次启用状态，将异步初始化交给 `FeatureLifetime.TrackStartup`，然后返回。通过 `CreateAsync<ModuleServices>` 获取宿主服务，异步读取独立记忆文件；载入结果和游戏数据目录仅回到框架线程后应用。宿主等待初始化成功后才将模块标记为启用，期间界面显示初始化状态。

框架更新、切区和登出事件均在订阅前分别登记 `FeatureLifetime.OnStopping` 解绑。回调捕获本次启用的服务；宿主服务由 Omni／Dalamud 管理，模块不自行销毁。停用时宿主先关闭入口、取消并等待托管工作，再调用 `OnStop` 保存记忆、清空请求与临时对象，最后释放本次启用的生命周期。部分初始化失败也走相同清理路径。

普通停用与启用复用模块实例，每次创建新的观察基线和游戏接口适配器，不继承旧恢复任务。初始化使用生命周期取消标记，停止后的服务完成回调不能重新订阅事件。所有原生访问仅在框架采样/提交路径执行。

1.2.3 修复了旧版本在框架调度器任务中调用同步 `Create<T>` 所造成的等待环。不能用 `.Wait()`、`.Result` 或同步 `GetResult()` 等待初始化，也不能以 `async void OnStart` 绕开宿主跟踪。

### 接入依据

已按固定提交阅读以下实现，并使用匹配的本机程序集检查：

- [Common：OmniModuleBase 启用、停止与销毁](https://github.com/YouShux/OmniToolbox.Common/blob/a3d42e61e23c9f2d5e72579609bd3213284529c5/Module/Abstractions/OmniModuleBase.cs)。
- [Common：模块操作协调器](https://github.com/YouShux/OmniToolbox.Common/blob/a3d42e61e23c9f2d5e72579609bd3213284529c5/Module/Runtime/ModuleOperationCoordinator.cs)。
- [TreePublic：AutoCommenceDuty 事件与资源登记](https://github.com/YouShux/OmniToolbox.TreePublic/blob/9e1594639a30ae095fd306bdcbb4975262e15a00/Automation/AutoCommenceDuty.cs)。
- [TreePublic：AutoMount 生命周期](https://github.com/YouShux/OmniToolbox.TreePublic/blob/9e1594639a30ae095fd306bdcbb4975262e15a00/Automation/AutoMount/AutoMount.cs)。
- [TreePublic：AutoFateSync 更新入口守卫](https://github.com/YouShux/OmniToolbox.TreePublic/blob/9e1594639a30ae095fd306bdcbb4975262e15a00/Combat/AutoFateSync.cs)。

公开内置模块可以由宿主传入构造配置，在线模块仍须符合外部模块的公共无参构造、内嵌 `Settings` 和可绑定 `config` 成员要求。这里保留在线模块契约，没有照搬内置模块的构造方式。

## 构建与发布

适配依据 [Omni 官方在线模块规范](https://github.com/YouShux/OmniToolbox.TreeHouseOnline)，采用单文件 `.cs` 交付。仓库根目录 `TreeHouseModules.json` 的 `File` 指向 `Modules/ClothingPairingOnline.cs`，文件名、可实例化模块类型和 `InternalName` 一致。

```powershell
.\build-online.ps1 -OmniDir '<Omni 程序集目录>' -DalamudDir '<Dalamud 程序集目录>' -DotnetPath 'dotnet'
```

脚本从 `src/` 与 `omni/` 源码合成单文件，使用真实宿主引用编译，运行逻辑和宿主契约检查，再输出摘要清单和交付副本。本仓库不需要独立 Dalamud 插件工程。所有 DLL、obj、bin 和核验记录位于项目外的“插件/OmniToolbox.Displaced”目录；宿主依赖不随仓库或模块分发。不要手工修改生成文件后保留旧摘要，更新应重新运行脚本并提高模块版本。

公开仓库清单即可接入自定义在线模块，不需要修改 Omni 官方仓库。Omni 仍会按自身规则验证仓库公开状态、摘要、版本及语言完整性。

当前验证：0 警告、0 错误，79 项恢复逻辑检查与 39 项 Omni 集成检查通过。除类型契约、配置、本地化及清单外，集成检查还在独立进程中使用实际 Common、FeatureLifetime 和 Dalamud 框架调度器调用本模块的启用、停用与最终释放，覆盖延迟服务、部分订阅失败、无效记忆文件、取消和反复启停。检查使用替代宿主服务和空 Lumina 数据表，不连接游戏或调用原生函数。**尚未在运行中的 Omni 里安装、启用或验证实际佩戴效果。**
