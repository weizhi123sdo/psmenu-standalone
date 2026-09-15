using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FontAwesome5;
using FontAwesome5.WPF;
using SD = System.Drawing;
using SWF = System.Windows.Forms;

#pragma warning disable 169, 649, 414, 219, 67
namespace PsMenuApp
{
public static partial class PsMenu
{
// #region ActionExecutor
public static class ActionExecutor
{
    public static string Exec(string act, string val, string title)
    {
        if (string.IsNullOrEmpty(act)) return title;
        switch (act)
        {
            case ACT_KEYS:
                ActivatePSWindow();
                SendKeyCombo(val);
                return title;
            case ACT_SCRIPT:
                if (!string.IsNullOrEmpty(val))
                {
                    try
                    {
                        string jp = Path.Combine(Path.GetTempPath(), $"psact_{Guid.NewGuid():N}.jsx");
                        string sv = val.TrimStart('﻿');
                        File.WriteAllText(jp, sv, new UTF8Encoding(false));
                        ActivatePSWindow();
                        object pa = PS.Get();
                        if (pa != null)
                        {
                            Type pt = pa.GetType();
                            pt.InvokeMember("DoJavaScriptFile", BindingFlags.InvokeMethod, null, pa, new object[] { jp });
                            try { File.Delete(jp); } catch { }
                        }
                        else
                        {
                            ShowInfo("未检测到 Photoshop", "请先启动 Photoshop 并打开一个文档，再执行脚本类菜单项。");
                            return "PS not running";
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowInfo("脚本执行失败", "错误信息：\n" + ex.Message);
                        return "Error: " + ex.Message;
                    }
                }
                return title;
            case ACT_RUN:
                if (!string.IsNullOrEmpty(val))
                    try
                    {
                        // 编辑窗约定「第一行=程序路径，其余行=参数」：必须拆行，整个多行串当文件名传会启动失败
                        string[] rln = val.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (rln.Length > 0)
                            Process.Start(new ProcessStartInfo(rln[0], rln.Length > 1 ? string.Join(" ", rln.Skip(1)) : "") { UseShellExecute = true });
                    }
                    catch (Exception ex) { LogFail("ActionExecutor.Run", ex); ShowInfo("无法启动程序", "错误信息：\n" + ex.Message); return "Error: " + ex.Message; }
                return title;
            case ACT_CLIPBOARD:
                ClipHelper.Set(val ?? "");
                return title;
            case ACT_QK:
                if (!string.IsNullOrEmpty(val))
                {
                    try
                    {
                        string qs2 = QuickerStarterPath();
                        if (qs2 == null) { QkToast("找不到 QuickerStarter.exe", "Warning"); return "Error: QuickerStarter not found"; }
                        string arg2 = val.Trim();
                        if (!arg2.StartsWith("runaction:", StringComparison.OrdinalIgnoreCase)) arg2 = "runaction:" + arg2;
                        Process.Start(new ProcessStartInfo(qs2, "-c30 \"" + arg2 + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    }
                    catch (Exception ex) { LogFail("ActionExecutor.QuickerAction", ex); return "Error: " + ex.Message; }
                }
                return title;
            default: return "Unknown: " + act;
        }
    }

    // 解析 QuickerStarter.exe：脚本就跑在 Quicker 进程里，优先取当前进程所在目录，避免写死安装路径
    // 供顶层复用（外层类访问不到嵌套类的 private 成员），改为 public
    public static string QuickerStarterPath()
    {
        try
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string p0 = Path.Combine(Path.GetDirectoryName(exe), "QuickerStarter.exe");
            if (File.Exists(p0)) return p0;
        }
        catch { }
        var cands = new List<string>();
        try { cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Quicker", "QuickerStarter.exe")); } catch { }
        try { cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Quicker", "QuickerStarter.exe")); } catch { }
        foreach (var c in cands) { try { if (File.Exists(c)) return c; } catch { } }
        return null;
    }

    static void ActivatePSWindow()
    {
        var procs = Process.GetProcessesByName("Photoshop");
        if (procs.Length > 0 && procs[0].MainWindowHandle != IntPtr.Zero)
        {
            IntPtr hWnd = procs[0].MainWindowHandle;
            uint psThread = GetWindowThreadProcessId(hWnd, out _);
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            bool attached = false;
            if (psThread != fgThread) { AttachThreadInput(psThread, fgThread, true); attached = true; }
            // 只还原最小化窗口；已最大化的窗口执行 SW_RESTORE 会被拉回普通尺寸
            if (IsIconic(hWnd)) ShowWindow(hWnd, 9);
            SetForegroundWindow(hWnd);
            if (attached) AttachThreadInput(psThread, fgThread, false);
            System.Threading.Thread.Sleep(60);
        }
    }

    static void SendKeyCombo(string combo)
    {
        if (string.IsNullOrEmpty(combo)) return;
        List<byte> dk = new List<byte>();
        string[] parts = combo.Split(';');
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            string s = part.Trim();
            if (s.Length == 0) continue;
            dk.Clear();
            foreach (char c in s) { if (c == '^') dk.Add(VK_CONTROL); else if (c == '+') dk.Add(VK_SHIFT); else if (c == '!') dk.Add(VK_MENU); else if (c == '#') dk.Add(VK_LWIN); }
            foreach (byte vk in dk) SendKey(vk, false);
            if (s.StartsWith("{"))
            {
                int ei = s.IndexOf('}');
                if (ei > 0)
                {
                    string kn = s.Substring(1, ei - 1).ToUpper();
                    byte vk = VK_RETURN;
                    if (kn.Length >= 2 && kn[0] == 'F' && int.TryParse(kn.Substring(1), out int fn) && fn >= 1 && fn <= 24) vk = (byte)(VK_F1 + fn - 1);
                    else switch (kn) { case "ESC": vk = VK_ESCAPE; break; case "DEL": case "DELETE": vk = VK_DELETE; break; case "TAB": vk = VK_TAB; break; case "SPACE": vk = VK_SPACE; break; case "BACKSPACE": case "BS": vk = VK_BACK; break; case "LEFT": vk = VK_LEFT; break; case "RIGHT": vk = VK_RIGHT; break; case "UP": vk = VK_UP; break; case "DOWN": vk = VK_DOWN; break; case "HOME": vk = VK_HOME; break; case "END": vk = VK_END; break; case "PGUP": case "PAGEUP": vk = VK_PRIOR; break; case "PGDN": case "PAGEDOWN": vk = VK_NEXT; break; case "INSERT": case "INS": vk = VK_INSERT; break; }
                    SendKey(vk, false); SendKey(vk, true);
                }
            }
            else
            {
                for (int i = s.Length - 1; i >= 0; i--)
                {
                    if ("^+!#".IndexOf(s[i]) >= 0) continue;
                    ushort v = 0;
                    for (int j = 0; j <= i; j++) { char cc = s[j]; if ("^+!#".IndexOf(cc) >= 0) continue; v = VkKeyScan(cc); break; }
                    if (v != 0) { SendKey((byte)(v & 0xFF), false); SendKey((byte)(v & 0xFF), true); }
                    break;
                }
            }
            foreach (byte vk in Enumerable.Reverse(dk)) SendKey(vk, true);
            System.Threading.Thread.Sleep(50);
        }
    }
}

// #region PS COM
public static class PS
{
    public static object Get()
    {
        string[] probes = { "Photoshop.Application", "Photoshop.Application.2025", "Photoshop.Application.2024", "Photoshop.Application.2023", "Photoshop.Application.2022", "Photoshop.Application.2021", "Photoshop.Application.2020", "Photoshop.Application.2019", "Photoshop.Application.2018", "Photoshop.Application.CC" };
        foreach (var pid in probes)
        {
            try { return Marshal.GetActiveObject(pid); } catch { }
            try { var t = Type.GetTypeFromProgID(pid); if (t != null) return Activator.CreateInstance(t); } catch { }
        }
        return null;
    }
}

// #region ClipboardHelper
public static class ClipHelper
{
    public static void Set(string t) { for (int i = 0; i < 10; i++) try { System.Windows.Forms.Clipboard.SetText(t); return; } catch { System.Threading.Thread.Sleep(50); } }
    public static string Get() { string r = null; for (int i = 0; i < 10; i++) try { r = System.Windows.Forms.Clipboard.GetText(); break; } catch { System.Threading.Thread.Sleep(50); } return r; }
}

// #region Win32 Interop
[DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] static extern ushort VkKeyScan(char ch);
[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
[DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
[DllImport("dwmapi.dll")] static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);
[DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
[StructLayout(LayoutKind.Sequential)] struct DWM_BLURBEHIND { public uint dwFlags; public bool fEnable; public IntPtr hRgnBlur; public bool fTransitionOnMaximized; public const uint DWM_BB_ENABLE = 1; }
[StructLayout(LayoutKind.Sequential)] struct MARGINS { public int leftWidth, rightWidth, topHeight, bottomHeight; }
const uint KEYEVENTF_KEYDOWN = 0; const uint KEYEVENTF_KEYUP = 2;
const byte VK_CONTROL = 0x11; const byte VK_SHIFT = 0x10; const byte VK_MENU = 0x12; const byte VK_LWIN = 0x5B;
const byte VK_RETURN = 0x0D; const byte VK_DELETE = 0x2E; const byte VK_ESCAPE = 0x1B; const byte VK_TAB = 0x09; const byte VK_SPACE = 0x20; const byte VK_BACK = 0x08;
const byte VK_LEFT = 0x25; const byte VK_UP = 0x26; const byte VK_RIGHT = 0x27; const byte VK_DOWN = 0x28;
const byte VK_HOME = 0x24; const byte VK_END = 0x23; const byte VK_PRIOR = 0x21; const byte VK_NEXT = 0x22; const byte VK_INSERT = 0x2D; const byte VK_F1 = 0x70;
static void SendKey(byte vk, bool up) { keybd_event(vk, 0, up ? KEYEVENTF_KEYUP : KEYEVENTF_KEYDOWN, UIntPtr.Zero); System.Threading.Thread.Sleep(up ? 20 : 10); }

}
}
