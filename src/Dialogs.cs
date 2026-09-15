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
// #region RecordShortcut Dialog
static string RecordShortcut()
{
    var sww = new Window { Title = "录制快捷键", Width = 380, Height = 130, WindowStartupLocation = WindowStartupLocation.CenterScreen, WindowStyle = WindowStyle.ToolWindow, Background = Th.BBR, Topmost = true, Focusable = true };
    sww.Loaded += (_, __) => { sww.Activate(); Keyboard.Focus(sww); };
    var si = new TextBlock { Text = "按下快捷键 (全部松开后完成)...", FontSize = Tk.FNav, Foreground = Th.BTP, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    sww.Content = si;
    var pressed = new HashSet<Key>(); var seq = new StringBuilder();
    string Mods() { var m = new StringBuilder(); if (pressed.Contains(Key.LeftCtrl) || pressed.Contains(Key.RightCtrl)) m.Append('^'); if (pressed.Contains(Key.LeftShift) || pressed.Contains(Key.RightShift)) m.Append('+'); if (pressed.Contains(Key.LeftAlt) || pressed.Contains(Key.RightAlt)) m.Append('!'); if (pressed.Contains(Key.LWin) || pressed.Contains(Key.RWin)) m.Append('#'); return m.ToString(); }
    string KStr(Key mk) { string kn = mk.ToString(); if (kn.Length == 1 && char.IsLetterOrDigit(kn[0])) return kn.ToLower(); if (mk == Key.Return || mk == Key.Enter) return "{Return}"; if (mk == Key.Delete) return "{DEL}"; if (mk == Key.Escape) return "{ESC}"; if (mk == Key.Tab) return "{TAB}"; if (mk == Key.Back) return "{BACKSPACE}"; if (mk == Key.Space) return "{SPACE}"; if (mk >= Key.F1 && mk <= Key.F24) return "{" + kn + "}"; return "{" + kn + "}"; }
    bool IsMod(Key k) { return k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftShift || k == Key.RightShift || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LWin || k == Key.RWin; }
    Action upd = () => si.Text = "录制: " + seq + Mods();
    sww.PreviewKeyDown += (_, e) => { Key k = e.Key == Key.System ? e.SystemKey : e.Key; if (!pressed.Contains(k)) { pressed.Add(k); if (!IsMod(k)) seq.Append(Mods() + KStr(k)); upd(); } e.Handled = true; };
    sww.PreviewKeyUp += (_, e) => { Key k = e.Key == Key.System ? e.SystemKey : e.Key; pressed.Remove(k); if (pressed.Count == 0) { if (seq.Length > 0) { sww.Tag = seq.ToString(); sww.Close(); } else sww.Close(); } else upd(); };
    sww.ShowDialog(); return (sww.Tag as string) ?? "";
}

// 点击菜单窗口外部任意位置即关闭（WH_MOUSE_LL 低级鼠标钩子），不依赖窗口失焦事件
public sealed class MenuDismissHook
{
    const int WH_MOUSE_LL = 14;
    const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207, WM_XBUTTONDOWN = 0x20B;
    [StructLayout(LayoutKind.Sequential)] struct P { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] struct MS { public P pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct R { public int L; public int T; public int Rt; public int B; }
    delegate IntPtr HP(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HP lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out R r);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string lpModuleName);

    readonly Window _w; readonly Action _dismiss;
    IntPtr _hook; HP _proc; bool _disposed;
    public bool Active { get { return _hook != IntPtr.Zero; } }

    public MenuDismissHook(Window w, Action dismiss)
    {
        _w = w; _dismiss = dismiss;
        _w.Closed += (_, __) => Dispose();   // 菜单关闭时自动卸载钩子
    }
    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_hook != IntPtr.Zero) { try { UnhookWindowsHookEx(_hook); } catch { } _hook = IntPtr.Zero; }
        _proc = null;
    }
    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            int m = wParam.ToInt32();
            if (nCode >= 0 && _hook != IntPtr.Zero &&
                (m == WM_LBUTTONDOWN || m == WM_RBUTTONDOWN || m == WM_MBUTTONDOWN || m == WM_XBUTTONDOWN))
            {
                var ms = (MS)Marshal.PtrToStructure(lParam, typeof(MS));
                R rc; if (GetWindowRect(new WindowInteropHelper(_w).Handle, out rc))
                {
                    bool inside = ms.pt.X >= rc.L && ms.pt.X <= rc.Rt && ms.pt.Y >= rc.T && ms.pt.Y <= rc.B;
                    if (!inside)
                    {
                        var act = _dismiss;
                        _w.Dispatcher.BeginInvoke(new Action(() => { try { act(); } catch { } }));
                    }
                }
            }
        }
        catch { }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

// 无边框窗口边缘/四角拖拽缩放
public static class ResizableWindow
{
    const int GWL_STYLE = -16, WS_THICKFRAME = 0x00040000;
    const int WM_NCHITTEST = 0x0084;
    const int HTCLIENT = 1;
    const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    public static void Enable(Window w)
    {
        var hi = new WindowInteropHelper(w); hi.EnsureHandle(); IntPtr h = hi.Handle;
        SetWindowLong(h, GWL_STYLE, GetWindowLong(h, GWL_STYLE) | WS_THICKFRAME);
        var src = HwndSource.FromHwnd(h); if (src == null) return;
                src.AddHook(Hook);
        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                RECT r; if (GetWindowRect(h, out r))
                {
                    const int m = 6;
                    bool L = x <= r.Left + m, R = x >= r.Right - m;
                    bool T = y <= r.Top + m, B = y >= r.Bottom - m;
                    if (!L && !R && !T && !B) return IntPtr.Zero;
                    if (T && L) { handled = true; return (IntPtr)HTTOPLEFT; }
                    if (T && R) { handled = true; return (IntPtr)HTTOPRIGHT; }
                    if (B && L) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
                    if (B && R) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
                    if (T) { handled = true; return (IntPtr)HTTOP; }
                    if (B) { handled = true; return (IntPtr)HTBOTTOM; }
                    if (L) { handled = true; return (IntPtr)HTLEFT; }
                    if (R) { handled = true; return (IntPtr)HTRIGHT; }
                }
            }
            return IntPtr.Zero;
        }
    }
}
}
}
