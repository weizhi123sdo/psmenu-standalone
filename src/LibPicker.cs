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
static void TryRunScriptUi(Window win, System.Windows.Controls.TextBlock stat, string code, string title, string okSuffix)
{
    if (win != null) { try { win.Hide(); } catch { } }
    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
    {
        Exception err = null; string r2 = null;
        try { r2 = ActionExecutor.Exec(ACT_SCRIPT, code, title); }
        catch (Exception ex) { err = ex; try { LogFail("tryrun", ex); } catch { } }
        if (win == null || stat == null) return;
        try
        {
            win.Dispatcher.Invoke((Action)(() =>
            {
                try { win.Show(); win.Activate(); } catch { }
                if (err != null) { stat.Text = "试运行失败：" + err.Message; stat.Foreground = Tk.B(Tk.Warn); }
                else if (r2 == title) { stat.Text = "已发到 Photoshop 试运行：" + title + (okSuffix ?? ""); stat.Foreground = Tk.B(Tk.InkMuted); }
                else { stat.Text = "试运行没成功：" + r2; stat.Foreground = Tk.B(Tk.Warn); }
            }));
        }
        catch { }
    });
}

// ---------- 脚本查看窗（与「PS脚本库」动作里那个同形；两个动作代码不共享，只能各写一份） ----------
static void ShowScriptViewer(Window owner, string title, string cat, string desc, string code)
{
    var card = Tk.B(Tk.Surface);
    var sub = Tk.B(Tk.SurfaceAlt);
    var hair = Tk.B(Tk.BorderDefault);
    var ink = Tk.B(Tk.Ink);
    var muted = Tk.B(Tk.InkMuted);
    var accent = Tk.B(Tk.Accent);
    var okC = Tk.B(Tk.Ok);
    var warnC = Tk.B(Tk.Warn);

    string body2 = code.Replace(((char)13).ToString(), "");
    var bodyLines = body2.Split((char)10);
    var dlg = new Window { Title = title, Width = 880, Height = 620, MinWidth = 560, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Tk.B(Tk.Canvas) };
    var grid = new Grid { Margin = new Thickness(Tk.S16) };
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition());
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    var head = new StackPanel();
    head.Children.Add(new TextBlock { Text = title, FontSize = Tk.FTitle, FontWeight = FontWeights.SemiBold, Foreground = ink });
    string info = cat + (string.IsNullOrEmpty(desc) ? "" : "  ·  " + desc) + "  ·  " + code.Length + " 字符 / " + bodyLines.Length + " 行";
    head.Children.Add(new TextBlock { Text = info, FontSize = Tk.FSmall, Foreground = muted, Margin = new Thickness(0, Tk.S4, 0, 0), TextWrapping = TextWrapping.Wrap });
    grid.Children.Add(head);

    var body = new Grid();
    body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    body.ColumnDefinitions.Add(new ColumnDefinition());
    var sb = new StringBuilder();
    for (int i = 0; i < bodyLines.Length; i++) { sb.Append(i + 1); sb.Append((char)10); }
    var ln = new TextBox { Text = sb.ToString().TrimEnd(), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = Tk.FSmall, Foreground = muted, Background = sub, BorderThickness = new Thickness(0), Padding = new Thickness(Tk.S10, Tk.S8, Tk.S6, Tk.S8), TextAlignment = TextAlignment.Right, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, IsTabStop = false };
    var tx = new TextBox { Text = body2, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = Tk.FSmall, Foreground = ink, Background = card, BorderThickness = new Thickness(0), Padding = new Thickness(Tk.S6, Tk.S8, Tk.S10, Tk.S8), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    tx.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => { try { ln.ScrollToVerticalOffset(e.VerticalOffset); } catch { } }));
    body.Children.Add(ln); body.Children.Add(tx); Grid.SetColumn(tx, 1);
    var bodyCard = new Border { CornerRadius = new CornerRadius(Tk.RMd), Background = card, BorderBrush = hair, BorderThickness = new Thickness(1), Margin = new Thickness(0, Tk.S12, 0, 0), Child = body };
    grid.Children.Add(bodyCard); Grid.SetRow(bodyCard, 1);

    var stat = new TextBlock { FontSize = Tk.FSmall, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 430 };
    var copy = U.Btn("复制脚本", Th.AB, () => { try { Clipboard.SetText(code); stat.Text = "已复制到剪贴板"; stat.Foreground = okC; } catch (Exception ex) { stat.Text = "复制失败：" + ex.Message; stat.Foreground = warnC; } });
    var tryRun = U.Btn("▶ 试运行", Th.TS, () => { TryRunScriptUi(dlg, stat, code, title, null); });
    var save = U.Btn("另存为 .jsx", Th.TS, () =>
    {
        try
        {
            var sd = new Microsoft.Win32.SaveFileDialog { FileName = title + ".jsx", Filter = "JSX 脚本|*.jsx|全部文件|*.*", Title = "另存脚本" };
            if (sd.ShowDialog() != true) return;
            File.WriteAllText(sd.FileName, code + Environment.NewLine, new UTF8Encoding(false));
            stat.Text = "已保存：" + sd.FileName; stat.Foreground = okC;
        }
        catch (Exception ex) { stat.Text = "保存失败：" + ex.Message; stat.Foreground = warnC; }
    });
    var foot = new DockPanel { LastChildFill = false, Margin = new Thickness(0, Tk.S12, 0, 0) };
    var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    DockPanel.SetDock(right, Dock.Right);
    right.Children.Add(tryRun); right.Children.Add(new TextBlock { Text = " ", FontSize = Tk.FSmall }); right.Children.Add(save); right.Children.Add(new TextBlock { Text = " ", FontSize = Tk.FSmall }); right.Children.Add(copy);
    foot.Children.Add(right);
    foot.Children.Add(stat);
    grid.Children.Add(foot); Grid.SetRow(foot, 2);

    dlg.Content = grid;
    if (owner != null) dlg.Owner = owner;
    dlg.ShowDialog();
}


// 挑选窗出图的结果（`shot=pick` 时的 PNG 路径 / 错误）。挑选窗本身返回"挑中的菜单项"，
// 没法把出图结果顺着返回值带出来，就用这个字段捎回给 Exec。
static string PickShotOut = "";

// 小药丸徽章（与 B 的置入窗同款形状）：风险 / 已在菜单 / 旧版都走它——
// 三处形状一致，才看得出"这是一套状态标记"，而不是三行各写各的说明文字。
static Border Pill(string text, string fgHex, string bgHex, string tip)
{
    var b = new Border
    {
        Background = new SolidColorBrush(Tk.C(bgHex)),
        CornerRadius = new CornerRadius(Tk.RLg),
        Padding = new Thickness(Tk.S6, 1, Tk.S6, 1),
        Margin = new Thickness(Tk.S8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = Tk.FMicro, Foreground = Tk.B(fgHex) }
    };
    if (!string.IsNullOrEmpty(tip)) b.ToolTip = tip;
    return b;
}

// ---------- 从脚本库挑脚本（A 侧只读缓存；挑中的直接变成菜单项） ----------
// 单独放成**文件级静态方法**，不嵌进 BuildLayerMenuPage：那个页面里已经堆了几十个嵌套局部函数，
// 库相关的逻辑塞进去只是给自己上风险（r53 就是这么加的）。这里本来也不需要页面上下文——
// 只要一个 owner 窗口、目标类型名、以及"菜单里已有哪些标题（标题 → 该项置入时的正文指纹）"。
// 返回空表 = 用户没挑或取消了（调用方什么都不做）。
// shot 非空时改成"渲染成 PNG 就返回"（`shot=pick;sel=0`），风险徽章只在特定选中态才出现，
// 菜单出图看不到它，必须能单独渲染这个窗（见 Exec 里的 shot= 分发）。
static List<MI> PickScriptsFromLib(Window owner, string layerLabel, Dictionary<string, string> haveHashes, string shot)
{
    var res = new List<MI>();
    var card = Tk.B(Tk.Surface);
    var sub = Tk.B(Tk.SurfaceAlt);
    var line = Tk.B(Tk.BorderDefault);
    var ink = Tk.B(Tk.Ink);
    var muted = Tk.B(Tk.InkMuted);
    var faint = Tk.B(Tk.InkFaint);
    var accent = Tk.B(Tk.Accent);
    var accSoft = Tk.B(Tk.AccentSoft);
    var mono = new FontFamily("Consolas");

    // 分类用缓存里的 categories；万一清单没带分类（老缓存 / 手工改过），
    // 落单的脚本会被收进一个虚拟分类——否则会出现"库里明明有货、列表却是空的"，
    // 而这种"静默地什么都没有"正是这套工具踩过最多的坑。
    // 抽成局部函数是为了"空态里启动 B → 缓存到手后原地重排一次"（下面那个按钮）。
    var cats = new List<string>();
    var byCat = new Dictionary<string, List<string>>();
    Action BuildCats = () =>
    {
        cats.Clear(); byCat.Clear();
        foreach (var c0 in NetScripts.CatNames) { cats.Add(c0); if (NetScripts.ByCat.ContainsKey(c0)) byCat[c0] = NetScripts.ByCat[c0]; }
        var loose = new List<string>();
        foreach (var k0 in NetScripts.Codes.Keys)
        {
            bool inAny = false;
            foreach (var c0 in cats) if (byCat.ContainsKey(c0) && byCat[c0].Contains(k0)) { inAny = true; break; }
            if (!inAny) loose.Add(k0);
        }
        if (loose.Count > 0) { cats.Add("未分类"); byCat["未分类"] = loose; }
    };
    BuildCats();

    var w = new Window { Title = "从脚本库添加脚本", Width = 1020, Height = 660, MinWidth = 780, MinHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Tk.B(Tk.Canvas) };

    string catSel = "全部";
    string curKey = null;
    var picked = new HashSet<string>();
    var shownKeys = new List<string>();
    var railHost = new StackPanel();
    var list = new StackPanel();
    var stat = new TextBlock { FontSize = Tk.FMicro, Foreground = muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    var search = new TextBox { FontSize = Tk.FBody, Padding = new Thickness(Tk.S8, Tk.S6, Tk.S8, Tk.S6), Background = card, Foreground = ink, BorderBrush = line, BorderThickness = new Thickness(1), CaretBrush = ink };
    // 聚焦态：描边变强调色（不然点了看不出焦点在哪）
    search.GotFocus += (_, __) => { try { search.BorderBrush = accent; } catch { } };
    search.LostFocus += (_, __) => { try { search.BorderBrush = line; } catch { } };
    var hint = new TextBlock { Text = "搜索：中文名 / 分类 / 说明 / 正文", FontSize = Tk.FMicro, Foreground = faint, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Tk.S10, 0, 0, 0) };
    var pvTitle = new TextBlock { Text = "未选择脚本", FontSize = Tk.FBody, FontWeight = FontWeights.SemiBold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };
    var pvMeta = new TextBlock { FontSize = Tk.FMicro, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Tk.S10, 0, 0, 0) };
    // 徽章行：风险 / 已在菜单 / 旧版。声明必须写在 RenderPreview 之前（局部函数不能引用后面才声明的局部变量）
    var pvPills = new WrapPanel { Margin = new Thickness(0, Tk.S2, 0, Tk.S2) };
    var pvCode = new TextBox { IsReadOnly = true, FontFamily = mono, FontSize = Tk.FSmall, Foreground = ink, Background = card, BorderBrush = line, BorderThickness = new Thickness(1), Padding = new Thickness(Tk.S8), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    // 效果图：只读本机缓存（B 取回过的图），A 自己不联网——新机器上先跑一次 B 就有了。
    // 声明放在 RenderPreview 之前：局部函数不能引用后面才声明的局部变量（CS0841）。
    var shotImg = new Image { Stretch = Stretch.Uniform, MaxHeight = 170, HorizontalAlignment = HorizontalAlignment.Center };
    var shotBox = new Border
    {
        CornerRadius = new CornerRadius(Tk.RSm),
        Background = card,
        Padding = new Thickness(Tk.S6),
        Margin = new Thickness(0, 0, 0, Tk.S6),
        Visibility = Visibility.Collapsed,
        Child = new StackPanel { Children = { new TextBlock { Text = "效果图", FontSize = Tk.FMicro, Foreground = muted, Margin = new Thickness(0, 0, 0, Tk.S4) }, shotImg } }
    };
    var footLbl = new TextBlock { FontSize = Tk.FSmall, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 430 };
    Button addBtn = null;
    Action renderRail = null, renderList = null, renderAll = null, refreshFoot = null;

    string Q() { return (search.Text ?? "").Trim(); }
    string Val(string key, Dictionary<string, string> d) { return (d != null && d.ContainsKey(key)) ? (d[key] ?? "") : ""; }
    string CnOf(string key) { string cn = Val(key, NetScripts.Cns); return cn.Length == 0 ? key : cn; }
    string CatOf(string key)
    {
        foreach (var c in cats)
            if (byCat.ContainsKey(c) && byCat[c].Contains(key)) return c;
        return "";
    }
    bool Match(string key, string cat, string q)
    {
        return CnOf(key).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || cat.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || Val(key, NetScripts.Descs).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || Val(key, NetScripts.Codes).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || NetScripts.PyMatch(key, q);      // 中文名还能打拼音首字母（清单带的 py 字段）
    }
    // 次要动作用灰字：一排按钮全是强调色会分不出主次（试运行是主，复制/大图查看是次）
    Button QBtnT(string t, Brush fg, Action a)
    {
        var b = new Button { Content = t, FontSize = Tk.FSmall, Padding = new Thickness(Tk.S8, Tk.S2, Tk.S8, Tk.S2), Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Foreground = fg, Focusable = false, FocusVisualStyle = null };
        b.Click += (s, e) => { try { a(); } catch { } };
        return b;
    }
    Button QBtn(string t, Action a) { return QBtnT(t, accent, a); }

    // 徽章行填装：列表行（append）与预览区（clear 后填）共用，口径只写一处——
    // 两个地方各写一遍，早晚会一处改了另一处没改。
    // 风险在前：红＝会改文件/关文档，灰＝只会弹窗。然后才是"在不在菜单里"。
    void FillPills(Panel host, string key, bool clear)
    {
        if (clear) host.Children.Clear();
        string rk9 = NetScripts.RiskText(key);
        if (rk9.Length > 0)
        {
            bool rh9 = NetScripts.RiskHard(key);
            host.Children.Add(Pill(rh9 ? "⚠ 会改文件" : "⚠ 会弹窗",
                rh9 ? Tk.Danger : Tk.Warn,
                rh9 ? Tk.DangerSoft : Tk.SurfaceAlt,
                "静态扫描命中：" + rk9));
        }
        string mh9 = null;
        bool have9 = haveHashes != null && haveHashes.TryGetValue(CnOf(key), out mh9);
        string lh9 = null;
        NetScripts.Hashes.TryGetValue(key, out lh9);
        if (have9 && !string.IsNullOrEmpty(mh9) && !string.IsNullOrEmpty(lh9) && mh9 != lh9)
            host.Children.Add(Pill("菜单里是旧版", Tk.Warn, Tk.SurfaceAlt, "菜单里那条还是老正文：点中它，右上有「↻ 更新菜单里的脚本」"));
        else if (have9) host.Children.Add(Pill("已在菜单", Tk.Ok, Tk.OkSoft, "已经在「PS便捷菜单」里，且与库里一致"));
    }

    // 分类标题带：次级底 + 小字半粗（原来只有灰字，列表的分段看不出来）
    Border CatBand(string text)
    {
        return new Border
        {
            Background = sub,
            CornerRadius = new CornerRadius(Tk.RSm),
            Padding = new Thickness(Tk.S8, Tk.S2, Tk.S8, Tk.S2),
            Margin = new Thickness(Tk.S6, Tk.S8, Tk.S6, Tk.S2),
            Child = new TextBlock { Text = text, FontSize = Tk.FMicro, FontWeight = FontWeights.SemiBold, Foreground = muted }
        };
    }
    void RenderPreview()
    {
        if (string.IsNullOrEmpty(curKey) || !NetScripts.Codes.ContainsKey(curKey))
        { pvTitle.Text = "未选择脚本"; pvMeta.Text = ""; pvCode.Text = ""; pvPills.Children.Clear(); shotBox.Visibility = Visibility.Collapsed; shotImg.Source = null; return; }
        string code = NetScripts.Codes[curKey] ?? "";
        string body2 = code.Replace(((char)13).ToString(), "");
        pvTitle.Text = CnOf(curKey);
        pvMeta.Text = CatOf(curKey) + "   ·   " + body2.Split((char)10).Length + " 行   ·   " + code.Length + " 字符";
        pvCode.Text = body2;
        pvCode.ScrollToHome();
        // 徽章行：与列表行同一套口径。放在这里而不是上面那段，是因为它要跟"当前选中的是哪条"同步
        FillPills(pvPills, curKey, true);
        // 效果图只认本机缓存：有就贴出来，没有就不显示（下载归 B）
        shotBox.Visibility = Visibility.Collapsed; shotImg.Source = null;
        try
        {
            string sp = NetScripts.ShotCached(curKey);
            if (sp != null)
            {
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bi.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(sp);
                bi.EndInit();
                bi.Freeze();
                shotImg.Source = bi;
                shotBox.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) { LogFail("shot.cached", ex); }
    }
    MI MkItem(string key)
    {
        var mi = new MI { Title = CnOf(key), Icon = "fa:Solid_Code", Action = ActionType.Script, Value = Val(key, NetScripts.Codes) };
        string d = Val(key, NetScripts.Descs);
        if (d.Length > 0) mi.Note = d;   // 说明顺带进备注：过俩月自己也认得出这个脚本干嘛的
        // 出处：库里的 key + 这一版的正文指纹。「PS脚本库」靠它判"菜单里这条是不是旧版"，
        // 少了这两行，库更新后只能靠肉眼发现菜单里还是老正文。
        mi.LibKey = key;
        string hh;
        if (NetScripts.Hashes.TryGetValue(key, out hh)) mi.LibHash = hh;
        return mi;
    }
    void Accept()
    {
        if (picked.Count == 0) return;
        // 按分类顺序收集：加入顺序跟用户在列表里看到的顺序一致（HashSet 的顺序不可预期）
        var seen = new HashSet<string>();
        foreach (var c in cats)
        {
            if (!byCat.ContainsKey(c)) continue;
            foreach (var k in byCat[c]) if (picked.Contains(k) && seen.Add(k)) res.Add(MkItem(k));
        }
        foreach (var k in picked) if (seen.Add(k)) res.Add(MkItem(k));
        w.Close();
    }

    // ---- 顶部：标题 + 说明 + 库状态 ----
    var hcol = new StackPanel();
    hcol.Children.Add(new TextBlock { Text = "从脚本库添加脚本", FontSize = Tk.FTitle, FontWeight = FontWeights.SemiBold, Foreground = ink });
    hcol.Children.Add(new TextBlock { Text = "选中的脚本会加成「" + layerLabel + "」下的菜单项。点一行＝选中/取消，可多选；右侧是正文预览。", FontSize = Tk.FSmall, Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Tk.S4, 0, 0) });
    hcol.Children.Add(stat);

    var sWrap = new Grid { Margin = new Thickness(0, Tk.S12, 0, Tk.S10) };
    sWrap.Children.Add(search); sWrap.Children.Add(hint);

    // ---- 左：分类栏（次级面铺底、不画边框；选中项左侧一根强调条）----
    // 面板一律"白底 + 1px 边框"（与 B 的置入窗同一套层次规则）：白卡压在浅灰画布上主要靠描边分界
    var railCard = new Border { CornerRadius = new CornerRadius(Tk.RMd), Background = card, BorderBrush = line, BorderThickness = new Thickness(1), Padding = new Thickness(Tk.S6, Tk.S8, Tk.S6, Tk.S8), Margin = new Thickness(0, 0, Tk.S12, 0), Child = railHost };
    // ---- 中：列表 ----
    var libListSv = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    double libListSvTarget = 0;
    var libListSvTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
    libListSvTimer.Tick += (s2, e2) =>
    {
        try
        {
            double ext = Math.Max(0, libListSv.ScrollableHeight);
            libListSvTarget = Math.Max(0, Math.Min(ext, libListSvTarget));
            double cur = libListSv.VerticalOffset;
            double nv = cur + (libListSvTarget - cur) * 0.38;
            if (Math.Abs(libListSvTarget - cur) < 0.5) { nv = libListSvTarget; libListSvTimer.Stop(); }
            libListSv.ScrollToVerticalOffset(nv);
        }
        catch { libListSvTimer.Stop(); }
    };
    libListSv.PreviewMouseWheel += (_, we) =>
    {
        try
        {
            double ext = Math.Max(0, libListSv.ScrollableHeight);
            libListSvTarget = Math.Max(0, Math.Min(ext, libListSvTarget - we.Delta / 120.0 * 3.4 * 30));
            if (!libListSvTimer.IsEnabled) libListSvTimer.Start();
            we.Handled = true;
        }
        catch { }
    };

    var listCard = new Border { CornerRadius = new CornerRadius(Tk.RMd), Background = card, BorderBrush = line, BorderThickness = new Thickness(1), Child = libListSv };
    // ---- 右：预览 ----
    var pvRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };
    pvRow.ColumnDefinitions.Add(new ColumnDefinition());
    pvRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    pvTitle.TextTrimming = TextTrimming.CharacterEllipsis;
    pvRow.Children.Add(pvTitle);
    pvMeta.TextWrapping = TextWrapping.Wrap;
    pvMeta.Margin = new Thickness(0, 0, 0, Tk.S6);
    var pvBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    pvBtns.Children.Add(QBtn("▶ 试运行", () =>
    {
        if (string.IsNullOrEmpty(curKey)) return;
        TryRunScriptUi(w, footLbl, Val(curKey, NetScripts.Codes), CnOf(curKey), "（确认好用再点「加入菜单」）");
    }));
    // 「大图查看」去掉了（与 B 一致）：预览区本身已是"图 / 码 二选一"，点「看代码」就地展开
    pvBtns.Children.Add(QBtnT("复制", muted, () => { if (!string.IsNullOrEmpty(curKey)) { try { Clipboard.SetText(Val(curKey, NetScripts.Codes)); footLbl.Text = "已复制：" + CnOf(curKey); } catch { } } }));
    pvRow.Children.Add(pvBtns); Grid.SetColumn(pvBtns, 1);
    var pvStack = new StackPanel();
    var pvBand = new Border
    {
        Background = sub,
        CornerRadius = new CornerRadius(Tk.RSm),
        Padding = new Thickness(Tk.S8, Tk.S4, Tk.S8, Tk.S4),
        Margin = new Thickness(0, 0, 0, Tk.S8),
        Child = new TextBlock { Text = "脚本预览", FontSize = Tk.FMicro, FontWeight = FontWeights.SemiBold, Foreground = ink }
    };
    pvStack.Children.Add(pvBand);
    pvStack.Children.Add(pvRow); pvStack.Children.Add(pvMeta); pvStack.Children.Add(pvPills); pvStack.Children.Add(shotBox); pvStack.Children.Add(pvCode);
    var pvCard = new Border { CornerRadius = new CornerRadius(Tk.RMd), Background = card, BorderBrush = line, BorderThickness = new Thickness(1), Padding = new Thickness(Tk.S10), Margin = new Thickness(Tk.S12, 0, 0, 0), Child = pvStack };

    var body = new Grid();
    body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
    body.ColumnDefinitions.Add(new ColumnDefinition());
    body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
    body.Children.Add(railCard);
    body.Children.Add(listCard); Grid.SetColumn(listCard, 1);
    body.Children.Add(pvCard); Grid.SetColumn(pvCard, 2);

    // ---- 底部：左＝已选统计 + 批量；右＝取消 / 加入 ----
    var foot = new Grid();
    foot.ColumnDefinitions.Add(new ColumnDefinition());
    foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    var footBand = new Border { Background = sub, BorderBrush = line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(Tk.S16, Tk.S8, Tk.S16, Tk.S8), Margin = new Thickness(-Tk.S16, 0, -Tk.S16, -Tk.S16), Child = foot };
    var fLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    fLeft.Children.Add(footLbl);
    fLeft.Children.Add(QBtn("全选当前列表", () => { foreach (var k in shownKeys) picked.Add(k); renderList(); refreshFoot(); }));
    fLeft.Children.Add(QBtn("清空选择", () => { picked.Clear(); renderList(); refreshFoot(); }));
    foot.Children.Add(fLeft);
    var fRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    var cancelBtn = new Button { Content = "取消", FontSize = Tk.FSmall, Padding = new Thickness(Tk.S10, Tk.S6, Tk.S10, Tk.S6), Cursor = Cursors.Hand, Background = card, Foreground = muted, BorderBrush = line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    cancelBtn.Click += (_, __) => { try { w.Close(); } catch { } };
    addBtn = new Button { Content = "加入菜单", FontSize = Tk.FSmall, FontWeight = FontWeights.SemiBold, Padding = new Thickness(Tk.S12, Tk.S6, Tk.S12, Tk.S6), Cursor = Cursors.Hand, Background = accent, Foreground = Brushes.White, BorderBrush = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
    addBtn.Click += (_, __) => { try { Accept(); } catch (Exception ex) { LogFail("PickFromLib.accept", ex); } };
    fRight.Children.Add(cancelBtn);
    fRight.Children.Add(new TextBlock { Text = " " });
    fRight.Children.Add(addBtn);
    foot.Children.Add(fRight); Grid.SetColumn(fRight, 1);

    var root = new Grid { Margin = new Thickness(Tk.S16) };
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    root.RowDefinitions.Add(new RowDefinition());
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    root.Children.Add(hcol);
    root.Children.Add(sWrap); Grid.SetRow(sWrap, 1);
    root.Children.Add(body); Grid.SetRow(body, 2);
    root.Children.Add(footBand); Grid.SetRow(footBand, 3);

    renderRail = () =>
    {
        railHost.Children.Clear();
        var cnt = new Dictionary<string, int>();
        int all = 0;
        foreach (var c in cats)
        {
            int n = 0;
            foreach (var k in byCat[c]) if (Q().Length == 0 || Match(k, c, Q())) n++;
            if (n > 0) { cnt[c] = n; all += n; }
        }
        cnt["全部"] = all;
        var keys = new List<string> { "全部" };
        foreach (var c in cats) if (cnt.ContainsKey(c)) keys.Add(c);
        foreach (var kk in keys)
        {
            string name = kk;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = new Border { Background = catSel == name ? accent : Brushes.Transparent, CornerRadius = new CornerRadius(Tk.RXs) };
            g.Children.Add(bar);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Tk.S8, 0, 0, 0) };
            sp.Children.Add(new TextBlock { Text = name == "全部" ? "全部脚本" : name, FontSize = Tk.FSmall, Foreground = ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = "  " + cnt[name], FontFamily = mono, FontSize = Tk.FMicro, Foreground = muted, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(sp); Grid.SetColumn(sp, 1);
            var rowB = new Border { CornerRadius = new CornerRadius(Tk.RSm), Background = catSel == name ? accSoft : Brushes.Transparent, BorderBrush = catSel == name ? accent : Brushes.Transparent, BorderThickness = new Thickness(1), Padding = new Thickness(Tk.S6), Margin = new Thickness(0, 0, 0, Tk.S2), Cursor = Cursors.Hand, Child = g };
            rowB.MouseLeftButtonUp += (_, __) => { catSel = name; renderRail(); renderList(); };
            railHost.Children.Add(rowB);
        }
    };

    renderList = () =>
    {
        list.Children.Clear();
        shownKeys.Clear();
        string q = Q();
        hint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (NetScripts.Codes.Count == 0)
        {
            // 空状态要指出"下一步在哪"：库是独立动作取回来的，缓存文件本身没法在 A 里刷新
            var em = new StackPanel { Margin = new Thickness(Tk.S16) };
            em.Children.Add(new TextBlock { Text = "本地还没有脚本库缓存。", FontSize = Tk.FBody, Foreground = ink });
            em.Children.Add(new TextBlock { Text = "库由独立动作「PS脚本库」取回并写进共享缓存，取完这里立刻就有。", FontSize = Tk.FSmall, Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Tk.S6, 0, Tk.S10) });
            var goLib = U.Btn("📚 启动 PS脚本库", Th.AB, () =>
            {
                try
                {
                    stat.Text = "正在启动「PS脚本库」…";
                    DateTime t0 = File.Exists(NetScripts.CachePath()) ? File.GetLastWriteTime(NetScripts.CachePath()) : DateTime.MinValue;
                    LaunchScriptLib(ok =>
                    {
                        // 回调在后台线程上，回 UI 线程再动控件
                        w.Dispatcher.Invoke((Action)(() =>
                        {
                            if (!ok) { stat.Text = "没找到「PS脚本库」动作 —— 装好那个动作再点一次。"; return; }
                            stat.Text = "已启动「PS脚本库」，等它把库拉回来…";
                        }));
                    });
                    // 它把缓存刷新出来（时间戳变了）就把列表原地填上，最多等 12 秒
                    WatchCacheChange(t0, 12000, w, () =>
                    {
                        NetScripts.Load();
                        BuildCats();
                        catSel = "全部"; curKey = null;
                        renderRail(); renderList(); RenderPreview(); refreshFoot();
                        stat.Text = "库已就绪：共 " + NetScripts.Codes.Count + " 个脚本。";
                    });
                }
                catch (Exception ex) { LogFail("PickFromLib.launchLib", ex); }
            });
            em.Children.Add(goLib);
            list.Children.Add(em);
            return;
        }
        int shown = 0;
        foreach (var c in cats)
        {
            if (catSel != "全部" && catSel != c) continue;
            var ks = new List<string>();
            foreach (var k in byCat[c]) if (q.Length == 0 || Match(k, c, q)) ks.Add(k);
            if (ks.Count == 0) continue;
            shown += ks.Count;
            if (catSel == "全部")
                list.Children.Add(CatBand(c));
            foreach (var k in ks)
            {
                shownKeys.Add(k);
                string key = k;
                bool on = picked.Contains(key);
                var g = new Grid { Height = 34 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var ck = new TextBlock { Text = on ? "☑" : "☐", FontSize = Tk.FBody, Foreground = on ? accent : faint, VerticalAlignment = VerticalAlignment.Center, Width = 20, TextAlignment = TextAlignment.Center, Margin = new Thickness(Tk.S6, 0, 0, 0) };
                g.Children.Add(ck);
                var lp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                lp.Children.Add(new TextBlock { Text = CnOf(key), FontSize = Tk.FBody, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
                string code0 = Val(key, NetScripts.Codes);
                int lnN = code0.Length == 0 ? 0 : code0.Replace(((char)13).ToString(), "").Split((char)10).Length;
                lp.Children.Add(new TextBlock { Text = "   " + lnN + " 行", FontFamily = mono, FontSize = Tk.FMicro, Foreground = muted, VerticalAlignment = VerticalAlignment.Center });
                // 徽章：先风险，再"在不在菜单里"。都只标出来，不拦着用户加——拦了反而要解释为什么不让加。
                FillPills(lp, key, false);
                g.Children.Add(lp); Grid.SetColumn(lp, 1);
                var wash = new SolidColorBrush(on ? Tk.C(Tk.AccentSoft) : Colors.Transparent);
                var rowB = new Border { CornerRadius = new CornerRadius(Tk.RSm), Background = wash, Padding = new Thickness(0, 0, Tk.S6, 0), Child = g, Cursor = Cursors.Hand, BorderBrush = line, BorderThickness = new Thickness(0, 0, 0, 1), ToolTip = "点击＝选中/取消（可多选）" };
                var rowScA = new ScaleTransform(1, 1); rowB.RenderTransform = rowScA; rowB.RenderTransformOrigin = new Point(0.5, 0.5);
                rowB.PreviewMouseLeftButtonDown += (_, __) => { try { var pdA = new DoubleAnimation(0.99, TimeSpan.FromMilliseconds(70)); rowScA.BeginAnimation(ScaleTransform.ScaleXProperty, pdA); rowScA.BeginAnimation(ScaleTransform.ScaleYProperty, pdA); } catch { } };
                rowB.PreviewMouseLeftButtonUp += (_, __) => { try { var puA = new DoubleAnimation(1d, TimeSpan.FromMilliseconds(90)); rowScA.BeginAnimation(ScaleTransform.ScaleXProperty, puA); rowScA.BeginAnimation(ScaleTransform.ScaleYProperty, puA); } catch { } };
                rowB.MouseEnter += (_, __) => { if (!picked.Contains(key)) { try { wash.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Tk.C(Tk.SurfaceAlt), TimeSpan.FromMilliseconds(120))); } catch { } } };
                rowB.MouseLeave += (_, __) => { if (!picked.Contains(key)) { try { wash.Color = Colors.Transparent; } catch { } } };
                rowB.MouseLeftButtonUp += (_, __) =>
                {
                    if (picked.Contains(key)) picked.Remove(key); else picked.Add(key);
                    curKey = key;
                    renderList(); RenderPreview(); refreshFoot();
                };
                list.Children.Add(rowB);
            }
        }
        if (shown == 0)
            list.Children.Add(new TextBlock { Text = "没有匹配的脚本。", FontSize = Tk.FBody, Foreground = muted, Margin = new Thickness(Tk.S12) });
    };

    refreshFoot = () =>
    {
        footLbl.Text = picked.Count == 0
            ? "已选 0 个   ·   点一行即选中（可多选），Enter 直接加入"
            : ("已选 " + picked.Count + " 个脚本");
        if (addBtn != null)
        {
            addBtn.Content = picked.Count == 0 ? "加入菜单" : ("加入菜单（" + picked.Count + "）");
            addBtn.IsEnabled = picked.Count > 0;
        }
    };

    renderAll = () =>
    {
        int n = NetScripts.Codes.Count;
        stat.Text = n > 0
            ? ("本地库共 " + n + " 个脚本 · " + cats.Count + " 个分类" + (NetScripts.Stamp == DateTime.MinValue ? "" : "   ·   缓存时间 " + NetScripts.Stamp.ToString("MM-dd HH:mm")))
            : "本地还没有缓存 —— 先到「PS脚本库」动作里刷新一次。";
        renderRail(); renderList(); RenderPreview(); refreshFoot();
    };

    search.TextChanged += (s, e) => { renderRail(); renderList(); };
    w.KeyDown += (s, e) => { if (e.Key == Key.Escape) { try { w.Close(); } catch { } } else if (e.Key == Key.Enter) { try { Accept(); } catch (Exception ex) { LogFail("PickFromLib.enter", ex); } } };
    w.Loaded += (_, __) => { try { search.Focus(); } catch { } };

    renderAll();
    // 内容顶部一条 1px 线：让"画布"有明确的上边界（贴着系统标题栏时不显得飘）
    var shell = new Border { BorderBrush = line, BorderThickness = new Thickness(0, 1, 0, 0), Child = root };
    // ---- 调试出图：`shot=pick;sel=0` 渲染成 PNG 不弹窗 ----
    // 风险徽章只在"选中某条"时才出现，菜单那套出图看不到它，所以这个窗也得能单独渲染。
    // 必须赶在 w.Content = shell 之前走：shell 一旦挂到 Window 上，再 Add 进下面那个垫底 Grid
    // 就撞"一个元素只能有一个逻辑父级"（编译期不报、运行期才抛）。
    if (!string.IsNullOrEmpty(shot))
    {
        try
        {
            string selS = ShotField(shot, "sel");
            int si4;
            if (!string.IsNullOrEmpty(selS) && int.TryParse(selS, out si4) && si4 >= 0 && si4 < shownKeys.Count)
            {
                curKey = shownKeys[si4];
                RenderPreview(); refreshFoot();
            }
            // fake=1：凭空造两条"菜单里已有"的记录，把「已在菜单 / 菜单里是旧版」两个徽章逼出来。
            // 只在出图模式下走——不这样这两个徽章没法离线验收（它们的真实来源是用户的菜单配置）。
            if (ShotField(shot, "fake") == "1" && shownKeys.Count > 3)
            {
                haveHashes = new Dictionary<string, string>();
                string fk0 = shownKeys[2], fk1 = shownKeys[3], fh0 = null;
                NetScripts.Hashes.TryGetValue(fk0, out fh0);
                haveHashes[CnOf(fk0)] = fh0;              // 指纹一致 → 已在菜单
                haveHashes[CnOf(fk1)] = "fake-old-hash";  // 指纹不同 → 菜单里是旧版
                renderList(); RenderPreview();
            }
            // 出图垫一层不透明画布底：根 Grid 本身不刷背景，直出会是透明底，留白和描边都看不清
            var bgp = new Grid { Background = Tk.B(Tk.Canvas) };
            bgp.Children.Add(shell);
            double swP = w.Width, shP = w.Height;
            bgp.Measure(new Size(swP, shP));
            bgp.Arrange(new Rect(new Point(0, 0), new Size(swP, shP)));
            bgp.UpdateLayout();
            int iw = (int)Math.Ceiling(swP), ih = (int)Math.Ceiling(shP);
            var rtb2 = new RenderTargetBitmap(iw, ih, 96, 96, PixelFormats.Pbgra32);
            rtb2.Render(bgp);
            var enc4 = new PngBitmapEncoder(); enc4.Frames.Add(BitmapFrame.Create(rtb2));
            string sdir4 = Path.Combine(Path.GetTempPath(), "ps_cm_shots"); Directory.CreateDirectory(sdir4);
            string fp4 = Path.Combine(sdir4, "pick_" + DateTime.Now.ToString("HHmmssfff") + ".png");
            using (var fs4 = File.Create(fp4)) enc4.Save(fs4);
            PickShotOut = "SHOT " + fp4 + "  " + iw + "x" + ih;
            return res;
        }
        catch (Exception ex) { PickShotOut = "SHOT-ERR " + ex.Message; return res; }
    }
    w.Content = shell;
    if (owner != null) w.Owner = owner;
    try { w.Opacity = 0; } catch { }
    bool _popw = false;
    w.ContentRendered += (_, __) => { if (_popw) return; _popw = true;
        try {
            var scw = new ScaleTransform(0.98, 0.98); w.RenderTransform = scw; w.RenderTransformOrigin = new Point(0.5, 0.5);
            var eaw = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            scw.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1d, TimeSpan.FromMilliseconds(160)) { EasingFunction = eaw });
            scw.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, 1d, TimeSpan.FromMilliseconds(160)) { EasingFunction = eaw });
            w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(130)));
        } catch { try { w.Opacity = 1; } catch { } } };
    var popGw = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
    popGw.Tick += (s2, e2) => { popGw.Stop(); try { if (w.Opacity < 0.05) { w.Opacity = 1; w.RenderTransform = null; } } catch { } };
    popGw.Start();
    w.ShowDialog();
    return res;
}


// ---------- 脚本库入口（取数归「PS脚本库」动作，这里只负责跳过去 / 没装就带去安装） ----------
const string LibActionId = "00224c3e-1573-4eb2-bf10-435230df9ce1";
const string LibShareUrl = "https://getquicker.net/Sharedaction?code=96c17a47-39e5-44de-0285-08df0ed109e3";

// 启动「PS脚本库」。**立即返回、绝不在 UI 线程上等子进程**（r58 实测：WaitForExit 会把界面卡住 2.5 秒，
// 而且重定向输出后不读流还会把子进程堵死）。
// 判据放在后台线程里慢慢等：进程一直没退出 = 它开着窗口 = 已安装；早早退出且回"未找到" = 没装 → 带你去分享页。
// 结果通过 done(installed) 回调，调用方负责切回 UI 线程。
static void LaunchScriptLib(Action<bool> done)
{
    try
    {
        // 注意：QuickerStarterPath() 是嵌套类 ActionExecutor 的成员，顶层直接用会 CS0103，必须限定
        string exe = ActionExecutor.QuickerStarterPath();
        if (!string.IsNullOrEmpty(exe))
        {
            var psi = new ProcessStartInfo(exe, "-c8 " + "\"runaction:" + LibActionId + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            var buf = new StringBuilder();
            var p = new Process { StartInfo = psi };
            // 两个流都要**边收边排空**：只在最后 ReadToEnd 会一直等到进程退出，而"窗口开着"时它压根不退出；
            // 更糟的是管道写满会把子进程堵死（r59 踩过）。
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (buf) buf.Append(e.Data).Append(' '); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (buf) buf.Append(e.Data).Append(' '); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                bool installed = true;
                try
                {
                    if (p.WaitForExit(2500))
                    {
                        string o; lock (buf) o = buf.ToString();
                        // 判据是**输出文本**，不是"进程有没有退出"：动作装了也可能秒退（转交给已在运行的 Quicker 实例），
                        // 用"没退出=装了"去猜就会误判成没装 → 平白弹出网页（用户实测踩到）。
                        // 实测：装了回 "OK"；没装回 "未能找到动作 / 未能成功运行动作"。
                        installed = o.IndexOf("未能找到", StringComparison.Ordinal) < 0
                                 && o.IndexOf("未能成功运行", StringComparison.Ordinal) < 0;
                    }
                }
                catch { }
                if (done != null) done(installed);
            });
            return;
        }
    }
    catch (Exception ex) { LogFail("LaunchScriptLib.run", ex); }
    // 一律**不自动开浏览器**：找不到动作时只把结果告诉调用方，由界面给一个"打开分享页"的手动入口。
    if (done != null) done(false);
}

// 打开「PS脚本库」的分享页（只在用户主动点的时候调；别在启动流程里自动弹）
static void OpenLibSharePage()
{
    try { Process.Start(new ProcessStartInfo(LibShareUrl) { UseShellExecute = true }); }
    catch (Exception ex) { LogFail("OpenLibSharePage", ex); }
}

// 盯着脚本库缓存文件：它被刷新（时间戳变了）就在 UI 线程回调一次，最多等 maxMs。
// 用于"启动 B 之后原地等结果"——省掉"去那边点刷新、回来关掉重开"的两步。
static void WatchCacheChange(DateTime t0, int maxMs, Window win, Action onChanged)
{
    string cp = NetScripts.CachePath();
    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
    {
        int loops = Math.Max(1, maxMs / 500);
        for (int i = 0; i < loops; i++)
        {
            System.Threading.Thread.Sleep(500);
            try
            {
                if (File.Exists(cp) && File.GetLastWriteTime(cp) != t0)
                {
                    if (win != null && onChanged != null) win.Dispatcher.Invoke(onChanged);
                    return;
                }
            }
            catch { }
        }
    });
}

}
}
