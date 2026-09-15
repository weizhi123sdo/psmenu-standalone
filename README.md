# PS便捷菜单 独立版

Quicker 动作「PS便捷菜单」的独立桌面版：脱离 Quicker 宿主，作为常驻程序提供 Photoshop 右键上下文菜单——按当前图层类型动态切换菜单项，支持子菜单、快捷键、JSX 脚本、外部程序、剪贴板动作，带完整设置编辑器、图标选择器（拼音搜索）、主题系统、脚本库。

## 构建与运行

```
build.cmd          # 一键构建 → out\PSMenu.exe（需要 lib\ 下两个 DLL，见下）
out\PSMenu.exe     # 无参常驻（托盘 + 热键 Ctrl+Alt+Space + PS 前台中键唤出）
out\PSMenu.exe config      # 打开设置编辑器
out\PSMenu.exe menu        # 呼出菜单（常驻时经管道转发）
out\PSMenu.exe "shot=menu;lk=text"   # 离线出图（AI 验收用，不弹窗）
```

## 依赖

- lib\Newtonsoft.Json.dll、lib\FontAwesomeIconsWpf.dll：从 Quicker 安装目录拷贝（`C:\Program Files\Quicker\`）。
- 编译器：test\roslyn\（Microsoft.Net.Compilers 3.9，NuGet 包解包），目标 .NET Framework 4.8，无 SDK 依赖。

## 结构

- src\App.cs / Shell.cs —— 独立外壳：单实例（Mutex+具名管道接力）、托盘、全局热键、PS 前台中键（WH_MOUSE_LL）、开机自启（HKCU Run）、CLI 单发模式
- src\Entry.cs —— 核心入口 Exec(menuKey, inParam)，替代原 Quicker IStepContext
- src\*.cs —— 其余全部由原动作 6561 行核心脚本机械拆分而来（partial class PsMenu），配置 schema 与 JSON 完全兼容
- 首次启动自动把 %APPDATA%\Quicker 的配置/缓存迁移到 %APPDATA%\PSMenu
