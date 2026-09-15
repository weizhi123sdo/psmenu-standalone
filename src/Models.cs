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
// #region Data Models
public enum ActionType { Keys, Script, Run, Clipboard, EditConfig, ExportConfig, ImportConfig, QuickerAction }
public enum LayerKind { None, Pixel, Text, Shape, SmartObject, Adjustment, Group, Background, Unknown, Fallback }

public static class AT
{
    public static ActionType From(string s)
    {
        switch (s)
        {
            case ACT_KEYS: return ActionType.Keys;
            case ACT_SCRIPT: return ActionType.Script;
            case ACT_RUN: return ActionType.Run;
            case ACT_CLIPBOARD: return ActionType.Clipboard;
            case ACT_QK: return ActionType.QuickerAction;
            default: return ActionType.Keys;
        }
    }
    public static string ToStr(ActionType a)
    {
        switch (a)
        {
            case ActionType.Keys: return ACT_KEYS;
            case ActionType.Script: return ACT_SCRIPT;
            case ActionType.Run: return ACT_RUN;
            case ActionType.Clipboard: return ACT_CLIPBOARD;
            case ActionType.QuickerAction: return ACT_QK;
            default: return ACT_KEYS;
        }
    }
    public static string Lbl(ActionType a)
    {
        switch (a)
        {
            case ActionType.Keys: return "⌨";
            case ActionType.Script: return "◇";
            case ActionType.Run: return "▶";
            case ActionType.Clipboard: return "▣";
            case ActionType.QuickerAction: return "⚡";
            default: return "●";
        }
    }
}

public class MI
{
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "fa:Solid_Play";
    public bool IsSep { get; set; }
    public ActionType Action { get; set; } = ActionType.Keys;
    public string Value { get; set; } = "";
    public List<MI> Sub { get; set; }
    public bool Header { get; set; }        // 分组标题行：小字弱色、不可点、不参与匹配
    public bool Disabled { get; set; }      // 停用：菜单里灰显且不可执行
    public string Note { get; set; } = "";  // 备注：悬停提示，给自己看（脚本项过俩月自己也认不出）
    // 出处：这条是从脚本库里哪一条置入的（libKey=库内 key，libHash=置入时那版的正文指纹）。
    // 「PS脚本库」(B) 靠这两个字段判"菜单里这条是不是旧版"，所以**存盘时必须带着**——
    // 少了它们，A 一保存配置就把 B 写下的标记抹掉，"旧版提示"再也亮不起来。
    public string LibKey { get; set; } = "";
    public string LibHash { get; set; } = "";
    public string UK => Title + "||" + AT.ToStr(Action) + "||" + Value;
    // 稳定键：UK 由标题+动作+值拼成，改标题就会变，置顶没法靠它长期跟踪一项；
    // K 在首次访问时生成、随配置持久化，置顶改用 K 找回当前项（UK 只作旧数据兜底）。
    string _k;
    public string K { get { if (_k == null) _k = Guid.NewGuid().ToString("N"); return _k; } set { _k = value; } }
    public bool HasSub => Sub != null && Sub.Count > 0;

    // 标题支持 "标题 | ^t"：竖线右侧作为**右对齐的按键提示**（纯显示，不绑定快捷键）。
    // 这样标题本身保持短、左对齐，按键列单独成列不参差。
    public static void SplitTitle(string t, out string main, out string key)
    {
        main = t ?? ""; key = "";
        if (main.Length == 0) return;
        int i = main.IndexOf('|');
        if (i < 0) return;
        key = main.Substring(i + 1).Trim();
        main = main.Substring(0, i).TrimEnd();
    }
    public string TitleMain { get { SplitTitle(Title, out var a, out var b); return a; } }
    public string TitleKey { get { SplitTitle(Title, out var a, out var b); return b; } }

    public MI Clone() => new MI { Title = Title, Icon = Icon, IsSep = IsSep, Header = Header, Disabled = Disabled, Note = Note, LibKey = LibKey, LibHash = LibHash, Action = Action, Value = Value, Sub = Sub?.Select(s => s.Clone()).ToList() };

    public JObject ToJ()
    {
        if (IsSep) return new JObject { ["isSeparator"] = true };
        var o = new JObject { ["title"] = Title, ["icon"] = Icon, ["action"] = AT.ToStr(Action), ["value"] = Value, ["k"] = K };
        if (Header) o["header"] = true;
        if (Disabled) o["disabled"] = true;
        if (!string.IsNullOrEmpty(Note)) o["note"] = Note;
        if (!string.IsNullOrEmpty(LibKey))
        {
            o["libKey"] = LibKey;
            if (!string.IsNullOrEmpty(LibHash)) o["libHash"] = LibHash;
        }
        if (HasSub) o["sub"] = new JArray(Sub.Select(s => s.ToJ()));
        return o;
    }
    public static MI FromJ(JObject o)
    {
        if (o == null) return null;
        if (o["isSeparator"]?.Value<bool>() == true) return new MI { IsSep = true };
        var m = new MI
        {
            Title = o["title"]?.ToString() ?? "",
            Icon = o["icon"]?.ToString() ?? "fa:Solid_Play",
            Header = o["header"]?.Value<bool>() == true,
            Disabled = o["disabled"]?.Value<bool>() == true,
            Note = o["note"]?.ToString() ?? "",
            LibKey = o["libKey"]?.ToString() ?? "",
            LibHash = o["libHash"]?.ToString() ?? "",
            Action = AT.From(o["action"]?.ToString() ?? ""),
            Value = o["value"]?.ToString() ?? "",
            K = o["k"]?.ToString()
        };
        if (o["sub"] is JArray sa)
            m.Sub = sa.Select(t => FromJ(t as JObject)).Where(x => x != null).ToList();
        return m;
    }
}

public class LM
{
    public LayerKind Kind { get; set; }
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "fa:Solid_ThLarge";
    public List<MI> Items { get; set; } = new List<MI>();
}

public class PI
{
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "fa:Solid_Play";
    public ActionType Action { get; set; } = ActionType.Keys;
    public string Value { get; set; } = "";
    public LayerKind LK { get; set; } = LayerKind.None;
    public string K { get; set; }   // 对应 MI.K：置顶与菜单项之间的稳定关联，改标题/图标不丢
    public string LibKey { get; set; } = "";   // 置顶项也要记出处（脚本库里哪一条、哪一版），理由同 MI
    public string LibHash { get; set; } = "";
    public string UK => Title + "||" + AT.ToStr(Action) + "||" + Value;
}

public class AC
{
    public double MW { get; set; } = 260;
    public double FS { get; set; } = 13;
    public double Zoom { get; set; } = 1;
    public double PS { get; set; } = 20;
    public double IS { get; set; } = 2;
    public double Op { get; set; } = 0.97;
    public string BC { get; set; } = "#262B33";
    public string FC { get; set; } = "#E9EDF3";
    public string HC { get; set; } = "#3B82F6";
    public string OC { get; set; } = "#3A4150";
    public string IC { get; set; } = "#8FB7FF";
    public bool FreqSort { get; set; }   // 按使用频率自动排序（分隔线分段内）
    public bool ShowFreq { get; set; } = true;   // 使用次数总开关：关掉则不记录、不显示、频率排序失效
    public bool ShowPinName { get; set; } = true;  // 置顶方格下是否常显名称（关掉回到紧凑方格条）
    public bool HoverSub { get; set; } = true;   // 悬停自动进子层总开关（行为页可关）
    public int HoverMs { get; set; } = 420;      // 悬停进子层等待毫秒（100~1500，行为页滑块）
    public int GlowK { get; set; } = 90;         // 悬停辉光强度 0~100（0=关），行为页可调
    public double SheenK { get; set; } = 100;    // 底部光带亮度 0~100
    public double CR { get; set; } = 4;          // 菜单行圆角 0~12
    public double BW { get; set; } = 1;          // 菜单行描边粗细 0~3
    public int UnderK { get; set; } = 60;        // 底部模糊高光强度 0~100（0=关）
    public double UnderH { get; set; } = 5;      // 底部模糊高光厚窄 2~14（条的厚度，配模糊成光晕）
    public string Theme { get; set; } = "psdark";
    public int FsTier { get; set; } = 1;      // 界面字号档位：0 小 / 1 中 / 2 大
    public List<LM> Menus { get; set; }
    public List<PI> Pinned { get; set; } = new List<PI>();
    public Dictionary<string, int> Freq { get; set; } = new Dictionary<string, int>();
}

// #region LayerTypeMeta
public static class LTM
{
    public static readonly IReadOnlyList<(LayerKind K, string L, string D, string I)> All = new[] {
        (LayerKind.Pixel,"像素层","位图图像","fa:Solid_Image"),
        (LayerKind.Text,"文字层","文本排版","fa:Solid_Font"),
        (LayerKind.Shape,"形状层","矢量图形","fa:Solid_Shapes"),
        (LayerKind.SmartObject,"智能对象","嵌入资源","fa:Solid_Cube"),
        (LayerKind.Adjustment,"调整层","颜色调整","fa:Solid_SlidersH"),
        (LayerKind.Group,"图层组","组织结构","fa:Solid_Folder"),
        (LayerKind.Background,"背景层","底层画布","fa:Solid_Lock"),
        (LayerKind.Unknown,"其他","未识别","fa:Solid_QuestionCircle"),
        (LayerKind.Fallback,"通用","默认菜单","fa:Solid_ThLarge")
    };
    // O(1) dictionary lookups
    static readonly Dictionary<LayerKind, (string L, string I)> _lkp = All.ToDictionary(x => x.K, x => (x.L, x.I));
    public static string L(LayerKind k) => _lkp.TryGetValue(k, out var v) ? v.L : "?";
    public static string I(LayerKind k) => _lkp.TryGetValue(k, out var v) ? v.I : "fa:Solid_ThLarge";
    public static LayerKind Parse(string s)
    {
        if (string.IsNullOrEmpty(s)) return LayerKind.None;
        switch (s.ToLower())
        {
            case "pixel": return LayerKind.Pixel;
            case "text": return LayerKind.Text;
            case "shape": return LayerKind.Shape;
            case "smartobject": return LayerKind.SmartObject;
            case "adjustment": return LayerKind.Adjustment;
            case "group": return LayerKind.Group;
            case "background": return LayerKind.Background;
            case "unknown": return LayerKind.Unknown;
            default: return LayerKind.None;
        }
    }
    public static Color C(LayerKind k)
    {
        switch (k)
        {
            case LayerKind.Pixel: return Color.FromRgb(230, 180, 80);
            case LayerKind.Text: return Color.FromRgb(80, 180, 230);
            case LayerKind.Shape: return Color.FromRgb(100, 210, 130);
            case LayerKind.SmartObject: return Color.FromRgb(180, 130, 230);
            case LayerKind.Adjustment: return Color.FromRgb(240, 220, 50);
            case LayerKind.Group: return Color.FromRgb(120, 180, 240);
            default: return Color.FromRgb(180, 180, 185);
        }
    }
}

}
}
