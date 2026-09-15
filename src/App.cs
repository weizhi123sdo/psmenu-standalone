using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PsMenuApp
{
// 程序入口：三种运行形态
//  1) 单发 shot 模式（shot= 参数）：渲染成 PNG 后立即退出，给 AI 离线验收界面用
//  2) 转发模式（menu/config 参数 + 已有常驻实例）：通过具名管道转发给第一实例后退出
//  3) 常驻模式：无参数或首次 menu/config，起隐藏外壳（托盘/热键/鼠标钩子）
internal static class App
{
    // winexe 没有控制台，shot 结果要能被外部读到：抢父进程控制台 + 落盘兜底
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private const string MutexName = @"Global\PSMenuStandalone";
    private const string PipeName = "psmenu-ctl";

    private static Mutex _mutex;
    private static bool _crashShown;   // 崩溃提示只弹一次，别把用户屏幕刷屏

    [STAThread]
    public static void Main(string[] args)
    {
        // 修复部分子进程/PTY环境中缺失 windir 环境变量导致 WPF Util..cctor 抛 UriFormatException 崩溃
        try
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("windir")))
            {
                string sr = Environment.GetEnvironmentVariable("SystemRoot");
                if (string.IsNullOrEmpty(sr)) sr = @"C:\Windows";
                Environment.SetEnvironmentVariable("windir", sr);
            }
        }
        catch { }

        // ---------- 全局崩溃兜底：常驻程序最怕静默死去，落日志 + 提示一次 ----------
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            try { PsMenu.LogFail("AppDomain.Unhandled", ex ?? new Exception(e.ExceptionObject?.ToString())); } catch { }
            if (!_crashShown && e.IsTerminating)
            {
                _crashShown = true;
                try { MessageBox.Show("PS便捷菜单遇到未处理异常，即将退出。\n\n" + (ex == null ? "" : ex.Message), "PS便捷菜单 独立版"); } catch { }
            }
        };

        // ---------- shot 单发模式（离线出图验收，不占单实例互斥体） ----------
        string shotSpec = FindShotSpec(args);
        if (shotSpec != null)
        {
            RunShotOnce(shotSpec);
            return;
        }

        // ---------- 单实例 ----------
        bool createdNew;
        _mutex = new Mutex(true, MutexName, out createdNew);
        if (!createdNew)
        {
            // 第二实例：有参数则转发给第一实例，无参数静默退出
            string msg = ParseForward(args);
            if (msg != null) PipeSend(msg);
            return;
        }

        // 首次启动：迁移 Quicker 旧配置（幂等）
        try { PsMenu.ConfigService.MigrateLegacy(); } catch { }

        // ---------- 常驻模式 ----------
        // menu/config 首次启动：起外壳后立即呼出对应动作
        string firstAction = null;
        foreach (var a in args)
        {
            string t = (a ?? "").TrimStart('-', '/');
            if (t.Equals("menu", StringComparison.OrdinalIgnoreCase)) firstAction = "menu";
            else if (t.Equals("config", StringComparison.OrdinalIgnoreCase)) firstAction = "config";
        }

        // WPF 应用对象必须先于任何 Window 创建
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // UI 线程异常：记日志、提示一次，尽量让常驻进程活着（托盘还在就不算白屏）
        app.DispatcherUnhandledException += (s, e) =>
        {
            try { PsMenu.LogFail("Dispatcher", e.Exception); } catch { }
            if (!_crashShown)
            {
                _crashShown = true;
                try { MessageBox.Show("界面出现异常但程序会继续运行。\n\n" + e.Exception.Message, "PS便捷菜单 独立版"); } catch { }
            }
            e.Handled = true;
        };
        var host = new ShellHost(firstAction);
        try
        {
            host.Run(); // 内部 Dispatcher.Run()，直到显式 Shutdown
        }
        catch (Exception ex)
        {
            try { MessageBox.Show("PS便捷菜单外壳异常：" + ex.Message, "PS便捷菜单"); } catch { }
        }
        finally
        {
            try { host.Shutdown(); } catch { }
            try { _mutex.ReleaseMutex(); } catch { }
        }
    }

    // 解析转发给第一实例的命令（menu/config）
    private static string ParseForward(string[] args)
    {
        foreach (var a in args)
        {
            string t = (a ?? "").TrimStart('-', '/');
            if (t.Equals("menu", StringComparison.OrdinalIgnoreCase)) return "menu";
            if (t.Equals("config", StringComparison.OrdinalIgnoreCase)) return "config";
        }
        return null;
    }

    // 找 shot= 规格串（--shot=xxx 也算）
    private static string FindShotSpec(string[] args)
    {
        foreach (var a in args)
        {
            if (a == null) continue;
            if (a.StartsWith("shot=", StringComparison.OrdinalIgnoreCase)) return a;
            if (a.StartsWith("--shot=", StringComparison.OrdinalIgnoreCase)) return a.Substring(2);
        }
        return null;
    }

    // 单发：创建 Application（不 Run），主 STA 线程直接调 Exec——
    // 同线程时 Exec 内部的 Dispatcher.Invoke 会内联执行，天然不阻塞
    private static void RunShotOnce(string shotSpec)
    {
        string result = null;
        try
        {
            new Application(); // 提供 Dispatcher 给 Exec 里的 Invoke 用
            result = PsMenu.Exec("", shotSpec);
        }
        catch (Exception ex)
        {
            result = "ERR " + ex.GetType().Name + " " + ex.Message;
        }

        // 输出三路兜底：父控制台 / 落盘文件 / 尽力而为什么都没有就结束
        string line = result ?? "ERR null";
        bool attached = false;
        try { attached = AttachConsole(ATTACH_PARENT_PROCESS); } catch { }
        if (!attached) { try { attached = AllocConsole(); } catch { } }
        try { Console.WriteLine(line); } catch { }
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "ps_cm_last_shot.txt");
            File.WriteAllText(path, line + Environment.NewLine);
        }
        catch { }
    }

    // 把命令转发给第一实例（管道），失败无所谓（第一实例可能刚好在退出）
    private static void PipeSend(string cmd)
    {
        try
        {
            using (var pipe = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.Out))
            {
                pipe.Connect(1500);
                using (var w = new StreamWriter(pipe)) { w.WriteLine(cmd); w.Flush(); }
            }
        }
        catch { }
    }

    // 给 ShellHost 用的管道监听（常驻实例接收转发）
    internal static void StartPipeServer(Action<string> onCommand)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            while (true)
            {
                try
                {
                    using (var pipe = new System.IO.Pipes.NamedPipeServerStream(PipeName, System.IO.Pipes.PipeDirection.In))
                    {
                        pipe.WaitForConnection();
                        using (var r = new StreamReader(pipe))
                        {
                            string cmd = r.ReadLine();
                            if (!string.IsNullOrEmpty(cmd)) onCommand(cmd);
                        }
                    }
                }
                catch { try { Thread.Sleep(500); } catch { } }
            }
        });
    }
}
}