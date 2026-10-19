# SHIP 组装工作台

SHIP 左上角 `ShipRig/CompactCockpit_WallIntegrated/Screen_RADAR/Geometry/SCREEN_RADAR`
显示组装工作台。场景里的 `Ship Assembly System` 持有组装会话，`ShipRig/Assembly Screen`
只负责显示和输入。原 `Assets/AssemblyDemo/Scenes/NavigationAssembly.unity` 与其 Prefab 保留。

## 配置

四份可编辑的 ScriptableObject 在 `Assets/ShipAssembly/Recipes/`。每份配置包含稳定配方 ID、
维修设备、材料需求、组装 Prefab、相机大小和拼图偏移。每份拥有独立实例和进度。

| 配方 | 组装 Prefab | 零件数 |
| --- | --- | ---: |
| Power Unit | `EnergyAssembly.prefab` | 3 |
| Interstellar Navigation System | `NavigationAssembly.prefab` | 4 |
| Propulsion Engine | 暂复用 `NavigationAssembly.prefab` | 4 |
| Life Support System | `LifeSupportAssembly.prefab` | 5 |

能源、生命关卡及其 `DeviceAssemblyLayout` 来自
`origin/codex/assembly-snapshot-engine-pending-20261019` 的 `90402b9`。
该提交尚无引擎关卡，因此引擎保留原配置。只接入拼装资源，没有导入远端屏幕、场景或维修逻辑。
能源与生命配方的 `PuzzleOffset` 抵消各自的 `viewCenter`，适配现有工作台相机。

| 材料 | Power Unit | Interstellar Navigation System | Propulsion Engine | Life Support System |
| --- | ---: | ---: | ---: | ---: |
| Rolled Electromechanical Alloy Sheets | 6 | 4 | 7 | 5 |
| Tinned Copper Wire & Windings | 4 | 5 | 3 | 3 |
| Precision Drive Gear Blanks | 2 | 7 | 3 | 2 |
| Heat-Resistant Ceramic Insulators | 7 | 2 | 6 | 2 |
| Vulcanized Airtight Rubber Liners | 3 | 2 | 4 | 7 |

维修按能源 0 → 导航 1 → 引擎 2 → 生命 3 顺序进行。`ShipAssemblyController` 和现有
`RepairScreenController` 都保留顺序限制。配方使用物品 ID，显示名称来自 `Assets/Salvage/ItemCatalog.asset`。

## 扣料与材料来源

- 点击 START ASSEMBLY 时先检查顺序、配方、全部材料和玩法资源，然后整批扣料一次。
- 材料不足不扣任何一项；重复开始／继续、RESET PARTS、隐藏屏幕均不再次扣料。
- 切换配方或离开屏幕保留已付费会话；重排仅恢复该会话的初始零件位置。
- 拼装全部连通后记录设备完成，并同步维修屏的颜色、状态文字、百分比和进度条。
- 已完成设备锁定，不能再次开工或通过拆件回退维修状态。
- 库存与会话保持现有单局内存生命周期，不跨退出游戏保存。

SHIP 的 `SalvageInventoryBridge.materialYieldCatalog` 已连接
`Assets/Salvage/SalvageMaterialYields.asset`。成功打捞按项目剧情资料里的拆解产出，直接入库五类材料：

| 原打捞物 ID | 合金 | 铜线 | 齿轮 | 陶瓷 | 橡胶 |
| --- | ---: | ---: | ---: | ---: | ---: |
| thermal_liner_bushing | 3 | 1 | 1 | 4 | 2 |
| fluid_manifold_assembly | 3 | 1 | 1 | 1 | 4 |
| relay_switch_unit | 2 | 2 | 4 | 1 | 1 |
| vacuum_tube_receiver | 2 | 4 | 1 | 2 | 1 |
| electromechanical_gauge_set | 3 | 2 | 1 | 1 | 1 |

产出表同样可配置，数量乘以本次打捞奖励数量；事务按 AttemptId 去重。其他未绑定产出表的场景仍按原方式获得零件。

## 操作

使用屏幕左右箭头选择设备；材料齐全且满足前置维修时点击 START ASSEMBLY。
点击拼图区域取得操作焦点。

- 鼠标点击选择，按住左键拖动零件；WASD 也可移动。
- Q/E 旋转，Shift 微调，数字键或 Tab 选择零件（能源 1–3，导航／引擎 1–4，生命 1–5）。
- X / DETACH 拆开选中零件，R / RESET PARTS 重排当前拼图，不重复扣料。
- 点击屏幕外、按右键观察或按 Esc 释放操作焦点；返回时点击拼图继续。
- Esc 同时遵循现有 ESC 菜单行为，菜单打开或游戏暂停时停止组装输入。

动作配置在 `Assets/InputSystem_Actions.inputactions` 的 `Assembly` action map。
屏幕输入使用私有动作实例，不会启停驾驶舱或 UI 共用的动作。

## 场景接线

- `Ship Assembly System.Inventory` 指向打捞与库存屏已共用的库存；不要另建库存。
- `RepairScreen` 指向 `REPAIR_SCREEN_Rig`。
- 独立工作台在世界 XY 平面，保持 rotation identity 与 scale 1，使用 Layer 29 `ShipAssembly`。
- `Assembly Camera` 只渲染该层到 `Assets/ShipAssembly/AssemblyScreenRT.renderTexture`。
- 主相机排除组装层。世界 Canvas 内的 RawImage 再通过已有曲面终端显示组件投影到左上屏。
- 业务组件在屏幕 Canvas 外；隐藏 Canvas 不销毁组装实例。

编辑器菜单 `Tools > Ship Assembly > Install In SHIP And Save` 可补装引用和创建缺失资产；
它只接受当前打开的 SHIP，不会打开或改写 NavigationAssembly，也不会覆盖已有配方配置或屏幕布局。

本次交付只编辑、导入和保存资源，未执行自动化测试或 Play Mode。运行时交互由项目作者验收。
