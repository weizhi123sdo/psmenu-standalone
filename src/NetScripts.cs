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
static class Txt
{
    public static string Copy() => "#target photoshop\nfunction f(){if(app.documents.length===0)return;var d=app.activeDocument,l=d.activeLayer;if(l.kind!==LayerKind.TEXT||l.textItem.contents=='')return;var t=l.textItem,o={};try{o.domFont=t.font}catch(e){}try{var v=t.size.value,ty=t.size.type.toString().toLowerCase(),dpi=d.resolution;if(ty.indexOf('px')>-1)o.ptSize=v*(72/dpi);else if(ty.indexOf('mm')>-1)o.ptSize=v*(72/25.4);else if(ty.indexOf('cm')>-1)o.ptSize=v*(72/2.54);else o.ptSize=v;}catch(e){}try{var c=t.color.rgb;o.domColor={r:c.red,g:c.green,b:c.blue};}catch(e){}try{var ref=new ActionReference();ref.putEnumerated(charIDToTypeID('Lyr '),charIDToTypeID('Ordn'),charIDToTypeID('Trgt'));var ld=executeActionGet(ref);var tkd=ld.getObjectValue(stringIDToTypeID('textKey'));var tsrl=tkd.getList(stringIDToTypeID('textStyleRange'));if(tsrl.count>0){var fs=tsrl.getObjectValue(0).getObjectValue(stringIDToTypeID('textStyle'));function sgu(desc,key){var id=stringIDToTypeID(key);if(desc.hasKey(id))return desc.getUnitDoubleValue(id,stringIDToTypeID('#Pnt'));return undefined;}if(fs.hasKey(stringIDToTypeID('leading'))){try{o.leading=sgu(fs,'leading');o.autoLeading=false}catch(e){o.autoLeading=true}}else o.autoLeading=true;if(fs.hasKey(stringIDToTypeID('tracking')))o.tracking=fs.getInteger(stringIDToTypeID('tracking'));if(fs.hasKey(stringIDToTypeID('horizontalScale')))o.horizontalScale=fs.getDouble(stringIDToTypeID('horizontalScale'));if(fs.hasKey(stringIDToTypeID('verticalScale')))o.verticalScale=fs.getDouble(stringIDToTypeID('verticalScale'));}}catch(e){}var ff=new File(Folder.temp+'/ps_v8_text_style.txt');ff.encoding='UTF-8';ff.open('w');ff.write(o.toSource());ff.close();}f();";
    public static string Paste() => "#target photoshop\nfunction f(){if(app.documents.length===0)return;var l=app.activeDocument.activeLayer;if(l.kind!==LayerKind.TEXT)return;var ff=new File(Folder.temp+'/ps_v8_text_style.txt');if(!ff.exists)return;ff.encoding='UTF-8';ff.open('r');var c=ff.read();ff.close();var o;try{o=eval(c)}catch(e){return}var t=l.textItem;try{if(o.domFont)t.font=o.domFont}catch(e){}try{if(o.ptSize)t.size=new UnitValue(o.ptSize,'pt')}catch(e){}try{if(o.domColor){var sc=new SolidColor();sc.rgb.red=o.domColor.r;sc.rgb.green=o.domColor.g;sc.rgb.blue=o.domColor.b;t.color=sc}}catch(e){}try{if(o.autoLeading===false&&o.leading!==undefined){t.useAutoLeading=false;t.leading=new UnitValue(o.leading,'pt')}else if(o.autoLeading===true)t.useAutoLeading=true}catch(e){}try{if(o.tracking!==undefined)t.tracking=o.tracking}catch(e){}try{if(o.horizontalScale!==undefined)t.horizontalScale=o.horizontalScale}catch(e){}}app.activeDocument.suspendHistory('apply','f()');";
    public static string Collapse() => "#target photoshop\nfunction f(){if(app.documents.length===0)return;var d=app.activeDocument,cl=d.activeLayer,ta=cl;while(ta.parent&&ta.parent.typename!=='Document')ta=ta.parent;if(ta.typename!=='LayerSet')return;try{executeAction(stringIDToTypeID('collapseAllGroupsEvent'),new ActionDescriptor(),DialogModes.NO)}catch(e){}if(ta.layers.length>0)d.activeLayer=ta.layers[0];else d.activeLayer=ta;}f();";
    public static string EnterSO() => "#target photoshop\nfunction f(){if(app.documents.length===0)return;var l=app.activeDocument.activeLayer;if(l.kind===LayerKind.SMARTOBJECT)try{executeAction(stringIDToTypeID('placedLayerEditContents'),new ActionDescriptor(),DialogModes.NO)}catch(e){}}f();";
    public static string Unlock() => "#target photoshop\nfunction f(){if(app.documents.length===0)return;var d=app.activeDocument;try{if(d.backgroundLayer)d.backgroundLayer.isBackgroundLayer=false}catch(e){}var ts=0;try{var r2=new ActionReference();r2.putProperty(charIDToTypeID('Prpr'),stringIDToTypeID('numberOfLayers'));r2.putEnumerated(charIDToTypeID('Dcmn'),charIDToTypeID('Ordn'),charIDToTypeID('Trgt'));ts=executeActionGet(r2).getInteger(stringIDToTypeID('numberOfLayers'));}catch(e){return}for(var j=1;j<=ts;j++){try{var r3=new ActionReference();r3.putIndex(charIDToTypeID('Lyr '),j);var ld=executeActionGet(r3);if(typeIDToStringID(ld.getEnumerationValue(stringIDToTypeID('layerSection')))!=='layerSectionEnd'&&ld.hasKey(stringIDToTypeID('layerLocking'))){var lk=ld.getObjectValue(stringIDToTypeID('layerLocking'));var d2=new ActionDescriptor();var rf=new ActionReference();rf.putIdentifier(charIDToTypeID('Lyr '),ld.getInteger(stringIDToTypeID('layerID')));d2.putReference(charIDToTypeID('null'),rf);var lp=new ActionDescriptor();var ks=['protectAll','protectComposite','protectPosition','protectTrans','protectArtboardAutonest'];var sh=false;for(var k=0;k<ks.length;k++){var kid=stringIDToTypeID(ks[k]);if(lk.hasKey(kid)&&lk.getBoolean(kid)===true){lp.putBoolean(kid,false);sh=true;}}if(sh){d2.putObject(charIDToTypeID('T   '),stringIDToTypeID('layerLocking'),lp);executeAction(charIDToTypeID('setd'),d2,DialogModes.NO);}}}catch(e){}}}app.activeDocument.suspendHistory('unlock','f()');";
}

// #region NetScripts — 脚本库（**只读**本地缓存）
// 分工：取数与浏览归独立动作「PS脚本库」，它把清单写到同一个缓存文件；
// 这里只读，不联网、不起线程。原先在呼出路径上起的后台拉取线程已退役——
// 那套做法赌"后台线程能活过本次调用"，实测没成，而且失败是静默的（查不到原因）。
// 好处：新机器上 A 不会因为网络卡住，库是空的也有明确提示（见编辑器·配置管理·脚本库卡片）。
public static class NetScripts
{
    public const string GiteeUrl = "https://gitee.com/weizhiOWO/quicker-actions/raw/main/ps-scripts/index.json";
    public const string Url = "https://raw.githubusercontent.com/weizhi123sdo/quicker-dist/main/ps-scripts/index.json";
    public const string ApiUrl = "https://api.github.com/repos/weizhi123sdo/quicker-dist/contents/ps-scripts/index.json";

    public static string CachePath()
    {
        string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMenu");
        try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch { }
        return Path.Combine(d, "ps_scripts.json");
    }

    public static readonly List<string> CatNames = new List<string>();
    public static readonly Dictionary<string, List<string>> ByCat = new Dictionary<string, List<string>>();
    public static readonly Dictionary<string, string> Cns = new Dictionary<string, string>();
    public static readonly Dictionary<string, string> Descs = new Dictionary<string, string>();
    public static readonly Dictionary<string, string> Codes = new Dictionary<string, string>();
    public static readonly Dictionary<string, string> Shots = new Dictionary<string, string>();   // key -> 效果图相对路径（B 取回后缓存在本机，A 只读缓存、不联网）
    public static readonly Dictionary<string, string> Hashes = new Dictionary<string, string>();  // key -> 正文指纹（挑脚本置入时写进菜单项，B 靠它判"菜单里是不是旧版"）
    public static readonly Dictionary<string, string> Pys = new Dictionary<string, string>();     // key -> 中文名拼音首字母（生成器算好，挑脚本时能打首字母搜）
    public static readonly Dictionary<string, List<string>> Risks = new Dictionary<string, List<string>>();   // key -> 危险 API 命中码
    public static readonly Dictionary<string, string> RiskNames = new Dictionary<string, string>();           // 码 -> 中文说明（清单里的词汇表）
    public static readonly Dictionary<string, int> RiskLevels = new Dictionary<string, int>();                // 码 -> 2 破坏性 / 1 提示性
    public static int Count { get { return Codes.Count; } }
    public static string LastMsg = "";
    public static DateTime Stamp = DateTime.MinValue;

    // 危险命中的中文说明（"会保存/覆盖文件、会关闭文档"）；没命中返回 ""
    public static string RiskText(string key)
    {
        List<string> rs;
        if (key == null || !Risks.TryGetValue(key, out rs) || rs == null || rs.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var c in rs)
        {
            string t;
            if (!RiskNames.TryGetValue(c, out t) || string.IsNullOrEmpty(t)) t = c;
            if (sb.Length > 0) sb.Append("、");
            sb.Append(t);
        }
        return sb.ToString();
    }
    // 破坏性（会写文件/关文档/删东西）：挑脚本时用红药丸标出来，跟"只会弹窗"区分开
    public static bool RiskHard(string key)
    {
        List<string> rs;
        if (key == null || !Risks.TryGetValue(key, out rs) || rs == null) return false;
        foreach (var c in rs)
        {
            int lv;
            if (!RiskLevels.TryGetValue(c, out lv)) lv = 2;
            if (lv >= 2) return true;
        }
        return false;
    }

    // 拼音首字母命中：只对纯字母查询走（汉字直搜由调用方负责）
    public static bool PyMatch(string key, string q)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(q)) return false;
        string py;
        if (!Pys.TryGetValue(key, out py) || string.IsNullOrEmpty(py)) return false;
        for (int i = 0; i < q.Length; i++)
        {
            char c = q[i];
            if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
            if (c < 'a' || c > 'z') return false;
        }
        return py.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // 效果图在本机是否已有缓存（有就返回路径；没有返回 null —— A 不去下载，那是 B 的活）
    public static string ShotCached(string key)
    {
        string rel;
        if (key == null || !Shots.TryGetValue(key, out rel) || string.IsNullOrEmpty(rel)) return null;
        try
        {
            string fn = Path.GetFileName(rel.Replace('/', Path.DirectorySeparatorChar));
            if (fn.Length == 0) return null;
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMenu", "ps_shots", fn);
            return (File.Exists(p) && new FileInfo(p).Length > 0) ? p : null;
        }
        catch { return null; }
    }

    static bool _loaded, _busy;

    static bool ApplyJson(string js)
    {
        try
        {
            var jo = JObject.Parse(js);
            if (jo["scripts"] == null) return false;
            CatNames.Clear(); ByCat.Clear(); Cns.Clear(); Descs.Clear(); Codes.Clear(); Shots.Clear(); Hashes.Clear(); Pys.Clear(); Risks.Clear();
            // 危险 API 的词汇表与级别随清单下来（生成器维护唯一一份）
            var rl0 = jo["riskLabels"] as JObject;
            if (rl0 != null)
                foreach (var kv2 in rl0.Properties())
                {
                    var o2 = kv2.Value as JObject;
                    if (o2 != null)
                    {
                        RiskNames[kv2.Name] = o2["label"] == null ? kv2.Name : o2["label"].ToString();
                        try { RiskLevels[kv2.Name] = o2["level"] == null ? 2 : (int)o2["level"]; } catch { RiskLevels[kv2.Name] = 2; }
                    }
                    else { RiskNames[kv2.Name] = kv2.Value.ToString(); RiskLevels[kv2.Name] = 2; }
                }
            if (jo["scripts"] is JObject so)
                foreach (var kv in so.Properties())
                {
                    var v = kv.Value;
                    string code = v["code"] == null ? "" : v["code"].ToString();
                    if (code.Length == 0) continue;
                    Cns[kv.Name] = v["cn"] == null ? kv.Name : v["cn"].ToString();
                    Descs[kv.Name] = v["desc"] == null ? "" : v["desc"].ToString();
                    Codes[kv.Name] = code;
                    string h0 = v["hash"] == null ? "" : v["hash"].ToString().Trim();
                    if (h0.Length > 0) Hashes[kv.Name] = h0;
                    string py0 = v["py"] == null ? "" : v["py"].ToString().Trim();
                    if (py0.Length > 0) Pys[kv.Name] = py0;
                    var ra0 = v["risk"] as JArray;
                    if (ra0 != null && ra0.Count > 0)
                    {
                        var rs0 = new List<string>();
                        foreach (var x0 in ra0) { string c0 = x0.ToString(); if (c0.Length > 0) rs0.Add(c0); }
                        if (rs0.Count > 0) Risks[kv.Name] = rs0;
                    }
                    string shot = v["shot"] == null ? "" : v["shot"].ToString().Trim();
                    if (shot.Length > 0) Shots[kv.Name] = shot;
                }
            if (jo["categories"] is JArray ca)
                foreach (var c in ca)
                {
                    string nm = c["name"] == null ? "" : c["name"].ToString();
                    var keys = new List<string>();
                    if (c["scripts"] is JArray ka)
                        foreach (var k in ka) { string s = k.ToString(); if (Codes.ContainsKey(s)) keys.Add(s); }
                    if (nm.Length > 0 && keys.Count > 0) { CatNames.Add(nm); ByCat[nm] = keys; }
                }
            Stamp = DateTime.Now;
            return true;
        }
        catch (Exception ex) { LogFail("NetScripts.ApplyJson", ex); return false; }
    }

    // 只读缓存。呼出路径上就调它。
    public static void Load()
    {
        try
        {
            string p = CachePath();
            if (File.Exists(p)) ApplyJson(File.ReadAllText(p, Encoding.UTF8));
        }
        catch (Exception ex) { LogFail("NetScripts.Load", ex); }
    }

    // 呼出时调用：读缓存（一次），必要时后台补拉。**不阻塞**。
}






// ---------- 试运行（窗口先藏 → 后台发 PS → 脚本结束后窗口再回来） ----------
// DoJavaScriptFile 是同步调用，PS 跑多久它就堵多久——直接在 UI 线程上发，整个窗口会被冻到脚本结束（r56 实测）。
// okSuffix：成功提示的尾巴（如挑选窗里的"确认好用再点「加入菜单」"），可为 null。
}
}
