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
// ===== 版本信息 =====
public const string ActionVersion = "2026.09.15 · 独立版 v1.0";
    public static readonly string ActionLog =
        "v1.0　独立版首个版本：脱离 Quicker 独立常驻运行（托盘 + 全局热键 + PS 前台中键唤出）、设置编辑器新增「启动与触发」页、Quicker 旧配置自动迁移到 %APPDATA%\\PSMenu；" +
        "r97　编辑器行悬停手感：条目行与左栏类型行的悬停底色从瞬变改为 120ms 颜色过渡（条目行换独立画刷——共享的 Th.BBC 一动画会全体变色），悬停把手的渐显保留；" +
        "r96　辉光语义修正与底部高光：辉光从「图标/文字外发光」改为「行与置顶方块的边缘发光」（影源=悬停点亮的半透明圆角底板；行的底板此前一直停在透明度 0，观感只剩文字发光——根因修复）；置顶角标对齐实时预览（30% 纯强调色三角，去角内箭头）；新增「底部模糊高光」：行底 accent 光条+Blur 成光晕，强度 0~100、厚窄 2~14px 可调，实时预览同步；并补齐 r95 半套落盘的光带亮度/圆角/描边可调项；" +
        "r95　可调项补全：光效卡新增「底部光带亮度」滑块（行底光带全面接亮度系数）；外观颜色卡新增「行圆角」「描边粗细」滑块（菜单行与预览同步）；预览行/置顶格可交互——鼠标进来临时呈现悬停态，效果直接试；行为卡文案统一为 ●/○ 前缀短词；配置管理页头间距对齐；" +
        "r94　设置页布局与光效调节：行为卡改流式布局（窄窗自动换行，修按钮截字）并收编「名称显示」；「图标大小」并入菜单尺寸卡；置顶栏卡取消；新增「光效」卡——悬停辉光强度 0~100（0=关）滑块，菜单辉光与实时预览同步呈现；预览另支持「名称显示」两档实时变化；" +
        "r93　外观主题页重做：预设卡片常显主题名（不再藏 ToolTip 里）、选中=强调色描边+名字点亮且全组即时同步（替换原来的闪绿框）、悬停上浮 1.05+投影、按下 0.95；实时预览拟真重做——画布随主题明暗自适应、预览含置顶方格行（第 1 格悬停态+子菜单强调色角标）、列表行含悬停高亮+底部光带+快捷键列，调节时轻淡入；" +
        "r92　窗口系列同款强化：设置编辑器与「从脚本库添加」窗补上弹入动画（scale 0.98→1+淡入，400ms 兜底）、平滑滚轮与行悬停颜色过渡/按压反馈——B（脚本库）同日完成同一套，两动作窗口语言完全对齐；" +
        "r91　手感三连：呼出弹入动画（缩放 0.96→1 + 淡入 + 模糊收束 170ms，带 400ms 兜底绝不卡透明）；列表滚轮平滑滚动（滚轮只累计目标位，16ms 计时器指数趋近滑过去，Ctrl+滚轮缩放不受影响）；按压反馈（行/置顶格按下轻微下压 0.985、松开回弹）；键盘 ↑↓ 选中态同权（同一套光带+光晕，和鼠标悬停观感一致）；" +
        "r90　光感与手感：悬停辉光重做——之前挂在透明行上只能从文字取形几乎看不见，改为专用强调色圆角光晕底板（外圈完整一圈辉光，悬停即亮、离开即摘零开销）+ 每行底部 2px 强调色光感带（悬停点亮、两侧渐隐）；辉光强度 0.5→0.9、半径 16→24；置顶条滑动手感重调：指数趋近的平滑加减速（起步/收住不再顿挫），限速 6.5→11px/帧，边缘感应区放宽到条宽 1/3；" +
        "r89　置顶条滑动与悬停可调：置顶条不再按容量裁掉（去 8 格上限），溢出改为边缘悬停自动滑（越靠边越快、离开即停、贴帧率步进）+ 两侧渐隐提示；悬停自动进子层改为可开关、等待时间可在行为页滑块调节（100~1500ms，默认 420ms 不变），带子层的置顶方格悬停也套用同一延时自动展开；元素级光效：悬停辉光（共享 DropShadow，离开即摘）+ 子层切换动感模糊收束（Blur 6→0）；" +
        "r88　性能与调试：正常运行时不再写调试 JSON；菜单关闭不再无条件保存配置，仅在 Ctrl+滚轮改变缩放时落盘；修正主脚本线程说明为 STA UI 线程；" +
        "只列大型版本；逐轮细节见源码目录的《PS便捷菜单测试版_开发说明.md》" +
        "· r65~r87　置顶与主题：置顶条四项修复（图标对齐/子层不再混入通用项/改标题与图标跟随/子项可试运行）、六项界面修正（搜索命中数、置顶容量、图标重量、悬停与次数、置顶托盘、标题截断）、图标按包围盒归一不再被切、弱化色改由前景色派生、10 套主题按成熟设计系统重配、说明与确认统一为自绘窗、帮助按钮做成明显的底部按钮（首次打开设置自动弹一次）、置顶方格不再显示次数、行为里新增「使用次数」总开关、含子菜单的项改成一眼可见的角标（行内强调色小角标 + 置顶方格折角＝整块强调色 + 反差色箭头（10 套主题逐个验过）、置顶名称两档切换：方格下常显 / 悬停时方格「化开」成文字（有备注只显示备注、无备注显示标题；标题 4 字以上与备注都按两排均分摆放；悬停文字字号放大、距方块边留 10~20px）；面板宽度钉住设置的「菜单宽度」（长标题改为省略，不再把面板撑宽）、名称类文字一律单行省略；" +
        "· r50~r64　脚本库：浏览/搜索/筛选/置入菜单、发给 Photoshop 试运行、共享缓存与最近使用；" +
        "· r20~r49　图标与选择器：网络矢量图标库（mdi 348+，中文可搜）、图标选择器（缩放/显示名字/最近）、频率排序与未保存提示；" +
        "· 早期版本　骨架：按图层类型的上下文菜单、方形置顶栏、嵌套子菜单、四种动作驱动、三标签页配置编辑器、快照回滚。" +
        "\n\n调试：runaction:<动作id>?shot=menu;lk=text;theme=lemon 渲染菜单、?shot=config;page=1 渲染设置窗，均不弹窗";
// 主题预设唯一数据源：菜单数字键(1~5暗/6~0亮)、编辑器预设色板、主题名对照全部由此派生
// 主题预设的配色参考成熟设计系统（不是随手调的色值）：
// · GitHub Primer（#0D1117 / #E6EDF3 / #1F6FEB）
// · Nord（#2E3440 / #ECEFF4 / #88C0D0）
// · Tokyo Night（#1A1B26 / #C0CAF5 / #7AA2F7）
// · Synthwave / 赛博霓虹（#241B2F / #FF2E97 / #2DE2FF）
// 取值校验：文字/底 对比度 ≥ 10.5、强调/底 ≥ 4（见配套脚本），暗色下都远超 AA。
// 注意：th（id）是配置里持久化的键，**只能改名字和色值，不能改 id**。
public static readonly (string name, string bc, string fc, string hc, string oc, string ic, double op, string th)[] DarkThemes = {
    ("PS 深空",       "#1C2128", "#E8EDF5", "#3B82F6", "#333B47", "#8FB7FF", 0.97, "psdark"),
    ("极夜·GitHub",   "#0D1117", "#E6EDF3", "#1F6FEB", "#30363D", "#A5D6FF", 0.98, "hires"),
    ("北境·Nord",     "#2E3440", "#ECEFF4", "#88C0D0", "#3B4252", "#8FBCBB", 0.97, "aurora"),
    ("东京午夜",       "#1A1B26", "#C0CAF5", "#7AA2F7", "#292E42", "#BB9AF7", 0.97, "midnight"),
    ("赛博霓虹",       "#241B2F", "#F8F8F2", "#FF2E97", "#3A2F4E", "#2DE2FF", 0.97, "cyber")
};
// 亮色同源：shadcn/ui 中性（#FFFFFF / #18181B）、GitHub Light（#0969DA）、
// Tailwind amber/emerald。文字/底 对比度 ≥ 14，强调/底 ≥ 3。
// 亮色主题下"分隔线/次要文字"由前景色派生（见 PMuted），所以描边色只管描边、可以很轻。
public static readonly (string name, string bc, string fc, string hc, string oc, string ic, double op, string th)[] LightThemes = {
    ("雾白·石板",     "#F8FAFC", "#0F172A", "#4F46E5", "#E2E8F0", "#6366F1", 0.98, "light"),
    ("暖纸·陶土",     "#FAF9F7", "#1C1917", "#C2410C", "#E7E5E4", "#9A3412", 0.97, "paper"),
    ("晨蓝·GitHub",   "#FFFFFF", "#1F2328", "#0969DA", "#D0D7DE", "#0969DA", 0.98, "dayblue"),
    ("柠檬苏打",       "#FFFBEB", "#422006", "#D97706", "#F0E2B6", "#B45309", 0.97, "lemon"),
    ("清新薄荷",       "#F0FDF4", "#052E16", "#059669", "#BBF7D0", "#047857", 0.97, "mint")
};
public static (string id, string cn)[] ThemeNames => DarkThemes.Concat(LightThemes).Select(t => (id: t.th, cn: t.name)).ToArray();
public static void QkToast(string msg, string kind = "Information")
{
    try
    {
        Application.Current?.Dispatcher.Invoke(new Action(() =>
        {
            var mw = Application.Current?.MainWindow;
            if (mw == null) return;
            object notifier = null;
            foreach (var f in mw.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
            {
                try { var v = f.GetValue(mw); var ft = v?.GetType().FullName ?? ""; if (ft.StartsWith("ToastNotifications")) { notifier = v; break; } } catch { }
            }
            if (notifier == null) return;
            var extType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a2 => a2.GetTypes()).FirstOrDefault(t2 => t2.FullName == "ToastNotifications.Messages." + kind + "Extensions");
            var mth = extType?.GetMethod("Show" + kind, new[] { notifier.GetType(), typeof(string) });
            if (mth == null) return;
            mth.Invoke(null, new object[] { notifier, msg });
        }));
    }
    catch { }
}
// 动作自带的圆角信息窗（替代系统 MessageBox，用于帮助/说明等只读提示）
// 自绘确认窗（替代系统 MessageBox）：确认类也必须和编辑器/帮助窗同风格——
// 系统 MessageBox 是另一套外观（浅灰方角 + 系统字体 + 系统按钮），一弹出来就是"这个软件里混进了别的东西"。
// 返回被点按钮的下标；Esc / 右上角 ✕ ＝ cancelIndex（默认最后一个按钮）。
public static int Confirm(string title, string body, string[] btns, int cancelIndex = -1, int accentIndex = 0, string accentHex = "#0A66C2")
{
    if (btns == null || btns.Length == 0) btns = new[] { "知道了" };
    if (cancelIndex < 0) cancelIndex = btns.Length - 1;
    int result = cancelIndex;
    try
    {
        var ink = new SolidColorBrush(PHex("#1F2328", Colors.Black));
        var dim = new SolidColorBrush(PHex("#3A3F47", Colors.Gray));
        var acc = new SolidColorBrush(PHex(accentHex, Colors.RoyalBlue));
        var w = new Window { Title = title, Width = 380, SizeToContent = SizeToContent.Height, MaxHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        var bg = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(PHex("#EEF0F3", Colors.Gray)), Padding = new Thickness(18), BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), BorderThickness = new Thickness(1) };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = ink, Margin = new Thickness(0, 0, 0, 8) });
        var sv2 = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 220, Content = new TextBlock { Text = body, FontSize = 12, Foreground = dim, TextWrapping = TextWrapping.Wrap, LineHeight = 19 } };
        grid.Children.Add(sv2); Grid.SetRow(sv2, 1);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        for (int bi = 0; bi < btns.Length; bi++)
        {
            int bidx = bi;
            bool primary = bi == accentIndex;
            var b = new Button
            {
                Content = btns[bi], FontSize = 12, MinWidth = 84, Height = 30, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand,
                Foreground = primary ? Brushes.White : dim,
                Background = primary ? acc : new SolidColorBrush(PHex("#E4E7EA", Colors.LightGray)),
                BorderBrush = Brushes.Transparent
            };
            b.Click += (_, __) => { result = bidx; try { w.Close(); } catch { } };
            row.Children.Add(b);
        }
        grid.Children.Add(row); Grid.SetRow(row, 2);
        bg.Child = grid; w.Content = bg;
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) { result = cancelIndex; try { w.Close(); } catch { } } };
        w.Owner = Application.Current?.Windows?.OfType<Window>().FirstOrDefault(x => (x.Title ?? "").Contains("PS Context Menu"));
        w.Opacity = 0d; w.Loaded += (_, __) => { try { w.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(140))); } catch { } };
        w.ShowDialog();
    }
    catch { }
    return result;
}
// 使用帮助（按钮与"首次使用自动弹出"共用一份正文）
public static void ShowHelp()
{
        ShowInfo("PS Context Menu · 使用帮助",
        "版本：" + ActionVersion + "\n\n" +
        "【常用操作】\n" +
        "PS 菜单：左键=执行/进入子层；悬停自动进子层；右键叶子项=直接编辑；Ctrl+右键=置顶；" +
        "直接打字即搜索（只显示命中的项，顶部有回显；汉字/英文/拼音首字母都行），↑↓ 选择、Enter 执行、" +
        "Esc 清输入/退层/关闭；呼出后默认高亮常用项\n" +
        "编辑器：单击=选中；Ctrl+单击=复制到批量；拖动=排序/拖到左栏类型（Ctrl=复制、Shift=置顶）；" +
        "Ctrl+A 全选、Ctrl+Z/Y 撤销重做、Ctrl+N 新建、Ctrl+D 复制项、Ctrl+F 跨类型搜索\n" +
        "置顶：按住拖动排序；右键=取消置顶（需确认）" +
        "\n\n" +
        "提示：这个窗口只在第一次打开设置时自动出现一次，之后点底部的「ⓘ 使用帮助」随时再看。");
}
public static void ShowInfo(string title, string body)
{
    try
    {
        var w = new Window { Title = title, Width = 540, Height = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize };
        var bg = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(PHex("#EEF0F3", Colors.Gray)), Padding = new Thickness(18) };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(PHex("#1F2328", Colors.Black)), Margin = new Thickness(0, 0, 0, 10) });
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = body, FontSize = 12, Foreground = new SolidColorBrush(PHex("#3A3F47", Colors.Gray)), TextWrapping = TextWrapping.Wrap, LineHeight = 20 } };
        grid.Children.Add(sv); Grid.SetRow(sv, 1);
        var bt = new Button { Content = "知道了", Width = 90, Height = 30, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand, Background = new SolidColorBrush(PHex("#0A66C2", Colors.Blue)), Foreground = Brushes.White, BorderBrush = Brushes.Transparent };
        bt.Click += (_, __) => w.Close();
        grid.Children.Add(bt); Grid.SetRow(bt, 2);
        bg.Child = grid; w.Content = bg;
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        w.Owner = Application.Current?.Windows?.OfType<Window>().FirstOrDefault(x => (x.Title ?? "").Contains("PS Context Menu"));
        w.Opacity = 0d; w.Loaded += (_, __) => { try { w.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(150))); } catch { } };
        w.ShowDialog();
    }
    catch { }
}
public static readonly List<string> RecentColors = new List<string>();
public static Style ThinScrollStyle()
{
    try
    {
        string x = "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>" +
                   "<Setter Property='Width' Value='9'/><Setter Property='Background' Value='Transparent'/>" +
                   "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'><Grid Background='Transparent'>" +
                   "<Track x:Name='PART_Track' IsDirectionReversed='True'><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border CornerRadius='4' Background='#64FFFFFF' Margin='2,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track>" +
                   "</Grid></ControlTemplate></Setter.Value></Setter></Style>";
        return (Style)System.Windows.Markup.XamlReader.Parse(x);
    }
    catch { return null; }
}

}
}
