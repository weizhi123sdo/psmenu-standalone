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
// #region Settings Editor — Left Nav + Right Content
static string ShowConfigEditor(AC cfg, string shot = null)
{
    UiCfg = cfg;
    bool isDirty = false; bool closingFromCode = false;
    TextBlock dirtyLbl = null; Grid contentArea = null; TextBox searchBox = null; TextBlock themeLbl = null;
    LayerKind selLK = LayerKind.Pixel; StackPanel itemsList = null; StackPanel pinList = null;
    HashSet<string> expandedSubs = new HashSet<string>();
    string selUK = null; Border selRow = null; string revealSubUK = null;   // 选中项 / 选中行 / 搜索跳转待高亮子项
    Dictionary<LayerKind, HashSet<int>> multiSel = new Dictionary<LayerKind, HashSet<int>>();   // 各类型下多选的顶层索引
    // ===== 拖放状态与辅助（提升到外层，避免嵌套局部函数捕获歧义） =====
    Dictionary<LayerKind, Border> ltBtns = null;
    // 跨类型搜索的 chip 要切类型，但 SelectLK 声明在列表渲染函数之后——
    // C# 局部函数按"声明点"做确定赋值检查，直接用会报 CS0165。这里用一层外层引用绕开。
    Action<LayerKind> GoLK = null;
    ScrollViewer itemsSV = null;
    bool _dArm = false, _dOn = false;
    int _dSrc = -1; Point _dPt = default;
    Window _ghost = null; TextBlock _gcap = null;
    Border _gline = null; int _glineChild = -1; int _gapCur = 0;
    LayerKind _ltHover = LayerKind.None; Border _ltHoverRow = null;
    List<(int itIdx, FrameworkElement el)> _geom = new List<(int, FrameworkElement)>();

    // 子项拖动重排：只在同一父项内排序，不跨父项、不跨左栏类型（与顶层拖拽共用 itemsList，但状态独立）
    bool _sdArm = false, _sdOn = false; int _sdFrom = -1; Point _sdPt = default;
    MI _sdParent = null; Border _sdLine = null; List<(int idx, Border el)> _sdSibs = null;
    Action SdRemoveInd = () =>
    {
        try { if (_sdLine != null && itemsList.Children.Contains(_sdLine)) itemsList.Children.Remove(_sdLine); } catch { }
        _sdLine = null;
    };
    Func<MI, List<(int idx, Border el)>> SdSibsOf = par =>
    {
        var res = new List<(int, Border)>();
        if (par == null || par.Sub == null) return res;
        foreach (var ch in itemsList.Children)
        {
            var bb = ch as Border;
            if (bb == null || ReferenceEquals(bb, _sdLine)) continue;
            var mm = bb.Tag as MI;
            if (mm == null || mm.IsSep) continue;
            int ix = par.Sub.IndexOf(mm);
            if (ix >= 0) res.Add((ix, bb));
        }
        return res;
    };
    Func<double, int> SdSlot = cy =>
    {
        int k = 0;
        if (_sdSibs != null) foreach (var sx in _sdSibs)
        {
            double mid = sx.el.TranslatePoint(new Point(0, 0), itemsList).Y + sx.el.ActualHeight / 2;
            if (cy > mid) k++;
        }
        return k;
    };
    Action<double> SdMoveInd = cy =>
    {
        SdRemoveInd();
        if (_sdSibs == null || _sdSibs.Count == 0) return;
        int k = SdSlot(cy);
        int at = (k < _sdSibs.Count) ? itemsList.Children.IndexOf(_sdSibs[k].el) : itemsList.Children.IndexOf(_sdSibs[_sdSibs.Count - 1].el) + 1;
        if (at < 0) return;
        _sdLine = new Border { Height = 2, Background = new SolidColorBrush(PHex("#00A8BE", Colors.Teal)), CornerRadius = new CornerRadius(1), Margin = new Thickness(40, 1, 12, 1) };
        itemsList.Children.Insert(Math.Min(at, itemsList.Children.Count), _sdLine);
    };
    string _focusUK = null; Border _focusRow = null;
    Action RemoveInd = () =>
    {
        if (_gline != null && itemsList.Children.Contains(_gline)) itemsList.Children.Remove(_gline);
        _gline = null; _glineChild = -1;
    };
    Func<int, int> ChildIdxOf = gi => Math.Max(0, itemsList.Children.IndexOf(_geom[gi].el));
    Func<int, double> TopOf = gi => _geom[gi].el.TranslatePoint(new Point(0, 0), itemsList).Y;
    Action<int> MoveInd = gap =>
    {
        RemoveInd();
        if (_geom.Count == 0) return;
        int childIdx;
        if (gap <= 0) childIdx = ChildIdxOf(0);
        else if (gap >= _geom.Count) { var la = _geom[_geom.Count - 1].el; childIdx = itemsList.Children.IndexOf(la) + 1; }
        else childIdx = ChildIdxOf(gap);
        _gline = new Border { Height = 2, Background = new SolidColorBrush(PHex("#00A8BE", Colors.Teal)), CornerRadius = new CornerRadius(1), Margin = new Thickness(6, 0, 6, 0) };
        itemsList.Children.Insert(Math.Min(childIdx, itemsList.Children.Count), _gline);
        _glineChild = childIdx;
    };
    Func<double, int> GapFor = cy =>
    {
        int n = _geom.Count; if (n == 0) return 0;
        double best = double.MaxValue; int bi = 0;
        for (int g = 0; g <= n; g++)
        {
            double a;
            if (g == 0) a = TopOf(0) - 1;
            else if (g == n) { var la = _geom[n - 1].el; a = la.TranslatePoint(new Point(0, 0), itemsList).Y + la.ActualHeight; }
            else a = TopOf(g);
            double d = Math.Abs(cy - a);
            if (d < best) { best = d; bi = g; }
        }
        return bi;
    };
    Action<MouseEventArgs> AutoScroll = me =>
    {
        try
        {
            var p = me.GetPosition(itemsSV);
            if (p.Y < 28) itemsSV.ScrollToVerticalOffset(Math.Max(0, itemsSV.VerticalOffset - 20));
            else if (p.Y > itemsSV.ViewportHeight - 28) itemsSV.ScrollToVerticalOffset(itemsSV.VerticalOffset + 20);
        }
        catch { }
    };
    Action ResetHoverLt = () =>
    {
        if (_ltHoverRow != null)
        {
            bool act = ReferenceEquals(_ltHoverRow, ltBtns[selLK]);
            _ltHoverRow.BorderBrush = act ? new SolidColorBrush(LTM.C(selLK)) : Brushes.Transparent;
            _ltHoverRow.BorderThickness = act ? new Thickness(2, 0, 0, 0) : new Thickness(0);
        }
        _ltHover = LayerKind.None; _ltHoverRow = null;
    };
    Action<LayerKind> SetHoverLt = k =>
    {
        if (k == _ltHover && _ltHoverRow != null) return;
        ResetHoverLt();
        if (k == LayerKind.None) return;
        if (ltBtns.TryGetValue(k, out var rb))
        {
            rb.BorderBrush = new SolidColorBrush(PHex("#00A8BE", Colors.Teal));
            rb.BorderThickness = new Thickness(2);
            _ltHover = k; _ltHoverRow = rb;
        }
    };
    Func<Point> CursorScreen = () => { var cp = System.Windows.Forms.Cursor.Position; return new Point(cp.X, cp.Y); };
    Func<Point, bool> InsideList = scr =>
    {
        var tl = itemsSV.PointToScreen(new Point(0, 0));
        return scr.X >= tl.X && scr.X <= tl.X + itemsSV.ActualWidth && scr.Y >= tl.Y && scr.Y <= tl.Y + itemsSV.ActualHeight;
    };
    Func<Point, LayerKind> LtAt = scr =>
    {
        foreach (var kv in ltBtns)
        {
            var tl = kv.Value.PointToScreen(new Point(0, 0));
            if (scr.X >= tl.X && scr.X <= tl.X + kv.Value.ActualWidth && scr.Y >= tl.Y && scr.Y <= tl.Y + kv.Value.ActualHeight) return kv.Key;
        }
        return LayerKind.None;
    };
    Action<MI> SpawnGhost = it =>
    {
        if (_ghost != null) { try { _ghost.Close(); } catch { } _ghost = null; }
        _ghost = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ShowActivated = false, IsHitTestVisible = false, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize };
        var cap = new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(235, 40, 42, 50)), BorderBrush = new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue)), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6) };
        var gr = new StackPanel { Orientation = Orientation.Horizontal };
        gr.Children.Add(IconR.Render(it.Icon, it.Action, 13, cfg.IC));
        gr.Children.Add(new TextBlock { Text = " " + it.Title, FontSize = Tk.FBody, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        _gcap = new TextBlock { FontSize = Tk.FMicro, Foreground = new SolidColorBrush(Color.FromArgb(255, 150, 205, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        gr.Children.Add(_gcap); cap.Child = gr;
        cap.RenderTransformOrigin = new Point(0.5, 0.5);
        try { cap.RenderTransform = new RotateTransform(-1.6); } catch { }
        _ghost.Content = cap;
        var sc = CursorScreen(); _ghost.Left = sc.X + 10; _ghost.Top = sc.Y + 14;
        _ghost.Show();
    };

    Dictionary<LayerKind, TextBlock> cntLbls = new Dictionary<LayerKind, TextBlock>();          // 左侧类型数量徽标
    Action fillPinCombo = null; Border previewPanel = null;
    List<MI> clipB = new List<MI>();
    int rebuildGen = 0;
    Action rebuildItemsList = null;
    Action rebuildQuickBar = null;
    Action<Key, ModifierKeys> layerShortcutHandler = null;   // 图层菜单页快捷键（页面赋值）
    Stack<string> uStack = new Stack<string>(); Stack<string> rStack = new Stack<string>();   // 撤销/重做栈
    SolidColorBrush pinStripBg = null, pinStripBd = null, pinStripFg = null;
    Border pinStripHost = null; WrapPanel pinStripPanel = null;
    string pinStripShot = null;   // 出图专用：`pins=N` 把置顶项在本地副本里复制成 N 个，压测"置顶很多"时的排布

    // ---------- 盒子化外壳 v3（浅色卡片风；文字字号三档只缩放文字，外壳不动；边缘可缩放） ----------
    Th.IsLight = true;                       // 编辑器固定浅色外壳；退出时恢复菜单主题
    const double BASE_W = 920, BASE_H = 680;
    double fz = 1.0;                          // 文字缩放系数
    Border edHost = null;                           // 内容宿主（导航切页时按字号档应用）
    var FS_SKIP = new object();               // 标记“不随字号缩放”的子树（外观实时预览用真实菜单字号）
    var ed = new Window { Title = "PS Context Menu — 设置", Width = BASE_W, Height = BASE_H, MinWidth = 700, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.CanResize, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent };
    // 圆角外壳要求 AllowsTransparency=true，而透明窗口里 WPF 只能用灰度抗锯齿；
    // 叠加默认的 TextFormattingMode=Ideal，小字号笔画就会发虚、看着"细"。
    // 改成 Display（按像素对齐 hinting）是比"加粗"更根治的做法。
    TextOptions.SetTextFormattingMode(ed, TextFormattingMode.Display);
    // 色板全部来自 Tk（单一来源）。变量名保持不变，下面上千行页面代码一行都不用动。
    // 注：这些是冻结画刷，只能赋值不能做颜色动画——编辑器里确实没有对画刷做动画。
    var bgBrush = Tk.B(Tk.Canvas);       // 画布浅灰
    var cardBrush = Tk.B(Tk.Surface);    // 卡片白
    var lineBrush = Tk.B(Tk.BorderDefault);   // 分隔浅线
    var navActBg = Tk.B(Tk.AccentSoft);
    var navActFg = Tk.B(Tk.Accent);
    var txtMain = Tk.B(Tk.Ink);
    var txtSub = Tk.B(Tk.InkMuted);

    var root = new Grid();
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    root.RowDefinitions.Add(new RowDefinition());
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    // 顶栏：白卡片，只放 app 名（整条都是拖拽区）。ⓘ 帮助与 ✕ 关闭**移到底部条**：
    // 顶栏只有"这是哪个窗口"一件事要说，动作类按钮集中在底部条上，键盘/鼠标都只走一条路径。
    var appTitle = new TextBlock { Text = "PS Context Menu  ·  设置", FontSize = Tk.FNav, FontWeight = FontWeights.SemiBold, Foreground = txtMain, VerticalAlignment = VerticalAlignment.Center };
    var tbar = new Grid();
    tbar.ColumnDefinitions.Add(new ColumnDefinition());
    tbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    tbar.Children.Add(appTitle);
    // 关闭按钮留在右上角（用户习惯位置）；底部的 ⓘ 使用帮助才是"动作按钮"
    var closeBtn = new Button { Content = "✕", FontSize = Tk.FNav, Foreground = txtSub, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Width = 34, Height = 26, Cursor = Cursors.Hand, ToolTip = "关闭（有未保存改动会先问）" };
    closeBtn.Click += (_, __) => TryClose();
    tbar.Children.Add(closeBtn); Grid.SetColumn(closeBtn, 1);
    // 帮助按钮：放在底部条、做成能一眼看到的按钮（原来是一个几乎看不见的 ⓘ）
    var helpBtn = new Button
    {
        Content = "ⓘ 使用帮助", FontSize = Tk.FSmall, FontWeight = FontWeights.Medium,
        Foreground = navActFg, Background = navActBg,
        BorderBrush = Tk.B(Tk.BorderDefault), BorderThickness = new Thickness(1),
        Padding = new Thickness(10, 3, 10, 3), Height = 26, Cursor = Cursors.Hand,
        ToolTip = "说明 / 更新日志 / 快捷键"
    };
    helpBtn.Click += (_, __) => ShowHelp();
    helpBtn.Click += (_, __) => { try { ShowInfo("PS Context Menu · 使用帮助", "版本：" + ActionVersion + "\n\n【更新日志】\n" + ActionLog + "\n\n【常用操作】\nPS 菜单：左键=执行/进入子层；悬停自动进子层；右键叶子项=直接编辑；Ctrl+右键=置顶；直接打字搜索（只显示命中的项，顶部有回显；支持汉字、英文、拼音首字母），↑↓ 选择、Enter 执行、Esc 清输入/退层/关闭；呼出后默认高亮常用项\n编辑器：单击=选中；Ctrl+单击=复制到批量；拖动=排序/拖到左栏类型（Ctrl=复制、Shift=置顶）；Ctrl+A 全选、Ctrl+Z/Y 撤销重做、Ctrl+N 新建、Ctrl+D 复制项、Ctrl+F 跨类型搜索\n置顶：按住拖动排序；右键=取消置顶（需确认）"); } catch { } };
    var tbCard = new Border { CornerRadius = new CornerRadius(12, 12, 0, 0), Background = cardBrush, Child = tbar, Padding = new Thickness(16, 2, 8, 2) };
    tbCard.MouseLeftButtonDown += (_, e) => { try { ed.DragMove(); } catch { } };
    root.Children.Add(tbCard);

    // 主体：浅灰画布上左右卡片
    var bodyHost = new Border { CornerRadius = new CornerRadius(12), Background = bgBrush, Padding = new Thickness(12), Margin = new Thickness(14, 10, 14, 8) };
    var canvasGrid = new Grid();
    canvasGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
    canvasGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
    canvasGrid.ColumnDefinitions.Add(new ColumnDefinition());
    bodyHost.Child = canvasGrid;

    // 左：导航卡
    var navStack = new StackPanel();
    var navCard = new Border { CornerRadius = new CornerRadius(Tk.RLg), Background = cardBrush, Padding = new Thickness(Tk.S6) };
    // 注意：括号里那个编号**必须等于它在数组里的位置**（0/1/2）——
    // 点击绑定传的是循环下标 i（见下面 `int ni = i;`），选中高亮比的也是位置。
    // 两者不一致就会出现"点A开了B"的错位。
    var navItems = new[] { ("📂 图层菜单", 0), ("🎨 外观主题", 1), ("⚙ 配置管理", 2), ("🔌 环境检查", 3), ("🎮 启动与触发", 4) };
    var navBtns = new List<Border>();
    int activeNav = 0;
    contentArea = new Grid();

    void SwitchNav(int idx)
    {
        activeNav = idx;
        for (int j = 0; j < navBtns.Count; j++)
        {
            bool act = j == idx;
            navBtns[j].Background = act ? navActBg : Brushes.Transparent;
            var tb = (TextBlock)navBtns[j].Child;
            tb.Foreground = act ? navActFg : txtSub;
            tb.FontWeight = act ? FontWeights.SemiBold : FontWeights.Normal;
        }
        contentArea.Children.Clear();
        switch (idx)
        {
            case 0: BuildLayerMenuPage(); break;
            case 1: BuildAppearancePage(); break;
            case 2: BuildConfigPage(); break;
            case 3: BuildEnvPage(); break;
            case 4: BuildTriggerPage(); break;
        }
        ScaleText(edHost, fz);   // 新页面按当前字号档位应用
    }

    for (int i = 0; i < navItems.Length; i++)
    {
        int ni = i;
        // 同心圆角：navCard 是 12、内边距 6，所以里面的按钮取 12-6=6；取 8 会让内角比外角"鼓"
    var nb = new Border { Cursor = Cursors.Hand, CornerRadius = new CornerRadius(Tk.RSm), Padding = new Thickness(Tk.S12, Tk.S8, Tk.S12, Tk.S8), Background = Brushes.Transparent };
        var ntxt = new TextBlock { Text = navItems[i].Item1, FontSize = Tk.FNav, Foreground = txtSub, VerticalAlignment = VerticalAlignment.Center };
        nb.Child = ntxt;
        nb.MouseEnter += (_, __) => { if (activeNav != ni) nb.Background = new SolidColorBrush(PHex("#F4F6F8", Colors.LightGray)); };
        nb.MouseLeave += (_, __) => { if (activeNav != ni) nb.Background = Brushes.Transparent; };
        nb.MouseLeftButtonUp += (_, __) => SwitchNav(ni);
        navBtns.Add(nb);
        navStack.Children.Add(nb);
    }
    navCard.Child = navStack;
    canvasGrid.Children.Add(navCard);

    // 右：内容卡
    var contentCard = new Border { CornerRadius = new CornerRadius(Tk.RLg), Background = cardBrush, Padding = new Thickness(0, Tk.S8, 0, 0) };
    contentCard.Child = contentArea;
    canvasGrid.Children.Add(contentCard); Grid.SetColumn(contentCard, 2);
    root.Children.Add(bodyHost); Grid.SetRow(bodyHost, 1);

    // 底部条：左=界面字号（Aa 三级，仅文字）；右=保存状态+快捷键
    var footer = new Border { Tag = "fs,,36",  CornerRadius = new CornerRadius(0, 0, Tk.RLg, Tk.RLg), Background = cardBrush, Padding = new Thickness(Tk.S16, Tk.S8, Tk.S16, Tk.S8) };
    var ftGrid = new Grid();
    ftGrid.ColumnDefinitions.Add(new ColumnDefinition());
    ftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    var fsLbl = new TextBlock { Text = "编辑器字号", FontSize = Tk.FSmall, Foreground = txtSub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = "只调编辑器这个窗口的文字大小；菜单自己的字号在外观页用「缩放比例」调" };
    var fsz = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    fsz.Children.Add(fsLbl);
    var fsBtns = new List<Border>();
    // 三档系数整体上移：原来 0.9/1.0/1.12 偏小，用户落在「小」档时会把字号的提升抵消掉。
    // 现在「小」= 我设定的基准（等价于旧版的 1.0+ 一档基数），中/大再各加约 12%。
    var fsLv = new[] { ("小", 1.0), ("中", 1.12), ("大", 1.25) };
    for (int fi = 0; fi < fsLv.Length; fi++)
    {
        int fii = fi; var fsop = fsLv[fi];
        var fb = new Border { Cursor = Cursors.Hand, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(2, 0, 2, 0), Background = Brushes.Transparent, ToolTip = "界面字号：" + fsop.Item1 };
        var fbT = new TextBlock { Text = "Aa", FontSize = (fii == 0 ? 11 : (fii == 1 ? 14 : 17)), Foreground = txtSub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, -2) };
        fb.Child = fbT;
        fb.MouseLeftButtonUp += (_, __) => ApplyFontScale(fsop.Item2, fii);
        fsBtns.Add(fb); fsz.Children.Add(fb);
    }
    fsz.Children.Add(new TextBlock { Text = "仅文字大小", FontSize = Tk.FMicro, Foreground = txtSub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
    ftGrid.Children.Add(fsz);
    var fst = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    // 预留宽度：这行字出现/消失时不能让右边的操作提示跟着左右移动（瞬时文字改变布局 = 观感缺陷）
    dirtyLbl = new TextBlock { Text = "", FontSize = Tk.FSmall, Foreground = navActFg, VerticalAlignment = VerticalAlignment.Center, MinWidth = 104, TextTrimming = TextTrimming.CharacterEllipsis };
    var hintLbl = new TextBlock { Text = "   拖动边缘可调大小 · Ctrl+S 保存 · Esc 关闭", FontSize = Tk.FSmall, Foreground = txtSub, VerticalAlignment = VerticalAlignment.Center };
    themeLbl = new TextBlock { Text = "", FontSize = Tk.FSmall, Foreground = txtSub, VerticalAlignment = VerticalAlignment.Center };
    fst.Children.Add(dirtyLbl); fst.Children.Add(hintLbl); fst.Children.Add(new TextBlock { Text = "   ", FontSize = Tk.FSmall }); fst.Children.Add(themeLbl);
    // 动作按钮统一放右端：先 ⓘ（说明）后 ✕（关闭），与"最右＝离开"的惯例一致
    fst.Children.Add(new Border { Width = 1, Height = 16, Background = lineBrush, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
    fst.Children.Add(helpBtn);
    ftGrid.Children.Add(fst); Grid.SetColumn(fst, 1);
    footer.Child = ftGrid;
    root.Children.Add(footer); Grid.SetRow(footer, 2);

    edHost = new Border { CornerRadius = new CornerRadius(Tk.RLg), Background = bgBrush, Child = root };
    ed.Content = edHost;
    try { var _ts2 = ThinScrollStyle(); if (_ts2 != null) ed.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = _ts2; } catch { }

    // ---- 字号：仅缩放文字（外壳不动）。首次记录每个元素原始字号存于 Tag，之后按系数缩放 ----
    void ApplyFontScale(double s, int idx)
    {
        fz = s;
        if (cfg.FsTier != idx) { cfg.FsTier = idx; try { ConfigService.SaveTier(idx); } catch { } }   // 记住档位，下次打开沿用
        for (int j = 0; j < fsBtns.Count; j++)
        {
            bool act = j == idx;
            fsBtns[j].Background = act ? navActBg : Brushes.Transparent;
            ((TextBlock)fsBtns[j].Child).Foreground = act ? navActFg : txtSub;
        }
        ScaleText(edHost, fz);
    }
    void ScaleText(DependencyObject d, double f)
    {
        int n = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(d, i) as DependencyObject;
            if (c == null) continue;
            if (c is FrameworkElement fw0 && ReferenceEquals(fw0.Tag, FS_SKIP)) continue;   // 跳过外观预览子树
            if (c is TextBlock || c is Control)
            {
                var fe2 = c as FrameworkElement;
                double bf;
                if (fe2.Tag is double d0) bf = d0;
                else { bf = (fe2 is TextBlock tb3) ? tb3.FontSize : ((Control)fe2).FontSize; fe2.Tag = bf; }
                if (fe2 is TextBlock tb4) tb4.FontSize = Math.Max(8, bf * f);
                else ((Control)fe2).FontSize = Math.Max(8, bf * f);
                // r64：**光放大字号不加宽高就会互相挤**——行高是写死的（36/40 之类），字大了就顶出格子，
                // 表现就是"被挡着/错位"。这里把显式的宽高也按同一系数缩放。
                // 基准存在元素自己的 Resources 里（不用字典）：局部字典会被更早创建的 lambda 捕获，
                // 触发 CS0165「使用了未赋值的局部变量」，这条路已经试过一次了。
                var gb = fe2.Resources["r64geoBase"] as double[];
                if (gb == null)
                {
                    gb = new[] { fe2.Height, fe2.MinHeight, fe2.Width, fe2.MaxWidth, fe2.MinWidth };
                    try { fe2.Resources["r64geoBase"] = gb; } catch { }
                }
                if (!double.IsNaN(gb[0])) fe2.Height = gb[0] * f;
                if (!double.IsNaN(gb[1])) fe2.MinHeight = gb[1] * f;
                if (!double.IsNaN(gb[2])) fe2.Width = gb[2] * f;
                if (!double.IsNaN(gb[3])) fe2.MaxWidth = gb[3] * f;
                if (!double.IsNaN(gb[4])) fe2.MinWidth = gb[4] * f;
            }
            if (c is FrameworkElement fw1 && ReferenceEquals(fw1.Tag, FS_SKIP)) continue;
            ScaleText(c, f);
        }
    }
    // 重建过的子树是"按原始尺寸新建"的：如果不再缩一次，页面上就会出现两种字号混排（看着就是错位）。
    void RescaleAll() { try { ScaleText(edHost, fz); } catch (Exception ex) { LogFail("RescaleAll", ex); } }
    // 无边框窗口：开启原生边缘/四角拖拽缩放
    ed.SourceInitialized += (_, __) => { try { ResizableWindow.Enable(ed); } catch { } };

    void MkDirty() { isDirty = true; dirtyLbl.Text = "● 有未保存更改"; UpdateThemeLbl(); }
    void MkClean() { isDirty = false; dirtyLbl.Text = ""; ConfigService.Snapshot("editor-save"); ConfigService.Save(cfg); UpdateThemeLbl(); }
    void UpdateThemeLbl()
    {
        if (themeLbl == null) return;
        string cn = "";
        foreach (var tt in ThemeNames) if (tt.id == cfg.Theme) { cn = tt.cn; break; }
        themeLbl.Text = "主题：" + (string.IsNullOrEmpty(cn) ? (cfg.Theme ?? "") : cn) + " · v" + ActionVersion;
    }
    UpdateThemeLbl();

    void PushUndo() { try { uStack.Push(AppConfigSerializer.ToJson(cfg)); if (uStack.Count > 60) { var tmp = uStack.ToArray(); Array.Reverse(tmp); uStack = new Stack<string>(tmp.Take(60).Reverse()); } } catch { } rStack.Clear(); }
    void CopyCfgFrom(AC s) { if (s == null) return; cfg.Menus = s.Menus ?? new List<LM>(); cfg.Pinned = s.Pinned ?? new List<PI>(); cfg.MW = s.MW; cfg.FS = s.FS; cfg.Zoom = s.Zoom; cfg.PS = s.PS; cfg.IS = s.IS; cfg.Op = s.Op; cfg.BC = s.BC ?? "#2D2D2D"; cfg.FC = s.FC ?? "#CCCCCC"; cfg.HC = s.HC ?? "#094771"; cfg.OC = s.OC ?? "#3E3E3E"; cfg.IC = s.IC ?? ""; cfg.FreqSort = s.FreqSort; cfg.ShowFreq = s.ShowFreq; cfg.ShowPinName = s.ShowPinName; cfg.HoverSub = s.HoverSub; cfg.HoverMs = s.HoverMs; cfg.GlowK = s.GlowK; cfg.SheenK = s.SheenK; cfg.CR = s.CR; cfg.BW = s.BW; cfg.UnderK = s.UnderK; cfg.UnderH = s.UnderH; cfg.Theme = s.Theme ?? "psdark"; }
    void DoUndo() { if (uStack.Count == 0) return; rStack.Push(AppConfigSerializer.ToJson(cfg)); var c = AppConfigSerializer.FromJson(uStack.Pop()); CopyCfgFrom(c); MkDirty(); RebuildCur(); }
    void DoRedo() { if (rStack.Count == 0) return; uStack.Push(AppConfigSerializer.ToJson(cfg)); var c = AppConfigSerializer.FromJson(rStack.Pop()); CopyCfgFrom(c); MkDirty(); RebuildCur(); }
        void RebuildCur() { contentArea.Children.Clear(); switch (activeNav) { case 0: BuildLayerMenuPage(); break; case 1: BuildAppearancePage(); break; case 2: BuildConfigPage(); break; case 3: BuildEnvPage(); break; case 4: BuildTriggerPage(); break; } RescaleAll(); }
    void TryClose() { if (isDirty) { int dr = Confirm("有未保存的更改", "关闭这个窗口前先保存吗？\n保存会写进配置，并自动留一份可回滚的快照。", new[] { "保存并关闭", "不保存", "取消" }, 2, 0); if (dr == 0) MkClean(); else if (dr != 1) return; } closingFromCode = true; ed.Close(); }

    // ========== Page 0: Layer Menu ==========
    void BuildLayerMenuPage()
    {
        TextBlock lhTitle = null;
        Border batchHost = null; StackPanel batchPanel = null; ComboBox batchTgt = null;
        void MarkSel(Border b, string uk) { if (selRow != null && !ReferenceEquals(selRow, b)) { selRow.BorderBrush = Brushes.Transparent; selRow.BorderThickness = new Thickness(0); } selUK = uk; selRow = b; b.BorderBrush = new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue)); b.BorderThickness = new Thickness(1.4); }

        // ---- 多选批量：状态与操作 ----
        HashSet<int> MSFor(LayerKind k) { if (!multiSel.TryGetValue(k, out var s)) { s = new HashSet<int>(); multiSel[k] = s; } return s; }
        void RefreshCountBadges()
        {
            foreach (var kv in cntLbls)
            {
                var mm = cfg.Menus.FirstOrDefault(x => x.Kind == kv.Key);
                if (kv.Value != null) kv.Value.Text = (mm?.Items?.Count ?? 0).ToString();
            }
        }
        List<MI> SelectedItems()
        {
            var lm2 = cfg.Menus.FirstOrDefault(m => m.Kind == selLK);
            var sel = MSFor(selLK);
            if (lm2 == null) return new List<MI>();
            return lm2.Items.Where((x, i) => !x.IsSep && sel.Contains(i)).ToList();
        }
        void BatchDelete()
        {
            var lm2 = cfg.Menus.FirstOrDefault(m => m.Kind == selLK); if (lm2 == null) return;
            var sel = MSFor(selLK);
            var idxs = sel.Where(i => i >= 0 && i < lm2.Items.Count && !lm2.Items[i].IsSep).OrderByDescending(i => i).ToList();
            if (idxs.Count == 0) return;
            PushUndo();
            foreach (var i in idxs) lm2.Items.RemoveAt(i);
            sel.Clear(); selUK = null; selRow = null;
            MkDirty(); RebuildItemsList(); RefreshCountBadges();
        }
        void BatchCopyClip()
        {
            var items = SelectedItems();
            if (items.Count == 0) return;
            clipB = items.Select(x => x.Clone()).ToList();
            QkToast("已复制 " + items.Count + " 项，切到目标类型后粘贴即可。");
        }
        void BatchMoveCopy(bool move)
        {
            var lm2 = cfg.Menus.FirstOrDefault(m => m.Kind == selLK); if (lm2 == null) return;
            var sel = MSFor(selLK);
            var idxs = sel.Where(i => i >= 0 && i < lm2.Items.Count && !lm2.Items[i].IsSep).OrderBy(i => i).ToList();
            if (idxs.Count == 0) return;
            var tgs = LTM.All.Where(t => t.K != LayerKind.None && t.K != selLK).ToList();
            int ti = batchTgt?.SelectedIndex ?? -1;
            if (ti < 0 || ti >= tgs.Count) return;
            var dst = cfg.Menus.FirstOrDefault(m => m.Kind == tgs[ti].K); if (dst == null) return;
            PushUndo();
            foreach (var i in idxs) dst.Items.Add(lm2.Items[i].Clone());
            if (move) { foreach (var i in idxs.OrderByDescending(x => x)) lm2.Items.RemoveAt(i); sel.Clear(); selUK = null; selRow = null; }
            MkDirty(); RebuildItemsList(); RefreshCountBadges();
            QkToast((move ? "已移动 " : "已复制 ") + idxs.Count + " 项 → " + LTM.L(dst.Kind));
        }
        // 把批量选中的多项打包成一个新的父菜单（它们全部变为其子项）
        void BatchPackToParent()
        {
            var lm2 = cfg.Menus.FirstOrDefault(m => m.Kind == selLK); if (lm2 == null) return;
            var sel = MSFor(selLK);
            var idxs = sel.Where(i => i >= 0 && i < lm2.Items.Count && !lm2.Items[i].IsSep).OrderBy(i => i).ToList();
            if (idxs.Count == 0) return;
            PushUndo();
            var kids = idxs.Select(i => lm2.Items[i].Clone()).ToList();
            foreach (var i in idxs.OrderByDescending(x => x)) lm2.Items.RemoveAt(i);
            var parent = new MI { Title = "组合菜单", Icon = "fa:Solid_Folder", Action = ActionType.Keys, Value = "", Sub = kids };
            EditItem(parent);                       // 让用户命名；取消则沿用默认名
            int ins0 = Math.Min(idxs[0], lm2.Items.Count);
            lm2.Items.Insert(ins0, parent);
            sel.Clear(); selUK = null; selRow = null;
            _focusUK = parent.UK;
            MkDirty(); RebuildItemsList(); RefreshCountBadges();
        }
        void RefreshBatchBar()
        {
            if (batchPanel == null) return;
            batchPanel.Children.Clear();
            batchTgt = null;
            var sel = MSFor(selLK);
            var lmNow = cfg.Menus.FirstOrDefault(m => m.Kind == selLK);
            if (lmNow != null) sel.RemoveWhere(i => i < 0 || i >= lmNow.Items.Count || lmNow.Items[i].IsSep);
            if (sel.Count == 0) { batchHost.Visibility = Visibility.Collapsed; return; }
            batchHost.Visibility = Visibility.Visible;
            batchPanel.Children.Add(new TextBlock { Text = "已选 " + sel.Count + " 项", FontSize = Tk.FSmall, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            batchPanel.Children.Add(U.Btn("✕ 取消", Th.TS, () => { MSFor(selLK).Clear(); RebuildItemsList(); }, 11));
            batchPanel.Children.Add(new TextBlock { Text = " " });
            batchPanel.Children.Add(U.Btn("🗑 删除", Th.AR, () => BatchDelete(), 11));
            batchPanel.Children.Add(new TextBlock { Text = " " });
            batchPanel.Children.Add(U.Btn("📋 复制", Th.AG, () => BatchCopyClip(), 11));
            batchPanel.Children.Add(new TextBlock { Text = "  →  ", VerticalAlignment = VerticalAlignment.Center, Foreground = Th.BTS });
            var tgs = LTM.All.Where(t => t.K != LayerKind.None && t.K != selLK).ToList();
            batchTgt = new ComboBox { Width = 104, FontSize = Tk.FSmall, ItemsSource = tgs.Select(t => t.L).ToList(), SelectedIndex = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            batchPanel.Children.Add(batchTgt);
            batchPanel.Children.Add(U.Btn("⤵ 复制到", Th.AB, () => BatchMoveCopy(false), 11));
            batchPanel.Children.Add(new TextBlock { Text = " " });
            batchPanel.Children.Add(U.Btn("➜ 移动到", Th.AB, () => BatchMoveCopy(true), 11));
            batchPanel.Children.Add(new TextBlock { Text = " " });
            batchPanel.Children.Add(U.Btn("⇥ 打包为父项", Th.AG, () => BatchPackToParent(), 11));
        }
        void LayerShortcuts(Key k, ModifierKeys m)
        {
            var lm2 = cfg.Menus.FirstOrDefault(x => x.Kind == selLK);
            if (lm2 == null) return;
            int idx = selUK == null ? -1 : lm2.Items.FindIndex(x => x.UK == selUK);
            if (k == Key.Delete && m == ModifierKeys.None)
            { if (idx >= 0) { PushUndo(); lm2.Items.RemoveAt(idx); selUK = null; selRow = null; MSFor(selLK).Clear(); MkDirty(); RebuildItemsList(); RefreshCountBadges(); } }
            else if (k == Key.D && m == ModifierKeys.Control)
            {
                if (idx >= 0) { var src = lm2.Items[idx]; if (src.IsSep) return;
                    var cp = src.Clone(); cp.Title = src.Title + " 副本";
                    PushUndo(); lm2.Items.Insert(idx + 1, cp); selUK = cp.UK; _focusUK = cp.UK; MkDirty(); RebuildItemsList(); RefreshCountBadges(); }
            }
            else if (k == Key.N && m == ModifierKeys.Control)
            {
                var ni2 = new MI { Title = "新菜单项", Icon = "fa:Solid_Play", Action = ActionType.Keys, Value = "" };
                EditItem(ni2);
                if (!string.IsNullOrEmpty(ni2.Title)) { PushUndo(); lm2.Items.Add(ni2); selUK = ni2.UK; MkDirty(); RebuildItemsList(); RefreshCountBadges(); }
            }
            else if (k == Key.A && m == ModifierKeys.Control)
            {
                var selA = MSFor(selLK); selA.Clear();
                string q0 = (searchBox?.Text ?? "").Trim();
                for (int i = 0; i < lm2.Items.Count; i++)
                {
                    var x = lm2.Items[i];
                    if (x.IsSep) continue;
                    if (!string.IsNullOrEmpty(q0) && (x.Title ?? "").IndexOf(q0, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    selA.Add(i);
                }
                RebuildItemsList();
            }
        }
        layerShortcutHandler = LayerShortcuts;
        // 试运行：隐藏编辑器 → 执行选中项动作 → 提示 → 恢复编辑器
        void TestRunSelected()
        {
            var lm = cfg.Menus.FirstOrDefault(x => x.Kind == selLK);
            if (lm == null) return;
            MI FindIt(List<MI> items)
            {
                if (items == null) return null;
                foreach (var it in items)
                {
                    if (it == null || it.IsSep) continue;
                    if (it.UK == selUK) return it;
                    var f = it.HasSub ? FindIt(it.Sub) : null;
                    if (f != null) return f;
                }
                return null;
            }
            var it = selUK == null ? null : FindIt(lm.Items);
            if (it == null) { QkToast("请先在右侧列表里选中一个菜单项", "Warning"); return; }
            if (it.IsSep) { QkToast("分隔线没有可运行的动作", "Warning"); return; }

            ed.Hide();                        // 界面暂时消失
            string er;
            try { er = ActionExecutor.Exec(AT.ToStr(it.Action), it.Value, it.Title); }
            catch (Exception ex) { er = "Error: " + ex.Message; }

            bool ok = !(string.IsNullOrEmpty(er) || er.StartsWith("Error") || er == "PS not running" || er.StartsWith("Unknown"));
            if (ok)
            {
                string tip = it.Action == ActionType.Keys ? "已发送快捷键" :
                             it.Action == ActionType.Script ? "脚本已执行" :
                             it.Action == ActionType.Run ? "已启动程序" :
                             it.Action == ActionType.Clipboard ? "已写入剪贴板" : "已执行";
                QkToast(tip + "：" + it.Title, "Success");
            }
            else if (er != null && !er.StartsWith("Error") && er != "PS not running")
            {
                QkToast("执行未成功：" + er, "Warning");
            }
            // 失败时（Error / PS not running）Exec 内部已弹窗提示，这里不再重复
            ed.Show();                        // 恢复界面
            ed.Activate();
        }
        var page = new Grid();
        // ---- 置顶条：按住拖动实时排序（带滑动动画）；右键取消置顶（弹确认） ----
        // 原来是琥珀色通栏——它是整窗最响的元素，而琥珀在界面语言里读作"警告"。
        // 置顶是"结构/状态"，不是警告，改成中性底，语义交给 📌 图标去表。
        pinStripBg = Tk.B(Tk.SurfaceAlt);
        pinStripBd = Tk.B(Tk.BorderSubtle);
        pinStripFg = Tk.B(Tk.InkMuted);
        void CommitPinnedOrder(List<PI> ordered)
        {
            var set = new HashSet<PI>(ordered);
            int rr = 0; var nl2 = new List<PI>();
            foreach (var x in cfg.Pinned) { if (set.Contains(x)) nl2.Add(ordered[rr++]); else nl2.Add(x); }
            cfg.Pinned = nl2; MkDirty();
        }
        void RefreshPinStrip()
        {
            if (pinStripPanel == null || pinStripHost == null) return;
            var strip = pinStripPanel;
            strip.Children.Clear();
            var rel = cfg.Pinned.Where(p => p.LK == LayerKind.None || p.LK == selLK).ToList();
            // 出图压测：`pins=N` 把 rel 在**本地**复制成 N 个——只影响这一次渲染，绝不写回配置
            if (!string.IsNullOrEmpty(pinStripShot) && rel.Count > 0)
            {
                int pnS; var psS = ShotField(pinStripShot, "pins");
                if (!string.IsNullOrEmpty(psS) && int.TryParse(psS, out pnS) && pnS > rel.Count)
                {
                    var seedS = rel.ToList();
                    for (int iS = rel.Count; iS < pnS; iS++) rel.Add(seedS[iS % seedS.Count]);
                }
            }
            if (rel.Count == 0) { pinStripHost.Visibility = Visibility.Collapsed; return; }
            pinStripHost.Visibility = Visibility.Visible;
            for (int pi = 0; pi < rel.Count; pi++)
            {
                var p = rel[pi];
                // 置顶副本可能过期：按稳定键 K 找回当前项并回写（改标题/图标/值都跟着变），见 PinSource
                PinSource(cfg, p);
                bool moved = false, didSwap = false; double sx = 0;
                var b = new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(30, 138, 90, 0)), Cursor = Cursors.Hand, Padding = new Thickness(5), Margin = new Thickness(2, 1, 2, 1), ToolTip = "按住拖动排序 · 右键取消置顶：" + p.Title + (string.IsNullOrEmpty(p.Value) ? "" : "\n" + AT.ToStr(p.Action) + "：" + (p.Value.Length > 48 ? p.Value.Substring(0, 48) + "…" : p.Value)) };
                var inner = new StackPanel { Orientation = Orientation.Horizontal };
                inner.Children.Add(IconR.Render(p.Icon, p.Action, 13, cfg.IC));
                inner.Children.Add(new TextBlock { Text = " " + p.Title, FontSize = Tk.FSmall, Foreground = pinStripFg, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis });
                b.Child = inner;
                double LeftOf(Border e) { return e.TransformToAncestor(strip).TransformBounds(new Rect(e.RenderSize)).X; }
                void Slide(Border e, double dx) { if (Math.Abs(dx) < 0.5) return; var tr = e.RenderTransform as TranslateTransform ?? new TranslateTransform(); e.RenderTransform = tr; tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0d, TimeSpan.FromMilliseconds(Tk.MMicro))); }
                void SwapOne(int dir)
                {
                    int i = strip.Children.IndexOf(b);
                    int j = i + dir;
                    if (j < 0 || j >= strip.Children.Count) return;
                    var bj = (Border)strip.Children[j];
                    double a0 = LeftOf(b), c0 = LeftOf(bj);
                    strip.Children.Remove(b); strip.Children.Insert(j, b);
                    var pp = rel[i]; rel[i] = rel[j]; rel[j] = pp;
                    double a1 = LeftOf(b), c1 = LeftOf(bj);
                    Slide(b, a0 - a1); Slide(bj, c0 - c1);
                    didSwap = true;
                }
                b.MouseLeftButtonDown += (_, de) => { if (strip.Children.Count > 1) { b.CaptureMouse(); b.Opacity = 0.6; sx = de.GetPosition(strip).X; } };
                b.MouseMove += (_, me) =>
                {
                    if (!b.IsMouseCaptured || strip.Children.Count < 2) return;
                    double cx = me.GetPosition(strip).X;
                    if (!moved && Math.Abs(cx - sx) > 8) moved = true;
                    if (!moved) return;
                    int target = 0;
                    for (int k = 0; k < strip.Children.Count; k++)
                    {
                        var el = (Border)strip.Children[k];
                        if (ReferenceEquals(el, b)) continue;
                        var rc = el.TransformToAncestor(strip).TransformBounds(new Rect(el.RenderSize));
                        if (rc.Left + rc.Width / 2 < cx) target++;
                    }
                    int guard = 0;
                    while (strip.Children.Count > 1 && guard++ < strip.Children.Count)
                    {
                        int ii = strip.Children.IndexOf(b);
                        if (ii < target) SwapOne(1);
                        else if (ii > target) SwapOne(-1);
                        else break;
                    }
                };
                b.MouseLeftButtonUp += (_, __) =>
                {
                    bool cap = b.IsMouseCaptured;
                    if (cap) b.ReleaseMouseCapture();
                    b.Opacity = 1.0; b.RenderTransform = null;
                    if (cap && didSwap)
                    {
                        didSwap = false; moved = false;
                        PushUndo(); CommitPinnedOrder(rel);
                    }
                    RefreshPinStrip();
                };
                b.MouseRightButtonUp += (_, __) =>
                {
                    try
                    {
                        int dr = Confirm("取消置顶", "不再把「" + p.Title + "」放在置顶条上？\n（菜单项本身不会被删除）", new[] { "取消置顶", "保留" }, 1, 0, "#D14343");
                        if (dr == 0) { PushUndo(); cfg.Pinned.Remove(p); MkDirty(); RebuildItemsList(); RefreshPinStrip(); }
                    }
                    catch { }
                };
                strip.Children.Add(b);
            }
        }
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        page.ColumnDefinitions.Add(new ColumnDefinition());

        // Left: layer type list
        var ltPanel = new StackPanel();
        var ltHeader = new TextBlock { Text = "图层类型", FontSize = Tk.FSmall, FontWeight = FontWeights.SemiBold, Foreground = Th.BTS, Margin = new Thickness(12, 10, 12, 6) };
        ltPanel.Children.Add(ltHeader);

        var ltList = new StackPanel();
        ltBtns = new Dictionary<LayerKind, Border>();

        Action<LayerKind> SelectLK = k =>
        {
            selLK = k;
            foreach (var kv in ltBtns)
            {
                bool act = kv.Key == k;
                var c = LTM.C(kv.Key);
                kv.Value.Background = act ? new SolidColorBrush(c) { Opacity = 0.15 } : Brushes.Transparent;
                kv.Value.BorderBrush = act ? new SolidColorBrush(c) : Brushes.Transparent;
                kv.Value.BorderThickness = new Thickness(act ? 2 : 0, 0, 0, 0);
            }
            RebuildItemsList();
        };
        GoLK = SelectLK;

        foreach (var tm in LTM.All.Where(t => t.K != LayerKind.None))
        {
            var ck = tm.K;
            var cc = LTM.C(ck);
            LM ml = cfg.Menus.FirstOrDefault(m => m.Kind == ck);
            int cnt = ml?.Items?.Count ?? 0;

            var row = new Border { Cursor = Cursors.Hand, Padding = new Thickness(16, 7, 12, 7), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
            var rGrid = new Grid();
            rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rGrid.ColumnDefinitions.Add(new ColumnDefinition());
            rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rGrid.Children.Add(IconR.Render(tm.I, ActionType.Keys, 13));
            rGrid.Children.Add(new TextBlock { Text = " " + tm.L, FontSize = Tk.FBody, Foreground = Th.BTP, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(rGrid.Children[rGrid.Children.Count - 1], 1);
            var cntTb = new TextBlock { Text = cnt.ToString(), FontSize = Tk.FSmall, Foreground = Th.BTS, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            rGrid.Children.Add(cntTb);
            Grid.SetColumn(cntTb, 2);
            cntLbls[ck] = cntTb;
            row.Child = rGrid;
            row.MouseLeftButtonUp += (_, __) => SelectLK(ck);
            ltBtns[ck] = row;
            var selCol2 = LTM.C(ck);
            // 悬停底色过渡（独立画刷；选中行保持常亮底）
            var railBr5 = new SolidColorBrush(selLK == ck ? Color.FromArgb(38, selCol2.R, selCol2.G, selCol2.B) : Colors.Transparent);
            row.Background = railBr5;
            row.MouseEnter += (_, __) => { if (selLK != ck) { try { railBr5.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(20, selCol2.R, selCol2.G, selCol2.B), TimeSpan.FromMilliseconds(120))); } catch { } } };
            row.MouseLeave += (_, __) => { if (selLK != ck) { try { railBr5.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.Transparent, TimeSpan.FromMilliseconds(160))); } catch { } } };
            ltList.Children.Add(row);
        }
        ltPanel.Children.Add(ltList);
        page.Children.Add(ltPanel);

        var ltDiv = new Border { Width = 1, Background = Th.B(Th.BS) };
        page.Children.Add(ltDiv); Grid.SetColumn(ltDiv, 1);

        // Right: menu items editor
        var rightPanel = new Grid();
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightPanel.RowDefinitions.Add(new RowDefinition());
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        lhTitle = new TextBlock { Text = "", FontSize = Tk.FNav, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        // WrapPanel 而不是 StackPanel：这一行现在有 5 个按钮，窗口缩到 MinWidth(700) 时
        // 右栏只有 ~290px，StackPanel 会直接把「▶ 试运行」裁掉（拖窄了就点不到）。
        // 换行版最差是把按钮挤到第二行，任何宽度都还在。
        var lhPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 4) };
        lhPanel.Children.Add(lhTitle);

        // Search box
        // 注意水平 Margin 用 0：外层 topCol 已经给了 12，这里再写 12 会让搜索框比同列的
        // 置顶条/类型标题多缩进一级，一个竖列上出现两种左边界。
        searchBox = new TextBox { Background = Th.BBI, Foreground = Th.BTP, BorderBrush = Th.B(Th.BS), BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 6), FontSize = Tk.FBody, Margin = new Thickness(0, 10, 0, 6) };
        // 空输入框没有提示时就是一块"死"的空白板（skill：空状态应当是邀请，不是空白）
        var searchHint = new TextBlock { Text = "搜索菜单项：汉字 / 英文 / 拼音首字母（如 zybh → 自由变换）", FontSize = Tk.FSmall, Foreground = Th.BTD, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var searchHost = new Grid();
        searchHost.Children.Add(searchBox);
        searchHost.Children.Add(searchHint);
        searchBox.TextChanged += (s, e) => { try { searchHint.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed; } catch { } RebuildItemsList(); };
    var topCol = new StackPanel { Margin = new Thickness(12, 6, 12, 0) };
    pinStripHost = new Border { CornerRadius = new CornerRadius(8), Background = pinStripBg, BorderBrush = pinStripBd, BorderThickness = new Thickness(1), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    // 置顶区原来是一行横排 + MaxWidth 620 的横向滚动：标签自己就占约 280，两者一叠加必然超过右栏宽度
    // （右栏才 ~470），被父级裁掉；横向滚动条自己也在可视区外，所以右侧的格子既看不见也点不到——
    // 置顶一多就"被遮挡"就是这么来的。改成：标签单独一行，置顶区在下面**往下换行**，
    // 宽度永远是右栏的实际宽度；超过 3 行才竖向滚动（Horizontal 必须 Disabled，
    // 否则 WrapPanel 拿到无限宽就永远不换行）。
    pinStripPanel = new WrapPanel { Orientation = Orientation.Horizontal };
    var pinScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 96, Content = pinStripPanel };
    var pinCol = new StackPanel();
    pinCol.Children.Add(new TextBlock { Text = "📌 已置顶到菜单顶部（拖动排序 · 右键取消）", FontSize = Tk.FSmall, FontWeight = FontWeights.SemiBold, Foreground = pinStripFg, Margin = new Thickness(0, 0, 0, 4) });
    pinCol.Children.Add(pinScroll);
    pinStripHost.Child = pinCol;
    topCol.Children.Add(pinStripHost);
    topCol.Children.Add(searchHost);
    batchHost = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(PHex("#EAF4FF", Colors.AliceBlue)), BorderBrush = new SolidColorBrush(PHex("#BBD9F5", Colors.LightBlue)), BorderThickness = new Thickness(1), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    batchPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    batchHost.Child = batchPanel;
    topCol.Children.Add(batchHost);
    rightPanel.Children.Add(topCol);

        // Layer header + add buttons
        lhPanel.Children.Add(new TextBlock { Text = "  " });
        lhPanel.Children.Add(U.Btn("+ 添加", Th.AB, () =>
        {
            var ni = new MI { Title = "新菜单项", Icon = "fa:Solid_Play", Action = ActionType.Keys, Value = "" };
            PushUndo(); EditItem(ni);
            var em2 = cfg.Menus.FirstOrDefault(m => m.Kind == selLK);
            if (em2 != null && !string.IsNullOrEmpty(ni.Title)) { PushUndo(); em2.Items.Add(ni); MkDirty(); RebuildItemsList(); }
        }));
        lhPanel.Children.Add(new TextBlock { Text = " " });
        // 「＋ 从脚本库」：脚本库由独立动作「PS脚本库」维护，这里只读同一份缓存（分工见开发说明 §19）。
        // 挑选窗是**文件级静态方法** PickScriptsFromLib，不在这段 lambda 里现搭界面。
        lhPanel.Children.Add(U.Btn("+ 从脚本库", Th.AB, () =>
        {
            var lm5 = cfg.Menus.FirstOrDefault(m => m.Kind == selLK);
            if (lm5 == null) return;
            // 键用 TitleMain：菜单项标题可能带「 | ^C」这种按键提示，直接比 Title 会漏掉这类项。
            // 值是这条置入时的正文指纹——挑选窗靠它判"菜单里那条是不是旧版"。
            var have = new Dictionary<string, string>();
            foreach (var hx in lm5.Items) if (!hx.IsSep) have[hx.TitleMain] = hx.LibHash ?? "";
            List<MI> pk;
            try { pk = PickScriptsFromLib(ed, LTM.L(selLK), have, null); }
            catch (Exception ex) { LogFail("PickFromLib", ex); QkToast("打开脚本库失败：" + ex.Message, "Warning"); return; }
            if (pk == null || pk.Count == 0) return;
            PushUndo();
            foreach (var px in pk) lm5.Items.Add(px);
            MkDirty(); RebuildItemsList(); RefreshCountBadges();
            QkToast("已从脚本库加入 " + pk.Count + " 个脚本项");
        }));
        // r64：去掉「━ 分隔线」按钮——用它的人几乎没有，菜单里要分段直接拖排序就行，
        // 多一个按钮只是让这排按钮更长（用户反馈"用处不大"）。已有的分隔线照常显示，只是不再新增。
        lhPanel.Children.Add(new TextBlock { Text = "  " });
        lhPanel.Children.Add(U.Btn("⤷ 添加子选项", Th.TS, () =>
        {
            var lm0 = cfg.Menus.FirstOrDefault(m => m.Kind == selLK);
            if (lm0 == null) return;
            int idx0 = selUK == null ? -1 : lm0.Items.FindIndex(x => x.UK == selUK);
            if (idx0 < 0) { QkToast("请先在右侧列表里点击选中一个菜单项", "Warning"); return; }
            var p0 = lm0.Items[idx0];
            if (p0.IsSep) { QkToast("分隔线不能添加子选项", "Warning"); return; }
            PushUndo();
            if (p0.Sub == null) p0.Sub = new List<MI>();   // 使该项成为父菜单
            var nsub2 = new MI { Title = "新子项", Icon = "fa:Solid_Play", Action = ActionType.Keys, Value = "" };
            EditItem(nsub2);
            if (!string.IsNullOrEmpty(nsub2.Title)) { p0.Sub.Add(nsub2); MkDirty(); RebuildItemsList(); }
        }));
        lhPanel.Children.Add(new TextBlock { Text = "  " });
        lhPanel.Children.Add(U.Btn("▶ 试运行", Th.TS, () => TestRunSelected()));
        rightPanel.Children.Add(lhPanel); Grid.SetRow(lhPanel, 1);

        // Scrollable items list
        itemsList = new StackPanel { Margin = new Thickness(12, 0, 12, 4) };
        itemsSV = new ScrollViewer { Content = itemsList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        rightPanel.Children.Add(itemsSV); Grid.SetRow(itemsSV, 2);

        // Bottom toolbar
        var toolBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 2, 12, 8) };
        if (clipB.Count > 0)
        {
            toolBar.Children.Add(U.Btn("📋 粘贴(" + clipB.Count + ")", Th.AG, () =>
            {
                var tml = cfg.Menus.FirstOrDefault(m => m.Kind == selLK);
                if (tml != null) { foreach (var ci in clipB) tml.Items.Add(new MI { Title = ci.Title, Icon = ci.Icon, Action = ci.Action, Value = ci.Value, Sub = ci.Sub?.Select(s => new MI { Title = s.Title, Icon = s.Icon, Action = s.Action, Value = s.Value }).ToList() }); MkDirty(); RebuildItemsList(); }
            }, 11));
        }
        rightPanel.Children.Add(toolBar); Grid.SetRow(toolBar, 3);
        page.Children.Add(rightPanel); Grid.SetColumn(rightPanel, 2);
        contentArea.Children.Add(page);

        // Init
        SelectLK(selLK);

        void RebuildItemsList()
        {
            rebuildItemsList = RebuildItemsList;
            RefreshPinStrip();
            itemsList.Children.Clear();
            _geom.Clear();
            rebuildGen++;
            string q = (searchBox?.Text ?? "").Trim();
            LM ml2 = cfg.Menus.FirstOrDefault(m => m.Kind == selLK);
            if (ml2 == null) return;
            lhTitle.Text = LTM.L(selLK) + " (" + ml2.Items.Count + " 项)";
            lhTitle.Foreground = new SolidColorBrush(LTM.C(selLK));
            if (ml2.Items.Count == 0)
            {
                var gb = new Border { CornerRadius = new CornerRadius(8), Background = Th.BBI, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 6, 0, 0) };
                gb.Child = new TextBlock { Text = "还没有菜单项：点上方「+ 添加」新建，或从其它类型直接拖一个过来。", FontSize = Tk.FBody, Foreground = Th.BTS, TextWrapping = TextWrapping.Wrap };
                itemsList.Children.Add(gb);
            }

            // 搜索不再分两个入口：本类型里过滤，同时把"别的类型里也有匹配"列出来，点一下切过去（搜索词保留）
            if (!string.IsNullOrEmpty(q))
            {
                var others = new List<LM>();
                foreach (var mm in cfg.Menus)
                {
                    if (mm.Kind == selLK) continue;
                    if (mm.Items.Any(x => !x.IsSep && MatchTitle(x, q))) others.Add(mm);
                }
                if (others.Count > 0)
                {
                    var orow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 8) };
                    orow.Children.Add(new TextBlock { Text = "其它类型：", FontSize = Tk.FSmall, Foreground = Th.BTS, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                    foreach (var mm in others)
                    {
                        var mk = mm.Kind;
                        int cnt = mm.Items.Count(x => !x.IsSep && MatchTitle(x, q));
                        var chip = new Border { Cursor = Cursors.Hand, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(Color.FromArgb(20, 10, 102, 194)), ToolTip = "切到「" + LTM.L(mk) + "」并保留搜索词" };
                        chip.Child = new TextBlock { Text = LTM.L(mk) + " " + cnt, FontSize = Tk.FSmall, Foreground = navActFg, VerticalAlignment = VerticalAlignment.Center };
                        chip.MouseLeftButtonUp += (_, __) => { if (GoLK != null) GoLK(mk); };
                        orow.Children.Add(chip);
                    }
                    itemsList.Children.Add(orow);
                }
            }
            var dupKeys = DupKeys(ml2.Items);

            for (int idx = 0; idx < ml2.Items.Count; idx++)
            {
                var it = ml2.Items[idx];
                if (!string.IsNullOrEmpty(q) && !it.IsSep && !MatchTitle(it, q)) continue;

                if (it.IsSep)
                {
                    int sidx = idx;
                    var sg = new Grid();
                    sg.ColumnDefinitions.Add(new ColumnDefinition());
                    sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    sg.Children.Add(new Border { Height = 1, Background = Th.B(Th.BS), Margin = new Thickness(0, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center });
                    var sdb = U.IB("✕", Th.AR, () => { PushUndo(); ml2.Items.RemoveAt(sidx); MkDirty(); RebuildItemsList(); });
                    sg.Children.Add(sdb); Grid.SetColumn(sdb, 1);
                    itemsList.Children.Add(sg); _geom.Add((idx, sg)); continue;
                }

                int cidx = idx;
                bool isExp = it.HasSub && expandedSubs.Contains(it.UK);
                // 紧跟着分隔线的行不再画自己的底线，否则两条线挤在一起（分隔线自身就是一条线）
                bool nextIsSep = (idx + 1 < ml2.Items.Count) && ml2.Items[idx + 1].IsSep;
                bool isSel0 = it.UK == selUK;
                bool isMulti = MSFor(selLK).Contains(cidx);
                var row = new Border { CornerRadius = new CornerRadius(8), Background = isMulti ? new SolidColorBrush(Color.FromArgb(28, 0, 168, 190)) : (isSel0 ? new SolidColorBrush(Color.FromArgb(20, 10, 102, 194)) : Th.BBC), BorderBrush = isMulti ? new SolidColorBrush(PHex("#00A8BE", Colors.Teal)) : (isSel0 ? new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue)) : (nextIsSep ? Tk.B(Tk.BorderSubtle) : Tk.B(Tk.RowLine))), BorderThickness = (isMulti || isSel0) ? new Thickness(1.4) : (nextIsSep ? new Thickness(0) : new Thickness(0, 0, 0, 1)), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, isExp ? 2 : 0), Cursor = Cursors.Hand };

                var rgrid = new Grid();
                rgrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rgrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rgrid.ColumnDefinitions.Add(new ColumnDefinition());
                rgrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // 拖动：整行可拖（≡ 仅作视觉提示，按住卡片任意处即可）
                var dgrip = new TextBlock { Text = "≡", FontSize = Tk.FBody, Foreground = Th.B(Th.BS), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.SizeAll, Opacity = 0.45 };
                rgrid.Children.Add(dgrip);
                // 常态压淡、悬停提亮：把手是操作入口，不悬停时不该抢戏（图钉不动，它表示「已置顶」状态）
                // 悬停底色过渡：行用独立画刷（共享的 Th.BBC 一动画会全体变色），普通态才做悬停高亮
                var rowBr5 = new SolidColorBrush(isMulti ? Color.FromArgb(28, 0, 168, 190) : isSel0 ? Color.FromArgb(20, 10, 102, 194) : Th.BC);
                row.Background = rowBr5;
                Color rowHov5 = Color.FromArgb(255, (byte)(Th.BC.R * 0.86 + 20), (byte)(Th.BC.G * 0.86 + 22), (byte)(Th.BC.B * 0.86 + 26));
                row.MouseEnter += (_, __) =>
                {
                    try { dgrip.Opacity = 1; } catch { }
                    if (!isMulti && !isSel0) try { rowBr5.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(rowHov5, TimeSpan.FromMilliseconds(120))); } catch { }
                };
                row.MouseLeave += (_, __) =>
                {
                    try { dgrip.Opacity = 0.45; } catch { }
                    if (!isMulti && !isSel0) try { rowBr5.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Th.BC, TimeSpan.FromMilliseconds(160))); } catch { }
                };

                // Pin button
                bool isPinned = cfg.Pinned.Any(p => p.UK == it.UK);
                var pinBtn = new Button { FontSize = Tk.FSmall, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Width = 22, Height = 22, Padding = new Thickness(0), Cursor = Cursors.Hand, ToolTip = it.Title };
                pinBtn.Content = IconR.Render("fa:Solid_Thumbtack", ActionType.Keys, 12, isPinned ? "#3BBE8F" : "#7A8499");
                pinBtn.Click += (_, __) =>
                {
                    PushUndo();
                    if (cfg.Pinned.Any(pp => pp.UK == it.UK)) cfg.Pinned.RemoveAll(pp => pp.UK == it.UK);
                    else cfg.Pinned.Add(new PI { Title = it.Title, Icon = it.Icon, Action = it.Action, Value = it.Value, LK = selLK });
                    MkDirty(); RebuildItemsList();
                };
                rgrid.Children.Add(pinBtn); Grid.SetColumn(pinBtn, 1);

                // Info
                var info = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (it.HasSub)
                {
                    var expBtn = new Button { Content = IconR.Render(isExp ? "fa:Solid_ChevronDown" : "fa:Solid_ChevronRight", ActionType.Keys, 10, "#8B93A5"), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Width = 18, Height = 18, Padding = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
                    expBtn.Click += (_, __) => { if (isExp) expandedSubs.Remove(it.UK); else expandedSubs.Add(it.UK); RebuildItemsList(); };
                    info.Children.Add(expBtn);
                }
                info.Children.Add(IconR.Render(it.Icon, it.Action, 13, cfg.IC));
                info.Children.Add(new TextBlock
                {
                    Text = " " + it.TitleMain, FontSize = Tk.FBody, Foreground = Th.BTP, VerticalAlignment = VerticalAlignment.Center,
                    // 长标题单行截断（原来没有截断设置，字一多就往外挤，把右侧按钮推出可视区）
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240, ToolTip = it.Title
                });
                if (it.Header) info.Children.Add(new TextBlock { Text = "[分组标题]", FontSize = Tk.FMicro, Foreground = Th.BTS, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });
                if (it.Disabled) info.Children.Add(new TextBlock { Text = "[已停用]", FontSize = Tk.FMicro, Foreground = new SolidColorBrush(PHex("#C2410C", Colors.OrangeRed)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });
                if (!string.IsNullOrEmpty(it.TitleKey))
                {
                    bool dupK = dupKeys.Contains(it.TitleKey);
                    info.Children.Add(new TextBlock { Text = it.TitleKey, FontSize = Tk.FMicro, FontFamily = new FontFamily("Consolas"), Foreground = Tk.B(dupK ? Tk.Warn : Tk.InkFaint), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), ToolTip = dupK ? "同一层里有其它项写了同一个快捷键 —— 多半是其中一个写错了" : "标题右侧的按键提示（仅显示，不绑定）" });
                }
                if (it.HasSub) info.Children.Add(new TextBlock { Text = string.Format(" ({0})", it.Sub.Count), FontSize = Tk.FMicro, Foreground = Th.BTS, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                info.Children.Add(new TextBlock { Text = " " + AT.Lbl(it.Action), FontSize = Tk.FMicro, Foreground = IconR.ActionColor(it.Action), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
                rgrid.Children.Add(info); Grid.SetColumn(info, 2);

                // Action buttons
                var abtns = new StackPanel { Orientation = Orientation.Horizontal };
                var eb = U.IB("✎", Th.AB, () => { PushUndo(); EditItem(it); MkDirty(); RebuildItemsList(); });
                abtns.Children.Add(eb);
                var db = U.IB("✕", Th.AR, () => { PushUndo(); ml2.Items.RemoveAt(cidx); MkDirty(); RebuildItemsList(); });
                abtns.Children.Add(db);
                rgrid.Children.Add(abtns); Grid.SetColumn(abtns, 3);

                row.Child = rgrid;

                // 点击选择/复制；按住即拖动（列表内重排 / 拖到左栏移入，Ctrl=复制）
                row.MouseLeftButtonDown += (_, de) =>
                {
                    if (!string.IsNullOrEmpty(searchBox?.Text?.Trim())) return;
                    _dArm = true; _dOn = false; _dSrc = idx; _dPt = de.GetPosition(itemsList);
                };
                row.MouseMove += (_, me) =>
                {
                    if (!_dArm) return;
                    if (!_dOn)
                    {
                        var np = me.GetPosition(itemsList);
                        if (Math.Abs(np.X - _dPt.X) + Math.Abs(np.Y - _dPt.Y) < 10) return;
                        _dOn = true;
                        SpawnGhost(it);
                        try { row.CaptureMouse(); } catch { }
                        row.Opacity = 0.45;
                    }
                    var scr = CursorScreen();
                    if (_ghost != null) { _ghost.Left = scr.X + 10; _ghost.Top = scr.Y + 14; }
                    var hk = LtAt(scr);
                    if (hk != LayerKind.None && hk != selLK)
                    {
                        SetHoverLt(hk); RemoveInd();
                        if (_gcap != null) _gcap.Text = "移入 " + LTM.L(hk) + (((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ? "（置顶）" : "");
                    }
                    else
                    {
                        if (hk == selLK) SetHoverLt(selLK); else ResetHoverLt();
                        if (InsideList(scr))
                        {
                            _gapCur = GapFor(me.GetPosition(itemsList).Y);
                            MoveInd(_gapCur);
                            if (_gcap != null) _gcap.Text = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? "复制到此处" : "";
                            AutoScroll(me);
                        }
                        else RemoveInd();
                    }
                };
                row.MouseLeftButtonUp += (_, ue) =>
                {
                    _dArm = false;
                    try
                    {
                    if (_dOn)
                    {
                        _dOn = false;
                        try { row.ReleaseMouseCapture(); } catch { }
                        row.Opacity = 1;
                        ResetHoverLt(); RemoveInd();
                        if (_ghost != null) { try { _ghost.Close(); } catch { } _ghost = null; }
                        var scr = CursorScreen();
                        var hk = LtAt(scr);
                        bool copy = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                        PushUndo();
                        if (hk != LayerKind.None && hk != selLK)
                        {
                            var dst = cfg.Menus.FirstOrDefault(m => m.Kind == hk);
                            if (dst == null) { dst = new LM { Kind = hk, Label = LTM.L(hk), Icon = LTM.I(hk), Items = new List<MI>() }; cfg.Menus.Add(dst); }
                            bool head = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                            if (copy) { if (head) dst.Items.Insert(0, it.Clone()); else dst.Items.Add(it.Clone()); }
                            else { int cur0 = ml2.Items.IndexOf(it); if (cur0 >= 0) { ml2.Items.RemoveAt(cur0); if (head) dst.Items.Insert(0, it); else dst.Items.Add(it); } }
                            MkDirty(); RefreshCountBadges();
                            foreach (var kv2 in ltBtns) { bool ac2 = kv2.Key == selLK; var c3 = LTM.C(kv2.Key); kv2.Value.Background = ac2 ? new SolidColorBrush(c3) { Opacity = 0.15 } : Brushes.Transparent; kv2.Value.BorderBrush = ac2 ? new SolidColorBrush(c3) : Brushes.Transparent; kv2.Value.BorderThickness = new Thickness(ac2 ? 2 : 0, 0, 0, 0); }
                            RebuildItemsList();
                        }
                        else if (hk == selLK)
                        {
                            if (copy) ml2.Items.Add(it.Clone());
                            else { int cur0 = ml2.Items.IndexOf(it); if (cur0 < 0) return; ml2.Items.RemoveAt(cur0); ml2.Items.Add(it); }
                            MkDirty(); RebuildItemsList();
                        }
                        else
                        {
                            int g = Math.Max(0, Math.Min(_gapCur, ml2.Items.Count));
                            if (copy) ml2.Items.Insert(g, it.Clone());
                            else { int cur0 = ml2.Items.IndexOf(it); if (cur0 < 0) return; var tt = it; ml2.Items.RemoveAt(cur0); int ins = g > cur0 ? g - 1 : g; ml2.Items.Insert(Math.Max(0, Math.Min(ins, ml2.Items.Count)), tt); }
                            MkDirty(); RebuildItemsList();
                        }
                    }
                    else
                    {
                        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                        {
                            clipB = new List<MI> { new MI { Title = it.Title, Icon = it.Icon, Action = it.Action, Value = it.Value, Sub = it.Sub?.Select(s2 => new MI { Title = s2.Title, Icon = s2.Icon, Action = s2.Action, Value = s2.Value }).ToList() } };
                        }
                            MarkSel(row, it.UK);
                        }
                    }
                    catch { }
                };
                itemsList.Children.Add(row);
                _geom.Add((idx, row));
                if (_focusUK != null && revealSubUK == null && it.UK == _focusUK) _focusRow = row;

                // Expanded sub items
                if (isExp && it.Sub != null)
                {
                    for (int si = 0; si < it.Sub.Count; si++)
                    {
                        var sub = it.Sub[si]; int ssidx = si;
                        if (sub.IsSep)
                        {
                            var sg2 = new Grid { Margin = new Thickness(28, 0, 0, 0) };
                            sg2.ColumnDefinitions.Add(new ColumnDefinition());
                            sg2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            sg2.Children.Add(new Border { Height = 1, Background = Th.B(Th.BS), Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center });
                            var sdb2 = U.IB("✕", Th.AR, () => { PushUndo(); it.Sub.RemoveAt(ssidx); MkDirty(); RebuildItemsList(); });
                            sg2.Children.Add(sdb2); Grid.SetColumn(sdb2, 1);
                            itemsList.Children.Add(sg2); continue;
                        }
                        var srow = new Border { CornerRadius = new CornerRadius(4), Background = Th.BBI, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(22, 1, 0, 1) };
                        var sgrid = new Grid();
                        sgrid.ColumnDefinitions.Add(new ColumnDefinition());
                        sgrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        var sinfo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                        sinfo.Children.Add(IconR.Render(sub.Icon, sub.Action, 12, cfg.IC));
                        sinfo.Children.Add(new TextBlock
                        {
                            Text = " " + sub.Title, FontSize = Tk.FSmall, Foreground = Th.BTP, VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220, ToolTip = sub.Title
                        });
                        sinfo.Children.Add(new TextBlock { Text = " " + AT.Lbl(sub.Action), FontSize = Tk.FMicro, Foreground = IconR.ActionColor(sub.Action), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                        sgrid.Children.Add(sinfo);
                        var sabtns = new StackPanel { Orientation = Orientation.Horizontal };
                        sabtns.Children.Add(U.IB("✎", Th.AB, () => { PushUndo(); EditItem(sub); MkDirty(); RebuildItemsList(); }));
                        sabtns.Children.Add(U.IB("✕", Th.AR, () => { PushUndo(); it.Sub.RemoveAt(ssidx); MkDirty(); RebuildItemsList(); }));
                        sgrid.Children.Add(sabtns); Grid.SetColumn(sabtns, 1);
                        srow.Tag = sub;
                        srow.Cursor = Cursors.SizeAll;
                        srow.MouseLeftButtonDown += (_, de) =>
                        {
                            if (!string.IsNullOrEmpty(searchBox?.Text?.Trim())) return;
                            _sdArm = true; _sdOn = false; _sdParent = it; _sdFrom = ssidx; _sdPt = de.GetPosition(itemsList);
                        };
                        srow.MouseMove += (_, me) =>
                        {
                            if (!_sdArm || !ReferenceEquals(_sdParent, it)) return;
                            var np = me.GetPosition(itemsList);
                            if (!_sdOn)
                            {
                                if (Math.Abs(np.X - _sdPt.X) + Math.Abs(np.Y - _sdPt.Y) < 8) return;
                                _sdOn = true;
                                _sdSibs = SdSibsOf(it);
                                try { srow.CaptureMouse(); } catch { }
                                srow.Opacity = 0.45;
                            }
                            SdMoveInd(np.Y);
                        };
                        srow.MouseLeftButtonUp += (_, ue) =>
                        {
                            _sdArm = false;
                            if (!_sdOn)
                            {
                                // 子项行单击也可选中：这样「▶ 试运行」对子项同样可用
                                MarkSel(srow, sub.UK);
                                return;
                            }
                            _sdOn = false;
                            try { srow.ReleaseMouseCapture(); } catch { }
                            srow.Opacity = 1;
                            SdRemoveInd();
                            int k2 = SdSlot(ue.GetPosition(itemsList).Y);
                            int from2 = _sdFrom; var sibs2 = _sdSibs;
                            _sdSibs = null; _sdParent = null;
                            if (from2 < 0 || from2 >= it.Sub.Count || sibs2 == null) return;
                            int to2 = (k2 < sibs2.Count) ? sibs2[k2].idx : it.Sub.Count;
                            if (to2 > from2) to2--;
                            if (to2 == from2) return;
                            PushUndo();
                            var moved2 = it.Sub[from2]; it.Sub.RemoveAt(from2);
                            it.Sub.Insert(Math.Min(to2, it.Sub.Count), moved2);
                            MkDirty(); RebuildItemsList();
                        };
                        srow.Child = sgrid; itemsList.Children.Add(srow);
                        if (sub.UK == selUK) { srow.BorderBrush = new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue)); srow.BorderThickness = new Thickness(1.4); selRow = srow; }
                        if (revealSubUK != null && sub.UK == revealSubUK) _focusRow = srow;
                    }
                    var saddRow = new Border { CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(22, 1, 0, 4), Cursor = Cursors.Hand };
                    var saddBtn = U.Btn("+ 添加子项", Th.AB, () => { var nsub = new MI { Title = "新子项", Icon = "fa:Solid_Play", Action = ActionType.Keys, Value = "" }; PushUndo(); EditItem(nsub); if (!string.IsNullOrEmpty(nsub.Title)) { it.Sub.Add(nsub); MkDirty(); RebuildItemsList(); } }, 10);
                    saddRow.Child = saddBtn; itemsList.Children.Add(saddRow);
                }
            }
            if (string.IsNullOrEmpty(q))
            {
                itemsList.Opacity = 0.55;
                itemsList.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 1d, TimeSpan.FromMilliseconds(Tk.MMicro)));
            }
            if (_focusRow != null)
            {
                var fr = _focusRow; _focusRow = null;
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { try { fr.BringIntoView(); } catch { } }));
            }
            revealSubUK = null;
            RescaleAll();   // 列表是新建的，按当前档位再缩一次，避免和页面其他部分字号不一致（r64）
        }
    }

    // ========== Page 2: Appearance (新预设 + 右侧实时预览拉满) ==========
    void BuildAppearancePage()
    {
        var page = new Grid();
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
        page.RowDefinitions.Add(new RowDefinition());

        // ---------- 左：控制项（卡片纵向排布） ----------
        var leftSV = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var st = new StackPanel { Margin = new Thickness(14, 2, 12, 16) };
        st.Children.Add(new TextBlock { Text = "外观主题", FontSize = Tk.FTitle, FontWeight = FontWeights.SemiBold, Foreground = txtMain, Margin = new Thickness(0, 2, 0, 10) });
        st.Children.Add(new TextBlock { Text = "左选样式或微调，右侧实时预览菜单效果", FontSize = Tk.FSmall, Foreground = txtSub, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });

        // 预设主题：分 暗色 / 亮色 两大类。卡片三态齐备——
        // 常显主题名（层级：看得到选的是什么）、选中=强调色描边+名字点亮（切主题全组即时同步）、
        // 悬停上浮 1.05+投影、按下 0.95（手感）。
        // 先声明后赋值：预设卡片的点击回调会调 UpdatePreview，而 UpdatePreview 里引用 previewCanvas（CS0165）
        Border previewCanvas = null;
        var darkP = DarkThemes;
        var lightP = LightThemes;
        var presetCards = new List<Tuple<Border, TextBlock, string>>();
        Action SyncPresetSel = () =>
        {
            foreach (var pc in presetCards)
            {
                bool cur = (cfg.Theme ?? "") == pc.Item3;
                pc.Item1.BorderBrush = cur ? new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue)) : Th.B(Th.BS);
                pc.Item1.BorderThickness = new Thickness(cur ? 2 : 1);
                pc.Item2.Foreground = cur ? Tk.B(Tk.Accent) : Tk.B(Tk.InkMuted);
            }
        };
        StackPanel BuildPresetGroup(string title, (string, string, string, string, string, string, double, string)[] ps)
        {
            var col = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            col.Children.Add(new TextBlock { Text = title, FontSize = Tk.FBody, FontWeight = FontWeights.SemiBold, Foreground = txtSub, Margin = new Thickness(2, 2, 0, 6) });
            var wrap = new WrapPanel { MaxWidth = 620 };
            for (int ti = 0; ti < ps.Length; ti++)
            {
                var tp2 = ps[ti]; int ti2 = ti;
                var sq = new Grid();
                sq.RowDefinitions.Add(new RowDefinition());
                sq.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                sq.Children.Add(new Border { Background = PB(tp2.Item2, Colors.Gray), CornerRadius = new CornerRadius(6, 6, 0, 0) });
                var strip = new Border { Height = 6, Background = PB(tp2.Item4, Colors.Gray), CornerRadius = new CornerRadius(0, 0, 6, 6) };
                sq.Children.Add(strip); Grid.SetRow(strip, 1);
                var pbtn = new Border { CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Width = 52, Height = 52, Margin = new Thickness(3), BorderBrush = Th.B(Th.BS), BorderThickness = new Thickness(1), ToolTip = tp2.Item1 + "（点击套用）", Child = sq };
                var nm = new TextBlock { Text = tp2.Item1, FontSize = 10, Foreground = Tk.B(Tk.InkMuted), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 62 };
                var cell = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(2) };
                cell.Children.Add(pbtn); cell.Children.Add(nm);
                var scT = new ScaleTransform(1, 1); pbtn.RenderTransform = scT; pbtn.RenderTransformOrigin = new Point(0.5, 0.5);
                pbtn.MouseEnter += (_, __) => { try { var e1 = new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } }; scT.BeginAnimation(ScaleTransform.ScaleXProperty, e1); scT.BeginAnimation(ScaleTransform.ScaleYProperty, e1); pbtn.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black }; } catch { } };
                pbtn.MouseLeave += (_, __) => { try { var e2 = new DoubleAnimation(1d, TimeSpan.FromMilliseconds(120)); scT.BeginAnimation(ScaleTransform.ScaleXProperty, e2); scT.BeginAnimation(ScaleTransform.ScaleYProperty, e2); pbtn.Effect = null; } catch { } };
                pbtn.MouseLeftButtonDown += (_, __) => { try { var e3 = new DoubleAnimation(0.95, TimeSpan.FromMilliseconds(60)); scT.BeginAnimation(ScaleTransform.ScaleXProperty, e3); scT.BeginAnimation(ScaleTransform.ScaleYProperty, e3); } catch { } };
                pbtn.MouseLeftButtonUp += (_, __) =>
                {
                    var tp3 = ps[ti2];
                    cfg.BC = tp3.Item2; cfg.FC = tp3.Item3; cfg.HC = tp3.Item4; cfg.OC = tp3.Item5;
                    cfg.IC = tp3.Item6; cfg.Op = tp3.Item7; cfg.Theme = tp3.Item8;
                    MkDirty(); UpdatePreview(); SyncPresetSel();
                    try { var e4 = new DoubleAnimation(1d, TimeSpan.FromMilliseconds(90)); scT.BeginAnimation(ScaleTransform.ScaleXProperty, e4); scT.BeginAnimation(ScaleTransform.ScaleYProperty, e4); } catch { }
                };
                wrap.Children.Add(cell);
                presetCards.Add(Tuple.Create(pbtn, nm, tp2.Item8));
            }
            col.Children.Add(wrap);
            return col;
        }
        var preCol = new StackPanel();
        preCol.Children.Add(BuildPresetGroup("暗色 · " + darkP.Length + " 套", darkP));
        preCol.Children.Add(BuildPresetGroup("亮色 · " + lightP.Length + " 套", lightP));
        SyncPresetSel();

        st.Children.Add(U.Card("预设主题", preCol));
        // "文字大小"滑块回归：r34 我把它当成与"缩放比例"重复去掉了，但两者不是一回事
        // ——字号只改字，缩放连面板宽度和图标一起放大。"整体字体大一点"要的只是前者。
        st.Children.Add(U.Card("菜单尺寸", new StackPanel { Children = { U.L("宽度", 11), CreateSlider(cfg.MW, 160, 500, v => { cfg.MW = v; MkDirty(); UpdatePreview(); }) } },
            new StackPanel { Children = { U.L("文字大小（只改字）", 11), CreateSlider(cfg.FS, 10, 26, v => { cfg.FS = v; MkDirty(); UpdatePreview(); }) } },
            new StackPanel { Children = { U.L("缩放比例 %", 11), CreateSlider(cfg.Zoom * 100, 50, 250, v => { cfg.Zoom = v / 100.0; MkDirty(); UpdatePreview(); }) } },
            new StackPanel { Children = { U.L("项目间距", 11), CreateSlider(cfg.IS, 0, 10, v => { cfg.IS = v; MkDirty(); UpdatePreview(); }) } },
            new StackPanel { Children = { U.L("置顶图标大小", 11), CreateSlider(cfg.PS > 0 ? cfg.PS : 20, 12, 48, v => { cfg.PS = v; MkDirty(); UpdatePreview(); }) } }
        ));
        Button fsSortBtn = null, freqBtn = null;
        // 频率排序按钮在「使用次数」关闭时置灰停用：两者是一个功能的开关与后续（关掉统计就无从排序）
        Action SyncFreqBtns = () =>
        {
            fsSortBtn.Content = U.BtnContent("按使用频率自动排序：" + (cfg.FreqSort ? "开" : "关"), Th.AR, 11);
            fsSortBtn.Opacity = cfg.ShowFreq ? 1 : 0.45;
            fsSortBtn.ToolTip = cfg.ShowFreq ? "开：同一分段里用得多的排在前面" : "需要先开启「使用次数」";
        };
        freqBtn = U.Btn("使用次数：" + (cfg.ShowFreq ? "开" : "关"), Th.AB, () =>
        {
            cfg.ShowFreq = !cfg.ShowFreq;
            if (!cfg.ShowFreq) cfg.FreqSort = false;    // 关掉统计时排序一并关掉，不留"排序但没数据"的中间态
            MkDirty(); SyncFreqBtns();
            QkToast(cfg.ShowFreq ? "已开启使用次数（菜单里显示次数角标）" : "已关闭使用次数（不再记录，也不显示角标）");
        });
        freqBtn.ToolTip = "是否统计并显示每个菜单项用过多少次；关闭后不再累计，菜单角落的次数角标也消失";
        fsSortBtn = U.Btn("按使用频率自动排序：" + (cfg.FreqSort ? "开" : "关"), Th.AR, () =>
        {
            if (!cfg.ShowFreq) { QkToast("先在左边开启「使用次数」", "Warning"); return; }
            cfg.FreqSort = !cfg.FreqSort; MkDirty(); SyncFreqBtns();
        });
        SyncFreqBtns();
        Button hovSubBtn = null;
        Action SyncHovSub = () => { hovSubBtn.Content = U.BtnContent("自动进子层：" + (cfg.HoverSub ? "开" : "关"), Th.AR, 11); };
        hovSubBtn = U.Btn("", Th.AR, () => { cfg.HoverSub = !cfg.HoverSub; MkDirty(); SyncHovSub(); QkToast(cfg.HoverSub ? "悬停自动进子层：开" : "已关闭悬停自动进子层"); });
        SyncHovSub();
        hovSubBtn.ToolTip = "开：悬停在「含子菜单」的行或置顶格上片刻后自动进入子层；关：只能左键点击进入";
        var hovMsVal = U.L(cfg.HoverMs + " ms", 11);
        hovMsVal.Margin = new Thickness(10, 0, 0, 0);
        var hovMsRow = new StackPanel
        {
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { U.L("进子层等待时间", 11), hovMsVal } },
                CreateSlider(cfg.HoverMs, 100, 1500, v => { cfg.HoverMs = Math.Max(100, Math.Min(1500, (int)v)); hovMsVal.Text = cfg.HoverMs + " ms"; MkDirty(); })
            }
        };
        // 名称显示（归入行为组）：构建提前到行为卡之前
        Button pinNameBtn = null;
        Action SyncPinNameBtn = () => { pinNameBtn.Content = U.BtnContent("名称显示：" + (cfg.ShowPinName ? "方格下常显" : "悬停预览"), Th.AB, 11); };
        pinNameBtn = U.Btn("名称显示：" + (cfg.ShowPinName ? "方格下常显" : "悬停预览"), Th.AB, () =>
        {
            cfg.ShowPinName = !cfg.ShowPinName; MkDirty(); SyncPinNameBtn(); UpdatePreview();
        });
        pinNameBtn.ToolTip = "两档切换：\n· 方格下常显——每个置顶方格下面常显名称，一眼看出是什么（置顶条会高一点、每格按名字宽度变宽）\n· 悬停预览——只显示图标保持紧凑，鼠标移到方格上时在其下方即时浮出名称（不改变面板高度）";
        SyncPinNameBtn();
        // 行为组用 WrapPanel：窗口窄时自动换行，不再把按钮截掉半个字
        var behWrap = new WrapPanel();
        foreach (var b in new FrameworkElement[] { freqBtn, fsSortBtn, hovSubBtn, pinNameBtn }) { b.Margin = new Thickness(0, 0, 8, 8); behWrap.Children.Add(b); }
        st.Children.Add(U.Card("行为", behWrap));
        st.Children.Add(U.Card("悬停进子层", hovMsRow));
        // 光效：悬停辉光强度（0=关），预览的悬停行/置顶格同步呈现
        var glowVal = U.L(cfg.GlowK + " %", 11);
        glowVal.Margin = new Thickness(10, 0, 0, 0);
        var sheenVal = U.L((int)cfg.SheenK + " %", 11);
        sheenVal.Margin = new Thickness(10, 0, 0, 0);
        var underKVal = U.L(cfg.UnderK + " %", 11);
        underKVal.Margin = new Thickness(10, 0, 0, 0);
        var underHVal = U.L((int)cfg.UnderH + " px", 11);
        underHVal.Margin = new Thickness(10, 0, 0, 0);
        var glowRow = new StackPanel
        {
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { U.L("悬停辉光强度", 11), glowVal } },
                CreateSlider(cfg.GlowK, 0, 100, v => { cfg.GlowK = Math.Max(0, Math.Min(100, (int)v)); glowVal.Text = cfg.GlowK + " %"; MkDirty(); UpdatePreview(); }),
                new StackPanel { Orientation = Orientation.Horizontal, Children = { U.L("底部光带亮度", 11), sheenVal } },
                CreateSlider(cfg.SheenK, 0, 100, v => { cfg.SheenK = Math.Max(0, Math.Min(100, v)); sheenVal.Text = (int)v + " %"; MkDirty(); UpdatePreview(); }),
                new StackPanel { Orientation = Orientation.Horizontal, Children = { U.L("底部高光强度", 11), underKVal } },
                CreateSlider(cfg.UnderK, 0, 100, v => { cfg.UnderK = Math.Max(0, Math.Min(100, (int)v)); underKVal.Text = cfg.UnderK + " %"; MkDirty(); UpdatePreview(); }),
                new StackPanel { Orientation = Orientation.Horizontal, Children = { U.L("底部高光厚窄", 11), underHVal } },
                CreateSlider(cfg.UnderH, 2, 14, v => { cfg.UnderH = Math.Max(2, Math.Min(14, v)); underHVal.Text = (int)v + " px"; MkDirty(); UpdatePreview(); })
            }
        };
        glowRow.ToolTip = "悬停时菜单行/置顶格的辉光亮度。0＝关闭光效；默认 90。";
        st.Children.Add(U.Card("光效", glowRow));
        st.Children.Add(U.Card("外观颜色", MkCR("背景颜色", cfg.BC, v => { cfg.BC = v; MkDirty(); }, "菜单面板底色"),
            MkCR("文字颜色", cfg.FC, v => { cfg.FC = v; MkDirty(); }, "菜单项文字颜色"),
            MkCR("高亮颜色", cfg.HC, v => { cfg.HC = v; MkDirty(); }, "悬停/选中高亮、置顶底色"),
            MkCR("描边颜色", cfg.OC, v => { cfg.OC = v; MkDirty(); }, "菜单项描边颜色"),
            MkCR("图标颜色", cfg.IC, v => { cfg.IC = v; MkDirty(); }, "统一图标色调"),
            new StackPanel { Children = { U.L("不透明度", 11), CreateSlider(cfg.Op * 100, 50, 100, v => { cfg.Op = v / 100; MkDirty(); UpdatePreview(); }) } },
            new StackPanel { Children = { U.L("行圆角", 11), CreateSlider(cfg.CR, 0, 12, v => { cfg.CR = v; MkDirty(); UpdatePreview(); }) } },
            new StackPanel { Children = { U.L("描边粗细", 11), CreateSlider(cfg.BW, 0, 3, v => { cfg.BW = v; MkDirty(); UpdatePreview(); }) } }
        ));
        leftSV.Content = st;
        page.Children.Add(leftSV);

        // ---------- 右：实时预览（撑满整列高度，随左侧调节同步刷新） ----------
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition());
        rightGrid.Children.Add(new TextBlock { Text = "实时预览", FontSize = Tk.FNav, FontWeight = FontWeights.SemiBold, Foreground = txtMain, Margin = new Thickness(2, 6, 0, 8) });
        previewCanvas = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(PHex("#CFD3D8", Colors.LightGray)), BorderBrush = lineBrush, BorderThickness = new Thickness(1), Tag = FS_SKIP };
        var pvGrid = new Grid { Margin = new Thickness(14) };
        pvGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pvGrid.RowDefinitions.Add(new RowDefinition());
        previewPanel = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb((byte)(cfg.Op * 255), PHex(cfg.BC, Color.FromRgb(45, 45, 45)).R, PHex(cfg.BC, Color.FromRgb(45, 45, 45)).G, PHex(cfg.BC, Color.FromRgb(45, 45, 45)).B)), BorderBrush = PB(cfg.OC, Color.FromRgb(62, 62, 62)), BorderThickness = new Thickness(1), MinWidth = 120, MaxWidth = 282, MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        pvGrid.Children.Add(previewPanel);
        var hintBig = new TextBlock { Text = "（菜单预览，随左侧实时更新）", FontSize = Tk.FSmall, Foreground = new SolidColorBrush(PHex("#8A8F98", Colors.Gray)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.9 };
        pvGrid.Children.Add(hintBig); Grid.SetRow(hintBig, 1);
        previewCanvas.Child = pvGrid;
        rightGrid.Children.Add(previewCanvas); Grid.SetRow(previewCanvas, 1);
        page.Children.Add(rightGrid); Grid.SetColumn(rightGrid, 2);

        void UpdatePreview()
        {
            var bgC = PHex(cfg.BC, Color.FromRgb(45, 45, 45));
            var fgC = PHex(cfg.FC, Color.FromRgb(204, 204, 204));
            var hcC = PHex(cfg.HC, Color.FromRgb(51, 153, 255));
            var ocC = PHex(cfg.OC, Color.FromRgb(62, 62, 62));
            // 画布随主题明暗自适应：暗色主题配深画布、亮色配浅画布，预览边缘始终有对比
            double lum2 = (0.2126 * bgC.R + 0.7152 * bgC.G + 0.0722 * bgC.B) / 255.0;
            previewCanvas.Background = new SolidColorBrush(lum2 < 0.5 ? PHex("#3A3D42", Colors.Gray) : PHex("#CFD3D8", Colors.LightGray));
            var ps2 = new StackPanel();
            // 标题行（真实菜单：图标 + 名称）
            var ph2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 4) };
            ph2.Children.Add(IconR.Render("fa:Solid_LayerGroup", ActionType.Keys, cfg.FS + 2, cfg.IC));
            ph2.Children.Add(new TextBlock { Text = "  示例菜单", FontSize = cfg.FS + 2, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(fgC), VerticalAlignment = VerticalAlignment.Center });
            ps2.Children.Add(ph2);
            ps2.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(28, fgC.R, fgC.G, fgC.B)), Margin = new Thickness(8, 0, 8, 4) });
            // 置顶方格行：第 1 格呈悬停态、第 1/2 格带子菜单强调色角标（与真菜单同构）
            float ppw2 = (float)(cfg.PS > 0 ? cfg.PS : 20);
            var ppn = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 2, 10, 5) };
            for (int pi2 = 0; pi2 < 3; pi2++)
            {
                bool hov = pi2 == 0;
                var pg2 = new Grid();
                var pib = new Border { Width = ppw2, Height = ppw2, Background = new SolidColorBrush(Color.FromArgb((byte)(hov ? 90 : 26), hcC.R, hcC.G, hcC.B)), CornerRadius = new CornerRadius(3), Margin = new Thickness(1) };
                pib.Child = IconR.Render(pi2 == 2 ? "fa:Solid_Export" : "fa:Solid_Play", ActionType.Keys, ppw2 - 4, cfg.IC);
                pg2.Children.Add(pib);
                if (hov || pi2 == 1)
                {
                    double wg = Math.Max(6, ppw2 * 0.3);
                    var wedge2 = new System.Windows.Shapes.Path { Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, "M 0,{0} L {0},0 L {0},{0} Z", wg)), Fill = new SolidColorBrush(hcC), Width = wg, Height = wg, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
                    pg2.Children.Add(wedge2);
                }
                if (cfg.GlowK > 0 && hov) { try { pg2.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = cfg.GlowK / 100.0, Color = hcC }; } catch { } }
                var pg2h = pg2;
                pg2h.MouseEnter += (_, __) => { try { if (cfg.GlowK > 0) pg2h.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = cfg.GlowK / 100.0, Color = hcC }; } catch { } };
                pg2h.MouseLeave += (_, __) => { try { pg2h.Effect = null; } catch { } };
                if (cfg.ShowPinName)
                {
                    ppn.Children.Add(pg2);
                    ppn.Children.Add(new TextBlock { Text = new[] { "变换", "样式", "导出" }[pi2], FontSize = Math.Max(8, cfg.FS - 4), Foreground = new SolidColorBrush(Color.FromArgb(190, fgC.R, fgC.G, fgC.B)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2, 1, 2, 0) });
                }
                else ppn.Children.Add(pg2);
            }
            ps2.Children.Add(ppn);
            ps2.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(28, fgC.R, fgC.G, fgC.B)), Margin = new Thickness(8, 0, 8, 4) });
            // 列表行：普通 / 悬停态（高亮底+底部光带+子菜单箭头）/ 带快捷键
            string[][] rows2 = { new[] { "自由变换", "^T", "0" }, new[] { "图层样式", "›", "1" }, new[] { "导出为 PNG", "⇧E", "0" } };
            for (int ri = 0; ri < rows2.Length; ri++)
            {
                bool hovR = rows2[ri][2] == "1";
                double sheenK2 = Math.Max(0, Math.Min(100, cfg.SheenK)) / 100.0;
                var rg2 = new Grid();
                var tTb2 = new TextBlock { Text = rows2[ri][0], FontSize = cfg.FS, Foreground = new SolidColorBrush(fgC), VerticalAlignment = VerticalAlignment.Center };
                var kTb2 = new TextBlock { Text = rows2[ri][1], FontSize = cfg.FS - 1, Foreground = new SolidColorBrush(Color.FromArgb(hovR ? (byte)230 : (byte)150, fgC.R, fgC.G, fgC.B)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                rg2.Children.Add(tTb2); rg2.Children.Add(kTb2);
                var hovBr2 = new SolidColorBrush(Color.FromArgb(hovR ? (byte)70 : (byte)0, hcC.R, hcC.G, hcC.B));
                var pr2 = new Border { CornerRadius = new CornerRadius(Math.Max(0, Math.Min(12, cfg.CR))), Background = hovBr2, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, cfg.IS, 4, 1), Child = rg2 };
                var sh3 = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(2), Margin = new Thickness(4, 0, 4, 0), Opacity = hovR ? sheenK2 : 0d, Background = new System.Windows.Media.LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), GradientStops = { new System.Windows.Media.GradientStop(Color.FromArgb(0, hcC.R, hcC.G, hcC.B), 0), new System.Windows.Media.GradientStop(Color.FromArgb((byte)(230 * sheenK2), hcC.R, hcC.G, hcC.B), 0.5), new System.Windows.Media.GradientStop(Color.FromArgb(0, hcC.R, hcC.G, hcC.B), 1) } } };
                var pg3 = new Grid(); pg3.Children.Add(pr2); pg3.Children.Add(sh3);
                var bar3 = new Border { Height = Math.Max(2, Math.Min(14, cfg.UnderH)), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3), Opacity = hovR ? Math.Max(0, Math.Min(100, cfg.UnderK)) / 100.0 : 0d, IsHitTestVisible = false, Margin = new Thickness(8, 0, 8, 0), Background = new SolidColorBrush(Color.FromArgb(210, hcC.R, hcC.G, hcC.B)) };
                try { bar3.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 7 }; } catch { }
                pg3.Children.Add(bar3);
                pr2.MouseEnter += (_, __) =>
                {
                    try
                    {
                        hovBr2.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(70, hcC.R, hcC.G, hcC.B), TimeSpan.FromMilliseconds(100)));
                        sh3.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(sheenK2, TimeSpan.FromMilliseconds(100)));
                        bar3.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(Math.Max(0, Math.Min(100, cfg.UnderK)) / 100.0, TimeSpan.FromMilliseconds(120)));
                        kTb2.Foreground = new SolidColorBrush(Color.FromArgb(230, fgC.R, fgC.G, fgC.B));
                        if (cfg.GlowK > 0) pg3.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = cfg.GlowK / 100.0, Color = hcC };
                    }
                    catch { }
                };
                pr2.MouseLeave += (_, __) =>
                {
                    try
                    {
                        hovBr2.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0, hcC.R, hcC.G, hcC.B), TimeSpan.FromMilliseconds(150)));
                        sh3.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, TimeSpan.FromMilliseconds(160)));
                        bar3.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, TimeSpan.FromMilliseconds(200)));
                        kTb2.Foreground = new SolidColorBrush(Color.FromArgb(150, fgC.R, fgC.G, fgC.B));
                        pg3.Effect = null;
                    }
                    catch { }
                };
                if (cfg.GlowK > 0 && hovR) { try { pg3.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = cfg.GlowK / 100.0, Color = hcC }; } catch { } }
                ps2.Children.Add(pg3);
            }
            ps2.Children.Add(new Border { Height = 4, Background = Brushes.Transparent });
            // 切换/调节时的轻淡入：变化被"看见"而不是瞬间跳变
            // 切换/调节时的轻淡入：变化被"看见"而不是瞬间跳变。
            // 首次填充不淡入——离屏出图不推进动画，首帧会停在 Opacity 0 变成空面板（实测踩过）。
            bool hadPv = previewPanel.Child != null;
            try
            {
                if (hadPv) { ps2.Opacity = 0; ps2.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(160))); }
                else ps2.Opacity = 1;
            }
            catch { }
            previewPanel.Background = new SolidColorBrush(Color.FromArgb((byte)(cfg.Op * 255), bgC.R, bgC.G, bgC.B));
            previewPanel.BorderBrush = PB(cfg.OC, Color.FromRgb(62, 62, 62));
            previewPanel.Child = ps2;
            double pw = cfg.MW * cfg.Zoom;
            previewPanel.Width = Math.Min(282, pw > 120 ? pw : 120);
            previewPanel.LayoutTransform = null;
            if (Math.Abs(cfg.Zoom - 1) > 0.001) previewPanel.LayoutTransform = new ScaleTransform(cfg.Zoom, cfg.Zoom);
        }

        contentArea.Children.Add(page);
        UpdatePreview();
    }
    // ========== Page 4: Script Library（**只读**共享缓存；取数与刷新归独立动作「PS脚本库」）==========
    // ========== Page 4: Script Library（分类栏 + 行内预览；取数与刷新归独立动作「PS脚本库」）==========
    // ========== Page 4: Script Library（只看"本地在用的脚本" + 跳转到动作库）==========
    // 分工：库的浏览/刷新/挑脚本归独立动作「PS脚本库」；这里回答另一个问题——
    // 我菜单里用到的脚本有哪些、跟库里对不对得上、要不要存进库。
    // ========== Page 4: 环境检查（库连通 / PS 连接 / 诊断）==========
    // 这一页只回答一个问题：**这套东西现在还能不能用，不能的话卡在哪一环**。
    // 「浏览脚本库」不在这里——那是独立动作「PS脚本库」的活，A 只留一个"启动它"的按钮
    // （分工见开发说明 §19；A 的呼出路径永不联网是这个动作的底线，别在这一页加网络请求）。
    void BuildEnvPage()
    {
        var page = new StackPanel { Margin = new Thickness(Tk.S16) };
        page.Children.Add(new TextBlock { Text = "环境检查", FontSize = Tk.FTitle, FontWeight = FontWeights.SemiBold, Foreground = txtMain });
        page.Children.Add(new TextBlock { Text = "看脚本库和 Photoshop 这两条链路通不通。浏览、刷新、挑脚本都在独立动作「PS脚本库」里做。", FontSize = Tk.FSmall, Foreground = txtSub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Tk.S4, 0, Tk.S12) });

        string shotDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quicker", "ps_shots");
        string logPath = Path.Combine(Path.GetTempPath(), "ps_lib_error.log");

        // ---- 卡片一：脚本库（缓存有没有到手、是几时的）----
        // renderLib 先声明后赋值：下面的按钮 lambda 要用它（局部函数不能引用后面才声明的局部变量，CS0841）
        Action renderLib = null;
        var libStat = new TextBlock { FontSize = Tk.FSmall, Foreground = txtSub, TextWrapping = TextWrapping.Wrap };
        var libRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Tk.S10, 0, 0) };
        // 先声明后使用：下面「启动」按钮的回调里要切它的可见性（CS0841 的坑，别再改回去）
        var shareBtn = U.Btn("🔗 分享页", Th.TS, () => { try { OpenLibSharePage(); } catch (Exception ex) { LogFail("env.share", ex); } });
        shareBtn.Visibility = Visibility.Collapsed;
        libRow.Children.Add(U.Btn("📚 启动 PS脚本库", Th.AB, () =>
        {
            try
            {
                libStat.Text = "正在启动「PS脚本库」…";
                DateTime t0 = File.Exists(NetScripts.CachePath()) ? File.GetLastWriteTime(NetScripts.CachePath()) : DateTime.MinValue;
                LaunchScriptLib(ok => contentArea.Dispatcher.Invoke((Action)(() =>
                {
                    libStat.Text = ok
                        ? "已启动「PS脚本库」。它拉完库会写进缓存，这一页的脚本数会跟着变；要看内容就到它窗口里点「↻ 刷新脚本库」。"
                        : "没找到「PS脚本库」动作。要装的话点右边「🔗 分享页」，装好再回来点启动。";
                    shareBtn.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
                })));
                // 缓存被刷新（说明它真的拉回来了）就自动刷新这张卡，最多等 12 秒
                WatchCacheChange(t0, 12000, Window.GetWindow(contentArea), () =>
                {
                    NetScripts.Load();
                    renderLib();
                });
            }
            catch (Exception ex) { LogFail("env.openLib", ex); }
        }));
        libRow.Children.Add(new TextBlock { Text = "  " });
        libRow.Children.Add(U.Btn("📂 缓存文件夹", Th.TS, () => { try { Process.Start("explorer.exe", Path.GetDirectoryName(NetScripts.CachePath())); } catch (Exception ex) { LogFail("env.openCacheDir", ex); } }));
        // 只有"确实没装"才露出来（见上面启动回调），而且要用户自己点——启动流程里不自动开浏览器
        libRow.Children.Add(new TextBlock { Text = "  " });
        libRow.Children.Add(shareBtn);
        var libBox = new StackPanel();
        libBox.Children.Add(libStat); libBox.Children.Add(libRow);
        page.Children.Add(U.Card("脚本库", libBox));

        renderLib = () =>
        {
            var sb = new StringBuilder();
            string cp = NetScripts.CachePath();
            if (!File.Exists(cp))
                sb.Append("本机还没有脚本库缓存。点下面「启动 PS脚本库」，在它窗口里点一次「↻ 刷新脚本库」。");
            else
            {
                int shots = 0;
                try
                {
                    if (Directory.Exists(shotDir))
                        shots = Directory.GetFiles(shotDir, "*.png").Length + Directory.GetFiles(shotDir, "*.jpg").Length;
                }
                catch { }
                sb.Append("脚本 " + NetScripts.Count + " 个 · 分类 " + NetScripts.CatNames.Count + " 个 · 效果图 " + shots + " 张在本机");
                try { sb.Append("\n缓存文件：" + cp + "（" + File.GetLastWriteTime(cp).ToString("MM-dd HH:mm") + "）"); } catch { }
            }
            libStat.Text = sb.ToString();
        };

        // ---- 卡片二：Photoshop（进程 + 脚本接口）----
        var psStat = new TextBlock { FontSize = Tk.FSmall, Foreground = txtSub, TextWrapping = TextWrapping.Wrap };
        var psRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Tk.S10, 0, 0) };
        var psBox = new StackPanel();
        psBox.Children.Add(psStat); psBox.Children.Add(psRow);
        page.Children.Add(U.Card("Photoshop", psBox));

        // 分两拍：进程数立刻报，COM 探测丢后台（PS 正忙时那句调用会等，不能让界面陪着等）
        Action checkPs = () =>
        {
            int n = 0;
            try { n = Process.GetProcessesByName("Photoshop").Length; } catch { }
            if (n == 0)
            {
                psStat.Text = "Photoshop 没在运行 —— 脚本类菜单项点了会提示先开 PS，图层类型探测也无从谈起。";
                psStat.Foreground = Tk.B(Tk.Warn);
                return;
            }
            psStat.Text = "Photoshop 正在运行 · 检测脚本接口中…";
            psStat.Foreground = txtSub;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                bool com = false; string err = null;
                try { com = PS.Get() != null; } catch (Exception ex) { err = ex.Message; }
                try
                {
                    contentArea.Dispatcher.Invoke((Action)(() =>
                    {
                        if (com)
                        {
                            psStat.Text = "Photoshop 正在运行 · 脚本接口（COM）可用 —— 脚本菜单项和「▶ 试运行」都能发过去。";
                            psStat.Foreground = Tk.B(Tk.Ok);
                        }
                        else
                        {
                            psStat.Text = "Photoshop 进程在，但脚本接口取不到" + (err == null ? "" : "（" + err + "）") + "。重启一次 PS 通常就好。";
                            psStat.Foreground = Tk.B(Tk.Warn);
                        }
                    }));
                }
                catch { }
            });
        };
        psRow.Children.Add(U.Btn("↻ 重新检测", Th.TS, () => { try { checkPs(); } catch (Exception ex) { LogFail("env.checkPs", ex); } }));

        // ---- 卡片三：诊断（出错时才有内容的那个日志）----
        var diagStat = new TextBlock { FontSize = Tk.FSmall, Foreground = txtSub, TextWrapping = TextWrapping.Wrap };
        var diagRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Tk.S10, 0, 0) };
        diagRow.Children.Add(U.Btn("📄 打开日志", Th.TS, () => { try { if (File.Exists(logPath)) Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true }); else QkToast("还没有错误日志 —— 这通常是好事"); } catch (Exception ex) { LogFail("env.openLog", ex); } }));
        diagRow.Children.Add(new TextBlock { Text = "  " });
        diagRow.Children.Add(U.Btn("🧹 清空日志", Th.TS, () =>
        {
            try
            {
                if (File.Exists(logPath)) { File.WriteAllText(logPath, "", new UTF8Encoding(false)); QkToast("日志已清空"); }
                else QkToast("没有日志可清");
                renderLib();
            }
            catch (Exception ex) { LogFail("env.clearLog", ex); }
        }));
        var diagBox = new StackPanel();
        diagBox.Children.Add(diagStat); diagBox.Children.Add(diagRow);
        page.Children.Add(U.Card("诊断", diagBox));

        page.Children.Add(new TextBlock { Text = "版本 " + ActionVersion + " · 库动作 ID " + LibActionId, FontSize = Tk.FMicro, Foreground = txtSub, Margin = new Thickness(0, Tk.S12, 0, 0) });

        void RenderDiag()
        {
            var sb = new StringBuilder();
            try
            {
                if (File.Exists(logPath))
                {
                    var fi = new FileInfo(logPath);
                    sb.Append("异常日志：" + logPath + "（" + Math.Max(1, fi.Length / 1024) + " KB · " + fi.LastWriteTime.ToString("MM-dd HH:mm") + "）");
                    sb.Append("\n里面记的是脚本库相关操作出错的原因，反馈问题时把它带上最有用。");
                }
                else sb.Append("没有错误日志 —— 说明到目前为止没出过错（有异常时才会写这个文件）。");
            }
            catch (Exception ex) { sb.Append("日志状态读不出来：" + ex.Message); }
            diagStat.Text = sb.ToString();
        }

        // ---- 卡片四：关于（应用名/版本/简介 + 配置目录与日志入口；排版压成两行，避免窄窗下按钮被截）----
        string cfgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMenu");
        string appLogPath = Path.Combine(Path.GetTempPath(), "ps_cm_error.log");
        var aboutTitle = new TextBlock { Text = "PS便捷菜单 独立版 · " + ActionVersion + " —— Photoshop 右键上下文菜单 · 独立常驻版", FontSize = Tk.FSmall, Foreground = txtMain, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        var aboutRow = new StackPanel { Orientation = Orientation.Horizontal };
        aboutRow.Children.Add(aboutTitle);
        aboutRow.Children.Add(new TextBlock { Text = "    " });
        aboutRow.Children.Add(U.Btn("📂 打开配置目录", Th.TS, () => { try { Process.Start("explorer.exe", cfgDir); } catch (Exception ex) { LogFail("env.openCfgDir", ex); } }));
        aboutRow.Children.Add(new TextBlock { Text = "  " });
        aboutRow.Children.Add(U.Btn("📄 打开日志", Th.TS, () => { try { Process.Start("explorer.exe", File.Exists(appLogPath) ? appLogPath : Path.GetDirectoryName(appLogPath)); } catch (Exception ex) { LogFail("env.openAppLog", ex); } }));
        var aboutBox = new StackPanel();
        aboutBox.Children.Add(aboutRow);
        page.Children.Add(U.Card("关于", aboutBox));

        contentArea.Children.Add(page);
        renderLib(); RenderDiag(); checkPs();
    }




    // ========== Page 4: 启动与触发 ==========
    // 把散落在外壳托盘里的触发设置（全局热键 / PS 前台中键唤出 / 开机自启）收进编辑器统一管理。
    // 数据源是 ShellConfig（%APPDATA%\PSMenu\shell_state.json）+ 注册表 Run 键，与托盘菜单同源，
    // 改完立即生效（热键走 ShellHost.ApplyHotKey，开关走 ShellHost.NotifySettingsChanged 同步托盘勾选态）。
    void BuildTriggerPage()
    {
        var page = new StackPanel { Margin = new Thickness(Tk.S16) };
        page.Children.Add(new TextBlock { Text = "启动与触发", FontSize = Tk.FTitle, FontWeight = FontWeights.SemiBold, Foreground = txtMain });
        page.Children.Add(new TextBlock { Text = "呼出菜单的几种方式都在这里。改动立即生效，不用重启；托盘菜单里也有同样的开关，两边状态实时同步。", FontSize = Tk.FSmall, Foreground = txtSub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Tk.S4, 0, Tk.S12) });

        // ---- 卡片一：全局热键 ----
        var hkText = new TextBlock { Text = ShellConfig.HotkeyText(ShellConfig.HkMod, ShellConfig.HkVk), FontSize = Tk.FBody, FontWeight = FontWeights.SemiBold, Foreground = txtMain, VerticalAlignment = VerticalAlignment.Center, MinWidth = 140 };
        var hkRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Tk.S10, 0, 0) };
        hkRow.Children.Add(hkText);
        hkRow.Children.Add(new TextBlock { Text = "  " });
        hkRow.Children.Add(U.Btn("⌨ 修改…", Th.AB, () =>
        {
            int nm, nv;
            if (!RecordHotkey(out nm, out nv)) return;   // Esc / 关窗 = 取消，什么都不改
            string err = ShellHost.ApplyHotKey(nm, nv);
            if (err != null)
            {
                ShowInfo("热键被占用", err + "\n\n想注册的组合键：" + ShellConfig.HotkeyText(nm, nv) + "\n当前仍生效：" + ShellConfig.HotkeyText(ShellConfig.HkMod, ShellConfig.HkVk));
                return;
            }
            ShellConfig.HkMod = nm; ShellConfig.HkVk = nv;
            try { ShellConfig.Save(); } catch (Exception ex) { LogFail("trigger.hkSave", ex); }
            hkText.Text = ShellConfig.HotkeyText(nm, nv);
            QkToast("全局热键已改为 " + ShellConfig.HotkeyText(nm, nv));
        }));
        page.Children.Add(U.Card("全局热键", new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "任何程序在前台时按这个组合键都能呼出菜单（不用先切到 Photoshop）。", FontSize = Tk.FSmall, Foreground = txtSub, TextWrapping = TextWrapping.Wrap },
                hkRow,
            }
        }));

        // ---- 卡片二：PS 前台中键唤出（与托盘菜单同一数据源）----
        var midCb = new CheckBox
        {
            Content = "在 Photoshop 前台按下鼠标中键唤出菜单",
            IsChecked = ShellConfig.MiddleCall,
            FontSize = Tk.FBody, Foreground = txtMain, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        midCb.Click += (_, __) =>
        {
            bool on = midCb.IsChecked == true;
            if (on == ShellConfig.MiddleCall) return;
            ShellConfig.MiddleCall = on;
            try { ShellConfig.Save(); } catch (Exception ex) { LogFail("trigger.midSave", ex); }
            ShellHost.NotifySettingsChanged();   // 让托盘菜单的勾选态跟着刷新
        };
        page.Children.Add(U.Card("PS 前台中键唤出", new StackPanel
        {
            Children =
            {
                midCb,
                new TextBlock { Text = "开着的时候，鼠标中键在 Photoshop 里不会被透传（那次点击由菜单接管）。", FontSize = Tk.FSmall, Foreground = txtSub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Tk.S8, 0, 0) },
            }
        }));

        // ---- 卡片三：开机自启（与托盘共用注册表读写）----
        var auCb = new CheckBox
        {
            Content = "开机自动启动（常驻托盘，随时呼出菜单）",
            IsChecked = ShellHost.AutostartEnabled(),
            FontSize = Tk.FBody, Foreground = txtMain, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        auCb.Click += (_, __) =>
        {
            ShellHost.SetAutostart(auCb.IsChecked == true);
            auCb.IsChecked = ShellHost.AutostartEnabled();   // 以注册表实际结果为准（写失败会弹不出提示但勾选会弹回去）
        };
        page.Children.Add(U.Card("开机自启", new StackPanel
        {
            Children =
            {
                auCb,
                new TextBlock { Text = "启动项命令行（HKCU\\...\\Run）：", FontSize = Tk.FMicro, Foreground = txtSub, Margin = new Thickness(0, Tk.S10, 0, 2) },
                new TextBlock { Text = ShellHost.AutostartCommand(), FontSize = Tk.FMicro, FontFamily = new FontFamily("Consolas"), Foreground = txtSub, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap },
            }
        }));

        contentArea.Children.Add(page);
    }

    // 热键录制小窗：按下组合键实时显示；非修饰键按下即确认；Esc / 直接关窗取消。
    // 录的是 RegisterHotKey 的修饰位+VK（注意与 RecordShortcut 的 PS 键语法是两码事，别混用）。
    static bool RecordHotkey(out int outMod, out int outVk)
    {
        outMod = 0; outVk = 0;
        int gotMod = 0, gotVk = 0; bool done = false;
        var w = new Window { Title = "录制全局热键", Width = 420, Height = 140, WindowStartupLocation = WindowStartupLocation.CenterScreen, WindowStyle = WindowStyle.ToolWindow, Background = Th.BBR, Topmost = true, ShowInTaskbar = false };
        var disp = new TextBlock { FontSize = Tk.FNav, Foreground = Th.BTP, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(16, 0, 16, 0) };
        w.Content = disp;
        w.Loaded += (_, __) => { w.Activate(); Keyboard.Focus(w); };
        w.PreviewKeyDown += (_, e) =>
        {
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (k == Key.Escape) { e.Handled = true; w.Close(); return; }   // Esc 取消
            var mk = Keyboard.Modifiers;
            int mod = ((mk & ModifierKeys.Alt) != 0 ? 1 : 0) | ((mk & ModifierKeys.Control) != 0 ? 2 : 0) |
                      ((mk & ModifierKeys.Shift) != 0 ? 4 : 0) | ((mk & ModifierKeys.Windows) != 0 ? 8 : 0);
            bool isMod = k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftShift || k == Key.RightShift ||
                         k == Key.LeftAlt || k == Key.RightAlt || k == Key.LWin || k == Key.RWin;
            if (isMod)
            {
                // 只按住修饰键：实时回显，不确认
                disp.Text = "已按住 " + ShellConfig.HotkeyText(mod, 0) + " …\n再按一个普通键完成录制（Esc 取消）";
                e.Handled = true;
                return;
            }
            // 非修饰键按下即确认（RegisterHotKey 允许不带修饰键的组合，这里不做强制）
            gotMod = mod;
            try { gotVk = KeyInterop.VirtualKeyFromKey(k); }
            catch { e.Handled = true; return; }
            done = true;
            e.Handled = true;
            w.Close();
        };
        disp.Text = "请按下新的组合键…\n（修饰键 Ctrl / Alt / Shift / Win + 一个普通键；Esc 取消）";
        w.ShowDialog();
        if (!done) return false;
        outMod = gotMod; outVk = gotVk;
        return true;
    }

    // 自绘单行输入小窗（风格贴 Confirm/ShowInfo，同一套圆角浅底）：返回输入文本（已 Trim），
    // 取消 / Esc / 关窗返回 null。validate 返回 null = 合法，否则为错误文案（显示在输入框下方）。
    static string PromptText(string title, string label, Func<string, string> validate)
    {
        string result = null;
        try
        {
            var ink = new SolidColorBrush(PHex("#1F2328", Colors.Black));
            var dim = new SolidColorBrush(PHex("#3A3F47", Colors.Gray));
            var acc = new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue));
            var w = new Window { Title = title, Width = 380, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
            var bg = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(PHex("#EEF0F3", Colors.Gray)), Padding = new Thickness(18), BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), BorderThickness = new Thickness(1) };
            var grid = new Grid();
            for (int ri = 0; ri < 5; ri++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = ink, Margin = new Thickness(0, 0, 0, 8) });
            grid.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = dim, TextWrapping = TextWrapping.Wrap });
            Grid.SetRow(grid.Children[grid.Children.Count - 1], 1);
            var tb = new TextBox { FontSize = 13, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(8, 6, 8, 6), Background = Brushes.White, BorderBrush = new SolidColorBrush(PHex("#C6CBD2", Colors.Gray)), Foreground = ink };
            grid.Children.Add(tb); Grid.SetRow(tb, 2);
            var err = new TextBlock { Text = "", FontSize = 11, Foreground = new SolidColorBrush(PHex("#D14343", Colors.Firebrick)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            grid.Children.Add(err); Grid.SetRow(err, 3);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var cancel = new Button { Content = "取消", FontSize = 12, MinWidth = 84, Height = 30, Cursor = Cursors.Hand, Foreground = dim, Background = new SolidColorBrush(PHex("#E4E7EA", Colors.LightGray)), BorderBrush = Brushes.Transparent };
            var ok = new Button { Content = "保存", FontSize = 12, MinWidth = 84, Height = 30, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Foreground = Brushes.White, Background = acc, BorderBrush = Brushes.Transparent };
            row.Children.Add(cancel); row.Children.Add(ok);
            grid.Children.Add(row); Grid.SetRow(row, 4);
            ok.Click += (_, __) =>
            {
                string e2 = validate != null ? validate(tb.Text) : null;
                if (e2 != null) { err.Text = e2; return; }
                result = (tb.Text ?? "").Trim();
                try { w.Close(); } catch { }
            };
            cancel.Click += (_, __) => { try { w.Close(); } catch { } };
            tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } };
            bg.Child = grid; w.Content = bg;
            w.KeyDown += (_, e) => { if (e.Key == Key.Escape) { result = null; try { w.Close(); } catch { } } };
            w.Owner = Application.Current?.Windows?.OfType<Window>().FirstOrDefault(x => (x.Title ?? "").Contains("PS Context Menu"));
            w.Opacity = 0d; w.Loaded += (_, __) => { try { w.Activate(); tb.Focus(); tb.SelectAll(); w.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(140))); } catch { } };
            w.ShowDialog();
        }
        catch { }
        return result;
    }

    // ========== Page 3: Config Management ==========
    void BuildConfigPage()
    {
        var cfgPage = new StackPanel { Margin = new Thickness(16) };
        var cfTitle = new TextBlock { Text = "配置管理", FontSize = Tk.FTitle, FontWeight = FontWeights.SemiBold, Foreground = Th.BTP, Margin = new Thickness(0, 2, 0, 10) };
        cfgPage.Children.Add(cfTitle);
        cfgPage.Children.Add(new TextBlock { Text = "导出、导入或恢复配置。配置存储在本地 Quicker 数据目录中。", FontSize = Tk.FSmall, Foreground = Th.BTS, Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap });

        // ---- 卡片〇：配置方案（多套完整配置的保存与快速切换，数据在 %APPDATA%\PSMenu\profiles\）----
        var profWrap = new StackPanel();
        var profCur = new TextBlock { FontSize = Tk.FSmall, FontWeight = FontWeights.SemiBold, Foreground = Th.BTP, Margin = new Thickness(0, 0, 0, 8) };
        Action RefreshProfiles = null;
        RefreshProfiles = () =>
        {
            profWrap.Children.Clear();
            string cur = ConfigService.Profiles.Current();
            profCur.Text = "当前方案：" + cur;
            foreach (var pn in ConfigService.Profiles.List())
            {
                string nm = pn;
                var prow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                prow.Children.Add(new TextBlock { Text = nm + (nm == cur ? "   ✓" : ""), FontSize = Tk.FSmall, Foreground = Th.BTP, VerticalAlignment = VerticalAlignment.Center, Width = 170, TextTrimming = TextTrimming.CharacterEllipsis });
                if (nm != cur)
                {
                    prow.Children.Add(U.Btn("切换", Th.AB, () =>
                    {
                        if (isDirty) MkClean();   // 编辑器里改的东西保存时属于当前方案：先把在改的落盘
                        if (!ConfigService.Profiles.SwitchTo(nm)) { ShowInfo("切换失败", "方案「" + nm + "」的配置文件不可用或已被删除。"); return; }
                        CopyCfgFrom(ConfigService.Load());
                        MkDirty();   // 之后 Ctrl+S / 关窗保存都会写进新方案（ps_cm_config.json 已是新内容）
                        RefreshProfiles();
                        QkToast("已切换到方案「" + nm + "」");
                    }, 11));
                }
                else prow.Children.Add(new TextBlock { Text = "（使用中）", FontSize = Tk.FSmall, Foreground = Th.BTS, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                if (nm != ConfigService.Profiles.DefaultName)
                {
                    prow.Children.Add(new TextBlock { Text = "  ", FontSize = Tk.FSmall });
                    prow.Children.Add(U.Btn("🗑 删除", Th.TS, () =>
                    {
                        if (Confirm("删除方案", "删掉方案「" + nm + "」？它的配置文件会被删除，不可恢复。", new[] { "删除", "取消" }, 1, 0, "#D14343") != 0) return;
                        if (!ConfigService.Profiles.Delete(nm)) { QkToast("删除失败（文件可能正被占用）", "Warning"); return; }
                        if (ConfigService.Profiles.Current() == ConfigService.Profiles.DefaultName && cur == nm)
                        { CopyCfgFrom(ConfigService.Load()); MkDirty(); }   // 删的是当前方案：内容不变，方案名切回默认
                        RefreshProfiles();
                        QkToast("已删除方案「" + nm + "」");
                    }, 11));
                }
                profWrap.Children.Add(prow);
            }
        };
        var profBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        profBtns.Children.Add(U.Btn("💾 另存为新方案…", Th.AG, () =>
        {
            string nm = PromptText("另存为新方案", "方案名（同时是配置文件的名字）：", s =>
            {
                string t = (s ?? "").Trim();
                if (string.IsNullOrEmpty(t)) return "方案名不能为空";
                if (t == ConfigService.Profiles.DefaultName) return "「默认」是保留名，换一个吧";
                if (t.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "名字里含有文件名不允许的字符";
                return null;
            });
            if (nm == null) return;   // 取消 / 关窗
            if (isDirty) MkClean();
            if (!ConfigService.Profiles.SaveAs(nm)) { ShowInfo("另存失败", "方案「" + nm + "」已经存在，换一个名字。"); RefreshProfiles(); return; }
            RefreshProfiles();
            QkToast("已保存为方案「" + nm + "」，当前配置不会再影响原方案");
        }));
        var profBox = new StackPanel();
        profBox.Children.Add(profCur);
        profBox.Children.Add(profWrap);
        profBox.Children.Add(profBtns);
        cfgPage.Children.Add(U.Card("配置方案", profBox));
        RefreshProfiles();

        cfgPage.Children.Add(U.Card("配置操作", new StackPanel { Orientation = Orientation.Horizontal, Children = { U.Btn("📋 导出配置", Th.AB, () => { ClipHelper.Set(AppConfigSerializer.ToJson(cfg)); QkToast("配置已复制到剪贴板"); }), new TextBlock { Text = "  " }, U.Btn("📥 导入配置", Th.AG, () => { try { string cl = ClipHelper.Get(); if (!string.IsNullOrEmpty(cl)) { var imp = AppConfigSerializer.FromJson(cl); if (imp != null && imp.Menus.Count > 0)
{
    if (imp.MW < 150 || imp.MW > 700) imp.MW = cfg.MW;
    if (imp.FS < 8 || imp.FS > 30) imp.FS = cfg.FS;
    if (imp.Zoom < 0.4 || imp.Zoom > 3.2) imp.Zoom = cfg.Zoom;
    int im2 = Confirm("导入配置", "选择导入方式：\n· 覆盖同名：用导入的内容替换该图层类型下的同名菜单\n· 追加合并：把导入的菜单项接到已有菜单后面", new[] { "覆盖同名", "追加合并", "取消" }, 2, 0);
    if (im2 == 2) return;
    PushUndo(); ConfigService.Snapshot("before-import");
    foreach (var m in imp.Menus)
    {
        var em = cfg.Menus.FirstOrDefault(x => x.Kind == m.Kind);
        if (em != null)
        {
            if (im2 == 0) { em.Label = m.Label; em.Icon = m.Icon; em.Items = m.Items; }
            else em.Items.AddRange(m.Items);
        }
        else cfg.Menus.Add(m);
    }
 cfg.Pinned = imp.Pinned ?? new List<PI>(); cfg.MW = imp.MW; cfg.FS = imp.FS; cfg.Zoom = imp.Zoom; cfg.PS = imp.PS; cfg.IS = imp.IS; cfg.Op = imp.Op; cfg.BC = imp.BC ?? "#2D2D2D"; cfg.FC = imp.FC ?? "#CCCCCC"; cfg.HC = imp.HC ?? "#094771"; cfg.OC = imp.OC ?? "#3E3E3E"; cfg.IC = imp.IC ?? ""; cfg.FreqSort = imp.FreqSort; cfg.ShowFreq = imp.ShowFreq; cfg.ShowPinName = imp.ShowPinName; cfg.Theme = imp.Theme ?? "psdark"; MkDirty(); SwitchNav(0); } } } catch { ShowInfo("导入失败", "剪贴板内容不是合法的配置 JSON。"); } }), new TextBlock { Text = "  " }, U.Btn("🔄 恢复默认", Th.AR, () => { if (Confirm("恢复默认", "所有菜单配置都会回到默认值，此操作不可撤销。\n（会先自动留一份快照，改坏了能回滚）", new[] { "恢复默认", "取消" }, 1, 0, "#D14343") == 0) { PushUndo(); ConfigService.Snapshot("before-restore-default"); var def = ConfigService.Default(); cfg.Menus = def.Menus; cfg.Pinned = def.Pinned ?? new List<PI>(); cfg.MW = def.MW; cfg.FS = def.FS; cfg.Zoom = def.Zoom; cfg.PS = def.PS; cfg.IS = def.IS; cfg.Op = def.Op; cfg.BC = def.BC; cfg.FC = def.FC; cfg.HC = def.HC; cfg.OC = def.OC; cfg.IC = def.IC ?? ""; cfg.FreqSort = def.FreqSort; cfg.ShowFreq = def.ShowFreq; cfg.ShowPinName = def.ShowPinName; cfg.Theme = def.Theme; MkDirty(); SwitchNav(0); } }) } }
        ));

        // 快照回滚：每次保存/导入/恢复默认之前自动留一份，改坏了能一键回来
        var snapWrap = new StackPanel();
        Action RefreshSnaps = null;
        RefreshSnaps = () =>
        {
            snapWrap.Children.Clear();
            var list = ConfigService.SnapList();
            if (list.Count == 0)
            {
                snapWrap.Children.Add(new TextBlock { Text = "还没有快照：在编辑器里保存一次（或导入/恢复默认一次）就会自动出现。", FontSize = Tk.FSmall, Foreground = Th.BTS, TextWrapping = TextWrapping.Wrap });
                return;
            }
            foreach (var fp in list)
            {
                string path = fp;
                string nm = Path.GetFileNameWithoutExtension(fp) ?? "";
                string when = nm.Length >= 15
                    ? nm.Substring(0, 4) + "-" + nm.Substring(4, 2) + "-" + nm.Substring(6, 2) + " " + nm.Substring(9, 2) + ":" + nm.Substring(11, 2) + ":" + nm.Substring(13, 2)
                    : nm;
                string tag = nm.Length > 16 ? nm.Substring(16) : "";
                var srow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                srow.Children.Add(new TextBlock { Text = when + (string.IsNullOrEmpty(tag) ? "" : "   · " + tag), FontSize = Tk.FSmall, Foreground = Th.BTP, VerticalAlignment = VerticalAlignment.Center, Width = 250 });
                srow.Children.Add(U.Btn("↩ 回滚到这份", Th.AR, () =>
                {
                    if (Confirm("回滚配置", "用这份快照覆盖当前菜单配置？\n（回滚前会自动再把当前状态留一份，能反悔）", new[] { "回滚", "取消" }, 1, 0, "#D14343") != 0) return;
                    if (ConfigService.RestoreSnap(path))
                    {
                        CopyCfgFrom(ConfigService.Load());
                        MkDirty(); RefreshSnaps();
                        ShowInfo("已回滚", "当前配置已替换为这份快照，改动记得保存（Ctrl+S）。");
                    }
                    else ShowInfo("回滚失败", "快照文件不可用或已被删除。");
                }, 11));
                // 自动档攒得快，不需要的得能删（用户要求：每条右侧一个删除键）
                srow.Children.Add(new TextBlock { Text = "  ", FontSize = Tk.FSmall });
                srow.Children.Add(U.Btn("🗑 删除", Th.TS, () =>
                {
                    if (Confirm("删除快照", "删掉这份快照？\n（只删历史备份，不影响当前配置）", new[] { "删除", "取消" }, 1, 0, "#D14343") != 0) return;
                    if (ConfigService.DeleteSnap(path)) { RefreshSnaps(); QkToast("已删除该快照"); }
                    else QkToast("删除失败（文件可能正被占用）", "Warning");
                }, 11));
                snapWrap.Children.Add(srow);
            }
        };
        RefreshSnaps();
        cfgPage.Children.Add(U.Card("配置快照（自动留最近 " + ConfigService.KeepSnaps + " 份）", snapWrap));

        cfgPage.Children.Add(U.Card("存储位置", new TextBlock { Text = ConfigService.P2, FontSize = Tk.FSmall, FontFamily = new FontFamily("Consolas"), Foreground = Th.BTS, TextWrapping = TextWrapping.Wrap }
        ));

        contentArea.Children.Add(cfgPage);
    }


    // Init
    SwitchNav(0);

    // 恢复上次记住的界面字号档位
    int tier0 = Math.Max(0, Math.Min(fsLv.Length - 1, cfg.FsTier));
    ApplyFontScale(fsLv[tier0].Item2, tier0);

    // Keyboard shortcuts
    ed.KeyDown += (_, e) =>
    {
                if (!(Keyboard.FocusedElement is TextBox) && !(Keyboard.FocusedElement is ComboBox))
        {
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { DoUndo(); e.Handled = true; }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) { DoRedo(); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { DoRedo(); e.Handled = true; }
        }
        if (activeNav == 0 && layerShortcutHandler != null && !(Keyboard.FocusedElement is TextBox) && !(Keyboard.FocusedElement is ComboBox))
        {
            bool isDel = e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None;
            bool isD = e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control;
            bool isN = e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control;
            if (isDel || isD || isN) { layerShortcutHandler(e.Key, Keyboard.Modifiers); e.Handled = true; }
        }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { if (searchBox != null) { searchBox.Focus(); searchBox.SelectAll(); } e.Handled = true; }
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { MkClean(); e.Handled = true; }
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && searchBox != null && searchBox.IsKeyboardFocused) { searchBox.Clear(); e.Handled = true; }
        else if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) TryClose();
    };
    ed.Closing += (_, e) =>
    {
        if (closingFromCode) { closingFromCode = false; return; }
        if (isDirty) { int dr = Confirm("有未保存的更改", "关闭这个窗口前先保存吗？\n保存会写进配置，并自动留一份可回滚的快照。", new[] { "保存并关闭", "不保存", "取消" }, 2, 0); if (dr == 0) MkClean(); else if (dr != 1) e.Cancel = true; }
    };

    // DWM blur
    try { var hi = new WindowInteropHelper(ed); hi.EnsureHandle(); var dbb = new DWM_BLURBEHIND { fEnable = true, dwFlags = DWM_BLURBEHIND.DWM_BB_ENABLE }; DwmEnableBlurBehindWindow(hi.Handle, ref dbb); var mg2 = new MARGINS { leftWidth = -1, rightWidth = -1, topHeight = -1, bottomHeight = -1 }; DwmExtendFrameIntoClientArea(hi.Handle, ref mg2); } catch { }

    // 第一次打开设置：自动弹一次使用帮助（之后不再弹，标志位存在 ps_cm_recent.json）
    if (!Recent.HelpShown)
    {
        Recent.HelpShown = true;
        try { Recent.Save(); } catch { }
        var hd = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        hd.Tick += (_, __) => { hd.Stop(); try { ShowHelp(); } catch { } };
        ed.Loaded += (_, __) => hd.Start();
    }
    // ---- 调试出图：把设置窗整窗渲染成 PNG（不弹窗），见 Exec 的 shot=config;page=N ----
    if (!string.IsNullOrEmpty(shot))
    {
        try
        {
            // 置顶条在 BuildLayerMenuPage 里（RefreshPinStrip 是那层的局部函数，这里够不着），
            // 但 SwitchNav 每次都会重建页面 → 里面的 RefreshPinStrip 会读到上面这个字段，所以先设再切页。
            pinStripShot = shot;
            string pg0 = ShotField(shot, "page");
            int pi0;
            if (!string.IsNullOrEmpty(pg0) && int.TryParse(pg0, out pi0)) SwitchNav(Math.Max(0, Math.Min(4, pi0)));
            try { ScaleText(edHost, fz); } catch { }
            try { ed.Content = null; } catch { }            // 先摘下来：一个元素只能有一个逻辑父级
            var host = new Grid { Background = bgBrush, Width = BASE_W, Height = BASE_H };
            host.Children.Add(edHost);
            host.Measure(new Size(BASE_W, BASE_H));
            host.Arrange(new Rect(new Point(0, 0), new Size(BASE_W, BASE_H)));
            host.UpdateLayout();
            int sw4 = (int)Math.Ceiling(BASE_W), sh4 = (int)Math.Ceiling(BASE_H);
            var rtb4 = new RenderTargetBitmap(sw4, sh4, 96, 96, PixelFormats.Pbgra32);
            rtb4.Render(host);
            var enc4 = new PngBitmapEncoder(); enc4.Frames.Add(BitmapFrame.Create(rtb4));
            string sdir4 = Path.Combine(Path.GetTempPath(), "ps_cm_shots"); Directory.CreateDirectory(sdir4);
            string fp4 = Path.Combine(sdir4, "config_p" + activeNav + "_" + DateTime.Now.ToString("HHmmssfff") + ".png");
            using (var fs4 = File.Create(fp4)) enc4.Save(fs4);
            return "SHOT " + fp4 + "  " + sw4 + "x" + sh4;
        }
        catch (Exception ex) { return "SHOT-ERR " + ex.Message; }
    }
    // 编辑器呼出弹入动画（与菜单/挑选窗同语感，400ms 兜底）
    try { ed.Opacity = 0; } catch { }
    bool _poped = false;
    ed.ContentRendered += (_, __) => { if (_poped) return; _poped = true;
        try {
            var sced = new ScaleTransform(0.98, 0.98); ed.RenderTransform = sced; ed.RenderTransformOrigin = new Point(0.5, 0.5);
            var eaed = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            sced.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1d, TimeSpan.FromMilliseconds(160)) { EasingFunction = eaed });
            sced.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, 1d, TimeSpan.FromMilliseconds(160)) { EasingFunction = eaed });
            ed.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(130)));
        } catch { try { ed.Opacity = 1; } catch { } } };
    var popGed = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
    popGed.Tick += (s2, e2) => { popGed.Stop(); try { if (ed.Opacity < 0.05) { ed.Opacity = 1; ed.RenderTransform = null; } } catch { } };
    popGed.Start();
    ed.ShowDialog();
    // 注：Th 这套调色板只服务编辑器外壳与几个对话框，菜单用的是 cfg.* —— 所以退出编辑器后不必"恢复主题"
    return null;
}

}
}

