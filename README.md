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
- src\Entry.cs —— 核心入口 Exec(menuKey, inParam)，替代原 Quicker IServiceContext
- src\*.cs —— 其余全部由原动作 6561 行核心脚本机械拆分而来（partial class PsMenu），配置 schema 与 JSON 完全兼容
- 首次启动自动把 %APPDATA%\Quicker 的配置/缓存迁移到 %APPDATA%\PSMenu

## 分发与自动更新

本仓是独立版的**源码与发布仓**；Quicker 侧的「PSMenu 独立版启动与更新」动作（`action\` 目录）负责下载、校验并常驻拉起。

### 发版流程（全自动）

```
pack.cmd           # 编译 + 打包 + 算 SHA256 + 生成 release\latest.json（并自动镜像到分发仓工作副本）
```

`pack.cmd` 完成后按提示推送分发仓即可对动作生效：

```
cd E:\code\quicker-dist
git add psmenu && git commit -m "chore(psmenu): bump to <版本>"
git push gitee main && git push origin main
```

### 地址约定（重要）

Gitee 侧**没有**独立仓，所有动作运行时抓取的内容都聚合在 `weizhiOWO/quicker-actions`（与 `ps-icons/`、`ps-scripts/` 同级），psmenu 放在 `psmenu/` 子目录：

| 用途 | 地址 |
| --- | --- |
| 清单（主） | `https://gitee.com/weizhiOWO/quicker-actions/raw/main/psmenu/latest.json` |
| 清单（备） | `https://raw.githubusercontent.com/weizhi123sdo/psmenu-standalone/main/release/latest.json` |
| 包（主） | `https://gitee.com/weizhiOWO/quicker-actions/raw/main/psmenu/psmenu_standalone.zip` |
| 包（备） | `https://raw.githubusercontent.com/weizhi123sdo/psmenu-standalone/main/release/psmenu_standalone.zip` |

注：`gitee.com/weizhiOWO/quicker-dist/...` 是**失效地址**（该仓在 Gitee 并不存在），历史文档里出现过，勿再使用。

### 两个编码坑（已处理）

- `scripts/generate_pack.ps1` **必须带 UTF-8 BOM**，否则 PowerShell 5.1 按 GBK 误读中文注释，脚本解析出错。
- `latest.json` / `version.txt` **必须无 BOM**，否则严格 JSON 解析器（含浏览器 `JSON.parse`）会失败。脚本用 `UTF8Encoding($false)` 写出。

### 动作侧行为

- 安装位：`%LocalAppData%\PSMenu\app\`（`version.txt` 记录已装版本）。
- 下载后强校验 `sha256`；解压按目录前缀校验防 Zip Slip。
- 未运行时静默常驻拉起；已在运行则经 `\\.\pipe\psmenu-ctl` 接力唤出，绝不双开。

