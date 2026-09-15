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
// #region LayerCtx — 图层上下文（只保留图层类型）
// r34：删掉「按选区/蒙版显示条件」那一套。选区与蒙版属于**文档态**，而菜单是按**图层态**分的，
// 用文档级开关去切图层菜单语义不对；而且它会让菜单项"隐式消失"（项少了，人第一反应是坏了）。
// 菜单项应当是全的、稳定的。
public class LayerCtx
{
    public LayerKind Kind = LayerKind.None;
    public bool Ok;              // 探测是否真的成功（PS 未运行/无文档 → false）
}

// #region LayerDetector — 图层探测
// r32 起：从"只回一个类型词（写 jsx 文件 + 轮询 30×50ms 等结果文件）"改成
// **一次 DoJavaScript 直接回 JSON**，省掉磁盘 IO 与轮询，呼出不可能再被它卡住。
// r34 删掉显示条件后回的内容就只剩类型词了，但这条快路径**保留不动**（纯性能改进，与条件显示无关）。
// 新路径失败时回退到旧的文件方式。
public class LayerDetector
{
    LayerCtx _c = null;
    DateTime _at = DateTime.MinValue;
    static readonly TimeSpan TTL = TimeSpan.FromMilliseconds(500);

    public LayerKind Detect() { return DetectCtx().Kind; }

    public LayerCtx DetectCtx()
    {
        if (_c != null && (DateTime.Now - _at) < TTL) return _c;
        LayerCtx r = null;
        try { r = DetectFast(); } catch { r = null; }
        if (r == null) { try { r = DetectViaFile(); } catch { r = null; } }
        if (r == null) r = new LayerCtx();
        _c = r; _at = DateTime.Now;
        return r;
    }

    // 单行 JSX：不用 #target、不落文件，最后一句的返回值就是结果
    // r34：只回类型词。原来还要算选区/蒙版（两个 try 块），条件显示删了就没必要了。
    const string ProbeJs = "(function(){try{"
        + "if(!app.documents.length)return'{\"ok\":1,\"k\":\"none\"}';"
        + "var d=app.activeDocument,l=null;"
        + "try{l=d.activeLayer;}catch(e){return'{\"ok\":1,\"k\":\"none\"}';}"
        + "var k='unknown';"
        + "if(l){"
        + "if(l.isBackgroundLayer)k='background';"
        + "else if(l.typename==='LayerSet')k='group';"
        + "else if(l.typename==='ArtLayer'){switch(l.kind){"
        + "case LayerKind.TEXT:k='text';break;"
        + "case LayerKind.SMARTOBJECT:k='smartObject';break;"
        + "case LayerKind.NORMAL:k='pixel';break;"
        + "case LayerKind.SOLIDFILL:case LayerKind.GRADIENTFILL:case LayerKind.PATTERNFILL:k='shape';break;"
        + "default:k='adjustment';break;}}"
        + "}"
        + "return'{\"ok\":1,\"k\":\"'+k+'\"}';"
        + "}catch(e){return'{\"ok\":0}';}})();";

    LayerCtx DetectFast()
    {
        object pa = PS.Get();
        if (pa == null) return null;
        Type pt = pa.GetType();
        object res = pt.InvokeMember("DoJavaScript", BindingFlags.InvokeMethod, null, pa, new object[] { ProbeJs });
        string raw = res as string;
        if (string.IsNullOrEmpty(raw)) return null;
        raw = raw.Trim();
        if (raw.Length == 0 || raw[0] != '{') return null;   // "undefined" 或异常串
        var o = JObject.Parse(raw);
        if (o["ok"] == null || o["ok"].Value<int>() != 1) return null;
        return new LayerCtx
        {
            Ok = true,
            Kind = LTM.Parse(o["k"]?.ToString() ?? "")
        };
    }

    // 兜底：老办法（写 jsx 文件 → DoJavaScriptFile → 轮询结果文件），只拿得到类型
    LayerCtx DetectViaFile()
    {
        string uid = Guid.NewGuid().ToString("N").Substring(0, 8);
        string jp = Path.Combine(Path.GetTempPath(), $"psdl_{uid}.jsx");
        string rfn = $"pslt_{uid}.txt";
        string rp = Path.Combine(Path.GetTempPath(), rfn);
        try
        {
            string js = $@"#target photoshop
function D(){{try{{if(!app.activeDocument)return'none';var d=app.activeDocument;try{{var l=d.activeLayer;}}catch(e){{return'none';}}if(!l)return'none';if(l.isBackgroundLayer)return'background';if(l.typename==='LayerSet')return'group';if(l.typename==='ArtLayer'){{switch(l.kind){{case LayerKind.TEXT:return'text';case LayerKind.SMARTOBJECT:return'smartObject';case LayerKind.NORMAL:return'pixel';case LayerKind.SOLIDFILL:case LayerKind.GRADIENTFILL:case LayerKind.PATTERNFILL:return'shape';default:return'adjustment';}}}}return'unknown';}}catch(e){{return'none';}}}}
var rr=D();var f=new File(Folder.temp+'/{rfn}');f.open('w');f.write(rr);f.close();rr;";
            File.WriteAllText(jp, js, new UTF8Encoding(false));
            object pa = PS.Get();
            if (pa != null)
            {
                Type pt = pa.GetType();
                pt.InvokeMember("DoJavaScriptFile", BindingFlags.InvokeMethod, null, pa, new object[] { jp });
                for (int i = 0; i < 30; i++) { if (File.Exists(rp)) break; System.Threading.Thread.Sleep(50); }
                if (File.Exists(rp))
                {
                    string raw = File.ReadAllText(rp, Encoding.UTF8).Trim();
                    try { File.Delete(rp); } catch { }
                    if (!string.IsNullOrEmpty(raw))
                        return new LayerCtx { Ok = true, Kind = LTM.Parse(raw.Split('\n')[0].Trim()) };
                }
            }
            try { File.Delete(jp); } catch { }
        }
        catch { }
        return null;
    }
}

}
}
