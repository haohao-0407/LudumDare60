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

星空与远处火星由程序天空材质绘制。最后一段由真实地面视差表现下降；扬尘使用柔边粒子。当前提供视觉演出与声音事件接口，未添加音频素材、出舱行走或舱门功能。

## 编辑器安装与人工验收

两个场景已经接线并加入全局构建场景列表，原构建启动场景顺序保留。使用覆盖全局场景列表的 Build Profile 时，也需包含 SHIP 与 MarsBase_RetroFuture。

`Tools/Landing/Install SHIP to Mars landing` 可以重新接线；它会保存两个目标场景，并更新着陆使用的相机、材质和环境配置。正常调参无需重复安装。

本轮仅进行 Unity 编译、场景引用及编辑器画面检查，没有进入 Play Mode 或运行自动测试。请在 Play 中检查：

1. 右键看向两侧舷窗，星空、大气与地面衔接连续。
2. ESC 暂停时下降、震动和粒子暂停，恢复后继续。
3. 原终端仍可操作，没有双主相机或双监听器。
4. 触地后飞船对齐 LandingAnchor，扬尘逐渐散去，SHIP 仍保持加载。
