# SHIP → Mars 着陆第一版

打开 `Assets/Scenes/SHIP.unity` 后进入 Play，`Landing Sequence` 默认自动播放约 45 秒的着陆演出。按住鼠标右键拖动转头，原相机初始位置和朝向保留；ESC 使用现有菜单暂停。左键继续用于终端。

## 场景与参数

- `ShipRig` 保留原驾驶舱、相机、两块终端。`CameraLook` 负责转头，`CameraShake` 负责演出震动。
- `Landing Sequence` 上的 `Duration` 调整总时长，`ApproachHeight` / `ApproachOffset` 调整最后下降段；后者相对于火星落点朝向。
- `AutoStart` 默认开启；关闭后可由代码调用 `BeginLanding()`。`Progress`、`Phase`、`HasLanded` 可查询状态，`OnTouchdown` 和 `OnLanded` 用于接声音、任务或舱门。
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
- `IsDamaged` 可读取损坏状态；后续修理逻辑可调用 `RepairAll()` 清除损坏表现，或调用 `StopDamageEffects()` 仅静音/关闭效果并保留损坏状态。此版没有更改终端功能、零件库存或维修判定，也没有添加出舱与舱门功能。

## 编辑器安装与人工验收

两个场景已经接线并加入全局构建场景列表，原构建启动场景顺序保留。使用覆盖全局场景列表的 Build Profile 时，也需包含 SHIP 与 MarsBase_RetroFuture。

`Tools/Landing/Install SHIP to Mars landing` 可以重新接线；它会保存两个目标场景，并更新着陆使用的相机、材质和环境配置。正常调参无需重复安装。

`Tools/Landing/Install hard landing effects in SHIP` 只安装/恢复此次硬着陆参数和损毁效果，保留现有相机、驾驶舱及 Mars 场景。已完成安装，无需再次执行；重复执行会覆盖这组效果节点的调参。

本轮仅进行 Unity 编译、场景引用及编辑器画面检查，没有进入 Play Mode 或运行自动测试。请在 Play 中检查：

1. 右键看向两侧舷窗，星空、大气与地面衔接连续。
2. ESC 暂停时下降、震动和粒子暂停，恢复后继续。
3. 原终端仍可操作，没有双主相机或双监听器。
4. 触地后飞船对齐 LandingAnchor，扬尘逐渐散去，SHIP 仍保持加载。
5. 触地撞击、摇晃、扬尘和随后三处电火花的节奏，以及警报停止后的环境音音量。
