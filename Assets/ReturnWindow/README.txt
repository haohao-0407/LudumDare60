返航窗口 / RETURN WINDOW

SHIP 正式场景接入（2026-10-20）
打开 Assets/Scenes/SHIP.unity 并 Play；原有着陆、四套维修、预检、起飞与片尾流程均保留。
座舱右侧使用现有 ReturnCalendar / ReturnCountdown 资产；右转查看，左键点击拉近，右键或 ESC 返回。
在着陆后的简报选择 BEGIN REPAIRS，开始 1200 秒有效操作倒计时；每 60 秒对应日历的一天。
剧情阅读、ESC 菜单和失焦暂停；操作菜单、打捞、维修、预检消耗时间。
纸质日历显示 08/25 至 09/13，逐日划叉、圈出末日，并按能源/导航/引擎/生命系统各自的实际维修状态打勾。
每天结束自动拉近日历，镜头到位后播放划叉，再恢复原先的自由视角或操作屏幕；过程中倒计时继续，ESC 可暂停。
倒计时仪表及预警不强制切换镜头；最后一天先展示划叉，再转入超时终端剧情。
剩余 50%、25%、10% 发布不打断操作的提示；最后 10% 亮灯并提示音。
必须修复四系统、完成四项预检，再于零点前点击 LAUNCH。点击即锁定返航授权，起飞演出可正常继续。
发射失败不锁定时钟；取消发射保留原剩余时间并要求重新预检。归零后接入现有 AnotherTomorrow 剧情与播种日历。
确认 AnotherTomorrow 最后一条旁白后，舱内画面淡入黑屏，依次播放四幕报幕字幕：未能启程、漫长的等候、播种之日、另一个明天。
字幕自动推进；每幕完全显示后，空格、回车或单击可提前进入下一幕。ESC 菜单、失焦和系统挂起暂停字幕，后台座舱操作锁定。
终幕保持黑屏并提供“再次启程”，重新加载 SHIP 开始新一局；成功返航不播放这段坏结局。
报幕文案/时长：Assets/Landing/AnotherTomorrow.asset；布局：Assets/Landing/Prefabs/Another Tomorrow Ending.prefab。
中英文跟随设置即时切换；专用字体为 Cormorant Garamond / 思源宋体（SIL OFL 1.1，许可证在 Assets/Fonts/Theatre），静态字形图集随游戏打包。
若改文案新增字形，需重新烘焙上述目录的专用 SDF 字体；不要修改终端的全局字体。
安装入口：Tools > Landing > Install Another Tomorrow theatre ending in SHIP；预览：Tools > Landing > Preview Another Tomorrow。
黑屏结局验证：AnotherTomorrowEndingValidation.Run()；报告：Logs/CockpitMission/theatre-ending-validation.txt。
SHIP 使用 CockpitMissionController 管理本局，复用纯 C# MissionClock；不挂载下述旧原型的 MissionController。
安装/修复引用：Tools > Landing > Install mission clock and calendar in SHIP。
联动验证：Tools > Landing > Validate SHIP mission clock in Play Mode；完整返航验证使用 CockpitMissionValidation.RunSuccess()。
报告：Logs/CockpitMission/validation.txt、success-validation.txt。纯计时测试：Tests/ReturnWindow/ReturnWindow.Tests.csproj。
.NET 8 SDK 仅用于上述独立开发测试；Unity 发布游戏无需向玩家分发 SDK。正常运行 Unity 游戏不调用 dotnet。
以下章节保留旧 ReturnWindow 单系统原型的操作与开发记录，不能作为 SHIP 当前玩法说明。

本次交接版本
最新剧情以 Exports/ReturnWindow/StoryReview/PIONEER-60_英文剧情交接稿.txt 为准；中文接入指示集中在文档开头。
该稿沿用用户原剧情，日志日期为 1981-08-22 至 1981-08-25，维修日历为 08/25 至 09/13。
该段记录的是旧 ReturnWindow 独立原型；最新 SHIP 接入状态见本文开头。
当前维修台已改用导航装置的四块原生模型，按提供的组装前/后坐标和方向配置；不使用模型截图或渲染图贴片。
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
维修：1-4 / 鼠标选部件，WASD 移动，Q/E 旋转，Shift 微调，X 拆卸，R 重置部件。
当前导航装置对应 TANK、OPTICS、ANTENNA 三个接口。全部实际接好后显示完整装置并锁定本轮组装；R、X 不再拆散已完成模型，重新开始游戏才恢复拆散姿态。
返回终端，选择 PREPARE TO DEPART > CONFIRM DEPARTURE。
完成前 R 只重置维修部件，不重置任务时钟。
补给回收是可选操作：出现信号后按 SPACE 锁定，按住提升控制带，松开下降；保持目标在控制带中。
回收失败可以重试，没有额外的返航资源门槛。

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
