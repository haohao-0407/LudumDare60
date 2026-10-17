# 打捞逻辑接入说明

命名空间：`LudumDare60.Salvage`。本目录只包含逻辑、数据和可选的 Unity 生命周期适配器，没有 UI、音效、动画、场景物体或按键绑定。

## 操作与规则

1. 开始打捞，进入 `Searching`，随机等待信号。
2. 进入 `SignalDetected` 后，在 `SignalWindow` 秒内重新按下控制键，或调用 `TryLockTarget()`。提前一直按住不会自动锁定。
3. 进入 `Salvaging`：按住使控制区加速上升，松开使其下落；控制区有惯性、速度上限和边界反弹。目标会随机选择目的地并平滑移动。
4. 目标点处于控制区内时增加进度，离开时降低进度。进度到 1 成功，到 0 失败，超过最大作业时间也失败。
5. 成功、失败、取消后可以再次开始。作业过程中再次开始会返回 `false`，不会覆盖当前作业。

状态流转：`Idle → Searching → SignalDetected → Salvaging → Succeeded / Failed`。等待时间为 0 时直接进入 `SignalDetected`。所有活动阶段都允许取消；暂停不改变阶段。

## 在 Unity 中使用

1. 通过 `Create > Salvage > Definition` 创建配置资产，填写目标 ID、奖励 ID、数量范围和操作参数。默认 `scrap` 是待接库存系统的占位 ID。
2. 在之后负责终端交互的 GameObject 上添加 `SalvageController`，设置 `Default Definition`。组件不会自动开始打捞。
3. 交互系统调用 `TryStartSalvage()`，或 `TryStartSalvage(definition, randomSeed)`，开始指定目标。
4. 输入系统/未来 UI 在按下时调用 `PressControl()`，松开时调用 `ReleaseControl()`；也可统一调用 `SetControlHeld(bool)`。
5. 订阅 `SnapshotChanged` 更新显示，订阅 `Completed` 接收作业结果。显示首次打开时先读取 `Snapshot`，关闭时取消订阅。

```csharp
using LudumDare60.Salvage;

// controller 为由调用方取得的 SalvageController 引用。
controller.SnapshotChanged += snapshot =>
{
    // snapshot.State：阶段；snapshot.TimeRemaining：当前阶段剩余秒数。
    // snapshot.ControlMin/ControlMax：控制区边界，0 为轨道底部，1 为顶部。
    // snapshot.TargetPosition：目标点；snapshot.Progress：打捞进度。
    // snapshot.IsTargetInControl / IsPaused / IsControlHeld：其他显示状态。
};
controller.Completed += result =>
{
    if (!result.IsSuccess) return;
    // 库存系统在这里接收 result.RewardId 和 result.RewardQuantity。
    // 使用 result.AttemptId 对自己的库存事务去重。
};
controller.TryStartSalvage();
```

以上是接口示意；实际组件应使用具名方法，在 `OnEnable` 订阅、`OnDisable` 取消订阅。

项目使用 Input System。接入现有 Input Action 时，由外部适配器读取按压边沿/保持状态后转发；若 Action 带 Hold interaction，应使用按下边沿（例如 `WasPressedThisFrame`），不要等待 Hold 的 performed 才锁定。打捞模块不读取键盘，也不启停共享 Action。

## 纯逻辑使用

`SalvageSettings`、`SalvageTypes`、`SalvageSession` 只依赖 `System`，不依赖 Unity。无需创建 GameObject 就可以使用：

```csharp
var session = new SalvageSession();
var settings = new SalvageSettings();
var target = new SalvageTarget("wreck-01", "scrap", 1, 3);
session.TryStart(settings, target, randomSeed: 12345);
// 由宿主循环调用 session.Tick(deltaSeconds)。
// 收到 SignalDetected 后重新按下：session.SetControlHeld(true)。
// 随后按需切换 true / false 来追踪目标。
```

- `Tick(float)` 由宿主驱动。内部固定步长为 1/120 秒，每次最多推进 5 秒，超出时间保留在积压中，后续 Tick 继续处理。暂停期间传入的时间不累计。
- Unity 适配器使用 `Time.deltaTime`，所以跟随 `Time.timeScale`。纯逻辑宿主可以自行决定时钟；不要同时用另一个循环推进同一作业。
- `SetPaused(true/false)` 冻结/恢复模拟，并释放保持输入；恢复后需要重新按住。取消在暂停期间同样有效。
- 禁用/销毁 Unity 组件会取消活动作业；失焦/应用挂起会释放输入，不会自动恢复按住状态。
- 启动作业时校验并复制参数。无效参数抛出异常，且不会覆盖上一作业；之后修改配置不会影响当前作业。
- 相同配置、种子和按模拟时间提供的输入可复现目标轨迹及奖励随机数；`AttemptId` 是每次独立生成的事务标识，不参与随机模拟。
- 快照不可写；`Result` 保留最后一次结果，直到下次成功开始。终态不会继续推进，也不会重复触发 `Completed`。
- `SnapshotChanged` 在公开命令完成或一次 Tick 推进后触发，不在内部子步中触发；单次长 Tick 可以跨过信号窗口并直接报告失败。`Completed` 在本次终态快照之后触发，参数始终对应结束的那次作业。
- 接口按主线程/单线程使用。事件处理程序应正常返回，避免抛出异常中断其他订阅者。

## 参数与结果

| 参数组 | 字段 | 含义 |
| --- | --- | --- |
| 信号 | `SearchDurationMin/Max`, `SignalWindow` | 搜索等待范围与响应时间 |
| 控制区 | `ControlSize`, `InitialControlPosition` | 轨道占比与初始中心位置 |
| 操控 | `LiftAcceleration`, `Gravity`, `MaxControlSpeed`, `BoundaryBounce` | 上升、下落、惯性与边界手感 |
| 目标 | `InitialTargetPosition`, `TargetSpeed`, `TargetAcceleration`, `RetargetIntervalMin/Max` | 初始位置、移动强度与变向频率；速度 0 可制作静止目标 |
| 进度 | `InitialProgress`, `ProgressGainPerSecond`, `ProgressLossPerSecond`, `MaxSalvageDuration` | 初始进度、每秒增减及作业限时 |

坐标及进度归一化为 `[0, 1]`；速度、加速度分别按“轨道长度/秒”“轨道长度/秒²”计。初始控制区必须完全位于轨道内；初始进度必须大于 0 且小于 1。单阶段时间最多为 600 秒，响应窗口/作业限时/变向间隔最少为 0.01 秒。

`SalvageEndReason` 区分 `Recovered`、`SignalMissed`、`TargetLost`、`TimedOut`、`Cancelled`。只有 `IsSuccess` 时奖励数量大于 0，数量范围含上下限；失败/取消数量为 0。

模块不消耗电力、扣除工具、移除世界目标或直接写入库存。后续世界/库存系统应在自己的事务中检查可打捞条件、处理资源消耗与奖励，并以 `AttemptId` 去重。本次没有添加这些尚不存在的系统。
