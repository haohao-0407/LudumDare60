返航窗口 / RETURN WINDOW

本次交接版本
最新剧情以 Exports/ReturnWindow/StoryReview/PIONEER-60_英文剧情交接稿.txt 为准；中文接入指示集中在文档开头。
该稿沿用用户原剧情，日志日期为 1981-08-22 至 1981-08-25，维修日历为 08/25 至 09/13。
最新剧情稿尚未接入 Unity；当前可玩场景沿用上一版剧情，维修台包含导航、生命、能源三套原生模型组装。
各装置按用户截图的组装前/后坐标和方向配置，不使用模型截图或渲染图贴片。能源装置的小附件随主机体移动；生命装置的门随机体保留。
以下操作说明、抵达记录及验证结果描述当前可玩原型，不代表最新剧情稿已完成接入。

运行
在 Unity 6000.6.5f1 中打开本功能工作区 return-window。
场景：Assets/ReturnWindow/Scenes/ReturnWindow.unity。
菜单 Tools > Return Window > Open Mission 打开场景，按 Play。
需要重新生成时使用 Build Playable Mission；它重建本功能场景、剧情和两件道具，先保存手工修改。
本功能基于 codex/audio-ready，原 SHIP 和组装演示场景均保留。

操作
阅读开场，选择 BEGIN MISSION 后开始计时。
鼠标转头；E 连接/离开终端；ESC 离开当前操作界面，或打开/关闭暂停菜单。
终端可查询时间、查看日程、阅读殖民地日志、回收补给、进入维修台。
维修：F1 导航 / F2 生命 / F3 能源，或点击顶部按钮切换装置。1-5 / Tab / 鼠标选部件，WASD 移动，Q/E 旋转，Shift 微调，X 拆卸，R 重置当前未完成装置。
导航装置 4 块 / 3 处连接；生命装置 5 块 / 4 处连接；能源装置 3 块 / 2 处连接。三套装置全部实际接好才通过任务维修检查。
完成后显示截图中的完整装置并锁定本轮组装；R、X 不再拆散已完成模型，切换装置、退出维修台和切换任务场景均保留组装进度，重新开始游戏才恢复拆散姿态。
返回终端，选择 PREPARE TO DEPART > CONFIRM DEPARTURE。
完成前 R 只重置维修部件，不重置任务时钟。
补给回收是可选操作：出现信号后按 SPACE 锁定，按住提升控制带，松开下降；保持目标在控制带中。
回收失败可以重试，没有额外的返航资源门槛。

组装独立试玩
Assets/AssemblyDemo/Scenes/ShipDeviceAssembly.unity：三套装置统一试玩，无剧情和计时。
Assets/AssemblyDemo/Scenes/LifeSupportAssembly.unity：默认打开生命装置。
Assets/AssemblyDemo/Scenes/EnergyAssembly.unity：默认打开能源装置。
编辑器菜单 Tools > Assembly Demo > Validate All Ship Devices 检查坐标、旋转、吸附、复位和完成锁定，报告及前后预览保存在 Logs/ShipDeviceAssembly。

时间规则
默认 1200 秒有效操作时间 = 20 个地球标准日；日期为1981-08-25至1981-09-13（含首尾）。
抵达记录为1980-09-08；主角在远端勘测数月后返回空荡殖民地。
LATEST DEPARTURE 显示日/时/分，指向最迟出发时刻；不是窗口刚刚开启的时间。
进入有明确标识的剧情阅读节点时，打字、阅读、选项和世界操作一起暂停。
普通状态查询、维修、回收和任务菜单持续耗时；暂停菜单、切后台和通过任务服务加载场景暂停。
每完成一天自动划一个叉，跨多天会补齐；最后日期09/13结束才关闭窗口。20格全部显示具体月/日，只有09/13有红圈；其他完成日期都打叉。
剩余 50%、25%、10% 时 AI 各提醒一次。临近窗口实体警示灯点亮并提示一次。
发射按点击确认时的时间判定，不等待玩家回复逐字播放。

结局
THE WAY HOME：完成维修并在零点前确认出发，只呈现启程，不承诺已经与家人团聚。
ANOTHER TOMORROW：窗口关闭，AI 提议温室与长期生活；日历保留全部叉号，并显示1981-09-14的播种计划。
两者使用当前驾驶舱和文字收尾，不显示胜利/失败，不包含后续种田循环。
窗口根据 NASA-TM-X-3184 表III改编：1981-09-03至09-13返航；下一期研究时段1983-11-09至11-19。
低燃料余量、储粮不足和地表授权截止时刻属于剧情设定；未进行载人飞船轨道仿真。
提前确认出发先进入等待轨道，在计划返航时段执行返航点火；不会把1980年抵达时段当作返航时段。

调整
场景根节点 Mission Session 的 MissionController：Duration Seconds、Mission Days、Start Date、Warning Fractions、Next Opportunity Start/End（日期格式yyyy-MM-dd）。
当前为20分钟/20日；运行时正文、日历和计时器读取同一任务配置。NASA原始返航时段属于历史参考，修改任务日期时应同步审阅导航台词。
物理计时器支持最多99日显示；日历以每页20格显示，默认整段日期同页可见。建议任务天数保持1-99。
TerminalDialogueDefinition 的故事节点可勾选 Pause Gameplay While Reading；任务/维修/发射菜单不勾选。
MissionClock 是无Unity依赖的唯一时钟；MissionController负责单局生命周期；其他组件只订阅或读取它。
ChoiceAccepted在选项确认时提交发射，原 EventTriggered 仍保持“玩家回复显示结束后触发”。

单局与场景
任务服务、库存与实际组装连接图跨场景保留。不要在场景切换时调用 Begin 重启终端会话。
加载场景优先使用 MissionController.LoadSceneAsync，确保整个加载区间暂停。
本局库存关闭 Load On Awake 和 Auto Save，不读取或写入原 inventory.json。
退出后重新开始；结局的 BEGIN AGAIN 同时创建新时钟、新库存及未维修的设备。
本轮没有磁盘续玩存档。

交付物
SourceArt/ReturnWindow/ReturnWindowProps.blend：可编辑模型源文件。
SourceArt/ReturnWindow/build_return_props.py：可重复运行的 Blender 建模/导出脚本。
Exports/ReturnWindow/ReturnCalendar.glb、ReturnCountdown.glb：模型导出。
Assets/ReturnWindow/Art/Prefabs：两个独立道具 Prefab，包含动态显示、材质和声音。
SourceArt/ReturnWindow/Previews：Blender与Unity检查图。
Assets/ReturnWindow/Story：正式剧情资产、中英文审稿、ArchiveBasis.txt档案依据和补给定义。
模型米制，底边中心原点，Unity正面+Z，上方+Y；计时数字和每日红叉由运行时独立控制。模型包含可编辑的独立日期印刷层；Unity用动态日期层替换它，以便调整任务配置。

验证入口
纯任务逻辑：dotnet run --project Tests/ReturnWindow/ReturnWindow.Tests.csproj --configuration Release。
终端回归：Tests/TerminalDialogue/RunStoryValidation.ps1 -UnityPath <Unity.exe路径>。
真实场景：Tools > Return Window > Validate Mission In Play Mode。
批处理入口 LudumDare60.ReturnWindow.Editor.ReturnWindowValidation.Run（不要传-quit）。
场景重建+道具图+运行验证：LudumDare60.ReturnWindow.Editor.ReturnWindowSceneBuilder.BuildAndValidate。
组装回归：Unity EditMode 测试 LudumDare60.AssemblyDemo.Tests。
运行验证报告及场景截图在 Logs/ReturnWindowRuntimeValidation.txt 和 Logs/ReturnWindowCaptures。
2026-10-18 已通过：任务时钟/日期20项、终端与剧情23项；本轮重跑实机场景验收9项。此前Unity组装EditMode 74项通过，本轮未修改组装逻辑。
可随项目交付的验证摘要位于 Exports/ReturnWindow/Validation.txt 和 RuntimeValidation.txt。
驾驶舱与两种结局预览位于 SourceArt/ReturnWindow/Previews/Unity_Cockpit.png、Unity_HomeEnding.png、Unity_StayEnding.png。

已知范围
20分钟是首版期限，维修玩法复用现有单个四部件谜题；熟悉解法后可能很快完成。
当前内容不是20分钟长度的完整探索关卡。增加维修关数和路线需后续单独设计、试玩调参。
