# 电脑终端对话系统

命名空间：`LudumDare60.TerminalDialogue`。

玩家点击屏幕空间的按钮 → 电脑屏幕逐字输入玩家回复 → 提交选项的变量和事件 → 播放预编写的 AI 台词 → 显示下一组选项。无需联网或大模型。

## 把 DOS 终端放到驾驶舱屏幕上

预制体：**`Assets/Prefabs/TerminalDialogue/DOS Terminal System.prefab`**。

1. 打开目标场景，把这个预制体拖到 Hierarchy 的空白处。
2. 选中实例，在 **Terminal Screen Mount** 的 **目标屏幕** 中，拖入 `SCREEN_NAVIGATION` 显示面，或它的 `Screen_NAVIGATION` 父节点。
3. 保持 **曲面贴合（推荐）** 勾选，点击 **贴合目标屏幕**。终端画面自动匹配显示面尺寸、位置和朝向，并沿玻璃曲面显示；玩家回复面板仍是屏幕空间 UI。
4. 根节点的 **Terminal Dialogue Controller → Conversation** 选择这台电脑使用的对话资产。预制体沿用已保存演示里的 `222`，可以换成其他对话。
5. 点击 Play。场景已有 EventSystem 时会共用；没有时运行中自动创建一个，不需要把演示相机或 EventSystem 一起复制过来。

也可先在 Hierarchy 选中 `Screen_NAVIGATION`，使用菜单 **GameObject → Terminal Dialogue → 添加 DOS 终端到所选屏幕**，会创建实例并指定目标。

**曲面贴合** 会把终端 UI 渲染到 RenderTexture，再通过原屏幕网格的临时副本显示，保留凸起玻璃形状，覆盖原导航图。原模型、共享材质及原贴图文件不会被修改；退出场景或关闭组件会清理临时资源。默认纹理宽度为 1024，可调高到 2048 让近距离文字更清晰；每块启用的屏幕会额外使用一张纹理和一次相机渲染。此模式使用项目现有的 URP。

关闭 **曲面贴合** 可恢复普通 World Space Canvas，适合平面显示器。默认填充比例 98%、离表面 2 毫米。若画面反了，切换 **翻转正反面**。目标电脑移动、旋转后终端跟随，玩家移动相机不会把它带走。请指定具体的显示面，不要指定含多个显示面的整个驾驶舱。终端 UI 仍在原 Canvas 下编辑，不用单独制作贴图。

编辑原 Canvas 的字号、颜色或布局后，再点击 **贴合目标屏幕** 刷新曲面预览；运行时对话打字和光标会自动更新。

预制体包含控制器、世界终端 Canvas、屏幕回复 Canvas 和内部绑定，字体均为 Glass TTY。直接修改实例内的界面即可制作 Prefab Variant。菜单 **Tools → Terminal Dialogue → 从已保存演示场景制作 DOS 预制体** 可以重新打包已保存的演示 UI；已有预制体时会另存新文件，不覆盖你的编辑，也不改动当前打开的场景。

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

每句台词的 **显示名字（可选）** 可以单独填写方括号里的名字。例如说话者选 `System`、名字填 `NAVIGATION`，这一句就显示为 `[NAVIGATION] ...`，下一句仍使用自己的设置。只填名字，不用输入方括号。

留空时保持原来的显示：AI 使用顶部 **AI 名称**（默认 `SHIP AI`）、System 使用 `SYSTEM`、玩家使用 `>`。玩家选项里的终端回复也支持自定义名字，填写后显示为 `[名字] ...`。显示名字只控制文字前缀，说话者分类、字速和剧情逻辑沿用原来的设置。修改后重新开始对话生效。

### 设置文字颜色

- 对话资产顶部的 **全局默认文字颜色** 用于整段对话的默认正文颜色，初始值沿用原来的浅绿色。
- 每句台词下勾选 **启用特殊颜色**，再选择 **特殊文字颜色**，即可单独覆盖这一句；取消勾选则恢复跟随全局颜色。
- 玩家选项的 **玩家输入到终端的完整回复** 下也有同样的开关，用来设置玩家在终端里输入的那句话的颜色。
- 颜色包含该句的 `[SHIP AI]`、`[SYSTEM]` 或 `>` 前缀；固定标题、光标及屏幕空间按钮仍由各自 UI 颜色控制。
- 修改对话资产后重新进入 Play 或调用 `Begin()` 生效；已开始的会话保留启动时的数据快照。

普通 World Space Canvas 与曲面贴合模式都支持逐句颜色；台词依旧按纯文本处理，不用写富文本标签。

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
