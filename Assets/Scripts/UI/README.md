# ESC 菜单

独立预制体：`Assets/Prefabs/UI/ESC Menu.prefab`。已安装到 `Assets/Scenes/SHIP.unity`。

进入 Play 后按 **ESC** 打开，再按 **ESC** 或点击 **RESUME** 关闭。预制体的根节点保持启用，内部 `Menu Overlay` 默认隐藏。其他场景可直接拖入预制体，或使用 **Tools → UI → 把 ESC 菜单安装到当前场景**。已有菜单时不会重复安装；已有 EventSystem 时会共用，没有时自动创建。

## 输入与功能

- 输入配置在 `Assets/InputSystem_Actions.inputactions` → **ESCMenu** → **Toggle**，绑定 `<Keyboard>/escape`。菜单控制器通过 Input System 动作订阅响应，不使用旧版 Input 或键盘轮询。
- **RESUME**：返回游戏，恢复打开菜单之前的时间倍率、鼠标状态及 UI 选中对象。
- **SETTINGS**：进入设置页，支持全屏开关、界面语言切换、返回与重置音频。
- **LANGUAGE**：界面语言选项行。左右方向键（或点击 / 回车）在 English / 简体中文 之间循环，选中结果写入 `PlayerPrefs`（`pioneer60.language`）。显示名来自词典的语言表，加语言不需要改这里。
- **RESET AUDIO**：把 `AudioListener.volume` 拉回 1。音量已不再在设置页暴露滑块，这个按钮留作“声音被调乱后的一键修复”。
- **QUIT GAME**：先显示确认；CANCEL 返回。确认退出在构建中关闭游戏，在 Unity 中停止 Play。
- 打开时默认将 `Time.timeScale` 设为 0，关闭或禁用组件时恢复原值。使用不受时间倍率影响的外部系统可通过 `Open Changed(bool)` 事件接入自己的暂停逻辑。

输入动作由菜单单独克隆并管理生命周期，不会禁用共享 Player/UI 动作映射。按钮同时支持鼠标和 EventSystem 的键盘导航。

## 编辑外观

打开预制体，在 `Menu Overlay/Menu Window` 下编辑背景、标题、Main Page、Settings Page、Language 和 Quit Confirmation。使用现有 `ui1111_components` 中的原始图片部件，保留原比例和相对布局；没有修改 PSD/PSB 或生成新的美术。字体统一为 Glass TTY。

Glass TTY 是静态 ASCII 图集，**没有中文字形**。切到中文前必须准备一个中文字体资产，三种方式任选其一：放到 `Assets/Resources/Localization/CJK Fallback SDF.asset`；给 `LocFontFallback.Override` 赋值；或直接在 `Glass_TTY_VT220 SDF` 的 Fallback Font Assets 里挂上中文字体（这样全工程生效，最省事）。

ESC 菜单的全部文案走 `Pioneer60.Localization`（词典：`Assets/Resources/Localization/pioneer60_dict.json`，源表 `Assets/Localization/pioneer60_dict.csv`）。改文案改 CSV，再跑 `Tools → PIONEER-60 → 本地化 → 1 从 CSV 重建词典 JSON`，不要在预制体里直接改字。

Canvas 为屏幕空间 Overlay，参考分辨率 1920×1080；固定比例的菜单窗口随 Canvas 缩放。它独立于驾驶舱的世界空间 DOS 终端。

## 验证

`Tests/EscMenu/RunValidation.ps1 -UnityPath <Unity.exe>` 会在 `.utmp/EscMenuValidation` 隔离项目中验证按键、交互和预制体引用，并输出 `Logs/ESC-Menu-Preview.png`、`Logs/ESC-Menu-Preview-4x3.png` 和验证日志。
