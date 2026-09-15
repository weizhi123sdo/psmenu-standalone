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
static string ShowContextMenu(AC cfg, LayerKind lk, string shot = null)
{
    UiCfg = cfg;   // 菜单侧也赋值：PMuted() 等派生色要取当前主题的前景色
    LM menu = cfg.Menus.FirstOrDefault(m => m.Kind == lk) ?? cfg.Menus.FirstOrDefault(m => m.Kind == LayerKind.Fallback);
    if (menu == null) return null;
    bool zoomDirty = false;

    var win = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual };

    double z = cfg.Zoom; if (z < 0.5) z = 0.5; if (z > 2.5) z = 2.5;
    double mw = cfg.MW > 0 ? cfg.MW : 260;
    byte opa = (byte)(cfg.Op * 255);

    // 宽度**钉住**在设置里的「菜单宽度」，而不是只当最小值：
    // 面板是 SizeToContent 的，只给 MinWidth 时"标题长 → 面板被撑宽"，TextTrimming 永远不生效
    // （截断只在"可用宽度 < 想要宽度"时发生）。钉住宽度后长标题才会真的省略号，
    // 面板宽度也变成可预期的（用户设多少就是多少）。
    var rp = new Border { Width = mw, MinWidth = mw, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(opa, PHex(cfg.BC, Color.FromRgb(45, 45, 45)).R, PHex(cfg.BC, Color.FromRgb(45, 45, 45)).G, PHex(cfg.BC, Color.FromRgb(45, 45, 45)).B)), BorderBrush = PB(cfg.OC, Color.FromRgb(62, 62, 62)), BorderThickness = new Thickness(1), Padding = new Thickness(0) };
    rp.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.34, Color = Colors.Black };
    var os = new StackPanel();

    // Layer type header
    var hc = LTM.C(lk);
    os.Children.Add(new Border { Height = 6, Background = Brushes.Transparent });
    var tb = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 6) };
    tb.Children.Add(IconR.Render(menu.Icon, ActionType.Keys, cfg.FS + 2, cfg.IC));
    tb.Children.Add(new TextBlock { Text = " " + menu.Label, FontSize = cfg.FS + 2, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(hc), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
    // 搜索回显挂在**已有的标题行**里，而不是自己占一行：
    // 自己占一行的话，一打字面板就长出一行、下面整块内容往下跳——"瞬时文字改变布局"是明确的观感缺陷。
    var kbHead = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
    tb.Children.Add(kbHead);
    os.Children.Add(tb);

    bool dismissed = false;
    MI _pendingEdit = null;   // 右键菜单项后转交编辑器
    int _navDir = 1;   // 1=进入子层，-1=返回，0/首层=不滑动
    bool _kbDefault = false;   // 已做“默认高亮常用项”
    string _kb = ""; int _kbCur = -1; Border _kbSel = null; Brush _kbOrig = null;
    List<(Border B, MI It, Brush Orig, bool Uni)> _meta = new List<(Border, MI, Brush, bool)>();
    Action KbUnsel = () => { if (_kbSel != null) { try { if (_kbOrig != null) _kbSel.Background = _kbOrig; } catch { } } _kbSel = null; _kbOrig = null; _kbCur = -1; };
    // 共享辉光刷子（KbSel/HoverBind/置顶方格共用）：必须声明在所有使用者之前（CS0841）
    var glowFx = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = Math.Max(0, Math.Min(100, cfg.GlowK)) / 100.0, Color = PHex(cfg.HC, Color.FromRgb(59, 130, 246)) };
    // 行底光带登记表：KbSel 键盘选中时也要点亮光带/光晕，和鼠标悬停同一套观感
    var sheenMap = new Dictionary<Border, FrameworkElement>();
    Action<int> KbSel = ix => { if (_meta.Count == 0) return; if (ix < 0) ix = 0; if (ix >= _meta.Count) ix = _meta.Count - 1; var rec = _meta[ix]; if (_kbSel != null && !ReferenceEquals(_kbSel, rec.B)) { try { if (_kbOrig != null) _kbSel.Background = _kbOrig; } catch { } try { _kbSel.Effect = null; } catch { } FrameworkElement os2; if (sheenMap.TryGetValue(_kbSel, out os2)) try { os2.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, TimeSpan.FromMilliseconds(240))); } catch { } } _kbSel = rec.B; _kbOrig = rec.B.Background; try { rec.B.Background = PBA(cfg.HC, 46, Color.FromArgb(46, 10, 102, 194)); } catch { } try { rec.B.Effect = glowFx; } catch { } FrameworkElement ns2; if (sheenMap.TryGetValue(rec.B, out ns2)) try { ns2.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(Math.Max(0, Math.Min(100, cfg.SheenK)) / 100.0, TimeSpan.FromMilliseconds(140))); } catch { } _kbCur = ix; try { rec.B.BringIntoView(); } catch { } };
    // 过滤串必须能清掉：原来 Esc 只取消高亮、没清 _kb，所以打错一个字后
    // 本次呼出这个菜单基本就废了（后续按键是在旧串上追加，几乎必然搜不到）。
    Action ClearKb = () => { _kb = ""; KbUnsel(); try { kbHead.Text = ""; } catch { } };
    // —— 动态：平滑关闭 / 悬停过渡 ——
    void FadeClose()
    {
        if (dismissed) return; dismissed = true;
        try
        {
            if (win.IsVisible)
            {
                var fa = new DoubleAnimation(win.Opacity, 0d, TimeSpan.FromMilliseconds(120));
                fa.Completed += (_, __) => { try { win.Close(); } catch { } };
                win.BeginAnimation(Window.OpacityProperty, fa);
            }
            else win.Close();
        }
        catch { try { win.Close(); } catch { } }
    }
    void CloseWin() { FadeClose(); }
    // 元素级辉光：共享一个 DropShadowEffect（ShadowDepth=0 → 是「辉光」不是投影），
    // 同一时刻只挂在悬停的那个元素上，离开即摘——比每行各挂一份省太多。
    // 挂/摘效果必须内联：csscript 新增方法（含局部函数）会触发整文件装载期 NRE（老坑，script check 仍报 valid）。
    // 光效是锦上添花：任何失败一律吞掉，不能因为它把菜单弄挂。
    void HoverBind(UIElement el, Brush hiB, FrameworkElement sheen = null, FrameworkElement halo = null, FrameworkElement bar = null)
    {
        var br = new SolidColorBrush(Colors.Transparent);
        if (el is Border bo) bo.Background = br;
        else if (el is Panel pa) pa.Background = br;
        var hc = (hiB as SolidColorBrush)?.Color ?? Colors.Transparent;
        var ht = TimeSpan.FromMilliseconds(130);
        var glowTgt = (UIElement)halo ?? el;   // 光晕取形用底板（halo），没有底板就挂自己
        el.MouseEnter += (_, __) =>
        {
            br.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(br.Color, hc, ht));
            try { halo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1d, TimeSpan.FromMilliseconds(120))); } catch { }
            try { glowTgt.Effect = glowFx; } catch { }
            if (bar != null) try { bar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(Math.Max(0, Math.Min(100, cfg.UnderK)) / 100.0, TimeSpan.FromMilliseconds(140))); } catch { }
            if (sheen != null) try { sheen.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(Math.Max(0, Math.Min(100, cfg.SheenK)) / 100.0, TimeSpan.FromMilliseconds(140))); } catch { }
        };
        el.MouseLeave += (_, __) =>
        {
            br.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(br.Color, Colors.Transparent, ht));
            try { halo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, TimeSpan.FromMilliseconds(200))); } catch { }
            try { if (ReferenceEquals(glowTgt.Effect, glowFx)) glowTgt.Effect = null; } catch { }
            if (bar != null) try { bar.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, TimeSpan.FromMilliseconds(220))); } catch { }
            if (sheen != null) try { sheen.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, TimeSpan.FromMilliseconds(240))); } catch { }
        };
    }
    double fs = cfg.FS > 0 ? cfg.FS : 13;
    var sl = new StackPanel { Name = "menuList", Margin = new Thickness(6, 0, 6, 6) };
    var nav = new Stack<List<MI>>();
    var uniNav = new Stack<bool>();      // 记录进入子菜单前是否位于“通用项”层级
    bool inUniSub = false;               // 当前浏览是否为通用项子菜单（避免底部通用块自重复）
    var cur = menu.Items;
    // 行 Tag（JSON）按 标题/动作/值 缓存：内容只跟菜单项有关，不必每次 rebuild 重新序列化
    var tagCache = new Dictionary<string, string>();
    string RowTag(MI it)
    {
        string k = it.Title + "|" + AT.ToStr(it.Action) + "|" + it.Value;
        string v;
        if (tagCache.TryGetValue(k, out v)) return v;
        v = Newtonsoft.Json.JsonConvert.SerializeObject(new { action = AT.ToStr(it.Action), value = it.Value, title = it.Title });
        tagCache[k] = v;
        return v;
    }
    // 悬停自动进子层：全菜单共用一个计时器（原先每行一个，20 项菜单就是 20 个 DispatcherTimer）
    var hovTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(cfg != null ? Math.Max(100, Math.Min(1500, cfg.HoverMs)) : 420) };
    UIElement hovRow = null; MI hovItem = null; bool hovUni = false;
    hovTimer.Tick += (s2, e2) =>
    {
        try
        {
            hovTimer.Stop();
            if (cfg == null || !cfg.HoverSub) return;
            if (hovItem != null && hovItem.HasSub && hovRow != null && hovRow.IsMouseOver)
            {
                _navDir = 1; nav.Push(cur); uniNav.Push(inUniSub); if (hovUni) inUniSub = true;
                ClearKb(); cur = hovItem.Sub; rebuild();
            }
        }
        catch { }
    };

    // 统一分隔线样式：内缩对齐图标列、上下留白均衡、降透明度弱化
    Border MakeSep() => new Border { Height = 1, Background = PMuted(34, Color.FromArgb(34, 120, 120, 130)), Margin = new Thickness(12, 4, 12, 4) };
    // 频率排序：开启时按使用次数降序（稳定排序），分隔线保持原位、仅分段内重排
    List<MI> FreqOrdered(List<MI> src)
    {
        if (src == null) return src;
        // 打字搜索 = 真过滤：不匹配的项直接不显示
        // （分组标题在过滤时一并隐藏——夹在少量结果里只会更乱）
        if (_kb.Length > 0)
        {
            // 搜索态：按匹配精准度排序（前缀 > 包含 > 备注 > 拼音 > 模糊），同档保持原顺序；
            // 频率排序在搜索时不掺和——精准度优先才好找。
            var scored = new List<KeyValuePair<int, MI>>();
            int idx = 0;
            foreach (var it in src)
            {
                if (it.IsSep || it.Header) continue;
                int s = MatchScore(it, _kb);
                if (s >= 0) scored.Add(new KeyValuePair<int, MI>(s * 100000 + idx, it));
                idx++;
            }
            scored.Sort((a, b) => a.Key.CompareTo(b.Key));
            return scored.Select(x => x.Value).ToList();
        }
        if (!cfg.FreqSort) return src;
        var res = new List<MI>(); var run = new List<MI>();
        void Flush() { foreach (var it in run.OrderByDescending(x => ConfigService.FreqCount(x, cfg))) res.Add(it); run.Clear(); }
        // 分组标题和分隔线一样是"分段标记"，不参与段内频率重排
        foreach (var it in src) { if (it.IsSep || it.Header) { Flush(); res.Add(it); } else run.Add(it); }
        Flush();
        return res;
    }
    // Pinned items
    float pw2 = (float)(cfg.PS > 0 ? cfg.PS : 20);
    var pinned = cfg.Pinned.Where(p => p.LK == LayerKind.None || p.LK == lk).ToList();
    // 调试用：shot 里带 pins=N 时，在内存里把置顶项复制成 N 个（不落盘），用来压测容量/溢出表现
    if (!string.IsNullOrEmpty(shot))
    {
        // 长名字压测：pinlong=1 把置顶项名字换成长名字（只在内存里改），看文字是省略还是换行
        if (ShotField(shot, "pinlong") == "1" && pinned.Count > 0)
        {
            var longNames = new[] { "图层对齐与分布（水平垂直居中）", "参考线：新建 / 删除 / 清除全部", "复制并粘贴图层样式（含效果）" };
            for (int iL = 0; iL < pinned.Count; iL++)
            {
                var tL = longNames[iL % longNames.Length];
                // 注意：置顶渲染会按稳定键 K 回读当前菜单项（r65 的"跟随配置"），所以**必须连源菜单项一起改**，
                // 否则只改副本会被真实名字覆盖掉，压测等于没做（踩过）。
                foreach (var lm9 in cfg.Menus)
                    foreach (var it9 in (lm9.Items ?? new List<MI>()))
                        if (!string.IsNullOrEmpty(pinned[iL].K) && it9.K == pinned[iL].K)
                        {
                            it9.Title = tL;
                            it9.Note = "这条备注会出现在悬停文字里，验证长文在方块内换行是否越界";
                        }
                pinned[iL] = new PI { Title = tL, Icon = pinned[iL].Icon, Action = pinned[iL].Action, Value = pinned[iL].Value, LK = pinned[iL].LK, K = pinned[iL].K, LibKey = pinned[iL].LibKey, LibHash = pinned[iL].LibHash };
            }
        }
        int pn0;
        var pinsS0 = ShotField(shot, "pins");
        if (!string.IsNullOrEmpty(pinsS0) && int.TryParse(pinsS0, out pn0) && pinned.Count > 0 && pn0 > pinned.Count)
        {
            var seed0 = pinned.ToList();
            for (int i0 = pinned.Count; i0 < pn0; i0++) pinned.Add(seed0[i0 % seed0.Count]);
        }
    }
    if (pinned.Count > 0)
    {
        // 图标列对齐：下面菜单行的图标圆心在 6(sl边距)+2(行边距)+10(图标左边距)+(fs+6)/2，
        // 置顶方格的圆心在 trayLeft(托盘)+innerLeft(内边距)+1(格边距)+2(内边距)+(pw2-6)/2
        // —— 反解 innerLeft 让两者重合，改字号/格径都不再错位。
        const double trayLeft = 6;   // 与 sl 的 6px 缩进对齐
        double innerLeft = 21 + fs / 2 - pw2 / 2 - trayLeft; if (innerLeft < 2) innerLeft = 2;
        // 名称常显：格子下面一行小字。字号跟着菜单字号走但更小，宽度按实测文字宽度（超过两格宽就省略号）
        double capFont = Math.Max(9, fs - 5);
        Func<string, double> capW = t =>
        {
            try
            {
                var ftp = new FormattedText(t ?? "", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei UI"), capFont, Brushes.Black);
                return Math.Min(ftp.Width + 1, pw2 * 2.0);
            }
            catch { return pw2; }
        };
        double slotW = pw2 + 2;
        if (cfg.ShowPinName)
        {
            double wmax = 0;
            foreach (var pp in cfg.Pinned) wmax = Math.Max(wmax, capW(pp.Title));
            slotW = Math.Max(slotW, wmax + 2);
        }
        var pr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(innerLeft, 3, 6, 3) };
        // 出图用：hoverpin=N → 第 N 格静态呈现"悬停态"（0 起）；真机悬停没法离线截图
        int shotHoverIdx = -1;
        {
            var hv = ShotField(shot, "hoverpin");
            if (!string.IsNullOrEmpty(hv)) { int hv2; if (int.TryParse(hv, out hv2)) shotHoverIdx = hv2; }
        }
        int pinIdx = 0;
        Action<PI> AddPinBtn = p =>
        {
            // 置顶副本可能过期（改了标题/图标/值）：解析回当前项，显示与执行都用最新的
            var pinSrc = PinSource(cfg, p);
            bool pinHasSub = pinSrc != null && pinSrc.HasSub;
            string vTitle = pinSrc != null ? pinSrc.Title : p.Title;
            string vIcon = pinSrc != null ? pinSrc.Icon : p.Icon;
            var vAct = pinSrc != null ? pinSrc.Action : p.Action;
            string vVal = pinSrc != null ? pinSrc.Value : p.Value;
            int pFreq = pinSrc != null ? ConfigService.FreqCount(pinSrc, cfg)
                       : (cfg.Freq != null && cfg.Freq.TryGetValue(p.Title + "||" + AT.ToStr(vAct) + "||" + vVal, out var pf2) ? pf2 : 0);
            string tip = vTitle + (pinHasSub ? "（含子菜单，点击展开）" : "") + (pFreq > 0 ? " · 用过 " + pFreq + " 次" : "");
            // 系统提示会把长标题折成两行（用户反馈"字多就变两排"）。改成自定义 ToolTip：
            // 内容是一个 NoWrap 的 TextBlock + MaxWidth，超长就省略号，永远是单行。
            var tipHost = new StackPanel { Orientation = Orientation.Vertical };
            tipHost.Children.Add(new TextBlock { Text = tip, FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 });
            if (pFreq > 0) tipHost.Children.Add(new TextBlock { Text = "用过 " + pFreq + " 次", FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) });
            var tipObj = new ToolTip { Content = tipHost, Padding = new Thickness(8, 5, 8, 5) };
            // 方格自己这套底色/悬停渐变：原来用系统 Button 模板，悬停是另一套语感，跟自绘的行不搭
            // 每个方格一个独立的底色刷子：PBA() 是带缓存的（同色同实例），
            // 悬停要在刷子上做颜色动画——共享实例会让所有方格一起变色（实测踩到）
            var hc0 = PHex(cfg.HC, Color.FromRgb(51, 153, 255));
            var bs = new SolidColorBrush(Color.FromArgb(20, hc0.R, hc0.G, hc0.B));
            Color cBase = bs.Color, cHov = PBA(cfg.HC, 55, Color.FromArgb(55, 51, 153, 255)).Color;
            var ib = new Button { Width = pw2, Height = pw2, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Background = bs, BorderBrush = Brushes.Transparent, Focusable = false, FocusVisualStyle = null, Padding = new Thickness(2), Margin = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tipObj, Tag = Newtonsoft.Json.JsonConvert.SerializeObject(new { action = AT.ToStr(vAct), value = vVal, title = vTitle }) };
            // 方格只画图标（+ 子菜单角标）：次数不再画在格子上——36px 的格子里塞数字既挤又难认，
            // 常用程度改用悬停提示表达，并且受「使用次数」总开关控制。
            var pg = new Grid();
            pg.Children.Add(IconR.Render(vIcon, vAct, pw2 - 6, cfg.IC));
            // 方格“边缘发光”的影源：半透明圆角方块（悬停随辉光强度点亮）——
            // 直接挂在透明底 Button 上只会照出图标字形（观感=文字外发光，不是要的方块边缘光）
            var edge2 = new Border { Width = pw2 - 2, Height = pw2 - 2, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(70, hc0.R, hc0.G, hc0.B)), Opacity = 0, IsHitTestVisible = false };
            pg.Children.Add(edge2);
            // 子菜单角标（定稿·强调色折角）：右下角一整块**强调色实心三角**（铺满，不留渐变/折线），
            // 角内一个**前景色 V 形箭头**（与强调色互为反差）。
            // 这样每套主题都用它自己的强调色——10 套预设都是各自最跳的那个颜色，不存在"某个主题看不清"。
            // 演进：8px 灰 ▸ → 渐变蒙版(太大) → 实色折角(偏大) → 纯色小角(亮色下一个白角，看不见)
            //       → 折角+折线+箭头(角用面板色/前景色，仍不够跳) → 本版：整块强调色。
            if (pinHasSub)
            {
                // 角标定稿对齐实时预览：约格子 30% 的纯强调色实心三角（无角内箭头）——预览里这套最干净
                double wg = Math.Max(6, pw2 * 0.3);
                var hcv = PHex(cfg.HC, Color.FromRgb(40, 120, 220));
                var wedge = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "M 0,{0} L {0},0 L {0},{0} Z", wg)),
                    Fill = new SolidColorBrush(Color.FromArgb(255, hcv.R, hcv.G, hcv.B)),
                    Width = wg, Height = wg, Stretch = Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom
                };
                pg.Children.Add(wedge);
            }
            ib.Content = pg;
            ib.MouseEnter += (_, __) =>
            {
                try { bs.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(bs.Color, cHov, TimeSpan.FromMilliseconds(130))); } catch { }
                try { ib.Effect = glowFx; } catch { }
                try { edge2.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1d, TimeSpan.FromMilliseconds(120))); } catch { }
                try { var pd3 = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(70)); var sc3 = ib.RenderTransform as ScaleTransform ?? new ScaleTransform(); ib.RenderTransform = sc3; ib.RenderTransformOrigin = new Point(0.5, 0.5); sc3.BeginAnimation(ScaleTransform.ScaleXProperty, pd3); sc3.BeginAnimation(ScaleTransform.ScaleYProperty, pd3); } catch { }
                if (pinHasSub && cfg.HoverSub) { try { hovTimer.Stop(); } catch { } hovRow = ib; hovItem = pinSrc; hovUni = true; hovTimer.Start(); }
            };
            ib.MouseLeave += (_, __) =>
            {
                try { bs.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(bs.Color, cBase, TimeSpan.FromMilliseconds(130))); } catch { }
                try { ib.Effect = null; } catch { }
                try { edge2.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, TimeSpan.FromMilliseconds(160))); } catch { }
                try { var pu3 = new DoubleAnimation(1d, TimeSpan.FromMilliseconds(90)); var sc4 = ib.RenderTransform as ScaleTransform ?? new ScaleTransform(); ib.RenderTransform = sc4; sc4.BeginAnimation(ScaleTransform.ScaleXProperty, pu3); sc4.BeginAnimation(ScaleTransform.ScaleYProperty, pu3); } catch { }
                try { hovTimer.Stop(); } catch { }
                if (ReferenceEquals(hovRow, ib)) { hovRow = null; hovItem = null; }
            };
            ib.Click += (s, e) =>
            {
                if (pinHasSub) { _navDir = 1; nav.Push(cur); uniNav.Push(inUniSub); inUniSub = true; ClearKb(); cur = pinSrc.Sub; rebuild(); return; }
                win.Tag = ib.Tag?.ToString(); FadeClose();
            };
            if (!cfg.ShowPinName)
            {
                // 悬停模式：**方格在悬停时长出文字**——图标淡出缩小、文字淡入放大（180~200ms CubicEase 交叉）。
                // 文字**严格限制在方块内**：标题 + 备注（备注就是"悬停提示"里那句给自己看的说明，见编辑菜单项窗口），
                // 字多就在方块内换行排布，再排不下则最后一行省略号——不越出方块、不改变布局。
                ib.ToolTip = null;                                  // 悬停模式下系统提示关掉（内容已并进文字）
                ib.HorizontalAlignment = HorizontalAlignment.Left;
                ib.Margin = new Thickness(0);
                pg.RenderTransformOrigin = new Point(0.5, 0.5);     // 图标：缩放中心=格子中心
                var isc = new ScaleTransform(1, 1); pg.RenderTransform = isc;
                // 悬停文字＝一段文本，两种排版规则（用户定的）：
                //   · 有备注 → 只显示备注，**多排**（最多两排，末行省略）——备注是完整的一句话，按宽度自然折行
                //   · 无备注 → 显示标题：**4 字及以上就按两排均分摆**（如"图层对齐"→"图层/对齐"），
                //              3 字以内单行；两排时不再缩字（方块高度足够），字大清楚
                string noteTxt = pinSrc != null ? (pinSrc.Note ?? "").Trim() : "";
                bool useNote = noteTxt.Length > 0 && noteTxt != vTitle.Trim();
                string htxt = useNote ? noteTxt : vTitle.Trim();
                // 排版规则（用户定的）：有备注只显示备注（多排）；无备注显示标题（≥4 字排两排）。
                // 这一版的新要求：**字号尽量大**，同时文字离方块边留 10~20px。
                // 做法是先按方块尺寸定边距，再在"剩余可用区域"里搜出能放下这段文字的最大字号。
                double padH = Math.Max(8, Math.Min(20, pw2 * 0.22));   // 左右边距 10~20px（随方块大小）
                double padV = Math.Max(6, Math.Min(14, pw2 * 0.15));   // 上下边距
                double useW = Math.Max(12, pw2 - 2 * padH), useH = Math.Max(12, pw2 - 2 * padV);
                double baseFz = useNote ? Math.Max(11, fs - 4) : Math.Max(11, fs - 3);   // 字号整体抬一档
                int n = Math.Max(1, htxt.Length);
                double fz = baseFz, perRow = n;
                // 从大往小搜：先找"≤2 排能放下"的最大字号；找不到再放宽到 3 排
                for (int maxRows = 2; maxRows <= 3; maxRows++)
                {
                    bool done = false;
                    for (double cand = baseFz; cand >= 8; cand -= 0.25)
                    {
                        double cpr = Math.Max(1, Math.Floor(useW / cand));       // 每排能放几个字
                        double rows = Math.Ceiling(n / cpr);
                        if (rows <= maxRows && rows * cand * 1.3 <= useH)
                        { fz = cand; perRow = cpr; done = true; break; }
                    }
                    if (done) break;
                }
                bool twoRow = n > perRow || useNote;
                double tbW = twoRow ? Math.Min(useW, perRow * fz + 0.6) : double.NaN;
                var ltb = new TextBlock
                {
                    Text = htxt,
                    FontSize = fz,
                    Foreground = useNote ? PMuted(190, Color.FromArgb(190, 205, 205, 215)) : PB(cfg.FC, Color.FromRgb(240, 240, 245)),
                    TextWrapping = twoRow ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    LineHeight = fz * 1.3,
                    MaxHeight = fz * 1.3 * Math.Max(2, Math.Ceiling(n / Math.Max(1, perRow))) + 1,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (!double.IsNaN(tbW)) ltb.Width = tbW;
                var lstack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
                lstack.Children.Add(ltb);
                var lbl = new Border
                {
                    Width = pw2 - 2, Height = pw2, ClipToBounds = true, Child = lstack,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0, IsHitTestVisible = false
                };
                lbl.RenderTransformOrigin = new Point(0.5, 0.5);
                var lsc = new ScaleTransform(0.94, 0.94); lbl.RenderTransform = lsc;
                // 槽宽就是格子宽：文字不再向外撑（长文在方块内换行），所以悬停模式也能排满一格数
                var cellH = new Grid { Width = pw2 + 2, Height = pw2, Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 0) };
                cellH.Children.Add(ib);
                cellH.Children.Add(lbl);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                bool isFirstPin = pinIdx == shotHoverIdx;
                pinIdx++;
                Action<bool> morph = on =>
                {
                    try
                    {
                        bool force = isFirstPin && on && shotHoverIdx >= 0;
                        if (force)
                        {
                            // 出图用：直接赋值。0 时长动画在同一帧内不会推进，属性会停在基值，
                            // 出图就会"看着像没悬停"（踩过）。这里绕开动画直接落到终态。
                            pg.Opacity = 0; isc.ScaleX = 0.86; isc.ScaleY = 0.86;
                            lbl.Opacity = 1; lsc.ScaleX = 1; lsc.ScaleY = 1;
                            return;
                        }
                        var d1 = new DoubleAnimation(on ? 0d : 1d, TimeSpan.FromMilliseconds(180)); d1.EasingFunction = ease;
                        var d2 = new DoubleAnimation(on ? 0.86 : 1d, TimeSpan.FromMilliseconds(180)); d2.EasingFunction = ease;
                        var d3 = new DoubleAnimation(on ? 1d : 0d, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(on ? 45 : 0) }; d3.EasingFunction = ease;
                        var d4 = new DoubleAnimation(on ? 1d : 0.94, TimeSpan.FromMilliseconds(200)) { BeginTime = d3.BeginTime }; d4.EasingFunction = ease;
                        pg.BeginAnimation(UIElement.OpacityProperty, d1);
                        isc.BeginAnimation(ScaleTransform.ScaleXProperty, d2); isc.BeginAnimation(ScaleTransform.ScaleYProperty, d2);
                        lbl.BeginAnimation(UIElement.OpacityProperty, d3);
                        lsc.BeginAnimation(ScaleTransform.ScaleXProperty, d4); lsc.BeginAnimation(ScaleTransform.ScaleYProperty, d4);
                    }
                    catch { }
                };
                cellH.MouseEnter += (_, __) => morph(true);
                cellH.MouseLeave += (_, __) => morph(false);
                if (shotHoverIdx >= 0 && isFirstPin) morph(true);   // 出图用：静态呈现指定格的悬停态
                pr.Children.Add(cellH);
                return;
            }
            // 格子左对齐放在单元格里：这样"图标列"的横坐标不变（和下面菜单行的图标列仍然对齐），
            // 名称在单元格内居中——单元格宽度取"格子宽"与"名字宽"的较大者。
            ib.HorizontalAlignment = HorizontalAlignment.Left;
            ib.Margin = new Thickness(1, 0, 1, 0);
            var capTb = new TextBlock
            {
                Text = vTitle, FontSize = capFont, Foreground = PMuted(185, Color.FromArgb(185, 200, 200, 210)),
                HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0), ToolTip = tip
            };
            var cell3 = new StackPanel { Orientation = Orientation.Vertical, Width = slotW - 2 };
            cell3.Children.Add(ib);
            cell3.Children.Add(capTb);
            pr.Children.Add(cell3);
        };
        // 全量渲染：不再按容量裁掉（原来 cap≤8、窄面板只剩 4~5 格，多的一律看不见）。
        // 溢出交给横向滑动：悬停靠近左右边缘自动向内滑（越靠边越快，离开即停，贴帧率 16ms 步进），
        // 两侧再叠一层渐隐提示，让“那边还有”一眼可见；滚到头的那一侧不渐隐。
        for (int i = 0; i < pinned.Count; i++) AddPinBtn(pinned[i]);
        var pinScroll = new ScrollViewer
        {
            Content = pr,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var gsL0 = new System.Windows.Media.GradientStop { Color = Colors.Transparent, Offset = 0 };
        var gsL1 = new System.Windows.Media.GradientStop { Color = Colors.Black, Offset = 0 };
        var gsR1 = new System.Windows.Media.GradientStop { Color = Colors.Black, Offset = 1 };
        var gsR0 = new System.Windows.Media.GradientStop { Color = Colors.Transparent, Offset = 1 };
        var pinMask = new System.Windows.Media.LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        pinMask.GradientStops.Add(gsL0); pinMask.GradientStops.Add(gsL1); pinMask.GradientStops.Add(gsR1); pinMask.GradientStops.Add(gsR0);
        pr.OpacityMask = pinMask;
        void UpdatePinMask()
        {
            try
            {
                double w = Math.Max(1, pr.ActualWidth);
                double fz = Math.Min(20, w * 0.12) / w;
                bool l = pinScroll.HorizontalOffset > 1;
                bool r = pinScroll.HorizontalOffset < pinScroll.ScrollableWidth - 1;
                gsL0.Offset = 0; gsL1.Offset = l ? fz : 0;
                gsR1.Offset = 1; gsR0.Offset = r ? 1 - fz : 1;
            }
            catch { }
        }
        pinScroll.ScrollChanged += (_, __) => UpdatePinMask();
        pinScroll.LayoutUpdated += (_, __) => UpdatePinMask();
        var pinSlide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double pinSlideV = 0, pinSlideTarget = 0;
        pinSlide.Tick += (s2, e2) =>
        {
            try
            {
                pinSlideV += (pinSlideTarget - pinSlideV) * 0.2;   // 指数趋近：起步/变速/收住都是渐入渐出
                if (Math.Abs(pinSlideV) < 0.03 && Math.Abs(pinSlideTarget) < 0.03) { pinSlideV = 0; pinSlide.Stop(); return; }
                if (pinScroll.ScrollableWidth <= 0.5) { pinSlideV = 0; pinSlideTarget = 0; pinSlide.Stop(); return; }
                pinScroll.ScrollToHorizontalOffset(pinScroll.HorizontalOffset + pinSlideV);
            }
            catch { pinSlide.Stop(); }
        };
        pinScroll.MouseMove += (_, e2) =>
        {
            try
            {
                if (pinScroll.ScrollableWidth <= 0.5) { pinSlideTarget = 0; return; }
                var p2 = e2.GetPosition(pinScroll);
                double zone = Math.Max(30, pinScroll.ViewportWidth * 0.33);
                double v = 0;
                if (p2.X < zone) v = -(Math.Pow(1 - p2.X / zone, 1.5) * 11 + 0.8);
                else if (p2.X > pinScroll.ViewportWidth - zone) { double d2 = pinScroll.ViewportWidth - p2.X; v = Math.Pow(1 - d2 / zone, 1.5) * 11 + 0.8; }
                if (pinScroll.HorizontalOffset <= 0.5 && v < 0) v = 0;
                if (pinScroll.HorizontalOffset >= pinScroll.ScrollableWidth - 0.5 && v > 0) v = 0;
                pinSlideTarget = v;
                if (v != 0 && !pinSlide.IsEnabled) pinSlide.Start();
            }
            catch { }
        };
        pinScroll.MouseLeave += (_, __) => { pinSlideTarget = 0; };
        // 置顶区做成一个浅托盘：原来它与下面的列表只靠一条和列表内同款的细线分界，
        // 两块读起来是一整块。有了托盘边界，后面那条分隔线就多余了（会变成两条线）。
        var tray = new Border { CornerRadius = new CornerRadius(6), Background = PMuted(26, Color.FromArgb(26, 120, 120, 130)), Margin = new Thickness(trayLeft, 0, 6, 5), Child = pinScroll };
        os.Children.Add(tray);
    }

    // 有置顶托盘时不再画这条分隔线（托盘边界已经分开了两块，再画就是两条线）
    if (pinned.Count == 0) os.Children.Add(MakeSep());

    // 行渲染：主菜单项与底部通用块共用同一套构造（两处行为差异只有两个，见下面 universal 的判断）
    Border MakeRow(MI item, bool universal)
    {
        // 分组标题：小字弱色、不可点、不进 _meta（不参与 Enter/↑↓），打字过滤时整行隐藏
        if (item.Header)
        {
            var hrow = new Border { Margin = new Thickness(2, 7, 2, 2), Background = Brushes.Transparent, Tag = RowTag(item) };
            var hin = new StackPanel { Orientation = Orientation.Horizontal };
            hin.Children.Add(new TextBlock { Text = item.TitleMain, FontSize = Math.Max(9, fs - 2), FontWeight = FontWeights.SemiBold, Foreground = PMuted(150, Color.FromRgb(150, 150, 150)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            hrow.Child = hin;
            hrow.ToolTip = string.IsNullOrEmpty(item.Note) ? "分组标题（分组说明可写在备注里）" : item.Note;
            hrow.MouseRightButtonUp += (_, re) => { re.Handled = true; _pendingEdit = item; try { dismissed = true; win.Close(); } catch { } };
            return hrow;
        }

        var row = new Border { CornerRadius = new CornerRadius(Math.Max(0, Math.Min(12, cfg.CR))), BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(Math.Max(0, Math.Min(3, cfg.BW))), Margin = new Thickness(2, 0, 2, cfg.IS), Cursor = Cursors.Hand, Tag = RowTag(item), Background = Brushes.Transparent };
        if (item.Disabled) row.Opacity = 0.4;
        if (item.Disabled) row.ToolTip = string.IsNullOrEmpty(item.Note) ? "（已停用）" : (item.Note + "\n（已停用）");
        else if (!string.IsNullOrEmpty(item.Note)) row.ToolTip = item.Note;

        // 三列：图标 / 标题（占满，好把快捷键列推到最右）/ 快捷键提示
        var rowInner = new Grid { Height = Math.Max(fs * 2.2, 30) };
        rowInner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowInner.ColumnDefinitions.Add(new ColumnDefinition());
        rowInner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 悬停光晕底板 + 底部光感带：光晕=强调色圆角底板（Effect 从它取形，外圈才是完整一圈辉光，
        // 挂在透明的行上只能从文字取形，几乎看不见——上一版就是这个原因）；光带=行底 2px 强调色渐隐条。
        // 两者平时 Opacity 0，悬停时由 HoverBind 点亮；Effect 只在悬停期间挂，不悬停零开销。
        var halo = new Border { CornerRadius = new CornerRadius(5), Background = PBA(cfg.HC, 70, Color.FromArgb(70, 40, 120, 220)), Opacity = 0, IsHitTestVisible = false };
        var hcv2 = PHex(cfg.HC, Color.FromRgb(59, 130, 246));
        var sheen = new Border
        {
            Height = 2, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(2),
            Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(9, 0, 9, 0),
            Background = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new System.Windows.Media.GradientStop(Color.FromArgb(0, hcv2.R, hcv2.G, hcv2.B), 0),
                    new System.Windows.Media.GradientStop(Color.FromArgb(255, hcv2.R, hcv2.G, hcv2.B), 0.5),
                    new System.Windows.Media.GradientStop(Color.FromArgb(0, hcv2.R, hcv2.G, hcv2.B), 1)
                }
            }
        };
        int cc = ConfigService.FreqCount(item, cfg);
        if (!item.Disabled)
        {
            rowInner.Children.Add(halo); Grid.SetColumnSpan(halo, 3);
            rowInner.Children.Add(sheen); Grid.SetColumnSpan(sheen, 3);
            sheenMap[row] = sheen;
            // 底部模糊高光：行底一条 accent 实色条 + Blur 成光晕（强度/厚窄在光效卡可调，0=关）
            var glowBar = new Border { Height = Math.Max(2, Math.Min(14, cfg.UnderH)), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3), Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(8, 0, 8, 0), Background = new SolidColorBrush(Color.FromArgb(210, hcv2.R, hcv2.G, hcv2.B)) };
            try { glowBar.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 7 }; } catch { }
            rowInner.Children.Add(glowBar); Grid.SetColumnSpan(glowBar, 3);
            HoverBind(row, PBA(cfg.HC, 60, Color.FromArgb(60, 51, 153, 255)), sheen, halo, glowBar);
            // 按压反馈：按下轻微下压（缩 0.985），松开/移出回弹——「跟手」的触觉暗示
            var rowSc = new ScaleTransform(1, 1); row.RenderTransform = rowSc; row.RenderTransformOrigin = new Point(0.5, 0.5);
            row.PreviewMouseLeftButtonDown += (_, __) => { try { var pd = new DoubleAnimation(0.985, TimeSpan.FromMilliseconds(70)); rowSc.BeginAnimation(ScaleTransform.ScaleXProperty, pd); rowSc.BeginAnimation(ScaleTransform.ScaleYProperty, pd); } catch { } };
            row.PreviewMouseLeftButtonUp += (_, __) => { try { var pu = new DoubleAnimation(1d, TimeSpan.FromMilliseconds(90)); rowSc.BeginAnimation(ScaleTransform.ScaleXProperty, pu); rowSc.BeginAnimation(ScaleTransform.ScaleYProperty, pu); } catch { } };
            row.MouseLeave += (_, __) => { try { var pr2 = new DoubleAnimation(1d, TimeSpan.FromMilliseconds(90)); rowSc.BeginAnimation(ScaleTransform.ScaleXProperty, pr2); rowSc.BeginAnimation(ScaleTransform.ScaleYProperty, pr2); } catch { } };
            // 原来这里还叠了「阴影 + 上移 1px」。悬停四层效果（底色/阴影/位移/箭头）叠在一起属于到处的小交互，
            // 只留底色（这行是活的）与箭头渐显（点了会开子层）两件回应性的；位移还会让行内文字亚像素抖动，一并去掉。
        }

        var ic = IconR.Render(item.Icon, item.Action, fs, cfg.IC);
        var icb = new Border { Width = fs + 6, Height = fs + 4, Margin = new Thickness(10, 0, 8, 0) }; icb.Child = ic;
        rowInner.Children.Add(icb);
        // 标题行：三列 = 标题（可截断）/ 箭头 / 次数。
        // 原来是横向 StackPanel——StackPanel 给子元素无限宽，标题永远不会截断，
        // 于是窄面板里"标题先把次数和按键列挤走"。改成 Grid：标题占星列且末尾省略号，
        // 箭头与次数各占 Auto 列，先保证它们的位置。
        var titleLine = new Grid { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        titleLine.ColumnDefinitions.Add(new ColumnDefinition());
        titleLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleLine.Children.Add(new TextBlock { Text = item.TitleMain, FontSize = fs, Foreground = PB(cfg.FC, Color.FromRgb(204, 204, 204)), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        // 子菜单角标：原来是一根 0.35 透明度的细 ▶，不悬停几乎看不见（用户反馈）。
        // 改成常显的小角标：强调色描边 + 强调色淡底 + 实心三角，一眼能读出"这条点开还有一层"。
        FrameworkElement subMark = null;
        if (item.HasSub)
        {
            subMark = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = PBA(cfg.HC, 44, Color.FromArgb(44, 40, 120, 220)),
                BorderBrush = PBA(cfg.HC, 150, Color.FromArgb(150, 40, 120, 220)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3, 1, 3, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Opacity = 0.9,
                ToolTip = "含子菜单",
                // 用 V 形箭头而不是实心三角：三角与"运行/播放"类图标同形，容易误读成按钮
                Child = IconR.Render("fa:Solid_ChevronRight", ActionType.Keys, Math.Max(9, fs - 3), cfg.HC)
            };
            titleLine.Children.Add(subMark); Grid.SetColumn(subMark, 1);
            row.MouseEnter += (_, __) => { try { subMark.Opacity = 1; } catch { } };
            row.MouseLeave += (_, __) => { try { subMark.Opacity = 0.9; } catch { } };
        }
        // 次数角标：开了"按频率排序"之后这个数字就是纯噪音（顺序本身已经说明了），不再显示
        if (cc > 0 && !cfg.FreqSort)
        {
            var cntTb = new TextBlock { Text = cc.ToString(), FontSize = fs - 3, Foreground = PMuted(95, Color.FromArgb(95, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
            cntTb.ToolTip = "用过 " + cc + " 次";
            titleLine.Children.Add(cntTb); Grid.SetColumn(cntTb, 2);
        }
        rowInner.Children.Add(titleLine); Grid.SetColumn(titleLine, 1);
        string tkey = item.TitleKey;
        if (!string.IsNullOrEmpty(tkey))
        {
            // 按键成列 → 用等宽，几列才对得齐（等宽只用在"值"上，不当装饰）
            var kt = new TextBlock { Text = tkey, FontSize = Math.Max(9, fs - 3), FontFamily = new FontFamily("Consolas"), Foreground = PMuted(165, Color.FromRgb(150, 150, 150)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
            rowInner.Children.Add(kt); Grid.SetColumn(kt, 2);
        }

        row.MouseLeftButtonUp += (_, __) =>
        {
            if (item.Disabled) return;   // 停用项：左键不执行（右键仍可编辑/启用）
            if (item.HasSub) { _navDir = 1; nav.Push(cur); uniNav.Push(inUniSub); if (universal) inUniSub = true; ClearKb(); cur = item.Sub; rebuild(); }
            else { win.Tag = row.Tag?.ToString(); FadeClose(); }
        };
        row.MouseRightButtonUp += (_, re) =>
        {
            // 通用块不支持 Ctrl+右键置顶（保持原有行为）
            if (!universal && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && !item.HasSub)
            {
                re.Handled = true;
                if (cfg.Pinned.Any(pp => pp.UK == item.UK)) cfg.Pinned.RemoveAll(pp => pp.UK == item.UK);
                else cfg.Pinned.Add(new PI { Title = item.Title, Icon = item.Icon, Action = item.Action, Value = item.Value, LK = lk, K = item.K });
                rebuild();
                return;
            }
            if (item.HasSub) { if (nav.Count > 0) { cur = nav.Pop(); inUniSub = uniNav.Pop(); _navDir = -1; ClearKb(); rebuild(); } return; }
            re.Handled = true;
            _pendingEdit = item;
            try { dismissed = true; win.Close(); } catch { }
        };
        row.MouseEnter += (_, __) => { if (item.HasSub && !item.Disabled && cfg.HoverSub) { try { hovTimer.Stop(); } catch { } hovRow = row; hovItem = item; hovUni = universal; hovTimer.Start(); } };
        row.MouseLeave += (_, __) => { try { hovTimer.Stop(); } catch { } if (ReferenceEquals(hovRow, row)) { hovRow = null; hovItem = null; } };
        // 可选中项：含"带子菜单"的行（原来把它们排除在外，导致搜索结果里明明看得见却选中不了、
        // 顶部命中数也比实际少——搜「文字」只有「文字属性」命中时却报 0 项）
        if (!item.Disabled) _meta.Add((row, item, row.Background, universal));
        row.Child = rowInner;
        return row;
    }

    void rebuild()
    {
        sl.Children.Clear();
        _meta.Clear();
        bool prevVis = false;   // 上一渲染位置是否有可见行：用于跳过行首/行尾/连续分隔线
        foreach (var item in FreqOrdered(cur))
        {
            if (item.IsSep) { if (prevVis) { sl.Children.Add(MakeSep()); prevVis = false; } continue; }
            if (!item.Header && cfg.Pinned.Any(p => p.UK == item.UK)) continue;
            sl.Children.Add(MakeRow(item, false));
            prevVis = !item.Header;   // 标题行本身就是分段，后面不必再跟分隔线
        }
        // Universal items
        var uniMenu = cfg.Menus.FirstOrDefault(m => m.Kind == LayerKind.Fallback);
        if (uniMenu != null && menu.Kind != LayerKind.Fallback && !inUniSub && uniMenu.Items.Count > 0)
        {
            if (prevVis) { sl.Children.Add(MakeSep()); prevVis = false; }
            foreach (var item in FreqOrdered(uniMenu.Items))
            {
                if (item.IsSep) { if (prevVis) { sl.Children.Add(MakeSep()); prevVis = false; } continue; }
                if (!item.Header && cfg.Pinned.Any(p => p.UK == item.UK)) continue;
                sl.Children.Add(MakeRow(item, true));
                prevVis = !item.Header;
            }
        }
        // 搜索空状态：过滤后 0 命中时给一行居中的灰字提示，别让列表直接空掉。
        // 样式贴普通菜单行（同高度、同字号），不可点击、不进 _meta（Enter/↑↓ 都拿不到它）。
        if (_kb.Length > 0 && _meta.Count == 0)
        {
            var emptyRow = new Border { Height = Math.Max(fs * 2.2, 30), Background = Brushes.Transparent, IsHitTestVisible = false };
            // 菜单宽度固定 260，查询词放不进来（标题行右侧已有 "/词 0 项" 回显），这里只留短句
            emptyRow.Child = new TextBlock
            {
                Text = "无匹配 · Esc 清除",
                FontSize = fs,
                Foreground = PMuted(110, Color.FromRgb(140, 140, 148)),
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 8, 0)
            };
            sl.Children.Add(emptyRow);
        }
        // 搜索回显（_meta 此时已是本层可见的可执行项）；写在标题行里，所以不改变任何高度
        if (_kb.Length > 0)
        {
            int hitN = _meta.Count;
            kbHead.Text = "/ " + _kb + "    " + hitN + " 项" + (hitN == 0 ? "（Backspace 删字 / Esc 清空）" : "");
            kbHead.Foreground = PB(hitN > 0 ? cfg.FC : Tk.Warn, Color.FromRgb(204, 204, 204));
        }
        else kbHead.Text = "";
        if (prevVis) sl.Children.Add(MakeSep());
        var cfgRow = new StackPanel { Orientation = Orientation.Horizontal, Height = 26, Cursor = Cursors.Hand, Tag = Newtonsoft.Json.JsonConvert.SerializeObject(new { action = ACT_EDIT_CONFIG }), Background = Brushes.Transparent };
        cfgRow.Children.Add(new TextBlock { Text = "  ⚙  设置", FontSize = fs - 2, Foreground = new SolidColorBrush(PHex(cfg.FC, Color.FromRgb(204, 204, 204))) { Opacity = 0.6 }, VerticalAlignment = VerticalAlignment.Center });
        HoverBind(cfgRow, PBA(cfg.HC, 30, Color.FromArgb(30, 51, 153, 255)));
        cfgRow.MouseLeftButtonUp += (_, __) => { win.Tag = cfgRow.Tag?.ToString(); FadeClose(); };
        sl.Children.Add(cfgRow);
        // 首次打开：默认高亮当前列表中使用次数最多的项（仅高亮，Enter 执行）
        if (!_kbDefault && nav.Count == 0 && _meta.Count > 0)
        {
            int bestI = -1; int bf = 0;
            for (int i2 = 0; i2 < _meta.Count; i2++) { int f2 = ConfigService.FreqCount(_meta[i2].It, cfg); if (f2 > bf) { bf = f2; bestI = i2; } }
            if (bestI >= 0 && bf > 0) { try { KbSel(bestI); } catch { } }
            _kbDefault = true;
        }
        // 进入/返回子菜单时轻微淡入 + 动感模糊收束（Blur 6→0，170ms；完场摘掉 Effect 不留开销）
        sl.Opacity = 0;
        sl.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(150)));
        try
        {
            var mBl = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
            var mBa = new System.Windows.Media.Animation.DoubleAnimation(6d, 0d, TimeSpan.FromMilliseconds(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            mBa.Completed += (_2, _3) => { try { if (ReferenceEquals(sl.Effect, mBl)) sl.Effect = null; } catch { } };
            sl.Effect = mBl;
            mBl.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, mBa);
        }
        catch { }
        try
        {
            double dx0 = (nav.Count == 0) ? 0 : (_navDir > 0 ? 9 : -9);
            if (Math.Abs(dx0) > 0.5)
            {
                var st0 = new TranslateTransform(dx0, 0);
                sl.RenderTransform = st0;
                var tx0 = new DoubleAnimation(dx0, 0d, TimeSpan.FromMilliseconds(200));
                tx0.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                st0.BeginAnimation(TranslateTransform.XProperty, tx0);
            }
        }
        catch { }
    }
    rebuild();

    os.Children.Add(sl);
    os.Children.Add(new Border { Height = 4, Background = Brushes.Transparent });
    var menuSV = new ScrollViewer { Content = os, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    // 滚轮平滑滚动：滚轮只累计目标位，16ms 计时器按指数趋近滑过去——替代默认一格一格硬跳。
    // Ctrl+滚轮是缩放，放行不拦。
    double msvTarget = 0;
    var msvTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
    msvTimer.Tick += (s2, e2) =>
    {
        try
        {
            double ext = Math.Max(0, menuSV.ScrollableHeight);
            msvTarget = Math.Max(0, Math.Min(ext, msvTarget));
            double cur = menuSV.VerticalOffset;
            double nv = cur + (msvTarget - cur) * 0.38;
            if (Math.Abs(msvTarget - cur) < 0.5) { nv = msvTarget; msvTimer.Stop(); }
            menuSV.ScrollToVerticalOffset(nv);
        }
        catch { msvTimer.Stop(); }
    };
    menuSV.PreviewMouseWheel += (_, we) =>
    {
        try
        {
            if (Keyboard.Modifiers == ModifierKeys.Control) return;   // 缩放通道自己处理
            double ext = Math.Max(0, menuSV.ScrollableHeight);
            msvTarget = Math.Max(0, Math.Min(ext, msvTarget - we.Delta / 120.0 * 3.4 * Math.Max(18, fs * 2.2 + cfg.IS + 4)));
            if (!msvTimer.IsEnabled) msvTimer.Start();
            we.Handled = true;
        }
        catch { }
    };
    try { var _ts = ThinScrollStyle(); if (_ts != null) menuSV.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = _ts; } catch { }
    rp.Child = menuSV;

    // Ctrl+MouseWheel zoom
    win.Loaded += (_, __) =>
    {
        try
        {
            var h = new WindowInteropHelper(win); h.EnsureHandle();
            var src = HwndSource.FromHwnd(h.Handle);
            if (src != null) src.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == 0x020A)
                {
                    long wp = wParam.ToInt64();
                    if ((wp & 0x0008) == 0) return IntPtr.Zero;
                    short d = (short)(wp >> 16);
                    double nz = cfg.Zoom + (d > 0 ? 0.1 : -0.1);
                    if (nz < 0.5) nz = 0.5;
                    if (nz > 2.5) nz = 2.5;
                    if (Math.Abs(nz - cfg.Zoom) > 0.001)
                    {
                        cfg.Zoom = nz;
                        zoomDirty = true;
                        rp.LayoutTransform = new ScaleTransform(nz, nz);
                    }
                    handled = true;
                    return IntPtr.Zero;
                }
                return IntPtr.Zero;
            });
        }
        catch { }
    };
    if (Math.Abs(z - 1) > 0.001) rp.LayoutTransform = new ScaleTransform(z, z);
    // ---- 调试出图：构建完成但不弹窗，直接渲染成 PNG（见 Exec 里的 shot= 参数）----
    if (!string.IsNullOrEmpty(shot))
    {
        try
        {
            string q0 = ShotField(shot, "q");
            if (!string.IsNullOrEmpty(q0)) { _kb = q0; rebuild(); if (_meta.Count > 0) KbSel(0); }
            string pinS = ShotField(shot, "pin");
            int pi3;
            if (!string.IsNullOrEmpty(pinS) && int.TryParse(pinS, out pi3) && pi3 >= 0 && pi3 < pinned.Count)
            {
                var ps3 = PinSource(cfg, pinned[pi3]);
                if (ps3 != null && ps3.HasSub) { _navDir = 1; nav.Push(cur); uniNav.Push(inUniSub); inUniSub = true; ClearKb(); cur = ps3.Sub; rebuild(); }
            }
            // sub=n：进入第 n 个「带子菜单的普通行」的子层（和 pin= 对照，看出通用块差异）
            string subS = ShotField(shot, "sub");
            int si3;
            if (!string.IsNullOrEmpty(subS) && int.TryParse(subS, out si3))
            {
                var withSub = cur.Where(x => x != null && x.HasSub).ToList();
                if (si3 >= 0 && si3 < withSub.Count) { _navDir = 1; nav.Push(cur); uniNav.Push(inUniSub); ClearKb(); cur = withSub[si3].Sub; rebuild(); }
            }
            // rebuild() 结尾会给 sl 挂 150ms 淡入（Opacity 0→1）；离屏渲染没有渲染时钟推进动画，
            // 不清掉的话整个列表会停在 Opacity 0，出图只有标题和置顶条。
            try { sl.BeginAnimation(UIElement.OpacityProperty, null); sl.Opacity = 1; } catch { }
            // 面板本身是半透明的，PNG 直接看会是透明底——垫一层不透明的面板底色，边框和留白才看得准
            // 另外把 os 从 ScrollViewer 里摘出来直接挂面板：ScrollViewer 在离屏渲染时内容不吐（出图不需要滚动）
            try { menuSV.Content = null; } catch { }
            rp.Child = os;
            var bc3 = PHex(cfg.BC, Color.FromRgb(45, 45, 45));
            var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(bc3.R, bc3.G, bc3.B)) };
            host.Children.Add(rp);
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(new Point(0, 0), host.DesiredSize));
            host.UpdateLayout();
            int sw3 = (int)Math.Ceiling(host.DesiredSize.Width), sh3 = (int)Math.Ceiling(host.DesiredSize.Height);
            if (sw3 < 40) sw3 = 40; if (sh3 < 40) sh3 = 40;
            var rtb = new RenderTargetBitmap(sw3, sh3, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);
            var enc3 = new PngBitmapEncoder(); enc3.Frames.Add(BitmapFrame.Create(rtb));
            string sdir = Path.Combine(Path.GetTempPath(), "ps_cm_shots"); Directory.CreateDirectory(sdir);
            string fp3 = Path.Combine(sdir, "menu_" + lk.ToString().ToLower() + "_" + DateTime.Now.ToString("HHmmssfff") + ".png");
            using (var fs3 = File.Create(fp3)) enc3.Save(fs3);
            return "SHOT " + fp3 + "  " + sw3 + "x" + sh3;
        }
        catch (Exception ex) { return "SHOT-ERR " + ex.Message; }
    }
    win.Content = rp;

    // Position near cursor
    try
    {
        var mp = System.Windows.Forms.Cursor.Position;
        var scr = System.Windows.Forms.Screen.FromPoint(mp);
        double scw = scr.WorkingArea.Width, sch = scr.WorkingArea.Height, scl = scr.WorkingArea.Left, sct = scr.WorkingArea.Top;
        double estW = cfg.MW > 0 ? cfg.MW * cfg.Zoom : 260;
        double wl = mp.X + 8, wt = mp.Y + 8;
        if (wl + estW > scl + scw) wl = mp.X - estW - 8;
        if (wt + 400 > sct + sch) wt = mp.Y - 400 - 8;
        if (wl < scl) wl = scl; if (wt < sct) wt = sct;
        win.Left = wl; win.Top = wt;
        if (menuSV != null) menuSV.MaxHeight = Math.Max(160, sct + sch - wt - 14);
    }
    catch { }
    // 精确定位：SourceInitialized（窗口尚未显示，不闪跳）按 DPI 换算光标物理像素坐标，
    // 并用实测窗口尺寸修正左右/上下翻转，修复高缩放屏或边缘翻转时弹在离鼠标较远处的问题
    try
    {
        win.SourceInitialized += (_, __) =>
        {
            try
            {
                var src = PresentationSource.FromVisual(win);
                if (src == null || src.CompositionTarget == null) return;
                var toDev = src.CompositionTarget.TransformToDevice;
                double dx = toDev.M11 > 0 ? toDev.M11 : 1, dy = toDev.M22 > 0 ? toDev.M22 : 1;
                var mp2 = System.Windows.Forms.Cursor.Position;
                var wa = System.Windows.Forms.Screen.FromPoint(mp2).WorkingArea;
                double waL = wa.Left / dx, waT = wa.Top / dy, waR = (wa.Left + wa.Width) / dx, waB = (wa.Top + wa.Height) / dy;
                double mx = mp2.X / dx, my = mp2.Y / dy;
                if (menuSV != null) menuSV.MaxHeight = Math.Max(160, waB - my - 30);
                win.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double aw = win.DesiredSize.Width, ah = win.DesiredSize.Height;
                if (aw <= 0) aw = cfg.MW > 0 ? cfg.MW * z : 260;
                if (ah <= 0) ah = 300;
                double wl2 = mx + 8, wt2 = my + 8;
                if (wl2 + aw > waR) wl2 = mx - aw - 8;
                if (wt2 + ah > waB) wt2 = my - ah - 8;
                if (wl2 < waL) wl2 = waL;
                if (wt2 < waT) wt2 = waT;
                if (wt2 + ah > waB) wt2 = Math.Max(waT, waB - ah);
                if (wl2 + aw > waR) wl2 = Math.Max(waL, waR - aw);
                win.Left = wl2; win.Top = wt2;
                if (menuSV != null) menuSV.MaxHeight = Math.Max(160, waB - wt2 - 14);
            }
            catch { }
        };
    }
    catch { }

    win.KeyDown += (_, e) =>
    {
        if (e.Key != Key.Escape) return;
        // 打过字时 Esc 的第一预期是"撤掉搜索"，而不是关菜单
        if (_kb.Length > 0) { ClearKb(); rebuild(); e.Handled = true; return; }
        if (nav.Count > 0) { cur = nav.Pop(); inUniSub = uniNav.Pop(); _navDir = -1; rebuild(); } else FadeClose();
    };
    win.KeyDown += (_, e) =>
    {
        try
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
            if (e.Key == Key.Back) { if (_kb.Length > 0) { _kb = _kb.Substring(0, _kb.Length - 1); KbUnsel(); rebuild(); if (_kb.Length > 0 && _meta.Count > 0) KbSel(0); e.Handled = true; } return; }
            if (e.Key == Key.Enter)
            {
                if (_kbCur >= 0 && _kbCur < _meta.Count)
                {
                    var rec = _meta[_kbCur];
                    // 带子菜单的项：Enter＝进入子层（和左键一致），不是执行
                    if (rec.It != null && rec.It.HasSub) { _navDir = 1; nav.Push(cur); uniNav.Push(inUniSub); if (rec.Uni) inUniSub = true; ClearKb(); cur = rec.It.Sub; rebuild(); }
                    else { win.Tag = rec.B.Tag?.ToString(); FadeClose(); }
                }
                e.Handled = true; return;
            }
            if (e.Key == Key.Up) { if (_meta.Count > 0) { int c0 = _kbCur; KbSel(c0 <= 0 ? _meta.Count - 1 : c0 - 1); e.Handled = true; return; } }
            if (e.Key == Key.Down) { if (_meta.Count > 0) { KbSel(_kbCur + 1); e.Handled = true; return; } }
            char add = '\0';
            if (e.Key >= Key.A && e.Key <= Key.Z) add = (char)('a' + (e.Key - Key.A));
            else if (e.Key >= Key.D0 && e.Key <= Key.D9) add = (char)('0' + (e.Key - Key.D0));
            else if (e.Key == Key.Space) add = ' ';
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && add >= 'a' && add <= 'z') add = (char)(add - 'a' + 'A');
            if (add == '\0') return;
            if (_kb.Length > 60) _kb = _kb.Substring(_kb.Length - 60);
            _kb += add;
            KbUnsel();
            rebuild();                       // 真过滤：不匹配的项直接不出现（原来只是"跳高亮"，名不副实）
            if (_meta.Count > 0) KbSel(0);    // 命中项逐个高亮，Enter 直接执行
            e.Handled = true;
        }
        catch { }
    };
    // 数字键**不再**切主题：它和"打字即时过滤"抢同一批键（标题里的 "1:1 视图"、"2 倍" 也要能按数字搜），
    // 而且过滤器对数字会 Handled，主题快切其实收不到——属于必清的死冲突。换主题去编辑器（那里有预设网格）。
    // 点击窗口外部任意位置 → 菜单消失（WH_MOUSE_LL 全局钩子检测窗口外的鼠标按下，不再依赖窗口失焦事件）
    var dismiss = new MenuDismissHook(win, CloseWin);
    dismiss.Install();
    if (!dismiss.Active)   // 钩子安装失败时退回原来的失焦检测
    {
        var fb = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        fb.Tick += (_, __) => { fb.Stop(); if (!win.IsActive && !dismissed) { var p = System.Windows.Forms.Cursor.Position; if (p.X < win.Left - 20 || p.X > win.Left + win.ActualWidth + 20 || p.Y < win.Top - 20 || p.Y > win.Top + win.ActualHeight + 20) CloseWin(); } };
        win.Deactivated += (_, __) => { if (!dismissed) fb.Start(); };
        win.Activated += (_, __) => fb.Stop();
    }
    win.Opacity = 0d;
    var sc0 = new ScaleTransform(0.96, 0.96);
    try
    {
        var cp0 = System.Windows.Forms.Cursor.Position;
        double estW2 = (cfg.MW > 0 ? cfg.MW : 260) * (cfg.Zoom < 0.5 ? 0.5 : cfg.Zoom);
        double ox = Math.Max(0.12, Math.Min(0.88, (cp0.X - win.Left) / Math.Max(1, estW2)));
        double oy = Math.Max(0.12, Math.Min(0.88, (cp0.Y - win.Top) / Math.Max(1, 460)));
        rp.RenderTransformOrigin = new Point(ox, oy);
    }
    catch { rp.RenderTransformOrigin = new Point(0.5, 0.5); }
    rp.RenderTransform = sc0;
    win.Loaded += (_, __) =>
    {
        try
        {
            var eo = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var oa = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(160)); oa.EasingFunction = eo;
            win.BeginAnimation(Window.OpacityProperty, oa);
            var sf = new DoubleAnimation(0.97, 1d, TimeSpan.FromMilliseconds(200)); sf.EasingFunction = eo;
            sc0.BeginAnimation(ScaleTransform.ScaleXProperty, sf);
            sc0.BeginAnimation(ScaleTransform.ScaleYProperty, sf);
        }
        catch { }
    };
    // 呼出弹入动画：先置透明，首帧渲染完成后 scale 0.96→1 + 淡入 + 模糊收束（与子层切换同语感）。
    // 兜底：动画万一没跑，400ms 后强制回到可见终态，绝不让菜单"卡在透明里"。
    try { win.Opacity = 0; } catch { }
    bool _popPlayed = false;
    win.ContentRendered += (_, __) =>
    {
        if (_popPlayed) return; _popPlayed = true;
        try
        {
            var sc0 = new ScaleTransform(0.96, 0.96);
            win.RenderTransform = sc0; win.RenderTransformOrigin = new Point(0.5, 0.5);
            var ea0 = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var a1 = new DoubleAnimation(0.96, 1d, TimeSpan.FromMilliseconds(170)) { EasingFunction = ea0 };
            var a2 = new DoubleAnimation(0.96, 1d, TimeSpan.FromMilliseconds(170)) { EasingFunction = ea0 };
            sc0.BeginAnimation(ScaleTransform.ScaleXProperty, a1);
            sc0.BeginAnimation(ScaleTransform.ScaleYProperty, a2);
            win.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(140)));
            try
            {
                var mBl0 = new System.Windows.Media.Effects.BlurEffect { Radius = 6 };
                var mBa0 = new System.Windows.Media.Animation.DoubleAnimation(6d, 0d, TimeSpan.FromMilliseconds(180)) { EasingFunction = ea0 };
                mBa0.Completed += (_2, _3) => { try { if (ReferenceEquals(win.Effect, mBl0)) win.Effect = null; } catch { } };
                win.Effect = mBl0;
                mBl0.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, mBa0);
            }
            catch { }
        }
        catch { try { win.Opacity = 1; } catch { } }
    };
    var popGuard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
    popGuard.Tick += (s2, e2) =>
    {
        popGuard.Stop();
        try { if (win.Opacity < 0.05) { win.Opacity = 1; win.RenderTransform = null; win.Effect = null; } } catch { }
    };
    popGuard.Start();
    win.ShowDialog();
    if (zoomDirty)
    {
        try { ConfigService.Save(cfg); } catch { }
    }
    if (_pendingEdit != null)
    {
        var pe = _pendingEdit; _pendingEdit = null;
        try { EditItem(pe); ConfigService.Save(cfg); } catch { }
    }
    return win.Tag as string;
}

}
}
