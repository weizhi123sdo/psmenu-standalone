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
// #region 编辑菜单项（盒子卡片风 · 分型编辑 · 图标选择器）
static readonly string[] FaIconChoices = {
    "fa:Solid_Play","fa:Solid_Pause","fa:Solid_Stop","fa:Solid_Forward","fa:Solid_Backward",
    "fa:Solid_ChevronDown","fa:Solid_ChevronUp","fa:Solid_ChevronLeft","fa:Solid_ChevronRight","fa:Solid_ArrowDown",
    "fa:Solid_ArrowUp","fa:Solid_ArrowLeft","fa:Solid_ArrowRight","fa:Solid_Repeat","fa:Solid_Undo",
    "fa:Solid_Redo","fa:Solid_Rotate","fa:Solid_Expand","fa:Solid_Compress","fa:Solid_Crop",
    "fa:Solid_Magic","fa:Solid_Sliders","fa:Solid_SlidersH","fa:Solid_Palette","fa:Solid_PaintBrush",
    "fa:Solid_FillDrip","fa:Solid_Eyedropper","fa:Solid_LayerGroup","fa:Solid_LayerMinus","fa:Solid_LayerPlus",
    "fa:Solid_Clone","fa:Solid_Copy","fa:Solid_Paste","fa:Solid_Cut","fa:Solid_Clipboard",
    "fa:Solid_Trash","fa:Solid_Pen","fa:Solid_Check","fa:Solid_Times","fa:Solid_Plus",
    "fa:Solid_Minus","fa:Solid_Star","fa:Solid_Heart","fa:Solid_Bookmark","fa:Solid_Flag",
    "fa:Solid_Tag","fa:Solid_Thumbtack","fa:Solid_MapPin","fa:Solid_Bolt","fa:Solid_Cog",
    "fa:Solid_Key","fa:Solid_Lock","fa:Solid_LockOpen","fa:Solid_Folder","fa:Solid_FolderOpen",
    "fa:Solid_File","fa:Solid_FileExport","fa:Solid_FileImport","fa:Solid_Image","fa:Solid_Camera",
    "fa:Solid_ExternalLink","fa:Solid_Link","fa:Solid_Search","fa:Solid_Print","fa:Solid_Keyboard",
    "fa:Solid_Font","fa:Solid_TextHeight","fa:Solid_AlignLeft","fa:Solid_AlignCenter","fa:Solid_AlignRight",
    "fa:Solid_Terminal","fa:Solid_Code","fa:Solid_Desktop","fa:Solid_WindowMaximize","fa:Solid_Upload",
    "fa:Solid_Download","fa:Solid_Share","fa:Solid_Inbox","fa:Solid_Bell","fa:Solid_Moon",
    "fa:Solid_Sun","fa:Solid_Cloud","fa:Solid_Database","fa:Solid_ChartLine","fa:Solid_Eye",
    "fa:Solid_EyeSlash","fa:Solid_BorderAll","fa:Solid_Stream","fa:Solid_Blender","fa:Regular_Image",
    "fa:Regular_Copy","fa:Regular_Clipboard","fa:Regular_File","fa:Regular_Folder","fa:Regular_Moon",
    "fa:Regular_Star","fa:Regular_Heart","fa:Regular_Comment","fa:Regular_Bell","fa:Solid_Box"
};

public static readonly string[] AllIconChoices =
    ((FontAwesome5.EFontAwesomeIcon[])System.Enum.GetValues(typeof(FontAwesome5.EFontAwesomeIcon))).Select(x => "fa:" + x.ToString()).ToArray();
public static readonly List<string> IconRecent = new List<string>();

static string PickIconDialog(string current)
{
    string chosen = current ?? "";
    var w = new Window { Width = 780, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize };
    var bgc = new SolidColorBrush(PHex("#EEF0F3", Colors.Gray));
    var cardc = new SolidColorBrush(PHex("#FFFFFF", Colors.White));
    var linec = new SolidColorBrush(PHex("#E4E7EA", Colors.Gray));
    var txtM = new SolidColorBrush(PHex("#1F2328", Colors.Black));
    var txtS = new SolidColorBrush(PHex("#6B7280", Colors.Gray));
    var border = new Border { CornerRadius = new CornerRadius(12), Background = bgc, Padding = new Thickness(14) };
    var grid = new Grid();
    grid.Children.Add(new TextBlock { Text = "选择图标", FontSize = Tk.FTitle, FontWeight = FontWeights.SemiBold, Foreground = txtM, Margin = new Thickness(2, 0, 0, 8) });
    // 行结构：0 标题 / 1 搜索行 / 2 选项行 / 3 列表区(Star 撑满) / 4 底栏
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition());
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    var q = new TextBox { FontSize = Tk.FBody, Padding = new Thickness(8, 6, 8, 6), BorderBrush = linec, BorderThickness = new Thickness(1), Background = cardc, Foreground = txtM, MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
    var qRow = new DockPanel { LastChildFill = true };
    qRow.Children.Add(q);
    grid.Children.Add(qRow); Grid.SetRow(qRow, 1);
    var optRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 7, 0, 1) };
    grid.Children.Add(optRow); Grid.SetRow(optRow, 2);
    // 列表区 = 两栏：左分类栏（来源 + 分组，脚本库同款语言）/ 右图标网格
    var listGrid = new Grid();
    listGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
    listGrid.ColumnDefinitions.Add(new ColumnDefinition());
    var railHost = new StackPanel();
    var railCard = new Border { CornerRadius = new CornerRadius(Tk.RMd), Background = cardc, BorderBrush = linec, BorderThickness = new Thickness(1), Padding = new Thickness(5, 6, 5, 6), Margin = new Thickness(0, 0, Tk.S12, 0) };
    railCard.Child = new ScrollViewer { Content = railHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    Grid.SetColumn(railCard, 0);
    listGrid.Children.Add(railCard);
    grid.Children.Add(listGrid); Grid.SetRow(listGrid, 3);
    var wp = new WrapPanel { VerticalAlignment = VerticalAlignment.Top };
    var sv = new ScrollViewer { Content = wp, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, Background = cardc, Padding = new Thickness(6) };
    double svWheelTarget = 0;
    // 拖拽滚动：按住列表拖 = 滚动（挪过 6px 才算拖，避免吞掉单击选中）
    bool dragScroll = false, dragMoved = false;
    double dragY0 = 0, dragOff0 = 0;
    var svWheelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
    svWheelTimer.Tick += (s2, e2) =>
    {
        try
        {
            double ext = Math.Max(0, sv.ScrollableHeight);
            svWheelTarget = Math.Max(0, Math.Min(ext, svWheelTarget));
            double cur = sv.VerticalOffset;
            double nv = cur + (svWheelTarget - cur) * 0.38;
            if (Math.Abs(svWheelTarget - cur) < 0.5) { nv = svWheelTarget; svWheelTimer.Stop(); }
            sv.ScrollToVerticalOffset(nv);
        }
        catch { svWheelTimer.Stop(); }
    };
    sv.PreviewMouseWheel += (_, we) =>
    {
        try
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;   // Ctrl 是缩放通道：这里绝不能吞事件（吞了缩放就永远不触发）
            double ext = Math.Max(0, sv.ScrollableHeight);
            svWheelTarget = Math.Max(0, Math.Min(ext, svWheelTarget - we.Delta / 120.0 * 3.4 * 30));
            if (!svWheelTimer.IsEnabled) svWheelTimer.Start();
            we.Handled = true;
        }
        catch { }
    };

    Grid.SetColumn(sv, 1);
    listGrid.Children.Add(sv);
    var foot = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
    var footRight = new StackPanel { Orientation = Orientation.Horizontal };
    DockPanel.SetDock(footRight, Dock.Right);
    foot.Children.Add(footRight);
    var curIc = new Border { Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
    foot.Children.Add(curIc);
    // 刷新结果直接显示在窗口里：QkToast 依赖 Quicker 内部结构、可能静默失效，这行文字才是必达的
    var refreshStat = new TextBlock { FontSize = Tk.FMicro, Foreground = txtS, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 };
    foot.Children.Add(refreshStat);
    var curTxt = new TextBlock { Text = chosen, FontSize = Tk.FSmall, Foreground = txtS, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 250, TextTrimming = TextTrimming.CharacterEllipsis };
    var modeAll = false; var modeNet = false;
    var showNames = Recent.ShowNames;
    var accBg = new SolidColorBrush(PHex("#E8F1FD", Colors.LightBlue));
    var accFg = new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue));
    Border MkChip(string tt, bool on)
    {
        var cb = new Border { CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Padding = new Thickness(Tk.S8, Tk.S4, Tk.S8, Tk.S4), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = on ? accBg : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
        cb.Child = new TextBlock { Text = tt, FontSize = Tk.FSmall, Foreground = on ? accFg : txtS, VerticalAlignment = VerticalAlignment.Center };
        return cb;
    }
    var chipR = MkChip("↻ 刷新网络库", false);
    // 联网刷新必须离开 UI 线程：同步下载会把窗口冻住（假死），完成后回到 UI 线程再刷新界面
    Action<Action<string>> RefreshAsync = done =>
    {
        if (!chipR.IsEnabled) return;
        chipR.IsEnabled = false;
        if (chipR.Child is TextBlock rtx) rtx.Text = "⟳ 刷新中…";
        refreshStat.Text = "正在刷新（依次尝试 Gitee / GitHub）…";
        refreshStat.Foreground = txtS;
        QkToast("正在刷新网络图标库…");
        var dsp = w.Dispatcher;
        var th = new System.Threading.Thread(() =>
        {
            string msg = NetIcons.Refresh();
            try
            {
                dsp.BeginInvoke(new Action(() =>
                {
                    try { chipR.IsEnabled = true; if (chipR.Child is TextBlock rtx2) rtx2.Text = "↻ 刷新网络库"; } catch { }
                    try
                    {
                        bool ok = msg.StartsWith("已更新") || msg.StartsWith("已是最新");
                        refreshStat.Text = msg;
                        refreshStat.Foreground = ok ? new SolidColorBrush(PHex("#15803D", Colors.Green)) : new SolidColorBrush(PHex("#C2410C", Colors.OrangeRed));
                    }
                    catch { }
                    QkToast(msg);
                    done(msg);
                }));
            }
            catch { }
        });
        th.IsBackground = true;
        th.Start();
    };
    // ---- 左侧分类栏（来源 + 分组；脚本库 B 窗口同款语言）----
    // 必须在 chipR 挂回调之前声明（CS0165：lambda 引用的局部函数读到的变量要在使用点之前可见）
    // 共享悬浮预览：Popup 只建一个，到点换内容（实测每磁贴各建一个 Popup 会明显卡顿）
    var prevPop = new System.Windows.Controls.Primitives.Popup { Placement = System.Windows.Controls.Primitives.PlacementMode.Right, StaysOpen = false, AllowsTransparency = true };
    var prevCard = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(PHex("#F8FAFC", Colors.White)), BorderBrush = linec, BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 8) };
    prevPop.Child = prevCard;
    var prevTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
    string prevSpec = ""; Border prevChip = null;
    prevTimer.Tick += (_, __) =>
    {
        prevTimer.Stop();
        try
        {
            var tcfgP = UiCfg ?? ConfigService.Load();
            var psp = new StackPanel();
            psp.Children.Add(IconR.Render(prevSpec, ActionType.Keys, 48, tcfgP.IC));
            psp.Children.Add(new TextBlock { Text = NetIcons.Label(prevSpec), FontSize = Tk.FMicro, Foreground = txtS, MaxWidth = 180, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
            prevCard.Child = psp;
            prevPop.PlacementTarget = prevChip;
            prevPop.IsOpen = true;
        }
        catch { }
    };
    string groupSel = "";
    void AddRailSep(string t)
    {
        railHost.Children.Add(new TextBlock { Text = t, FontSize = Tk.FMicro, Foreground = new SolidColorBrush(PHex("#9AA4AF", Colors.Gray)), Margin = new Thickness(6, 8, 0, 4) });
    }
    Border MkRailRow(string nm, string cnt, bool on, System.Action click)
    {
        var dp3 = new DockPanel { LastChildFill = true };
        var bar3 = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = on ? accFg : Brushes.Transparent, Margin = new Thickness(0, 2, 6, 2) };
        DockPanel.SetDock(bar3, Dock.Left);
        dp3.Children.Add(bar3);
        if (cnt.Length > 0)
        {
            var tb1 = new TextBlock { Text = cnt, FontSize = Tk.FMicro, Foreground = new SolidColorBrush(PHex("#9AA4AF", Colors.Gray)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };
            DockPanel.SetDock(tb1, Dock.Right);
            dp3.Children.Add(tb1);
        }
        var tb0 = new TextBlock { Text = nm, FontSize = Tk.FSmall, Foreground = on ? accFg : txtS, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        dp3.Children.Add(tb0);
        var row = new Border { CornerRadius = new CornerRadius(Tk.RSm), Background = on ? accBg : Brushes.Transparent, Padding = new Thickness(6, 4, 4, 4), Margin = new Thickness(0, 0, 0, 2), Cursor = Cursors.Hand, Child = dp3, ToolTip = nm };
        row.MouseLeftButtonUp += (_, __) => click();
        row.MouseEnter += (_, __) => { if (!on) row.Background = new SolidColorBrush(PHex("#F4F6F8", Colors.WhiteSmoke)); };
        row.MouseLeave += (_, __) => { if (!on) row.Background = Brushes.Transparent; };
        return row;
    }
    void RebuildRail()
    {
        railHost.Children.Clear();
        AddRailSep("来源");
        railHost.Children.Add(MkRailRow("常用", IconRecent.Count.ToString(), !modeAll && !modeNet && groupSel.Length == 0, () => { modeAll = false; modeNet = false; groupSel = ""; RebuildRail(); Rebuild(q.Text); }));
        railHost.Children.Add(MkRailRow("全部", AllIconChoices.Length.ToString(), modeAll, () => { modeAll = true; modeNet = false; groupSel = ""; RebuildRail(); Rebuild(q.Text); }));
        railHost.Children.Add(MkRailRow("网络", NetIcons.Names.Count.ToString(), modeNet, () =>
        {
            modeAll = false; modeNet = true;
            if (NetIcons.Names.Count == 0) RefreshAsync(_ => { RebuildRail(); Rebuild(q.Text); });
            RebuildRail(); Rebuild(q.Text);
        }));
        AddRailSep("分组");
        railHost.Children.Add(MkRailRow("全部图标", "", groupSel.Length == 0, () => { groupSel = ""; RebuildRail(); Rebuild(q.Text); }));
        foreach (var g in NetIcons.Groups)
        {
            string captured = g.Key;
            railHost.Children.Add(MkRailRow(captured, g.Value.Count.ToString(), groupSel == captured, () => { groupSel = groupSel == captured ? "" : captured; modeAll = false; modeNet = true; RebuildRail(); Rebuild(q.Text); }));
        }
    }
    RebuildRail();
    chipR.MouseLeftButtonUp += (_, __) => RefreshAsync(_ => { RebuildRail(); Rebuild(q.Text); });
    chipR.Margin = new Thickness(0, 0, 8, 0);
    footRight.Children.Add(chipR);
    var cbNames = new CheckBox { Content = "显示名字", FontSize = Tk.FSmall, Foreground = txtS, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), IsChecked = showNames };
    cbNames.Checked += (_, __) => { showNames = true; Recent.ShowNames = true; Rebuild(q.Text); };
    cbNames.Unchecked += (_, __) => { showNames = false; Recent.ShowNames = false; Rebuild(q.Text); };
    optRow.Children.Add(cbNames);
    var zoomTip = new TextBlock { Text = "Ctrl+滚轮 缩放（" + (int)Recent.IconCell + "）", FontSize = Tk.FMicro, Foreground = txtS, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    optRow.Children.Add(zoomTip);
    // Ctrl+滚轮缩放图标格子
    sv.PreviewMouseWheel += (_, we) =>
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        double nc = Recent.IconCell + (we.Delta > 0 ? 6 : -6);
        if (nc < 28) nc = 28;
        if (nc > 96) nc = 96;
        if (Math.Abs(nc - Recent.IconCell) < 0.1) return;
        Recent.IconCell = nc;
        zoomTip.Text = "Ctrl+滚轮 缩放（" + (int)nc + "）";
        we.Handled = true;
        Rebuild(q.Text);
    };
    // 拖拽滚动：按住列表空白/磁贴拖动 = 滚动；挪过阈值才捕获鼠标（捕获后磁贴收不到 Up，不会误选）
    void ZoomBy(double d)
    {
        double nc = Recent.IconCell + d;
        if (nc < 28) nc = 28;
        if (nc > 96) nc = 96;
        if (Math.Abs(nc - Recent.IconCell) < 0.1) return;
        Recent.IconCell = nc;
        zoomTip.Text = "Ctrl+滚轮 缩放（" + (int)nc + "）";
        Rebuild(q.Text);
    }
    sv.PreviewMouseLeftButtonDown += (_, e2) =>
    {
        dragScroll = true; dragMoved = false;
        dragY0 = e2.GetPosition(sv).Y; dragOff0 = sv.VerticalOffset;
    };
    sv.PreviewMouseMove += (_, e2) =>
    {
        if (!dragScroll) return;
        double dy = e2.GetPosition(sv).Y - dragY0;
        if (!dragMoved && Math.Abs(dy) < 6) return;
        if (!dragMoved) { dragMoved = true; sv.CaptureMouse(); }
        sv.ScrollToVerticalOffset(dragOff0 - dy);
        e2.Handled = true;
    };
    sv.PreviewMouseLeftButtonUp += (_, e2) =>
    {
        if (dragMoved) e2.Handled = true;
        dragScroll = false; dragMoved = false;
        if (sv.IsMouseCaptured) sv.ReleaseMouseCapture();
    };
    w.Closed += (_, __) => Recent.Save();
    foot.Children.Add(curTxt);
    var okBtn = new Button { Content = "用这个", FontSize = Tk.FBody, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(10, 0, 0, 0), Background = new SolidColorBrush(PHex("#0A66C2", Colors.Blue)), Foreground = Brushes.White, BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand };
    foot.Children.Add(okBtn);
    grid.Children.Add(foot); Grid.SetRow(foot, 4);
    void Rebuild(string qv)
    {
        wp.Children.Clear();
        string qq = qv.Trim().ToLowerInvariant();
        List<string> pool;
        if (qq.Length >= 2) pool = AllIconChoices.Concat(NetIcons.Specs).Where(x => NetIcons.Hit(x, qq)).ToList();
        else if (modeNet) pool = NetIcons.Specs;
        else if (modeAll) pool = AllIconChoices.Take(400).ToList();
        else { pool = new List<string>(IconRecent); pool.AddRange(FaIconChoices.Where(x => !IconRecent.Contains(x))); }
        // 选中了分组：只留该组的网络图标（FA 图标不在组里，会被滤掉——想看它们就切回「分组：全部」）
        if (groupSel.Length > 0)
            pool = pool.Where(x => NetIcons.InGroup(x, groupSel)).ToList();
        var tcfg = UiCfg ?? ConfigService.Load();
        double cs = Recent.IconCell;
        if (cs < 28) cs = 28;
        if (cs > 96) cs = 96;
        // 图标至少按 24px 设计尺寸渲染：mdi/MS 的选择框类图标是"2x2 小点"拼的，
        // 缩到 20px 以下点会糊成一条粗毛边（看着像"线条变粗"），按设计尺寸画就分得开
        double isz = Math.Min(Math.Max(26, cs * 0.47), Math.Max(14, (cs - 10) / 1.25));
        var tileBg = PB(tcfg.BC, Color.FromRgb(45, 45, 45));
        var tileBd = PB(tcfg.OC, Color.FromRgb(62, 62, 62));
        try { var ocC = PHex(tcfg.OC, Color.FromRgb(62, 62, 62)); var tb2 = new SolidColorBrush(Color.FromArgb(110, ocC.R, ocC.G, ocC.B)); tb2.Freeze(); tileBd = tb2; } catch { }
        var themeFg = PB(tcfg.FC, Color.FromRgb(204, 204, 204));

        int shown = 0;
        foreach (var spec in pool)
        {
            if (++shown > 400) break;
            // 格子自带主题底、图标用主题色（深色主题的浅色图标在浅色选择器里才看得见）
            double cellW = showNames ? Math.Max(cs, 58) : cs;
            var icEl = IconR.Render(spec, ActionType.Keys, isz, tcfg.IC);
            var icFe = icEl as FrameworkElement;
            if (icFe != null) icFe.HorizontalAlignment = HorizontalAlignment.Center;
            UIElement content;
            double tileH = cs;
            if (showNames)
            {
                // 名字放进格子内、用主题前景色，并固定名字区高度让整行高度对齐
                bool isNet2 = spec.StartsWith("net:", StringComparison.OrdinalIgnoreCase);
                string nm = isNet2 ? (NetIcons.CnOf(spec.Substring(4)) ?? spec.Substring(4))
                                   : spec.Substring(spec.IndexOf(':') + 1);
                var stack2 = new StackPanel { Orientation = Orientation.Vertical, Width = Math.Max(20, cellW - 10) };
                stack2.Children.Add(icEl);
                stack2.Children.Add(new TextBlock
                {
                    Text = nm, FontSize = Tk.FMicro, LineHeight = 11.5, Height = 23,
                    Foreground = themeFg, TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                content = stack2;
                tileH = double.NaN;
            }
            else content = icEl;
            var tile = new Border { Width = cellW, Height = tileH, CornerRadius = new CornerRadius(8), Background = tileBg, BorderBrush = tileBd, BorderThickness = new Thickness(1), Padding = new Thickness(Tk.S6, Tk.S6, Tk.S6, Tk.S6), Child = content };
            var chip = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(3), Child = tile, ToolTip = NetIcons.Label(spec) };
            // 悬停：描边点亮 + 浮起；选中：常亮描边（点选后立即生效）
            var accBr = new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue));
            bool isSel = spec == chosen;
            if (isSel) tile.BorderBrush = accBr;
            chip.MouseEnter += (_, __) => { tile.BorderBrush = accBr; tile.Margin = new Thickness(0, -1, 0, 1); };
            chip.MouseLeave += (_, __) => { tile.BorderBrush = isSel ? accBr : tileBd; tile.Margin = new Thickness(0); prevTimer.Stop(); prevPop.IsOpen = false; };
            // 悬停 220ms 弹 48px 大图预览：Popup 全局只建一个、到点换内容（每磁贴各建一个会卡顿）
            chip.MouseEnter += (_, __) => { prevSpec = spec; prevChip = chip; prevTimer.Stop(); prevTimer.Start(); };
            chip.MouseLeftButtonUp += (_, __) =>
            {
                if (dragMoved) return;   // 刚才是拖拽滚动，不是点选
                chosen = spec; curTxt.Text = NetIcons.Label(spec); curIc.Child = IconR.Render(spec, ActionType.Keys, 20, tcfg.IC);
                isSel = true; tile.BorderBrush = accBr;
            };
            wp.Children.Add(chip);
        }
        if (shown == 0)
        {
            var em = new StackPanel { Margin = new Thickness(10, 14, 0, 6) };
            em.Children.Add(new TextBlock { Text = "🔍  没有匹配的图标", FontSize = Tk.FBody, Foreground = txtM });
            em.Children.Add(new TextBlock { Text = "换个关键词试试；网络图标在左侧「网络」里点 ↻ 刷新", FontSize = Tk.FMicro, Foreground = txtS, Margin = new Thickness(0, 4, 0, 0) });
            // 兜底建议：拿查询串首字符再捞一把，给 3 个最接近的（点一下直接替你搜）
            if (qq.Length >= 2)
            {
                string head = qq.Substring(0, 1);
                var sug = AllIconChoices.Concat(NetIcons.Specs).Where(x => NetIcons.Hit(x, head) && NetIcons.Hit(x, qq) == false).Distinct().Take(3).ToList();
                if (sug.Count > 0)
                {
                    em.Children.Add(new TextBlock { Text = "要不要试试这些：", FontSize = Tk.FMicro, Foreground = txtS, Margin = new Thickness(0, 8, 0, 3) });
                    var srow = new StackPanel { Orientation = Orientation.Horizontal };
                    foreach (var s2 in sug)
                    {
                        string plain = s2.StartsWith("net:", StringComparison.OrdinalIgnoreCase) ? s2.Substring(4) : s2.Substring(s2.IndexOf(':') + 1);
                        var sb2 = new Border { CornerRadius = new CornerRadius(6), Background = cardc, BorderBrush = linec, BorderThickness = new Thickness(1), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand };
                        sb2.Child = new TextBlock { Text = plain, FontSize = Tk.FMicro, Foreground = accFg };
                        string captured = plain;
                        sb2.MouseLeftButtonUp += (_, __) => { q.Text = captured; };
                        srow.Children.Add(sb2);
                    }
                    em.Children.Add(srow);
                }
            }
            wp.Children.Add(em);
        }
    }
    q.TextChanged += (_, __) => Rebuild(q.Text);
    okBtn.Click += (_, __) => { if (!string.IsNullOrEmpty(chosen)) { IconRecent.Remove(chosen); IconRecent.Insert(0, chosen); if (IconRecent.Count > 24) IconRecent.RemoveAt(IconRecent.Count - 1); Recent.Save(); } w.Close(); };
    Rebuild("");
    if (!string.IsNullOrEmpty(chosen)) curIc.Child = IconR.Render(chosen, ActionType.Keys, 20);
    border.Child = grid;
    w.Content = border;
    w.KeyDown += (_, e) =>
    {
        if (e.Key == Key.Escape) { chosen = ""; w.Close(); }
        else if (e.Key == Key.Add || e.Key == Key.OemPlus) { ZoomBy(6); e.Handled = true; }
        else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) { ZoomBy(-6); e.Handled = true; }
    };
    w.Owner = Application.Current?.Windows?.OfType<Window>().FirstOrDefault(x => (x.Title ?? "").Contains("PS Context Menu"));
    w.Opacity = 0d;
    w.Loaded += (_, __) => { try { w.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(Tk.MMicro))); } catch { } };
    w.ShowDialog();
    return chosen;
}

static void EditItem(MI item)
{
    string wt = string.IsNullOrEmpty(item.Title) ? "添加菜单项" : "编辑菜单项";
    var dlg = new Window { Title = wt, Width = 560, Height = 640, MinHeight = 480, MinWidth = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent };
    var bgc = new SolidColorBrush(PHex("#EEF0F3", Colors.Gray));
    var cardc = new SolidColorBrush(PHex("#FFFFFF", Colors.White));
    var linec = new SolidColorBrush(PHex("#E4E7EA", Colors.Gray));
    var actBg = new SolidColorBrush(PHex("#E8F1FD", Colors.LightBlue));
    var actFg = new SolidColorBrush(PHex("#0A66C2", Colors.RoyalBlue));
    var txtM = new SolidColorBrush(PHex("#1F2328", Colors.Black));
    var txtS = new SolidColorBrush(PHex("#6B7280", Colors.Gray));

    TextBlock Lb(string s) { return new TextBlock { Text = s, FontSize = Tk.FSmall, Foreground = txtS, Margin = new Thickness(2, 8, 0, 2) }; }
    TextBox Tx(string s, bool multi, bool mono)
    {
        var tb2 = new TextBox { Text = s, FontSize = Tk.FBody, Padding = new Thickness(8, 5, 8, 5), Background = cardc, Foreground = txtM, BorderBrush = linec, BorderThickness = new Thickness(1), AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = multi ? 96 : 0, CaretBrush = txtM };
        if (mono) tb2.FontFamily = new FontFamily("Consolas");
        return tb2;
    }

    string title = item.Title ?? "";
    string icon = item.Icon ?? "";
    string value = item.Value ?? "";
    ActionType act = item.Action;

    var border = new Border { CornerRadius = new CornerRadius(12), Background = bgc, Padding = new Thickness(14) };
    var sc = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(2) };
    var col = new StackPanel();

    col.Children.Add(new TextBlock { Text = wt, FontSize = Tk.FTitle, FontWeight = FontWeights.SemiBold, Foreground = txtM, Margin = new Thickness(0, 0, 0, 2) });

    col.Children.Add(Lb("标题"));
    var tBox = Tx(title, false, false); tBox.Margin = new Thickness(0, 2, 0, 0);
    col.Children.Add(tBox);
    col.Children.Add(new TextBlock { Text = "想标快捷键就写「标题 | ^t」：竖线右边会右对齐显示成按键提示（只是给人看的，不会真的绑定按键）", FontSize = Tk.FMicro, Foreground = txtS, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0) });

    col.Children.Add(Lb("图标"));
    var iconRow = new StackPanel { Orientation = Orientation.Horizontal };
    var iconView = new Border { Width = 28, Height = 28, Background = new SolidColorBrush(PHex("#F4F6F8", Colors.LightGray)), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 6, 0) };
    iconRow.Children.Add(iconView);
    var icBox = Tx(icon, false, false); icBox.Margin = new Thickness(0, 0, 6, 0); icBox.MinWidth = 220; icBox.VerticalAlignment = VerticalAlignment.Center;
    iconRow.Children.Add(icBox);
    var pickBtn = new Button { Content = "图标库…", FontSize = Tk.FBody, Padding = new Thickness(10, 4, 10, 4), Background = cardc, Foreground = txtM, BorderBrush = linec, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
    iconRow.Children.Add(pickBtn);
    col.Children.Add(iconRow);
    void RefreshIcon() { iconView.Child = string.IsNullOrEmpty(icon) ? null : IconR.Render(icon, ActionType.Keys, 18); }
    RefreshIcon();

    col.Children.Add(Lb("动作类型"));
    var chips = new StackPanel { Orientation = Orientation.Horizontal };
    var actChips = new[] { (ActionType.Keys, "⌨ 快捷键"), (ActionType.Script, "◇ 脚本"), (ActionType.Run, "▶ 运行程序"), (ActionType.Clipboard, "▣ 剪贴板"), (ActionType.QuickerAction, "⚡ Quicker动作") };
    var chipBorders = new List<Border>();
    Border valueHost = null; ScrollViewer valueSV = null; TextBlock valHint = null;

    void RefreshValueArea()
    {
        if (valueHost == null) return;
        valueHost.Child = null;
        StackPanel inner = new StackPanel();
        if (act == ActionType.Keys)
        {
            inner.Children.Add(Lb("快捷键组合"));
            var krow = new StackPanel { Orientation = Orientation.Horizontal };
            var kShow = new TextBox { Text = value, IsReadOnly = true, FontSize = Tk.FTitle, FontWeight = FontWeights.SemiBold, Padding = new Thickness(10, 7, 10, 7), Background = new SolidColorBrush(PHex("#F4F6F8", Colors.LightGray)), Foreground = actFg, BorderBrush = linec, BorderThickness = new Thickness(1), MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
            var recBtn = new Button { Content = "● 录制", FontSize = Tk.FBody, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(PHex("#C2410C", Colors.OrangeRed)), Foreground = Brushes.White, BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            var clearBtn = new Button { Content = "清空", FontSize = Tk.FSmall, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(6, 0, 0, 0), Background = cardc, Foreground = txtS, BorderBrush = linec, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            krow.Children.Add(kShow); krow.Children.Add(recBtn); krow.Children.Add(clearBtn);
            inner.Children.Add(krow);
            recBtn.Click += (_, __) => { string r = RecordShortcut(); if (!string.IsNullOrEmpty(r)) { value = r; kShow.Text = r; } };
            clearBtn.Click += (_, __) => { value = ""; kShow.Text = ""; };
            valHint = new TextBlock { Text = "录制规则：^=Ctrl  +=Shift  !=Alt  #=Win，如 ^+t = Ctrl+Shift+T", FontSize = Tk.FMicro, Foreground = txtS, Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            inner.Children.Add(valHint);
        }
        else if (act == ActionType.Script)
        {
            inner.Children.Add(Lb("JSX 脚本（发送到 Photoshop 执行）"));
            var sv2 = Tx(value, true, true); sv2.MinHeight = 150; sv2.Margin = new Thickness(0, 2, 0, 0);
            sv2.TextChanged += (_, __) => value = sv2.Text;
            inner.Children.Add(sv2);
        }
        else if (act == ActionType.Clipboard)
        {
            inner.Children.Add(Lb("剪贴板文本（点击后写入剪贴板）"));
            var cv = Tx(value, true, false); cv.MinHeight = 110; cv.Margin = new Thickness(0, 2, 0, 0);
            cv.TextChanged += (_, __) => value = cv.Text;
            inner.Children.Add(cv);
        }
        else if (act == ActionType.QuickerAction)
        {
            inner.Children.Add(Lb("Quicker 动作（动作 ID 或名称，可跟 ?参数）"));
            var qv = Tx(value, false, true); qv.Margin = new Thickness(0, 2, 0, 0);
            qv.TextChanged += (_, __) => value = qv.Text;
            inner.Children.Add(qv);
            inner.Children.Add(new TextBlock { Text = "例：c2b4ebf9-1a50-4e7c-ac20-acef79fb0cdb  或  该ID?city=广州。动作需在 Quicker 里存在（通用面板或已绑定）。", FontSize = Tk.FMicro, Foreground = txtS, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 5, 0, 0) });
        }
        else // Run
        {
            inner.Children.Add(Lb("命令（第一行=程序路径，其余行=参数）"));
            var rv = Tx(value, true, true); rv.MinHeight = 90; rv.Margin = new Thickness(0, 2, 0, 0);
            rv.TextChanged += (_, __) => value = rv.Text;
            inner.Children.Add(rv);
            var brow = new Button { Content = "浏览程序…", FontSize = Tk.FSmall, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 6, 0, 0), Background = cardc, Foreground = txtM, BorderBrush = linec, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            brow.Click += (_, __) => { var od = new Microsoft.Win32.OpenFileDialog { Filter = "程序|*.exe;*.bat;*.cmd;*.com|所有文件|*.*" }; if (od.ShowDialog() == true) { string[] ln = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries); string rest = ln.Length > 1 ? "\r\n" + string.Join("\r\n", ln.Skip(1)) : ""; value = od.FileName + rest; rv.Text = value; } };
            inner.Children.Add(brow);
        }
        valueHost.Child = inner;
    }

    for (int i = 0; i < actChips.Length; i++)
    {
        int ci = i; var a2 = actChips[i];
        var cb2 = new Border { Cursor = Cursors.Hand, CornerRadius = new CornerRadius(Tk.RMd), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 2, 6, 0), Background = Brushes.Transparent };
        var ctxt = new TextBlock { Text = a2.Item2, FontSize = Tk.FBody, Foreground = txtS, VerticalAlignment = VerticalAlignment.Center };
        cb2.Child = ctxt;
        cb2.MouseLeftButtonUp += (_, __) => { act = a2.Item1; RefreshChips(); RefreshValueArea(); };
        chipBorders.Add(cb2); chips.Children.Add(cb2);
    }
    col.Children.Add(chips);
    void RefreshChips()
    {
        for (int j = 0; j < chipBorders.Count; j++)
        {
            bool on = act == actChips[j].Item1;
            chipBorders[j].Background = on ? actBg : Brushes.Transparent;
            ((TextBlock)chipBorders[j].Child).Foreground = on ? actFg : txtS;
            ((TextBlock)chipBorders[j].Child).FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
    RefreshChips();

    col.Children.Add(Lb("参数"));
    valueHost = new Border { Background = cardc, CornerRadius = new CornerRadius(8), Padding = new Thickness(8), BorderBrush = linec, BorderThickness = new Thickness(1) };
    col.Children.Add(valueHost);
    RefreshValueArea();
    // 备注 + 两个开关：备注是给"未来的自己"解释这条脚本干嘛用的（脚本 Value 就是一坨 JS，过俩月认不出）
    bool isHeader = item.Header, isDisabled = item.Disabled;
    col.Children.Add(Lb("备注（悬停提示，只给自己看）"));
    var noteBox = Tx(item.Note ?? "", false, false); noteBox.Margin = new Thickness(0, 2, 0, 0);
    col.Children.Add(noteBox);
    var swRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    CheckBox MkSw(string txt, bool on, Action<bool> onCh, string tip)
    {
        var cb = new CheckBox { Content = txt, IsChecked = on, FontSize = Tk.FBody, Foreground = txtM, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0), Cursor = Cursors.Hand, ToolTip = tip };
        cb.Checked += (_, __) => onCh(true);
        cb.Unchecked += (_, __) => onCh(false);
        return cb;
    }
    swRow.Children.Add(MkSw("分组标题", isHeader, v => isHeader = v, "只作分组标签：小字弱色、不可点、不参与搜索匹配"));
    swRow.Children.Add(MkSw("启用", !isDisabled, v => isDisabled = !v, "取消勾选 = 停用：菜单里灰显且点了不执行（右键仍可进来改）"));
    col.Children.Add(swRow);
    // 备注/开关放参数区之后、按钮之前

    var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
    var cancelBtn = new Button { Content = "取消", FontSize = Tk.FBody, Padding = new Thickness(14, 6, 14, 6), Background = cardc, Foreground = txtS, BorderBrush = linec, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
    var saveBtn = new Button { Content = "保存", FontSize = Tk.FBody, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush(PHex("#0A66C2", Colors.Blue)), Foreground = Brushes.White, BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand };
    btns.Children.Add(cancelBtn); btns.Children.Add(saveBtn);
    col.Children.Add(btns);

    pickBtn.Click += (_, __) => { string p = PickIconDialog(icon); if (!string.IsNullOrEmpty(p)) { icon = p; icBox.Text = p; RefreshIcon(); } };
    icBox.TextChanged += (_, __) => { icon = icBox.Text.Trim(); RefreshIcon(); };
    cancelBtn.Click += (_, __) => dlg.Close();
    saveBtn.Click += (_, __) =>
    {
        item.Title = tBox.Text.Trim();
        item.Icon = icon;
        item.Action = act;
        item.Value = value;
        item.Header = isHeader;
        item.Disabled = isDisabled;
        item.Note = (noteBox.Text ?? "").Trim();
        dlg.Close();
    };
    dlg.KeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { saveBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; } if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) dlg.Close(); };

    sc.Content = col;
    border.Child = sc;
    dlg.Content = border;
    dlg.Owner = Application.Current?.Windows?.OfType<Window>().FirstOrDefault(x => (x.Title ?? "").Contains("PS Context Menu"));
    dlg.Opacity = 0d;
    dlg.Loaded += (_, __) => { try { dlg.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(Tk.MMicro))); } catch { } };
    dlg.ShowDialog();
}
}
}
