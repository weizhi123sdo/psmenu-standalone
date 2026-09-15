using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace PsMenuApp
{
// 常驻外壳：隐藏窗口 + 托盘 + 全局热键 + PS 中键唤出 + 管道命令接收
internal sealed class ShellHost : IDisposable
{
    // ---------- user32 / kernel32 P/Invoke ----------
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private const int WH_MOUSE_LL = 14;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0xB00B;

    // 编辑器「启动与触发」页要调的静态入口都走这个单例引用
    private static ShellHost _inst;
    // 当前生效的热键组合（ApplyHotKey 失败回滚时要用）
    private uint _curMod, _curVk;
    private Forms.ToolStripMenuItem _miAuto, _miMid;   // 托盘勾选项引用，编辑器改设置后同步勾选态

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ---------- 状态 ----------
    private readonly string _firstAction;   // 首次启动要立即执行的动作（menu/config），null=不执行
    private Window _host;                    // 隐藏 host 窗口（热键/钩子消息泵）
    private HwndSource _hwndSource;
    private Forms.NotifyIcon _tray;
    private IntPtr _mouseHook = IntPtr.Zero;
    private LowLevelMouseProc _hookProc;     // 持引用防委托被 GC
    private volatile bool _closing;

    public ShellHost(string firstAction) { _firstAction = firstAction; }

    // 呼出类动作一律线程池调用，不在钩子/热键/托盘线程直接调——
    // LayerDetector 有 COM 调用可能长时间阻塞，而 Exec 内部 UI 部分会自己 Invoke 回 UI 线程，线程池调用是安全的
    private void ShowMenu() { try { PsMenu.Exec("", ""); } catch (Exception ex) { try { PsMenu.LogFail("ShellShowMenu", ex); } catch { } } }
    private void ShowConfig() { try { PsMenu.Exec("config", ""); } catch (Exception ex) { try { PsMenu.LogFail("ShellShowConfig", ex); } catch { } } }

    // 入口：创建窗口、挂热键/钩子、起托盘、进消息循环（Run 返回即外壳结束）
    public void Run()
    {
        _inst = this;
        // 触发设置统一从 ShellConfig 读（热键组合 / 中键唤出开关），文件缺失或坏 JSON 走默认值
        ShellConfig.Load();
        _host = new Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "PS便捷菜单 独立版",
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000, Top = -32000, // Show 后挪到屏幕外；不用 Visibility.Hidden（有些消息循环场景会影响 Dispatcher）
        };

        _host.Loaded += (s, e) =>
        {
            try
            {
                _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(_host).Handle);
                _hwndSource.AddHook(WndProc);

                // 全局热键：组合来自 ShellConfig（默认 Ctrl + 空格），编辑器里可改
                _curMod = (uint)ShellConfig.HkMod; _curVk = (uint)ShellConfig.HkVk;
                if (!RegisterHotKey(_hwndSource.Handle, HOTKEY_ID, _curMod, _curVk))
                    try { PsMenu.LogFail("HotkeyRegister", new Exception("全局热键注册失败 mod=" + _curMod + " vk=" + _curVk)); } catch { }

                // 鼠标钩子单独包一层：钩子装不上（DLL 入口点缺失等）不该拖死托盘/气泡/热键
                try { InstallMouseHook(); }
                catch (Exception ex) { try { PsMenu.LogFail("InstallMouseHook", ex); } catch { } }

                // 管道监听：接收第二实例转发来的 menu/config
                App.StartPipeServer(cmd =>
                {
                    if (_closing) return;
                    if (cmd == "menu") ThreadPool.QueueUserWorkItem(_ => ShowMenu());
                    else if (cmd == "config") _host.Dispatcher.BeginInvoke(new Action(ShowConfig));
                });

                CreateTray();

                // 首启气泡：a) 这次启动真的迁移过 Quicker 配置 → 提示；b) 首次常驻启动 →
                // 「已就绪」提示（热键文本从 ShellConfig 取，用户改过热键后文案自动跟着变）。
                // firstRunDone 落盘在 shell_state.json，保证只弹一次。
                try
                {
                    if (PsMenu.ConfigService.DidMigrate)
                        _tray.ShowBalloonTip(3000, "PS便捷菜单", "已从 Quicker 迁移配置", Forms.ToolTipIcon.Info);
                    if (!ShellConfig.FirstRunDone)
                    {
                        _tray.ShowBalloonTip(3000, "PS便捷菜单",
                            "PS便捷菜单已就绪：" + ShellConfig.HotkeyText(ShellConfig.HkMod, ShellConfig.HkVk) + " 或 PS 前台中键呼出",
                            Forms.ToolTipIcon.Info);
                        ShellConfig.FirstRunDone = true;
                        ShellConfig.Save();
                    }
                }
                catch { }

                // 启动预热：延迟 1.5s 再做（不抢启动），把配置/最近使用/脚本库/拼音表先读进内存，
                // 首次呼出菜单就不用现场加载了。全部 try-catch 静默——预热失败大不了首呼慢一点。
                var warm = new Thread(() =>
                {
                    try { Thread.Sleep(1500); } catch { }
                    try { PsMenu.ConfigService.Load(); } catch { }
                    try { PsMenu.Recent.Ensure(); } catch { }
                    try { PsMenu.NetScripts.Load(); } catch { }
                    try { PsMenu.PinYin.Warmup(); } catch { }
                });
                warm.IsBackground = true;
                warm.Start();

                // 首次带 menu/config 参数启动：外壳就绪后立即呼出一次
                if (_firstAction == "menu") ThreadPool.QueueUserWorkItem(_ => ShowMenu());
                else if (_firstAction == "config") _host.Dispatcher.BeginInvoke(new Action(ShowConfig));
            }
            catch (Exception ex)
            {
                try { PsMenu.LogFail("ShellInit", ex); } catch { }
            }
        };

        try { _host.Show(); } catch { }
        Dispatcher.Run(); // 消息循环，显式 Shutdown 后返回
    }

    // ---------- 热键消息 ----------
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            ThreadPool.QueueUserWorkItem(_ => ShowMenu());
        }
        return IntPtr.Zero;
    }

    // ---------- 低级鼠标钩子：PS 前台中键唤出 ----------
    private void InstallMouseHook()
    {
        _hookProc = MouseHookProc;
        using (var cur = Process.GetCurrentProcess())
        {
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(cur.MainModule.ModuleName), 0);
        }
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 钩子回调必须极快：判断 + 丢线程池，绝不在这里干重活
        if (nCode >= 0 && wParam.ToInt32() == WM_MBUTTONDOWN && !_closing && MiddleEnabled)
        {
            if (ForegroundIsPhotoshop())
            {
                ThreadPool.QueueUserWorkItem(_ => ShowMenu());
                return new IntPtr(1); // 拦截这次中键，不让 PS 收到
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    // 中键唤出开关的数据与读写全部走 ShellConfig（%APPDATA%\PSMenu\shell_state.json）
    private static bool MiddleEnabled { get { return ShellConfig.MiddleCall; } }

    // ---------- 编辑器「启动与触发」页调用的静态入口 ----------
    // 换全局热键：注销旧热键→注册新的；失败（组合键被占用等）弹不出提示、这里只回滚并返回错误文本。
    // 返回 null = 成功；否则为给用户看的失败原因。热键本身立即生效，配置由调用方（编辑器）写回 ShellConfig。
    public static string ApplyHotKey(int mods, int vk)
    {
        var inst = _inst;
        if (inst == null || inst._host == null) return null;   // 单发 shot 模式没有外壳：无事可做，视为成功
        // 热键挂在 host 窗口上，注册/注销必须在它的 UI 线程
        if (!inst._host.Dispatcher.CheckAccess())
            return inst._host.Dispatcher.Invoke(new Func<string>(() => inst.ApplyHotKeyCore((uint)mods, (uint)vk)));
        return inst.ApplyHotKeyCore((uint)mods, (uint)vk);
    }

    private string ApplyHotKeyCore(uint mods, uint vk)
    {
        if (_hwndSource == null) return "外壳尚未就绪，请稍后再试。";
        IntPtr hwnd = _hwndSource.Handle;
        UnregisterHotKey(hwnd, HOTKEY_ID);
        if (RegisterHotKey(hwnd, HOTKEY_ID, mods, vk))
        {
            _curMod = mods; _curVk = vk;
            return null;
        }
        // 新组合注册失败（多半是被其他程序占用）：回滚旧热键，别让用户落得连热键都没有
        RegisterHotKey(hwnd, HOTKEY_ID, _curMod, _curVk);
        return "组合键注册失败：它可能已被其他程序占用，换一个试试。（原热键已恢复）";
    }

    // 编辑器改了触发设置后喊一声：重读配置并刷新托盘菜单勾选态（两边同源）
    public static void NotifySettingsChanged()
    {
        ShellConfig.Load();
        var inst = _inst;
        if (inst == null || inst._tray == null) return;
        Action upd = () =>
        {
            try
            {
                if (inst._miMid != null) inst._miMid.Checked = ShellConfig.MiddleCall;
                if (inst._miAuto != null) inst._miAuto.Checked = AutostartEnabled();
            }
            catch { }
        };
        if (inst._host != null && !inst._host.Dispatcher.CheckAccess()) inst._host.Dispatcher.BeginInvoke(upd);
        else upd();
    }

    private long _fgCacheTick = -1000;
    private bool _fgCacheVal;

    // 前台窗口是否属于 Photoshop 进程（200ms 防抖缓存，钩子回调里不能每次查进程名）
    private bool ForegroundIsPhotoshop()
    {
        long now = Environment.TickCount;
        if (now - _fgCacheTick < 200) return _fgCacheVal;
        bool isPs = false;
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid != 0)
                {
                    using (var p = Process.GetProcessById((int)pid))
                        isPs = string.Equals(p.ProcessName, "Photoshop", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch { }
        _fgCacheTick = now;
        _fgCacheVal = isPs;
        return isPs;
    }

    // ---------- 托盘 ----------
    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "PS便捷菜单 独立版",
            Visible = true,
        };

        var menu = new Forms.ContextMenuStrip();

        menu.Items.Add("呼出菜单", null, (s, e) => ThreadPool.QueueUserWorkItem(_ => ShowMenu()));
        menu.Items.Add("设置…", null, (s, e) => ShowConfig()); // 托盘事件在 UI 线程，Exec 的 config 分支直接调
        menu.Items.Add(new Forms.ToolStripSeparator());

        var miAuto = new Forms.ToolStripMenuItem("开机自启") { Checked = AutostartEnabled() };
        miAuto.Click += (s, e) => { SetAutostart(!AutostartEnabled()); miAuto.Checked = AutostartEnabled(); };
        menu.Items.Add(miAuto);
        _miAuto = miAuto;

        var miMid = new Forms.ToolStripMenuItem("PS 前台中键唤出") { Checked = MiddleEnabled };
        miMid.Click += (s, e) =>
        {
            ShellConfig.MiddleCall = !ShellConfig.MiddleCall;
            ShellConfig.Save();
            miMid.Checked = ShellConfig.MiddleCall;
        };
        menu.Items.Add(miMid);
        _miMid = miMid;

        // 配置方案子菜单：动态列出全部方案（编辑器页2 / 这里共用 ConfigService.Profiles 一组静态方法），
        // 当前方案打勾，点击即切换。切换是几个 KB 的文件复制，托盘点击事件本就在主线程，同步做最省心，
        // 顺手把勾选态刷对；失败只留日志，不打断托盘。
        var miProf = new Forms.ToolStripMenuItem("配置方案");
        Action refreshProfChecks = () =>
        {
            string cur = PsMenu.ConfigService.Profiles.Current();
            foreach (Forms.ToolStripMenuItem o in miProf.DropDownItems)
                o.Checked = o.Text == cur;
        };
        foreach (var pn in PsMenu.ConfigService.Profiles.List())
        {
            string nm = pn;
            var pit = new Forms.ToolStripMenuItem(nm) { Checked = nm == PsMenu.ConfigService.Profiles.Current() };
            pit.Click += (s, e) =>
            {
                try
                {
                    if (!PsMenu.ConfigService.Profiles.SwitchTo(nm)) return;
                    refreshProfChecks();
                }
                catch (Exception ex2) { try { PsMenu.LogFail("tray.SwitchProfile", ex2); } catch { } }
            };
            miProf.DropDownItems.Add(pit);
        }
        miProf.DropDownOpened += (s, e) => refreshProfChecks();   // 每次展开前重算勾选态（编辑器里可能切过）
        menu.Items.Add(miProf);

        menu.Items.Add(new Forms.ToolStripSeparator());
        // 常用入口：配置目录 / 错误日志。日志不存在时开 %TEMP%，至少让用户知道去哪找。
        menu.Items.Add("打开配置目录", null, (s, e) =>
        {
            try
            {
                string d = PsMenu.ConfigService.DataDir;
                Directory.CreateDirectory(d);
                Process.Start("explorer.exe", d);
            }
            catch { }
        });
        menu.Items.Add("打开日志", null, (s, e) =>
        {
            try
            {
                string log = PsMenu.FailLogPath;
                if (File.Exists(log)) Process.Start("explorer.exe", "/select,\"" + log + "\"");
                else Process.Start("explorer.exe", Path.GetTempPath());
            }
            catch { }
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => Shutdown());

        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ThreadPool.QueueUserWorkItem(_ => ShowMenu());
        };
    }

    // 现画一个 32×32 图标：深色圆角方块 + 白色鼠标指针造型，不依赖外部文件
    private static System.Drawing.Icon MakeIcon()
    {
        using (var bmp = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new GraphicsPath())
                {
                    // 圆角方块底
                    bg.AddArc(0, 0, 12, 12, 180, 90);
                    bg.AddArc(20, 0, 12, 12, 270, 90);
                    bg.AddArc(20, 20, 12, 12, 0, 90);
                    bg.AddArc(0, 20, 12, 12, 90, 90);
                    bg.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(38, 38, 42))) g.FillPath(b, bg);
                }
                // 简化鼠标指针多边形
                PointF[] cursor =
                {
                    new PointF(10, 6), new PointF(10, 24), new PointF(14, 19),
                    new PointF(17, 26), new PointF(20, 24), new PointF(17, 18),
                    new PointF(23, 18),
                };
                using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, cursor);
                using (var pen = new Pen(Color.FromArgb(38, 38, 42), 1.2f)) g.DrawPolygon(pen, cursor);
            }
            IntPtr hIcon = bmp.GetHicon();
            using (var tmp = System.Drawing.Icon.FromHandle(hIcon))
                return (System.Drawing.Icon)tmp.Clone(); // Clone 出独立副本，Bitmap 释放后仍可用
        }
    }

    // ---------- 开机自启（HKCU\...\Run，托盘与编辑器「启动与触发」页共用同一套静态方法） ----------
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "PSMenu";

    public static bool AutostartEnabled()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                return k != null && k.GetValue(RunValue) != null;
        }
        catch { return false; }
    }

    // 启动项命令行：注册表里有现成的就读它，否则给出按当前程序路径算出来的（编辑器小字展示用）
    public static string AutostartCommand()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                var v = k == null ? null : k.GetValue(RunValue) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { }
        return "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"";
    }

    // 由编辑器直接指定开 / 关（托盘是取反，编辑器开关是置值，所以拆成两个入口）
    public static void SetAutostart(bool on)
    {
        bool cur = AutostartEnabled();
        if (on == cur) return;
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue(RunValue, "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                else k.DeleteValue(RunValue, false);
            }
        }
        catch (Exception ex) { try { PsMenu.LogFail("AutostartSet", ex); } catch { } }
    }

    // ---------- 退出：摘钩子、注销热键、销托盘、Shutdown ----------
    public void Shutdown()
    {
        if (_closing) return;
        _closing = true;
        _inst = null;
        try
        {
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            if (_hwndSource != null) UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }
        catch { }
        var app = Application.Current;
        if (app != null) app.Dispatcher.BeginInvoke(new Action(() => { try { app.Shutdown(); } catch { } }));
    }

    public void Dispose()
    {
        try { Shutdown(); } catch { }
    }
}
}
