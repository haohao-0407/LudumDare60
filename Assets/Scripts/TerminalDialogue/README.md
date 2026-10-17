# 电脑终端对话系统

命名空间：`LudumDare60.TerminalDialogue`。

玩家点击屏幕空间的按钮 → 电脑屏幕逐字输入玩家回复 → 提交选项的变量和事件 → 播放预编写的 AI 台词 → 显示下一组选项。无需联网或大模型。

## 先运行示例

打开 `Assets/Scenes/TerminalDialogueDemo.unity` 后点击 Play。示例展示两条分支、等待继续、坐标解锁条件，以及关闭/重新打开。

也可以在任意场景使用菜单 `Tools > Terminal Dialogue > 在当前场景搭建演示`。它创建一套新对象和新的示例资产，不替换现有 UI。首次使用会静默导入 TMP Essential Resources。

- `Computer Screen (World Space)`：世界空间 Canvas，放在电脑屏幕表面。演示初始放在相机前方三米，之后不跟随相机。可以拖到实际电脑模型下，调整位置、旋转和缩放。
- `Reply UI (Screen Space)`：屏幕空间 Canvas，包含回复按钮、继续、离开/连接电脑。
- `Terminal Dialogue Demo`：包含对话控制器。
- 终端与屏幕按钮统一使用用户提供的 `Assets/Fonts/Terminal/Glass_TTY_VT220.ttf`，对应的 TMP 字体资产为同目录的 `Glass_TTY_VT220 SDF.asset`。游戏内台词、按钮和提示采用纯英文；编辑器说明保留中文。
- 字体资产已预烘焙可打印 ASCII 字符，运行时不依赖操作系统字体或动态生成字形。使用普通英文标点即可。

示例资产在 `Assets/Dialogue/`。菜单 `Tools > Terminal Dialogue > 创建示例对话资产` 只创建内容资产。

## 编辑台词和选项

在 Project 窗口选中对话资产即可使用中文 Inspector。也可通过 `Create > Terminal Dialogue > Conversation` 新建。

### 给 AI 增加一句话

1. 展开要修改的节点。
2. 点击“＋ 添加一句台词”，填写文本，说话者选 `AI`。
3. 默认逐句自动播放，可调整播完后的停顿；勾选“等待点击继续”则等玩家按继续。

同一节点的台词按列表顺序播放，全部完成后显示选项。说话者也可以选 `System` 或 `Player`。

### 增加玩家选项/分支

1. 在当前节点点击“＋ 添加玩家选项”。
2. 填写“按钮文字”和“玩家输入到终端的完整回复”。两者可以不同。
3. 若要增加新分支，点击“＋ 创建对话节点”，填写标题和 AI 台词。
4. 在选项的“下一节点”下拉框中选择该节点。选择“结束对话”表示玩家说完后结束。

新节点/选项自动获得稳定 ID。修改编辑标题不会改变连接；手动修改 ID 后需要重新选择相关跳转，Inspector 会报告断开的连接。

没有选项的节点可以设置“台词结束后自动前往”；为空时结束。有选项的节点由玩家选择决定跳转。

### 剧情条件和变量

“显示条件”中的所有条件都满足时，按钮才出现。`Key` 是变量名，`Value` 是要求的布尔值。不存在的变量默认为 `false`。

例如 `has_coordinates = true` 才显示“查询坐标”；“保存坐标”选项在“提交后写入变量”里设置同名变量为 `true`。

外部游戏系统也能调用 `SetFlag("has_coordinates", true)`。等待选择时会立即重新计算选项。全部选项都因条件隐藏时保持等待并显示提示，不会自动结束；应提供无条件的返回/离开选项，或由外部更新条件。

### 游戏事件

选项里的“提交后触发事件 ID”只在玩家输入完全显示后提交。控制器的 `Event Bindings` 中用相同 `Event Id` 绑定 UnityEvent，例如启动设备；也能在 C# 订阅 `EventTriggered`。

显示全文、翻阅记录、关闭再打开不会重放事件。再次选择同一个选项、重新 `Begin()` 则属于新操作，会再次触发。需要一次性奖励时，在游戏系统里做事务去重，或用变量条件隐藏已完成的选项。

`TerminalSalvageBridge` 是可选接入组件：指定终端与 `SalvageController`，接收默认 `start_salvage` 事件，更新 `salvage_started` / `salvage_succeeded` 并显示结果。不要同时为同一个事件绑定另一个启动打捞的监听器。

桥接只负责启动和结果反馈。打捞的锁定/按住控制仍由玩法输入适配器接入已有 `PressControl()` / `ReleaseControl()` 等接口。关闭终端不取消打捞，后台结果会排队等待重新打开。

## 接入实际场景

1. 在一个保持启用的对象上添加 `TerminalDialogueController`，指定 `Conversation`。
2. 电脑上使用 World Space Canvas。添加 `TerminalTranscriptView`，指定同一个控制器、TMP 文字和可选的 ScrollRect / 光标 / 打字音。
3. 屏幕空间 Canvas 上添加 `TerminalOptionsPanel`，指定同一个控制器、窗口根节点、选项容器、按钮模板以及继续/离开/连接按钮。
4. 模板按钮需要 `UnityEngine.UI.Button`、`TerminalChoiceButton` 和 TMP 标签；设置 `Label` 引用，模板保持隐藏。面板会复用按钮，不需要逐一绑定选项点击事件。
5. `TerminalOptionsPanel` 必须放在 `Window Root` 外面，否则隐藏窗口会同时禁用负责重新打开它的组件。控制器也不要放在关闭时销毁或禁用的窗口内。
6. 场景保留一个 EventSystem，使用项目 Input System 的 `InputSystemUIInputModule`，屏幕 Canvas 需要 GraphicRaycaster。
7. 从世界交互系统调用 `Open()` / `Close()`。通过控制器的 `Interaction Changed(bool)` UnityEvent 管理角色输入、鼠标光标和相机控制，本模块不自行启停共享 Input Actions。

控制器默认启动对话；实际接入时可以关闭 `Start Automatically`，等玩家操作电脑再调用 `Open()`。

## API 和行为约定

```csharp
terminal.Open();                         // 首次开始；再次打开恢复
terminal.Close();                        // 暂停文字和交互，保留当前会话
terminal.Advance();                      // 打字时补全当前句；等继续/停顿时进入下一步
terminal.SetFlag("has_coordinates", true);
terminal.PostSystemMessage("Scanner connected."); // 在句子边界插入通知，之后恢复对话
terminal.Begin();                        // 明确重新开始，清空会话与变量
```

- `Conversation` 是内容资产，一个资产包含多个节点；运行时会复制数据，Play 中修改资产不会改变当前会话，重新 Begin 后生效。
- 字速 `0` 表示立即显示。默认使用不受 `Time.timeScale` 影响的时钟，可在控制器切换。
- 首次“显示全文”不会同时跳过等待继续的下一步。选项在 AI 台词与停顿结束后出现；输入/回复中拒绝重复选择。
- 系统消息会排队，不能切开当前一句台词。等待选择时插入消息，播放后恢复原节点的选项。
- 世界屏幕在关闭交互时保留最后内容；对话停止推进，选项窗口隐藏，连接按钮显示。
- 历史默认保留最近 150 条，可调；玩家向上滚动时不会强行追随新消息。内容数据和玩法状态不因历史裁剪而消失。
- 文本按字素边界推进，TMP 映射到对应字符位置，避免半个代理对/组合字符。文本按纯文本显示，`<...>` 不作为富文本标签执行。
- 会话、历史和变量保存在当前控制器内存中。跨场景销毁或退出游戏后不会保留；本版没有实现磁盘存档、命令自由输入或本地化数据库。

## 文件职责

| 文件 | 职责 |
| --- | --- |
| `TerminalDialogueData` / `Definition` | 内容结构、校验、Unity 资产入口 |
| `TerminalDialogueSession` | 不依赖 Unity 的流程、打字进度、条件、通知和事件队列 |
| `TerminalDialogueController` | Unity 生命周期、时钟、公开 API 和游戏事件绑定 |
| `TerminalTranscriptView` | 世界终端文字、滚动、光标、可选打字音 |
| `TerminalOptionsPanel` / `ChoiceButton` | 屏幕回复按钮池与交互状态 |
| `TerminalSalvageBridge` | 可选的打捞接入 |
| `Editor/` | 中文内容 Inspector、演示生成、批处理集成验证 |

## 验证

纯逻辑回归测试（需要 .NET 10 SDK，无第三方 NuGet 依赖）：

```powershell
dotnet run --project Tests/TerminalDialogue/TerminalDialogue.Tests.csproj --configuration Release
```

覆盖分支与条件刷新、重复点击、输入后才提交事件、跳过/继续、关闭恢复、通知插入、Unicode、数据快照、历史上限和无效内容校验。

Unity 批处理入口：`LudumDare60.TerminalDialogue.Editor.TerminalDialogueValidation.CreateDemoScene` 生成并验证独立演示场景；`RunPlayModeSmoke` 验证实际运行时按钮、分支、统一的 Glass TTY 字体和恢复状态，成功后自行退出并写入 `Logs/TerminalDialogueSmoke.txt`。两个入口都不带 `-quit`，以允许异步资源导入/Play Mode 完成后自行退出。图形设备可用时还会输出 `Logs/TerminalDialoguePreview.png`。这些入口供验证使用，不应在有未保存场景时调用。
