# ESC 菜单

独立预制体：`Assets/Prefabs/UI/ESC Menu.prefab`。已安装到 `Assets/Scenes/SHIP.unity`。

进入 Play 后按 **ESC** 打开，再按 **ESC** 或点击 **RESUME** 关闭。预制体的根节点保持启用，内部 `Menu Overlay` 默认隐藏。其他场景可直接拖入预制体，或使用 **Tools → UI → 把 ESC 菜单安装到当前场景**。已有菜单时不会重复安装；已有 EventSystem 时会共用，没有时自动创建。

## 输入与功能

- 输入配置在 `Assets/InputSystem_Actions.inputactions` → **ESCMenu** → **Toggle**，绑定 `<Keyboard>/escape`。菜单控制器通过 Input System 动作订阅响应，不使用旧版 Input 或键盘轮询。
- **RESUME**：返回游戏，恢复打开菜单之前的时间倍率、鼠标状态及 UI 选中对象。
- **SETTINGS**：进入设置页，支持全屏开关、返回与重置音量。
- **VOLUME**：调整 `AudioListener.volume` 主音量；显示百分比。音量在当前运行期间保留，不写入存档或系统设置。
- **QUIT GAME**：先显示确认；CANCEL 返回。确认退出在构建中关闭游戏，在 Unity 中停止 Play。
- 打开时默认将 `Time.timeScale` 设为 0，关闭或禁用组件时恢复原值。使用不受时间倍率影响的外部系统可通过 `Open Changed(bool)` 事件接入自己的暂停逻辑。

输入动作由菜单单独克隆并管理生命周期，不会禁用共享 Player/UI 动作映射。按钮同时支持鼠标和 EventSystem 的键盘导航。

## 编辑外观

打开预制体，在 `Menu Overlay/Menu Window` 下编辑背景、标题、Main Page、Settings Page、Master Volume 和 Quit Confirmation。使用现有 `ui1111_components` 中的原始图片部件，保留原比例和相对布局；没有修改 PSD/PSB 或生成新的美术。字体统一为 Glass TTY，游戏内文字使用英文。

Canvas 为屏幕空间 Overlay，参考分辨率 1920×1080；固定比例的菜单窗口随 Canvas 缩放。它独立于驾驶舱的世界空间 DOS 终端。

## 验证

`Tests/EscMenu/RunValidation.ps1 -UnityPath <Unity.exe>` 会在 `.utmp/EscMenuValidation` 隔离项目中验证按键、交互和预制体引用，并输出 `Logs/ESC-Menu-Preview.png`、`Logs/ESC-Menu-Preview-4x3.png` 和验证日志。
