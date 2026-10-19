# SHIP → Mars 着陆、维修与起飞

打开 `Assets/Scenes/SHIP.unity` 后进入 Play，先进入正式开局对话，四个设备保持正常，维修与打捞屏显示待机画面。对话到 25-AUG 日志选择 `BEGIN LANDING`，才启动约 45 秒的着陆演出。按住鼠标右键拖动转头，原相机初始位置和朝向保留；ESC 使用现有菜单暂停。左键继续用于终端。

## 开局与对话驱动

`Landing Sequence/CockpitOpeningController` 用显式场景引用连接现有对话、维修、打捞与着陆组件：

1. 开始时通过 `ShipAssemblyController.InitializeOperationalDevices()` 初始化完整健康状态，不消耗材料或触发真实维修完成回调；播放健康舱内底噪。维修业务入口与打捞业务入口都关闭。
2. 维修工作台、维修诊断屏和打捞屏各覆盖一张绿色 CRT 待机画面，由 `CockpitStandbyScreen` 管理。原 Canvas、相机、曲面屏和 RenderTexture 保留；遮罩同时阻止交互，并禁用相关轮询输入脚本。
3. 正式对话使用独立的 `Assets/Landing/PioneerOpeningStory.asset`，原 MainStory 与预览资产保留。`pioneer60.begin_landing` 事件调用 `BeginLanding()`；下降期间不会提前进入损坏报告。加载失败或取消会跳转 `landing_retry`，允许重试。
4. 实际 `OnTouchdown` 先调用 `ApplyCrashDamage()`，清掉旧拼装会话与完成记录，并一次性提交四项全损状态；随后开放维修工作台/诊断信息，再由原硬着陆效果播放撞击、扬尘、震动和漏电。打捞继续待机。
5. 余振结束的 `OnLanded` 将对话转到 `landing_crash_report`，然后继续原维修教学。教学结束进入 `operations_menu`，选择 `ACTIVATE SALVAGE` 发出 `pioneer60.enable_salvage`，开放打捞屏及其输入；现有打捞逻辑会在允许输入时自动开始作业。

三处漏电仍跟随对应维修部件，四项全修好后恢复健康底噪。`SalvageController.FeatureEnabled` 与现有菜单/任务暂停相互独立；其他场景默认保持功能开启。维修的 `RepairsAvailable` 也默认开启，只由本场景开局流程锁定。

## 全部维修后的检查与起飞

`Landing Sequence` 新增 `CockpitDepartureController` 与 `LaunchSequenceController`。前者读取同一维修系统的四项状态，并且要求已经坠毁、着陆完成，才会自动进入 `departure_ready`；开场四项正常不会误触发离场。检查使用已有部件状态，不替代真实组装判定，也不消耗材料。

1. 全部修好后，终端提示执行 `RUN PRE-FLIGHT CHECKS`；选 `NOT YET` 可以先返回操作菜单，再通过 `PRE-FLIGHT CHECKS` 回来。
2. 按电源、导航、推进引擎、生命维持顺序逐项确认测试。普通检查约 2.5 秒，引擎测试约 3.5 秒，诊断屏显示各项百分比与 `PASS`，同步播放对应设备检查音效。`departure_testing` 没有自动下一节点，只有实际检查结束才进入下一项，快进对话不会直接完成检测。
3. 四项全通过后出现 `LAUNCH`。确认时再次检查完整维修状态与检测结果，关闭维修和打捞操作，屏幕切为飞行状态；不会因为组装完毕就自动发射。任何部件状态失效都清除检查结果；中止或禁用途中流程后必须重新检查。
4. 起飞默认共 40 秒：3 秒倒数、2 秒点火预热，随后真实移动 `ShipRig` 离地加速，并施加轻微俯仰与引擎振动；地面大范围扬尘、点火照明、引擎声同步播放。
5. 升空过程中逐步淡化火星光照、地面雾和后处理，穿过完全覆盖舱外的大气雾罩后隐藏火星外景；然后星空显现、火星缩小。驾驶舱和自由观察相机保持同一套对象，演出不改相机局部位置或视角。
6. 抵达轨道后熄火、收束震动，屏幕显示 `ORBIT / CRUISE`，播放返航对话并保持太空画面。终端节点 `launch_ignition / launch_ascent / launch_exit / launch_orbit` 跟随真实动画阶段，无法靠快进提前抵达轨道。

`LaunchSequenceController.Duration` 包含倒数与预热；`CountdownDuration / IgnitionDuration / AscentHeight / ForwardDistance / PitchDegrees` 控制节奏与轨迹，`AccelerationShakePosition / AccelerationShakeRotation / GroundDustEmission / LiftoffDustBurstCount` 控制表现。音效使用现有 `STORY_ready_departure / STORY_power_restore / STORY_navigation_online / STORY_engine_test / STORY_life_support_valve / STORY_takeoff / AMB_Engine_Idle_Loop / UI_confirm`。

ESC 使用现有暂停菜单，检查计时、起飞移动、粒子和各自音源都随暂停停止。`CancelLaunch()` 或起飞中禁用组件会清理新效果、恢复起飞前地面姿态与环境，并开放原来可用的维修/打捞功能；不会卸载借用的火星场景或启用其重复驾驶舱、相机。轨道完成后保留太空状态。

SHIP 当前未绑定独立 `ReturnWindow` 演示场景的任务时钟/结局组件，本流程不调用会立即暂停游戏的旧结算入口。此处完成到离开火星、建立轨道与返航航线，不添加地球抵达或新关卡。

## 场景与参数

- `ShipRig` 保留原驾驶舱、相机、两块终端。`CameraLook` 负责转头，`CameraShake` 负责演出震动。
- `Landing Sequence` 上的 `Duration` 调整总时长，`ApproachHeight` / `ApproachOffset` 调整最后下降段；后者相对于火星落点朝向。
- `AtmosphericEntryClip` 使用新增的 `494827__nicktimesaudio__seamless-rocket-booster-roar-crackle.mp3`，`AtmosphericEntryVolume` 默认 `0.55`。进度 18% 开始循环，23% 完成渐入，55%–69% 淡出并停止；默认 45 秒演出对应约 8.1 秒开始、31.05 秒停止。ESC 暂停保留播放位置，取消/失败/禁用会停止并清理音源。
- 开场火星在 `SpaceToMarsSky` 材质中微调。着陆启动时读取当前天空的火星方向、角半径、受光方向、可见度和大气/辉光参数；从这些初值开始动画，受光方向随进入大气过渡到场景太阳。当前天空使用不同 shader 时回退到 `TransitionSkyTemplate`。目标地面的全景、曝光和旋转在雾罩完全遮挡时切换，不会因场景加载完成而提前覆盖开场材质。
- SHIP 中 `AutoStart` 已关闭，由上述对话事件调用 `BeginLanding()`。`Progress`、`Phase`、`HasLanded` 可查询状态，`OnTouchdown` 和 `OnLanded` 用于接声音、任务或舱门。
- `MarsBase_RetroFuture` 的 `Mars Landing Environment/LandingAnchor` 保存原基地驾驶舱的位置、朝向。移动此锚点即可调整落点。
- 火星基地异步 Additive 加载。驾驶舱、终端、相机和 ESC 始终留在 SHIP，着陆完成后火星基地成为活动场景。
- 火星的展示相机、AudioListener 与重复驾驶舱仅在着陆过程中关闭。单独打开火星场景仍保留原展示配置。
- 火星 `Global Volume` 保持在外景根之外，能够在外景显示前逐步混合调色；原火星天空资产保留。

## 演出节奏

约前 18% 为太空接近，随后进入大气。47–55% 期间舱外薄雾完全遮蔽远景，50% 时将飞船对齐到基地上空。如果加载未完成，会停留在此段等待。71% 前薄雾散开，93% 时到达落点，最后播放冲击与余振、停止喷尘。

星空与远处火星由程序天空材质绘制。最后一段由真实地面视差表现下降；扬尘使用柔边粒子。触地前保留约 4 m/s 的垂直速度，触地瞬间产生大范围尘土、撞击音和约 3 秒的六轴衰减摇晃。即使缩短 `Duration`，余振也会完整播放后再发出 `OnLanded`。扬尘停止发射后自然消散。

## 硬着陆与舱内损毁

- `Landing Sequence` 的 `TouchdownSpeed` 调整触地速度；`HardLandingShakeDuration` / `HardLandingPosition` / `HardLandingRotation` 调整冲击时长、位移和角度；`DustEmissionRate` / `DustBurstCount` / `DustRadius` 调整扬尘。震动只写入 `CameraShake`，结束或取消后恢复其原始姿态，不改变已调整的相机位置。
- `ShipRig/Hard Landing Damage` 上的 `HardLandingDamageEffects` 自动订阅 `OnTouchdown`。三处故障点是左侧电源继电器、右侧控制台线路和顶棚线束，可直接在场景里移动这些新节点以调整火花位置。
- 三处故障使用指定样例目录的 `Sparks.vfx`，按驾驶舱米制尺度缩小粒子速度、寿命和尺寸。触地后约 0.2 / 0.5 / 0.8 秒错峰喷发，此后各自每 2.5–7.5 秒间歇漏电，并同步短闪光和电流噼啪声。
- 撞击使用 `Pioneer60Audio/Audio/03_Composites/CRASH_Impact_Short.wav`，金属变形使用 `CRASH_Hull_Buckle.wav`；火花使用 `SFX_electrical_glitch_01/02.wav` 脉冲串。短暂警报使用现有 `WindowWarning.wav`，持续约 6 秒；受损环境声使用 `AMB_Cabin_Damaged_Loop.wav` 低音量循环。各子物体的 `AudioSource.volume` 可单独调音，仍受 ESC 菜单总音量控制。
- ESC 暂停时这些专属 VFX、音频和演出计时一起暂停，恢复后继续。取消或加载失败会清理演出。着陆结束后漏电和受损环境音继续存在。
- `IsDamaged` 可读取损坏表现状态；`StopDamageEffects()` 仅静音/关闭效果并保留损坏状态。`RepairAll()` 是表现层的手动清除接口，不会完成真实维修、扣除材料或更新维修屏。正常流程由下述桥接组件同步，避免直接调用 `RepairAll()` 绕过四项维修完成条件。

## 与现有维修系统联动

`HardLandingRepairBridge` 显式引用场景中的 `HardLandingDamageEffects` 与 `RepairScreenController`。既有 `ShipAssemblyController` 在真实拼装完成后写入维修屏状态，桥接组件订阅 `StateChanged`，只有装置达到 `Repaired` 才关闭对应火花、闪光和电流声；`Repairing` 仍属于故障状态。桥接不会改写维修状态、零件库存或拼装判定。

| 火花点 / `Effects.Sites` 下标 | `SiteDevices` 默认映射 | 维修屏装置下标 |
| --- | --- | --- |
| 左侧电源继电器 / 0 | `PowerUnit` 能源装置 | 0 |
| 右侧控制台线路 / 1 | `Navigation` 导航装置 | 1 |
| 顶棚线束 / 2 | `LifeSupport` 生命装置 | 3 |

三处火花全部停止不代表飞船全部修好：健康底噪必须等 `RepairScreen.AllRepaired`，即能源、导航、引擎（`PropulsionEngine`，下标 2）、生命装置四项全部完成后才淡入，同时淡出受损底噪。任一项恢复未修复状态时，会恢复对应故障表现和受损底噪，不会重播着陆撞击与金属变形声。

桥接在 `Start` 读取完整快照，避开维修屏 `Awake/resetOnPlay` 的初始化重置；重新启用或修改绑定后也会重新同步。正常运行依赖状态事件，不逐帧轮询维修状态；代码直接修改 `SiteDevices` 数组元素后应调用 `RefreshBindings()`。`ResetAll()` 先重置四项完整状态，再向实际发生变化的装置发布状态事件，因此运行时手动重置也会同步故障表现。缺失引用时保留现有表现并提示一次，不会自动寻找其他场景里的维修屏。

`RepairScreen.ResetAll()` 只重置维修屏状态，拼装系统会在 `LateUpdate` 将本局已经完成的维修记录重新写回。需要整体坠毁时应调用 `ShipAssemblyController.ApplyCrashDamage()`，它先清理会话和完成记录，再发布完整损坏状态；不要直接用 `ResetAll()` 代替坠毁入口。

## 编辑器安装与人工验收

两个场景已经接线并加入全局构建场景列表，原构建启动场景顺序保留。使用覆盖全局场景列表的 Build Profile 时，也需包含 SHIP 与 MarsBase_RetroFuture。

`Tools/Landing/Install SHIP to Mars landing` 可以重新接线；它会保存两个目标场景，并更新着陆使用的相机、材质和环境配置。正常调参无需重复安装。

`Tools/Landing/Install hard landing effects in SHIP` 只安装/恢复此次硬着陆参数和损毁效果，保留现有相机、驾驶舱及 Mars 场景。已完成安装，无需再次执行；重复执行会覆盖这组效果节点的调参。

`Tools/Landing/Install opening flow in SHIP` 可以重新绑定开局控制器、正式对话和三个待机屏，不重建驾驶舱或调整相机。SHIP 已写入绑定，无需再次安装。

`Tools/Landing/Install departure flow in SHIP` 可重新绑定检查/起飞控制器和八个音频，保留已有演出数值调参。SHIP 已完成接线，无需额外操作。

着陆基础版已进行 Unity 编译和编辑器画面检查。追加维修联动、开局与起飞流程时 Unity 已关闭；源码编译与场景静态引用检查不代表运行时验收，待机画面布局、起飞镜头、音量和实际交互仍需在 Unity 中确认。没有运行 Play Mode 或自动测试。请在 Play 中检查：

1. 右键看向两侧舷窗，星空、大气与地面衔接连续。
2. ESC 暂停时下降、震动和粒子暂停，恢复后继续。
3. 原终端仍可操作，没有双主相机或双监听器。
4. 触地后飞船对齐 LandingAnchor，扬尘逐渐散去，SHIP 仍保持加载。
5. 触地撞击、摇晃、扬尘和随后三处电火花的节奏，以及警报停止后的环境音音量。
6. 初始三张待机画面且四项健康；`BEGIN LANDING` 才启动下降，触地时四项同时损坏、维修屏解除待机；教学末尾 `ACTIVATE SALVAGE` 才解除打捞待机，之前按键/点击不会开始作业。
7. 通过现有拼装维修依次修好装置，对应火花点停止；四项全部完成后受损底噪淡出、健康底噪淡入，且不会再次播放撞击声。
8. 全修后只提示检查，不自动起飞；逐项测试显示进度和结果，`NOT YET` 可返回菜单，四项通过后 `LAUNCH` 才有效。
9. 发射时倒数、预热、扬尘和震动同步，窗口能看到地面远离、大气过渡、星空与退远的火星；ESC 暂停/恢复没有音效漏播或画面跳进。
10. 中止起飞后回到地面并允许重新检查；正常完成后不再受损、不再自动打捞，终端显示进入轨道的返航对话。
