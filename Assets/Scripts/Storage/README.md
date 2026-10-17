# 库存系统

命名空间：`LudumDare60.Storage`。核心 `Inventory` 不依赖 Unity；`InventoryJsonStore` 负责 JSON 文件，`InventoryController` 负责 Unity 生命周期，`SalvageInventoryBridge` 连接打捞完成事件。

## 数据与规则

- 一个物品 ID 对应一项库存，数量为正整数；ID 区分大小写，直接使用 `SalvageResult.RewardId`，例如 `scrap`。暂不限制容量，也不引入尚未定义的格子、品质、重量或装备规则。
- 物品名称、图标等静态配置可由后续物品目录根据 ID 查找。当前保存的数据是物品 ID、数量、存档格式/版本，以及已领取的奖励事务 ID。
- 同 ID 自动合并；单项数量最多为 `int.MaxValue`。空白 ID、非正增减量、数量不足、溢出等操作返回 `false`，不改变数据。
- 修改数量只接受已有物品；设为 0 删除该项，负数无效。删除/清空不删除奖励领取历史，已消耗的打捞奖励不能重新领取。
- 查询结果是只读快照，不能绕过接口修改库存。`Changed` 在一次成功修改后触发；无效操作和数量未变不触发。加载有效存档也会触发。
- 按主线程/单线程使用。事件处理程序应正常返回；不要抛出异常中断保存或其他订阅者。

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

1. 在负责玩家数据的 GameObject 上添加 `InventoryController`。每个存档文件只使用一个控制器，其他系统共享其引用。
2. 默认启用 `Load On Awake` 和 `Auto Save`，保存地址为 `Application.persistentDataPath/inventory.json`；可通过 `SavePath` 查看完整路径。`Save File Name` 只能填写文件名。跨场景时由现有场景管理系统保留宿主对象，或在新场景加载同一份存档。
3. 在打捞终端的 `SalvageController` 所在物体上添加 `SalvageInventoryBridge`，将 `Inventory Controller` 指向上述共享库存。`Salvage Controller` 留空时使用同物体的组件。
4. 桥接器订阅 `Completed`，只有成功结果会入库，使用 `AttemptId` 去重。失败和取消不入库。重新启用桥接器会尝试接收源控制器保留的最后一次结果；重复接收安全。桥接器应覆盖整个作业周期，禁用期间多次完成的历史结果不会被源控制器保留。
5. UI/其他系统通过 `inventoryController.Items` 调用上述接口，通过 `inventoryController.Changed` 刷新显示；在 `OnEnable` 订阅、`OnDisable` 取消订阅，并在打开时读取一次当前库存。

本模块提供数据、保存和接入组件，没有新增场景物体或库存界面；当前场景还没有配置打捞终端，按上述步骤接入实际终端。

## 保存、加载与错误处理

```csharp
// inventoryController 是场景中的共享 InventoryController 引用。
inventoryController.Items.TryAdd("scrap", 1); // 默认立即自动保存
bool saved = inventoryController.Save();     // 手动保存
bool loaded = inventoryController.Load();    // 手动重读；成功后替换内存数据
```

- 自动保存发生在库存变更之后，应用挂起/退出或宿主销毁时会再尝试保存待写入数据。关闭 `Auto Save` 后由调用方显式调用 `Save()`。
- 保存先写同目录 `.tmp` 文件并刷新，再替换正式文件；覆盖时将上一版保留为 `.bak`。物品和事务历史写在同一文件，避免正常读档后重复领取。
- 读档会校验格式、版本、物品数量/重复 ID 和奖励事务 ID；全部通过才替换内存数据。首次没有存档时以空库存开始，不立即创建文件。
- 读档损坏或版本不支持时，通过 Console、`LastError` 和 `PersistenceFailed` 报告；`CanSave` 变为 `false`，阻止覆盖旧文件，也阻止打捞桥接器在缺少领取历史时发奖。先备份问题文件、用有效 `.bak` 恢复正式文件，再调用 `Load()`；新游戏则显式移走旧文件并重启宿主。没有自动回退或删除旧档的行为。
- 写入失败时，内存修改保留，`HasUnsavedChanges` 为 `true`，可在处理错误后再次 `Save()`。入库成功表示内存修改成功，持久化状态须另看 `HasUnsavedChanges`/`LastError`。
- 手动 `Load()` 会丢弃尚未保存的修改；失败则保留现有内存数据。请只在需要重新读档时调用。

纯逻辑宿主可使用 `CreateSaveData()`/`TryRestore(data, out error)` 获取或恢复数据，或者直接调用 `InventoryJsonStore.TrySave/TryLoad`，无需创建场景对象。存档是本地 JSON，不承担防作弊或云同步功能。
