
# Win+Space 微软拼音中英切换工具（模块化）

常驻任务托盘的 Windows 小工具：按 `Win+Space`（可自定义）切换微软拼音输入法的中英文模式，通过 IMM32 的 `ImmSetConversionStatus` 修改 conversion 值实现。附带代理修复等扩展模块，采用"主程序 + 模块"的插件式架构。

## 目录结构（GitHub 风格布局）
- `src\WinSpaceImeToggle\` —— 主程序源码：`Program.cs`（入口）、`MainConfig.cs`（主配置）、`ModuleManager.cs`（模块加载）、`KeyRecorder.cs`（键位录制）、`UiAssets.cs`（字体/图标）、`TrayApplicationContext.cs`（托盘宿主）、`SettingsForm.cs`（设置界面）。只负责开机自启动开关、设置界面、托盘界面、键位录制，以及按模板渲染各模块的设置分页
- `src\WinSpaceImeToggle.Contracts\` —— 模块契约源码（编译为 `WinSpaceImeToggle.Contracts.dll`）：`IModule` 接口、设置模板类型、`ModuleContext` 配置存储、`Hotkey`/`KeyNames` 工具
- `src\WinSpaceImeToggle.Modules\Ime\` —— 输入法实现模块（编译为 `Ime.dll`）：按键捕获、中英文切换、防开始菜单注入、防误触、切换策略
- `src\WinSpaceImeToggle.Modules\ProxyFix\` —— 代理修复实现模块（编译为 `ProxyFix.dll`）：开机自启动时自动关闭系统代理
- `scripts\` —— 构建脚本：`build.ps1`（契约库 → 主程序 → 各模块 → 可选测试版 → 可选打包 Export）
- `assets\` —— 运行时资源：`HarmonyOS_Sans_SC_Medium.ttf`（字体）、`icon.ico`（图标）
- `Languages\` —— 多语言文件：以语言代码命名的 INI 键值文件（`zh-CN.ini` 中文、`en.ini` 英文），新增语言只需添加一个新文件
- `artifacts\` —— 构建产物（自动生成，已被 .gitignore 忽略）：`WinSpaceImeToggle.exe`、`WinSpaceImeToggle.Contracts.dll`、`modules\`（模块 DLL）、`assets\`（运行时资源副本）、`Languages\`（语言文件副本）
- `Export\` —— 打包输出（`-Package` 生成，已被 .gitignore 忽略）：`WinSpaceImeToggle\` 发布目录与 `WinSpaceImeToggle.zip`

新增功能模块时，在 `src\WinSpaceImeToggle.Modules\` 下新建一个文件夹存放模块源码即可；删除某功能文件夹并重新构建（或直接删除运行目录 `modules\` 中对应 DLL），即可移除该功能。

## 模块解耦原则
主程序不直接引用任何具体模块类型，运行时扫描程序目录 `modules` 文件夹中的 DLL，加载实现了 `IModule` 契约的类：

- 删除 `modules\Ime.dll` → 输入法工具分页、快捷键、托盘"切换中英文"项全部移除，主程序照常运行
- 删除 `modules\ProxyFix.dll` → 启动策略分页与代理修复逻辑移除
- 新增按契约实现的模块 DLL → 设置分页、托盘项、生命周期回调自动出现在主程序中，无需改动主程序代码

模块与主程序通过"模板"通讯，设置模板类型包括：
- `ToggleItem` 选项开关（复选框）
- `NumericItem` 数字输入（防误触时间等）
- `ChoiceItem` 下拉页（切换策略、防开始菜单按键等）
- `HotkeyItem` 键位录制（复用主程序的录制能力）
- `NoteItem` 说明文字

模块设置保存在 `%APPDATA%\WinSpaceImeToggle\modules\<模块Id>.ini`；主程序设置（快捷键总开关、开机自启动）保存在 `%APPDATA%\WinSpaceImeToggle\config.ini`。旧版单文件配置会在首次运行时自动迁移到模块配置。

## 功能
- 全局快捷键切换微软拼音中英文，默认 `Win+Space`（可在"输入法工具"分页录制自定义组合键）
- 托盘图标仅显示快捷键功能状态：启用（彩色原图）/ 禁用（灰度），不显示输入法模式（模式以微软拼音状态栏自带显示为准）
- 开机自启动开关（写入 HKCU Run 注册表项，位于"主程序选项"分页，设置窗口底部 `保存/取消` 不受分页影响）
- "启动策略"分页（代理修复模块）：`开机自启动时自动修复代理问题` 开关——程序启动时若检测到系统代理已开启（ProxyEnable=1），自动关闭并通知系统刷新，解决第三方 VPN 异常退出后残留代理导致的断网
- 托盘右键菜单：切换中英文（模块贡献）/ 设置 / 启用快捷键 / 开机自动启动 / 退出
- 切换策略可选：自动切换到微软拼音后切换 / 仅当微软拼音为当前输入法时切换
- 防误触时间可调（默认 0.3 秒）：快捷键触发后窗口期内重复的按键事件仅被拦截不切换，避免键盘双击导致中英来回切换；设为 0 可关闭
- 防开始菜单按键可选（默认 F24，可选 F13-F24 / Ctrl / Alt / Shift），避免与其它软件占用的按键冲突
- 程序以 Per-Monitor V2 DPI 感知运行并启用 WinForms 高 DPI 自动缩放；界面使用 `assets\HarmonyOS_Sans_SC_Medium.ttf` 字体，托盘/窗口/exe 图标使用 `assets\icon.ico`
- 设置窗口可拖拽调整大小（最小 720×480），内部布局随窗口等比伸缩
- 多语言支持：界面文本由 `Languages` 目录中的语言文件提供，内置英文（`en`，默认）与中文（`zh-CN`），可在“主程序选项”分页切换语言

## 多语言
界面文本全部由 `Languages` 目录中的语言文件提供（INI 键值对，文件名即语言代码，如 `zh-CN.ini`、`en.ini`）。
每个语言文件内置 `meta.nativeName` 键，作为“界面语言”下拉框中的语言显示名；文件内的键（如 `app.title`、`ime.name`）与源码一一对应，值为该语言下的文本，支持 `{0}` 占位符。

新增语言只需三步：
1. 在 `Languages` 目录新建 `<语言代码>.ini`（如 `ja.ini`）；
2. 参照 `en.ini` 填写全部键的翻译（缺失的键会自动回退英文）；
3. 重新构建或直接复制新文件到运行目录 `Languages\`，打开设置即可在“界面语言”下拉框中选择。

模块文本同样来自语言文件：模块通过 `ModuleContext.T("key")` / `TF("key", ...)` 读取，主程序通过 `Localization.T("key")` 读取；键未命中时自动回退英文，再未命中则显示键名本身。

## 构建
要求：Windows + .NET Framework 4.x（Win10/Win11 自带），无需任何 SDK。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

产物输出到 `artifacts\`：`WinSpaceImeToggle.exe`（主程序）+ `WinSpaceImeToggle.Contracts.dll`（契约）+ `modules\Ime.dll`、`modules\ProxyFix.dll` + `assets\`（运行时资源副本，保证可直接双击运行）+ `Languages\`（语言文件副本）。

```powershell
.\scripts\build.ps1 -Test      # 额外生成命令行调试版 WinSpaceImeToggle.test.exe
.\scripts\build.ps1 -Package   # 构建并刷新 Export\WinSpaceImeToggle\ 与 Export\WinSpaceImeToggle.zip（含运行时文件）
```

调试版支持 `--state` / `--toggle` / `--list` / `--config` / `--fix-proxy` / `--settings-smoke` 参数（`--settings-smoke` 会逐页截图保存为 `settings-smoke-N.png` 便于检查排版）。

## 使用
1. 将 `artifacts\` 目录整体拷贝（或解压 `Export\WinSpaceImeToggle.zip`），双击其中的 `WinSpaceImeToggle.exe` 即可运行。
2. 右键托盘图标 → `设置...`：
   - `主程序选项`：开机自动启动、启用快捷键总开关、已加载模块列表
   - `输入法工具`（输入法模块）：录制组合键、切换策略、防误触时间、防开始菜单按键
   - `启动策略`（代理修复模块）：开机自启动时自动修复代理问题
3. 左键单击托盘图标手动切换一次；双击打开设置。
4. 删除运行目录 `modules` 中不需要的模块 DLL，重启程序即可移除对应功能与设置分页。

## 实现原理
- 主程序启动即通过 `SetProcessDpiAwarenessContext` 声明 Per-Monitor V2 DPI 感知，并开启 WinForms 高 DPI 自动缩放，设置窗体使用 `AutoScaleMode.Dpi`。
- Windows 保留 `Win+Space`（切换输入语言），`RegisterHotKey` 无法注册，因此输入法模块使用 `WH_KEYBOARD_LL` 低级键盘钩子拦截并吞掉组合键；命中时注入无害键（默认 F24，可更换）的按下/抬起，使系统认为 Win 被用于组合键而不弹出开始菜单，随后正常放行 Win 键抬起，避免按键状态卡住。
- 通过 `ImmGetDescription` 描述匹配 / KLID（`00000804`、`E0200804`）识别微软拼音 HKL；对前台窗口 `ImmGetContext` 取得 HIMC，用 `ImmSetConversionStatus` 将模式写为精确的 `IME_CMODE_NATIVE`（中文，0x1）或 `IME_CMODE_ALPHANUMERIC`（英文，0x0）；TSF 输入法 `ImmGetContext` 返回 0 时自动回退到 `ImmGetDefaultIMEWnd` + `WM_IME_CONTROL`。
- 托盘图标与切换动作完全解耦：图标只在快捷键总开关变化时更新，切换输入法不触发托盘重绘。
- 键位录制由主程序的 `KeyRecorder` 临时钩子完成，录制期间通过契约通知模块暂停自身快捷键处理，避免冲突。
- 代理修复模块通过 HKCU `Internet Settings` 的 `ProxyEnable` 置 0 并调用 `InternetSetOption`（INTERNET_OPTION_SETTINGS_CHANGED / REFRESH）通知系统刷新。

## 注意事项
- 若曾在"兼容性 → 更改高 DPI 设置"里配置过缩放替代（如"应用程序"），请恢复为默认：程序已自行声明 Per-Monitor V2 DPI 感知，兼容性覆盖会阻止其生效并导致排版异常。
- 程序默认以普通权限运行；对管理员权限窗口（UAC 界面、部分游戏/IDE）的按键事件，低级钩子收不到。
- 自定义快捷键时请避免与系统快捷键冲突（如 `Win+E`、`Ctrl+Space` 等）。
- 若组合键包含 Shift，注意微软拼音自带"按 Shift 切换中英"功能，可能与本工具行为叠加。



声明出处：使用了“HarmonyOS Sans Fonts”


