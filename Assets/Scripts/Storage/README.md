# 库存系统

命名空间：`LudumDare60.Storage`。库存仅保存在内存中：核心 `Inventory` 不依赖 Unity；`InventoryController` 持有当前游戏过程中的库存，`SalvageInventoryBridge` 连接打捞完成事件。没有文件读写、自动存档或读档接口。

## 数据与规则

- 一个物品 ID 对应一项库存，数量为正整数；ID 区分大小写，直接使用 `SalvageResult.RewardId`，例如 `scrap`。暂不限制容量，也不引入尚未定义的格子、品质、重量或装备规则。
- 物品名称、图标等静态配置由 `ItemCatalog` 根据 ID 查找。内存中只保留物品 ID、数量和本次库存对象已领取的奖励事务 ID。
- 同 ID 自动合并；单项数量最多为 `int.MaxValue`。空白 ID、非正增减量、数量不足、溢出等操作返回 `false`，不改变数据。
- 修改数量只接受已有物品；设为 0 删除该项，负数无效。删除/清空不删除奖励领取历史，已消耗的打捞奖励不能重新领取。
- 查询结果是只读快照，不能绕过接口修改库存。`Changed` 在一次成功修改后触发；无效操作和数量未变不触发。
- 按主线程/单线程使用。事件处理程序应正常返回；不要抛出异常中断其他订阅者。

## 增删改查

```csharp
using LudumDare60.Storage;

var inventory = new Inventory();
inventory.TryAdd("scrap", 3);             // 增：创建或合并，得到 3
inventory.TryAdd("scrap", 2);             // 现在为 5
inventory.TryRemove("scrap", 1);          // 减：现在为 4，数量不足时不扣除
inventory.TrySetQuantity("scrap", 10);    // 改：已有项改为 10
inventory.TryGetItem("scrap", out var item);
int quantity = inventory.GetQuantity("scrap"); // 不存在时为 0
var all = inventory.GetItems();            // 按 ID 排序的只读快照
var matches = inventory.FindItems("scr"); // ID 子串搜索，不区分大小写
inventory.TryDelete("scrap");              // 删：整项删除
inventory.Clear();                         // 清空物品，保留奖励去重历史
```

`DistinctItemCount` 是物品种类数；`TotalQuantity` 是总件数（`long`）。`TryGetItem` 不存在时返回 `false`。`TryAddOnce(rewardId, itemId, quantity)` 将奖励入库和事务去重作为一次修改：空事务 ID、重复事务或无法完整入库时返回 `false`，且不会提前消耗事务 ID。

## Unity 接入

1. 在负责当前游戏状态的 GameObject 上添加 `InventoryController`，其他系统共享其引用。
2. 控制器首次初始化时创建空库存，库存随宿主对象存活。需要在本局跨场景保留物品时，由场景管理系统保留宿主对象；新一局创建新的控制器，库存和奖励去重历史都会重新开始。
3. 在打捞终端的 `SalvageController` 所在物体上添加 `SalvageInventoryBridge`，将 `Inventory Controller` 指向上述共享库存。`Salvage Controller` 留空时使用同物体的组件。
4. 桥接器订阅 `Completed`，只有成功结果会入库，使用 `AttemptId` 去重。失败和取消不入库。重新启用桥接器会尝试接收源控制器保留的最后一次结果；重复接收安全。桥接器应覆盖整个作业周期，禁用期间多次完成的历史结果不会被源控制器保留。
5. UI/其他系统通过 `inventoryController.Items` 调用上述接口，通过 `inventoryController.Changed` 刷新显示；在 `OnEnable` 订阅、`OnDisable` 取消订阅，并在打开时读取一次当前库存。

`Assets/Scenes/TerminalDialogueDemo.unity` 已完成场景接入：`Salvage Screen (World Space)` 根物体同时挂载 `SalvageController`、`InventoryController` 和 `SalvageInventoryBridge`，桥接器的两个引用指向同物体上的对应组件。成功打捞后自动增加内存中的库存，失败/取消不入库。停止游戏或销毁该库存对象后，物品不会保留到下次游戏。

菜单 `Tools > Salvage > 在当前场景搭建打捞屏` 也会创建并绑定这三个组件。新场景已有本局的共享库存时，将桥接器改为引用该库存，并移除屏幕上的独立库存。

## CRT 库存显示屏

`TerminalDialogueDemo` 场景中的 `Storage Screen` 已制作成世界空间库存屏，与对话屏、打捞屏共用 Glass TTY 字体。画面包含绿字清单、扫描线、暗角、缓慢移动的亮带、物品种类/总件数、连接状态和空库存提示；超过显示高度后可用鼠标滚轮查看。

`InventoryScreenView` 绑定打捞屏上的共享 `InventoryController`，通过 `Changed` 刷新列表。成功打捞、增加/扣除/删除物品都会更新显示；禁用时取消订阅，重新打开时读取最新快照。显示屏只查询库存，不创建第二份库存，也不修改物品。`ONLINE` / `OFFLINE` 表示是否绑定了库存控制器。

`ItemCatalog` 的名称包含可打印 ASCII 字符时直接使用；当前中文名称超出 Glass TTY 的字库，因此回退为物品 ID 转成的英文名称（与打捞屏一致）。物品数量和总数带千位分隔符。列表按物品 ID 排序，缺少目录条目仍可正常显示。

菜单 `Tools > Storage > 在当前场景搭建库存屏` 可重新创建显示内容，优先使用场景中已有的 `Storage Screen` 并保留其位置；只有 `Inventory Display` 子层级由生成器管理。它会复用场景中的库存，并补齐缺少的打捞桥接。界面显示逻辑在 `InventoryScreenView.cs`，CRT 图形在 `InventoryCrtOverlay.cs`，场景生成器在 `Editor/InventoryScreenBuilder.cs`。

## 内存生命周期

```csharp
// inventoryController 是场景中的共享 InventoryController 引用。
inventoryController.Items.TryAdd("scrap", 1); // 只修改当前内存，立即通知界面
inventoryController.Items.TryRemove("scrap", 1);
```

- 暂停、关闭库存屏、失焦不会清空仍然存活的库存对象，也不会写入文件。
- `Clear()` 清空当前物品，同时保留本对象已领取的奖励 ID，防止已消耗的奖励被重复领取。
- 全新一局应创建新的 `InventoryController` / `Inventory`，以同时清空物品和奖励历史。保留旧控制器的场景切换仍共享本局库存。
- 退出游戏后内存释放，下次游戏从空库存开始。旧版生成的 JSON 文件不会再被读取或更新。

纯逻辑宿主可以直接 `new Inventory()` 并订阅 `Changed`，无需创建场景对象。
