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
// #region Tk — UI 设计令牌（方向 C：亮色卡片工作台）
// 规范：颜色 / 圆角 / 间距 / 字号 / 动效**只在这里定义**，页面代码里不再出现裸数值。
// 取用：颜色 Tk.B(Tk.Xxx)（带缓存的冻结画刷）/ Tk.C(Tk.Xxx)（颜色值）；数值直接用常量。
//
// 为什么要有这一层：编辑器原先有**两套浅色板**且互相对不上——
//   手写的那套：画布 #EEF0F3、强调 #0A66C2、细边 #E4E7EA
//   Th 类那套（U.* 工厂在用）：画布 #F5F5FA、强调 #0078D4、细边 #D0D0D8
// 于是同一个窗口里两种蓝、两种灰。现在两边都从 Tk 取值，Th 退化为"按 Color 取画刷"的薄访问器。
//
// 圆角规范（5 档，7 这类没有来由的中间值一律并到最近的档）：
//   2/4 微元素 · 6 按钮/chip/输入框 · 8 卡片/行/图标块 · 12 窗口外壳/弹窗
// 间距按 4px 节奏；字号收敛到 5 档（原来有 9.5/10/11/12/13/14/15 七种）。
public static class Tk
{
    // ---- 颜色（语义命名，不叫"浅灰1"叫"画布/卡片/细边/主文字"）----
    public const string Canvas = "#DD202020";      // 画布底：极浅灰
    public const string Surface = "#DD323232";     // 卡片面
    public const string SurfaceAlt = "#22FFFFFF";  // 次级面（悬停底/分区底）
    // 边框三级：层次靠"分隔强度"编码，不是一种灰铺到底
    public const string BorderSubtle = "#00FFFFFF";   // 卡内分隔（最弱）
    public const string RowLine = "#18FFFFFF";        // 列表行底边：行与行要数得清，比卡内分隔强一档
    public const string BorderDefault = "#33FFFFFF";  // 面板/控件边
    public const string BorderStrong = "#55FFFFFF";   // 强调边（拖放目标/聚焦）
    public const string Ink = "#F0F0F0";         // 主文字
    public const string InkMuted = "#B4B4B4";    // 次文字
    public const string InkFaint = "#8B93A5";    // 更弱（占位/图标）
    public const string Accent = "#0078D4";      // 强调（选中/主按钮）
    public const string AccentSoft = "#1AFFFFFF";  // 强调底
    public const string Danger = "#E81123";      // 危险动作（删除等）
    public const string DangerSoft = "#44E81123";  // 危险态淡底（徽章/药丸）
    public const string Warn = "#C2410C";        // 警示文字
    public const string Ok = "#00B294";
    public const string OkSoft = "#4400B294";     // 成功态淡底（徽章/药丸）          // 成功文字
    public const string Info = "#0A66C2";        // 信息态（与强调同值，但语义分开命名）
    public const string Pin = "#3BBE8F";         // 置顶标记

    // ---- 圆角 ----
    public const double RXs = 2, RXm = 4, RSm = 8, RMd = 8, RLg = 8;

    // ---- 间距（4px 节奏）----
    public const double S2 = 2, S4 = 4, S6 = 6, S8 = 8, S10 = 10, S12 = 12, S16 = 16, S20 = 20, S24 = 24;

    // ---- 字号 ----
    // 2026-09-10 反馈"整体字体大一点、有点太细"后整体抬一档（原 10/11/12/13/15）
    public const double FMicro = 11, FSmall = 12, FBody = 13, FNav = 14, FTitle = 16;

    // ---- 动效时长(ms)：微交互 ~150ms，较大的过渡 200~250ms ----
    public const int MMicro = 150, MSlow = 240;

    static readonly Dictionary<string, Color> _cc = new Dictionary<string, Color>();
    static readonly Dictionary<string, SolidColorBrush> _bb = new Dictionary<string, SolidColorBrush>();

    public static Color C(string hex)
    {
        Color c;
        if (_cc.TryGetValue(hex, out c)) return c;
        c = PHex(hex, Colors.Gray); _cc[hex] = c; return c;
    }
    // 带缓存 + 冻结：同一颜色不再每次 new。
    // ⚠ 冻结后**不能**再对返回的画刷做 BeginAnimation —— 需要做颜色动画的悬停效果请自己 new 一个可变画刷。
    public static SolidColorBrush B(string hex)
    {
        SolidColorBrush b;
        if (_bb.TryGetValue(hex, out b)) return b;
        b = new SolidColorBrush(C(hex)); b.Freeze(); _bb[hex] = b; return b;
    }
}

// #region Theme — PS Native Fusion
public static class Th
{
    static Th() { Sync(); }
    static SolidColorBrush _bBR, _bBC, _bBI, _bAB, _bAG, _bAR, _bTP, _bTS, _bTD, _bBS, _bBO;
    static bool _isLight;
    public static bool IsLight { get => _isLight; set { if (_isLight != value) { _isLight = value; Sync(); } } }
    public static Color BR { get; private set; } public static Color BC { get; private set; } public static Color BI { get; private set; }
    public static Color AB { get; private set; } public static Color AG { get; private set; } public static Color AR { get; private set; }
    public static Color TP { get; private set; } public static Color TS { get; private set; } public static Color TD { get; private set; }
    public static Color BS { get; private set; } public static Color BO { get; private set; }
    public static SolidColorBrush BBR => _bBR; public static SolidColorBrush BBC => _bBC; public static SolidColorBrush BBI => _bBI;
    public static SolidColorBrush BAB => _bAB; public static SolidColorBrush BAG => _bAG; public static SolidColorBrush BAR => _bAR;
    public static SolidColorBrush BTP => _bTP; public static SolidColorBrush BTS => _bTS; public static SolidColorBrush BTD => _bTD;
    public static SolidColorBrush BBS => _bBS; public static SolidColorBrush BBO => _bBO;

    static void Sync()
    {
        if (IsLight)
        {
            // 亮色 = 方向 C。值全部取自 Tk，别再在这里写第二套（原来这里的强调色/细边和手写那套对不上）
            BR = Tk.C(Tk.Canvas); BC = Tk.C(Tk.Surface); BI = Tk.C(Tk.SurfaceAlt);
            AB = Tk.C(Tk.Accent); AG = Tk.C(Tk.Ok); AR = Tk.C(Tk.Danger);
            TP = Tk.C(Tk.Ink); TS = Tk.C(Tk.InkMuted); TD = Tk.C(Tk.InkFaint);
            BS = Tk.C(Tk.BorderDefault); BO = Color.FromArgb(30, 0, 0, 0);
        }
        else
        {
            // PS native dark palette
            BR = Color.FromRgb(37, 37, 38);    // window bg #252526
            BC = Color.FromRgb(45, 45, 45);    // panel bg #2D2D2D
            BI = Color.FromRgb(60, 60, 60);    // input bg #3C3C3C
            AB = Color.FromRgb(51, 153, 255);  // accent #3399FF
            AG = Color.FromRgb(78, 201, 176);  // success #4EC9B0
            AR = Color.FromRgb(244, 71, 71);   // danger #F44747
            TP = Color.FromRgb(204, 204, 204); // primary text #CCCCCC
            TS = Color.FromRgb(153, 153, 153); // secondary text #999999
            TD = Color.FromRgb(100, 100, 100); // disabled text
            BS = Color.FromRgb(62, 62, 62);    // border #3E3E3E
            BO = Color.FromArgb(40, 255, 255, 255);
        }
        if (_bBR == null) { _bBR = new SolidColorBrush(); _bBC = new SolidColorBrush(); _bBI = new SolidColorBrush(); _bAB = new SolidColorBrush(); _bAG = new SolidColorBrush(); _bAR = new SolidColorBrush(); _bTP = new SolidColorBrush(); _bTS = new SolidColorBrush(); _bTD = new SolidColorBrush(); _bBS = new SolidColorBrush(); _bBO = new SolidColorBrush(); }
        _bBR.Color = BR; _bBC.Color = BC; _bBI.Color = BI; _bAB.Color = AB; _bAG.Color = AG; _bAR.Color = AR;
        _bTP.Color = TP; _bTS.Color = TS; _bTD.Color = TD; _bBS.Color = BS; _bBO.Color = BO;
    }
    public static SolidColorBrush B(Color c)
    {
        if (c == BR) return _bBR; if (c == BC) return _bBC; if (c == BI) return _bBI; if (c == AB) return _bAB;
        if (c == AG) return _bAG; if (c == AR) return _bAR; if (c == TP) return _bTP; if (c == TS) return _bTS;
        if (c == TD) return _bTD; if (c == BS) return _bBS; if (c == BO) return _bBO;
        return new SolidColorBrush(c);
    }
}


// #region Color helpers
static Color PHex(string h, Color fb)
{
    if (string.IsNullOrEmpty(h) || h.Length < 7) return fb;
    try
    {
        if (h.Length == 9) { byte a = byte.Parse(h.Substring(1, 2), System.Globalization.NumberStyles.HexNumber); byte r = byte.Parse(h.Substring(3, 2), System.Globalization.NumberStyles.HexNumber); byte g = byte.Parse(h.Substring(5, 2), System.Globalization.NumberStyles.HexNumber); byte b = byte.Parse(h.Substring(7, 2), System.Globalization.NumberStyles.HexNumber); return Color.FromArgb(a, r, g, b); }
        else { byte r = byte.Parse(h.Substring(1, 2), System.Globalization.NumberStyles.HexNumber); byte g = byte.Parse(h.Substring(3, 2), System.Globalization.NumberStyles.HexNumber); byte b = byte.Parse(h.Substring(5, 2), System.Globalization.NumberStyles.HexNumber); return Color.FromRgb(r, g, b); }
    }
    catch { return fb; }
}
// 同色 brush 复用：原先每次调用都 new（一次 rebuild 就几十个），冻结后可安全共享
static readonly Dictionary<string, SolidColorBrush> _brushCache = new Dictionary<string, SolidColorBrush>();
static SolidColorBrush PB(string h, Color fb)
{
    string key = (h ?? "") + "|" + fb.A + "," + fb.R + "," + fb.G + "," + fb.B;
    SolidColorBrush b;
    if (_brushCache.TryGetValue(key, out b)) return b;
    b = new SolidColorBrush(PHex(h, fb)); b.Freeze();
    _brushCache[key] = b;
    return b;
}
// "主题色 + 指定透明度"必须用这个，不能用 PB：
// PB 的第二个参数只是 hex 解析失败时的兜底，而 PHex 对 #RRGGBB 一律返回 alpha=255，
// 所以 PB(cfg.HC, Color.FromArgb(40, 51, 153, 255)) 看着像"40/255 淡底"，实际是**实心**强调色。
// （2026-09-10 真机看菜单时发现：置顶栏那两个图标块成了刺眼的高饱和色。）
// 弱化前景（分隔线 / 次要文字 / 托盘底）：必须由**前景色**加透明度派生，不能用描边色。
// 描边色在亮色主题里是浅色（如「柠檬苏打」#EFE6B0），拿它画分隔线和灰字会直接糊掉——
// 用户截图反馈"每个项底部颜色和分割颜色不明显"就是这个原因；前景色在暗主题是浅色、亮主题是深色，
// 同一个透明度两边都成立，所以这类角色不该单独配一个色值。
static SolidColorBrush PMuted(byte a, Color fb)
{
    string fcs = (UiCfg != null && !string.IsNullOrEmpty(UiCfg.FC)) ? UiCfg.FC : null;
    return PBA(fcs ?? "#CCCCCC", a, fb);
}
static SolidColorBrush PBA(string h, byte a, Color fb)
{
    string key = (h ?? "") + "#" + a + "|" + fb.R + "," + fb.G + "," + fb.B;
    SolidColorBrush b;
    if (_brushCache.TryGetValue(key, out b)) return b;
    Color c = PHex(h, fb);
    b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze();
    _brushCache[key] = b;
    return b;
}

// #region IconRenderer
public static class IconR
{
    // net（mdi）图标的目标字形占比：实测 FA 字形占框 ~93%，net 归一后按同一比例，两者视觉重量齐平
    public const double NetGlyphFraction = 0.93;
    public static UIElement Render(string iconStr, ActionType at, double size, string iconClr = null)
    {
        if (!string.IsNullOrEmpty(iconStr) && iconStr.StartsWith("net:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var nel = NetIcons.Render(iconStr.Substring(4), size, FaBrush(iconStr, at, iconClr));
                if (nel != null) return nel;
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(iconStr) && iconStr.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string url = iconStr.Substring(4);
                string cd = Path.Combine(Path.GetTempPath(), "ps_cm_ic"); Directory.CreateDirectory(cd);
                uint h = (uint)url.GetHashCode(); string cp = Path.Combine(cd, h.ToString("X8") + ".png");
                if (!File.Exists(cp)) using (var wc = new WebClient()) wc.DownloadFile(url, cp);
                var img = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
                var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(cp); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
                img.Source = bi; return img;
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(iconStr) && iconStr.StartsWith("fa:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string en = iconStr.Substring(3); int ci = en.LastIndexOf('#'); if (ci >= 0) en = en.Substring(0, ci);
                en = en.Trim();
                FontAwesome5.EFontAwesomeIcon iv;
                try { iv = (FontAwesome5.EFontAwesomeIcon)Enum.Parse(typeof(FontAwesome5.EFontAwesomeIcon), en); }
                catch { en = Fa6To5(en); if (en == null) throw; iv = (FontAwesome5.EFontAwesomeIcon)Enum.Parse(typeof(FontAwesome5.EFontAwesomeIcon), en); }
                var fg = FaBrush(iconStr, at, iconClr);
                var path = FontAwesome5.WPF.SvgAwesome.CreatePath(iv, fg, size);
                var vb = new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = path };
                return vb;
            }
            catch { }
        }
        var fc2 = ActionColor(at);
        if (!string.IsNullOrEmpty(iconClr)) { var icc = PHex(iconClr, Color.FromRgb(200, 200, 200)); fc2 = new SolidColorBrush(icc); fc2.Freeze(); }
        return new TextBlock { Text = AT.Lbl(at), FontSize = size, Foreground = fc2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }
    static string Fa6To5(string e)
    {
        if (string.IsNullOrEmpty(e)) return null;
        if (e.Contains("Gear")) { var alt = e.Replace("Gear", "Cog"); try { Enum.Parse(typeof(FontAwesome5.EFontAwesomeIcon), alt); return alt; } catch { } }
        try { Enum.Parse(typeof(FontAwesome5.EFontAwesomeIcon), e); return e; } catch { return null; }
    }
    static Brush FaBrush(string istr, ActionType at, string iconClr = null)
    {
        if (!string.IsNullOrEmpty(iconClr)) { var b2 = new SolidColorBrush(PHex(iconClr, Color.FromRgb(200, 200, 200))); b2.Freeze(); return b2; }
        int ci = istr.LastIndexOf('#');
        if (ci >= 0 && istr.Length - ci >= 7) try { byte r = byte.Parse(istr.Substring(ci + 1, 2), System.Globalization.NumberStyles.HexNumber); byte g = byte.Parse(istr.Substring(ci + 3, 2), System.Globalization.NumberStyles.HexNumber); byte b = byte.Parse(istr.Substring(ci + 5, 2), System.Globalization.NumberStyles.HexNumber); return new SolidColorBrush(Color.FromRgb(r, g, b)); } catch { }
        return ActionColor(at);
    }
    public static SolidColorBrush ActionColor(ActionType a)
    {
        Color c;
        switch (a) { case ActionType.Keys: c = Color.FromRgb(200, 200, 100); break; case ActionType.Script: c = Color.FromRgb(100, 200, 180); break; case ActionType.Run: c = Color.FromRgb(200, 140, 50); break; case ActionType.Clipboard: c = Color.FromRgb(140, 140, 220); break; case ActionType.QuickerAction: c = Color.FromRgb(210, 120, 220); break; default: c = Color.FromRgb(150, 150, 155); break; }
        var b = new SolidColorBrush(c); b.Freeze(); return b;
    }
}
// #region U — Quick UI builders
public static class U
{
    public static TextBlock L(string t, double fs = Tk.FBody, Color? c = null) => new TextBlock { Text = t, FontSize = fs, FontWeight = FontWeights.Medium, Foreground = Th.B(c ?? Th.TS), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
    public static TextBox T(string t = "", double w = double.NaN, double fs = 12)
    {
        var tb = new TextBox { Text = t, FontSize = fs, Background = Th.BBI, Foreground = Th.BTP, BorderBrush = Th.B(Th.BS), BorderThickness = new Thickness(1), Padding = new Thickness(Tk.S8, Tk.S6, Tk.S8, Tk.S6), CaretBrush = Th.BTP };
        if (!double.IsNaN(w)) tb.Width = w; return tb;
    }
    public static ComboBox Cb(params string[] it)
    {
        var cb = new ComboBox { FontSize = Tk.FBody, Background = Th.BBI, Foreground = Th.BTP, BorderBrush = Th.B(Th.BS), BorderThickness = new Thickness(1), Padding = new Thickness(Tk.S6, Tk.S4, Tk.S6, Tk.S4) };
        foreach (var i in it) cb.Items.Add(i); return cb;
    }
    public static Button Btn(string t, Color c, Action a, double fs = Tk.FSmall, double w = double.NaN)
    {
        var b = new Button { Content = BtnContent(t, c, fs), Background = Th.BBC, BorderBrush = Th.B(Th.BS), BorderThickness = new Thickness(1), Padding = new Thickness(Tk.S10, Tk.S6, Tk.S10, Tk.S6), Cursor = Cursors.Hand };
        b.Click += (s, e) => a(); if (!double.IsNaN(w)) b.Width = w; return b;
    }
    public static UIElement BtnContent(string t, Color c, double fs)
    {
        string ic = null; string rest = t;
        string[] heads = { "📋", "🗑", "📥", "📤", "🔄" };
        string[] fas = { "fa:Solid_Clipboard", "fa:Solid_Trash", "fa:Solid_FileImport", "fa:Solid_FileExport", "fa:Solid_Sync" };
        for (int i2 = 0; i2 < heads.Length; i2++)
            if (t.StartsWith(heads[i2])) { ic = fas[i2]; rest = t.Substring(heads[i2].Length).TrimStart(); break; }
        string hx = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        if (ic == null) return new TextBlock { Text = t, FontSize = fs, FontWeight = FontWeights.Medium, Foreground = Th.B(c) };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        try { sp.Children.Add(IconR.Render(ic, ActionType.Keys, Math.Max(12, fs), hx)); }
        catch { sp.Children.Add(new TextBlock { Text = t, FontSize = fs, FontWeight = FontWeights.Medium, Foreground = Th.B(c) }); }
        if (rest.Length > 0) sp.Children.Add(new TextBlock { Text = " " + rest, FontSize = fs, FontWeight = FontWeights.Medium, Foreground = Th.B(c), VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }
    public static Button IB(string t, Color c, Action a)
    {
        var b = new Button { Content = IconText(t, c), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Width = 26, Height = 26, Padding = new Thickness(0), Cursor = Cursors.Hand };
        b.Click += (s, e) => a(); return b;
    }
    public static UIElement IconText(string t, Color c)
    {
        string hx = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        string fa = null;
        if (t == "✎" || t == "✏") fa = "fa:Solid_Pen";
        else if (t == "✕") fa = "fa:Solid_Times";
        else if (t == "🗑") fa = "fa:Solid_Trash";
        if (fa != null) { try { return IconR.Render(fa, ActionType.Keys, 12, hx); } catch { } }
        return new TextBlock { Text = t, FontSize = Tk.FSmall, Foreground = Th.B(c) };
    }
    public static Border Sep => new Border { Height = 1, Background = Tk.B(Tk.BorderSubtle), Margin = new Thickness(Tk.S6, Tk.S4, Tk.S6, Tk.S4) };
    // 安静的"行内动作"：无边框、强调色文字。卡片里的次要动作不该长得像主按钮（系统灰按钮就是那个问题）
    public static Button Link(string t, Action a)
    {
        var b = new Button { Content = t, FontSize = Tk.FSmall, Padding = new Thickness(Tk.S8, Tk.S2, Tk.S8, Tk.S2), Margin = new Thickness(0), Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Foreground = Th.B(Th.AB) };
        b.Click += (s, e) => { try { a(); } catch { } };
        return b;
    }
    // 分节卡片。相对上一版改了三处，都是按 design-system 技能的规则：
    // ① 不再"白底 + 1px 边框"——白面板上的白卡片只能靠边框区分，而"每张卡都有同样的圆角、同样的边框、
    //    同样的柔和阴影"正是它点名的最典型生成痕迹；改成次级面填充、无边框，靠灰阶分层。
    // ② 标题改中性色：颜色只用来表达状态/动作，分节是**结构**，结构该用灰阶。
    // ③ 圆角 RMd(8) 与外壳 12 同心（外壳 padding 6 → 12-6 才是理想值，卡片不在外壳内嵌套，取 8）。
    public static Border Card(string h, params UIElement[] cs)
    {
        var s = new StackPanel { Margin = new Thickness(Tk.S12) };
        if (!string.IsNullOrEmpty(h)) s.Children.Add(new TextBlock { Text = h, FontSize = Tk.FSmall, FontWeight = FontWeights.SemiBold, Foreground = Th.B(Th.TS), Margin = new Thickness(0, 0, 0, Tk.S8) });
        foreach (var c in cs) s.Children.Add(c);
        return new Border { CornerRadius = new CornerRadius(Tk.RMd), Background = Th.BBI, Margin = new Thickness(0, 0, 0, Tk.S8), Child = s };
    }
}

}
}

