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
// #region Serialization
public static class AppConfigSerializer
{
    public static string ToJson(AC c)
    {
        var r = new JObject();
        r["menuWidth"] = c.MW; r["fontSize"] = c.FS;
        r["_zoom"] = c.Zoom; r["_pinnedSize"] = c.PS;
        r["_itemSpacing"] = c.IS; r["_opacity"] = c.Op;
        r["_bgColor"] = c.BC; r["_fgColor"] = c.FC;
        r["_hoverColor"] = c.HC; r["_outlineColor"] = c.OC;
        r["_iconColor"] = c.IC;
        r["_theme"] = c.Theme; r["_fsTier"] = c.FsTier;
        r["_freqSort"] = c.FreqSort; r["_showFreq"] = c.ShowFreq; r["_showPinName"] = c.ShowPinName;
        r["_hoverSub"] = c.HoverSub; r["_hoverMs"] = c.HoverMs; r["_glowK"] = c.GlowK; r["_sheenK"] = c.SheenK; r["_cr"] = c.CR; r["_bw"] = c.BW; r["_underK"] = c.UnderK; r["_underH"] = c.UnderH;
        var pa = new JArray();
        foreach (var p in c.Pinned)
        {
            var pj = new JObject { ["title"] = p.Title, ["icon"] = p.Icon, ["action"] = AT.ToStr(p.Action), ["value"] = p.Value, ["layerType"] = p.LK == LayerKind.None ? "" : p.LK.ToString().ToLower(), ["k"] = p.K };
            if (!string.IsNullOrEmpty(p.LibKey))
            {
                pj["libKey"] = p.LibKey;
                if (!string.IsNullOrEmpty(p.LibHash)) pj["libHash"] = p.LibHash;
            }
            pa.Add(pj);
        }
        r["_pinned"] = pa;
        var fo = new JObject();
        foreach (var kv in c.Freq) fo[kv.Key] = kv.Value;
        r["_freq"] = fo;
        var mo = new JObject();
        foreach (var m in c.Menus)
        {
            var ia = new JArray();
            foreach (var i in m.Items) ia.Add(i.ToJ());
            mo[m.Kind == LayerKind.Fallback ? "fallback" : m.Kind.ToString().ToLower()] = new JObject { ["label"] = m.Label, ["icon"] = m.Icon, ["items"] = ia };
        }
        r["menus"] = mo;
        return r.ToString(Newtonsoft.Json.Formatting.Indented);
    }
    public static AC FromJson(string json) { try { return FromJ(JObject.Parse(json)); } catch { return null; } }
    public static AC FromJ(JObject r)
    {
        var c = new AC();
        c.MW = r["menuWidth"]?.Value<double>() ?? 260;
        c.FS = r["fontSize"]?.Value<double>() ?? 13;
        c.Zoom = r["_zoom"]?.Value<double>() ?? 1;
        c.PS = r["_pinnedSize"]?.Value<double>() ?? 20;
        c.IS = r["_itemSpacing"]?.Value<double>() ?? 2;
        c.Op = r["_opacity"]?.Value<double>() ?? 0.96;
        c.BC = r["_bgColor"]?.ToString() ?? "#2D2D2D";
        c.FC = r["_fgColor"]?.ToString() ?? "#CCCCCC";
        c.HC = r["_hoverColor"]?.ToString() ?? "#094771";
        c.OC = r["_outlineColor"]?.ToString() ?? "#3E3E3E";
        c.IC = r["_iconColor"]?.ToString() ?? "";
        c.Theme = r["_theme"]?.ToString() ?? "psdark";
        c.FsTier = r["_fsTier"]?.Value<int>() ?? 1;
        c.FreqSort = r["_freqSort"]?.Value<bool>() ?? false;
        c.ShowFreq = r["_showFreq"]?.Value<bool>() ?? true;   // 老配置没有这个键 → 默认开
        c.ShowPinName = r["_showPinName"]?.Value<bool>() ?? true;
        c.HoverSub = r["_hoverSub"]?.Value<bool>() ?? true;   // 老配置没有这个键 → 默认开
        c.HoverMs = Math.Max(100, Math.Min(1500, (int)Math.Round(r["_hoverMs"]?.Value<double>() ?? 420)));
        c.GlowK = Math.Max(0, Math.Min(100, (int)Math.Round(r["_glowK"]?.Value<double>() ?? 90)));
        c.SheenK = Math.Max(0, Math.Min(100, r["_sheenK"]?.Value<double>() ?? 100));
        c.CR = Math.Max(0, Math.Min(12, r["_cr"]?.Value<double>() ?? 4));
        c.BW = Math.Max(0, Math.Min(3, r["_bw"]?.Value<double>() ?? 1));
        c.UnderK = Math.Max(0, Math.Min(100, (int)Math.Round(r["_underK"]?.Value<double>() ?? 60)));
        c.UnderH = Math.Max(2, Math.Min(14, r["_underH"]?.Value<double>() ?? 5));   // 老配置默认 90（与旧版观感一致）
        if (r["_pinned"] is JArray pa)
            foreach (JToken t in pa)
            {
                if (!(t is JObject o)) continue;
                string lt = o["layerType"]?.ToString() ?? "";
                c.Pinned.Add(new PI
                {
                    Title = o["title"]?.ToString() ?? "",
                    Icon = o["icon"]?.ToString() ?? "fa:Solid_Play",
                    Action = AT.From(o["action"]?.ToString() ?? ""),
                    Value = o["value"]?.ToString() ?? "",
                    LK = string.IsNullOrEmpty(lt) ? LayerKind.None : LTM.Parse(lt),
                    K = o["k"]?.ToString(),
                    LibKey = o["libKey"]?.ToString() ?? "",
                    LibHash = o["libHash"]?.ToString() ?? ""
                });
            }
        if (r["_freq"] is JObject fo)
            foreach (var kv in fo) c.Freq[kv.Key] = kv.Value?.Value<int>() ?? 0;
        c.Menus = new List<LM>();
        if (r["menus"] is JObject mo)
            foreach (var p in mo.Properties())
            {
                LayerKind k = LTM.Parse(p.Name);
                if (p.Name == "fallback") k = LayerKind.Fallback;
                if (k == LayerKind.None && p.Name != "fallback") continue;
                if (!(p.Value is JObject mj)) continue;
                var lm = new LM { Kind = k, Label = mj["label"]?.ToString() ?? p.Name, Icon = mj["icon"]?.ToString() ?? "fa:Solid_ThLarge" };
                if (mj["items"] is JArray ia)
                    foreach (JToken t in ia) { var mi = MI.FromJ(t as JObject); if (mi != null) lm.Items.Add(mi); }
                c.Menus.Add(lm);
            }
        foreach (var m in LTM.All)
            if (!c.Menus.Any(x => x.Kind == m.K))
                c.Menus.Add(new LM { Kind = m.K, Label = m.L, Icon = m.I });
        return c;
    }
}

}
}
