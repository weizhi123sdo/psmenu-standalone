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
// #region ConfigService
// #region FailLog — 运行期错误落盘（原先大量 catch 是静默的，出问题无迹可循）
static public string FailLogPath = Path.Combine(Path.GetTempPath(), "ps_cm_error.log");
public static void LogFail(string where, Exception ex)
{
    try
    {
        string line = DateTime.Now.ToString("MM-dd HH:mm:ss") + "  [" + where + "]  " + ex.ToString() + Environment.NewLine;
        var fi = new FileInfo(FailLogPath);
        if (fi.Exists && fi.Length > 256 * 1024) File.Delete(FailLogPath);   // 超 256KB 滚动重建，避免无限增长
        File.AppendAllText(FailLogPath, line, Encoding.UTF8);
    }
    catch { }
}
// #endregion

public static class ConfigService
{
    static string _p;
    static AC _cache;
    static DateTime _cacheStamp;

    /// <summary>存储根：%APPDATA%\PSMenu</summary>
    public static string DataDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMenu"); }
    }

    /// <summary>
    /// 独立版首次启动迁移：旧 Quicker 动作时代的配置/最近使用/图标与脚本库缓存/效果图，
    /// 一次性拷到 %APPDATA%\PSMenu，之后两者互不干扰。幂等：新目录已存在配置则跳过。
    /// </summary>
    /// <summary>本次启动是否真的发生了迁移（供首启气泡提示用，App.cs 调 MigrateLegacy 后可读）</summary>
    public static bool DidMigrate = false;

    public static void MigrateLegacy()
    {
        try
        {
            string nd = DataDir;
            string od = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quicker");
            if (!Directory.Exists(od)) return;
            if (File.Exists(Path.Combine(nd, "ps_cm_config.json"))) return;   // 已迁移过
            Directory.CreateDirectory(nd);
            foreach (var fn in new[] { "ps_cm_config.json", "ps_cm_recent.json", "ps_icons.json", "ps_scripts.json" })
            {
                string src = Path.Combine(od, fn);
                if (File.Exists(src)) { var dst = Path.Combine(nd, fn); if (!File.Exists(dst)) File.Copy(src, dst); }
            }
            foreach (var dir in new[] { "ps_cm_snapshots", "ps_shots" })
            {
                string sd = Path.Combine(od, dir);
                if (!Directory.Exists(sd)) continue;
                string dd = Path.Combine(nd, dir);
                Directory.CreateDirectory(dd);
                foreach (var f in Directory.GetFiles(sd)) { var dst = Path.Combine(dd, Path.GetFileName(f)); if (!File.Exists(dst)) File.Copy(f, dst); }
            }
            DidMigrate = true;   // 迁移真的发生过了：外壳首启气泡要据此提示
            LogFail("MigrateLegacy", new Exception("已从 Quicker 目录迁移配置到 " + nd));  // 借错误日志留痕（成功也记）
        }
        catch { }
    }

    public static string P2
    {
        get
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMenu");
            Directory.CreateDirectory(d);
            return _p = Path.Combine(d, "ps_cm_config.json");
        }
    }
    public static AC Load()
    {
        string p = P2;
        try { var ft = File.GetLastWriteTimeUtc(p); if (_cache != null && _cacheStamp == ft) return _cache; } catch { }
        if (File.Exists(p))
            try { _cache = AppConfigSerializer.FromJ(JObject.Parse(File.ReadAllText(p, Encoding.UTF8))); _cacheStamp = File.GetLastWriteTimeUtc(p); return _cache; }
            catch { string bk = p + ".bak"; if (File.Exists(bk)) try { _cache = AppConfigSerializer.FromJ(JObject.Parse(File.ReadAllText(bk, Encoding.UTF8))); _cacheStamp = File.GetLastWriteTimeUtc(bk); return _cache; } catch { } }
        _cache = null;
        return Default();
    }
    public static void Save(AC c)
    {
        string p = P2;
        try { _cache = null; string tmp = p + ".tmp"; File.WriteAllText(tmp, AppConfigSerializer.ToJson(c), Encoding.UTF8); Swap(tmp, p); }
        catch (Exception ex) { LogFail("ConfigService.Save", ex); }
    }
    // ===== 快照：菜单配置是长期攒出来的资产，改坏了原先只能靠"之前导出过的剪贴板内容"救 =====
    public const int KeepSnaps = 5;
    public static string SnapDir
    {
        get
        {
            string d = Path.Combine(Path.GetDirectoryName(P2) ?? ".", "ps_cm_snapshots");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }
    // 在"可能改坏配置"的动作之前留一份（编辑器保存 / 导入 / 恢复默认 / 回滚）
    public static string Snapshot(string tag)
    {
        try
        {
            string p = P2;
            if (!File.Exists(p)) return null;
            string nm = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + (string.IsNullOrEmpty(tag) ? "snap" : tag) + ".json";
            string dst = Path.Combine(SnapDir, nm);
            File.Copy(p, dst, true);
            PruneSnaps();
            return dst;
        }
        catch (Exception ex) { LogFail("ConfigService.Snapshot", ex); return null; }
    }
    static void PruneSnaps()
    {
        try
        {
            var fs = new DirectoryInfo(SnapDir).GetFiles("*.json");
            foreach (var f in fs.OrderByDescending(x => x.LastWriteTimeUtc).Skip(KeepSnaps)) f.Delete();
        }
        catch { }
    }
    // 快照文件全路径，最新在前
    public static List<string> SnapList()
    {
        var res = new List<string>();
        try
        {
            var fs = new DirectoryInfo(SnapDir).GetFiles("*.json").OrderByDescending(x => x.LastWriteTimeUtc).Take(KeepSnaps);
            foreach (var f in fs) res.Add(f.FullName);
        }
        catch { }
        return res;
    }
    // 删掉一份快照（快照列表右侧的删除键）。只动快照目录里的文件，不碰当前配置。
    public static bool DeleteSnap(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return false;
            // 只允许删快照目录里的文件：路径是界面传进来的，但这类"按路径删文件"的操作值得卡一道
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(Path.GetFullPath(SnapDir), StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(full)) return true;      // 已经不在了，当作成功
            File.Delete(full);
            return true;
        }
        catch (Exception ex) { LogFail("ConfigService.DeleteSnap", ex); return false; }
    }

    public static bool RestoreSnap(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var c = AppConfigSerializer.FromJ(JObject.Parse(File.ReadAllText(path, Encoding.UTF8)));
            if (c == null || c.Menus == null || c.Menus.Count == 0) return false;
            Snapshot("before-rollback");   // 回滚本身也留一份：万一回错了还能再回来
            Save(c);
            return true;
        }
        catch (Exception ex) { LogFail("ConfigService.RestoreSnap", ex); return false; }
    }

    // 原子换入：目标已存在则 Replace（顺带生成 .bak），否则直接 Move —— 替代原先 Copy+Delete+Move 三步
    static void Swap(string tmp, string target)
    {
        if (File.Exists(target)) File.Replace(tmp, target, target + ".bak");
        else File.Move(tmp, target);
    }
    // 只更新配置文件里的界面字号档位，避免把其它未保存的改动一并落盘
    public static void SaveTier(int tier)
    {
        string p = P2;
        try
        {
            if (!File.Exists(p)) return;
            var j = JObject.Parse(File.ReadAllText(p, Encoding.UTF8));
            j["_fsTier"] = tier;
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, j.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
            Swap(tmp, p);
            _cache = null;
        }
        catch (Exception ex) { LogFail("ConfigService.SaveTier", ex); }
    }
    public static void Track(AC c, string t, string a, string v)
    {
        if (c == null || !c.ShowFreq) return;   // 关闭「使用次数」后不再累计
        string k = t + "||" + a + "||" + v;
        c.Freq.TryGetValue(k, out var cnt);
        c.Freq[k] = cnt + 1;
    }
    public static int FreqCount(MI item, AC cfg)
    {
        if (cfg == null || cfg.Freq == null || item == null || !cfg.ShowFreq) return 0;
        return cfg.Freq.TryGetValue(item.UK, out var v) ? v : 0;
    }

    // ===== 配置方案（profiles） =====
    // 一个方案 = %APPDATA%\PSMenu\profiles\ 下的一份完整配置 JSON（与 ps_cm_config.json 同 schema，
    // 文件名即方案名，中文名直接用文件名）。「默认」方案即 ps_cm_config.json 本体：永远存在、不可删除。
    // 切换语义（简化实现）：当前配置永远实时落盘在 ps_cm_config.json，切换 = 把当前配置存回它原来
    // 所在的方案文件（离开默认方案时顺带把本体物化成 默认.json）→ 目标方案内容写进 ps_cm_config.json
    // → sidecar shell_state.json 的 profile 字段记下新方案名（缺省 = 默认）。
    public static class Profiles
    {
        public const string DefaultName = "默认";
        static string Dir { get { return Path.Combine(DataDir, "profiles"); } }
        static string PathOf(string name) { return Path.Combine(Dir, name + ".json"); }

        // 另存名合法性：非空、不是保留名「默认」、不含文件名非法字符、结尾不能是点（Windows 会吞掉）
        public static bool IsValidName(string n)
        {
            n = (n ?? "").Trim();
            if (n.Length == 0 || n == DefaultName || n.EndsWith(".")) return false;
            return n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        // 当前方案名：直接读写 shell_state.json 的 profile 字段。不经过 ShellConfig（那边保存是
        // 整文件重写，两边各管各的字段互不知晓，这里读改写整份 JSON 才不会覆盖掉它管的触发字段）。
        public static string Current()
        {
            try
            {
                string sp = Path.Combine(DataDir, "shell_state.json");
                if (!File.Exists(sp)) return DefaultName;
                var o = JObject.Parse(File.ReadAllText(sp, Encoding.UTF8));
                string p = o["profile"] != null ? o["profile"].ToString() : null;
                return string.IsNullOrEmpty(p) ? DefaultName : p;
            }
            catch { return DefaultName; }
        }
        static void SetCurrent(string name)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                string sp = Path.Combine(DataDir, "shell_state.json");
                var o = File.Exists(sp) ? JObject.Parse(File.ReadAllText(sp, Encoding.UTF8)) : new JObject();
                o["profile"] = name;
                string tmp = sp + ".tmp";
                File.WriteAllText(tmp, o.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
                if (File.Exists(sp)) File.Replace(tmp, sp, null);
                else File.Move(tmp, sp);
            }
            catch (Exception ex) { LogFail("Profiles.SetCurrent", ex); }
        }

        // 全部方案名：默认固定在最前，其余按名称排序（目录不存在时只有默认一项）
        public static List<string> List()
        {
            var res = new List<string> { DefaultName };
            try
            {
                if (Directory.Exists(Dir))
                    foreach (var f in Directory.GetFiles(Dir, "*.json"))
                    {
                        string n = Path.GetFileNameWithoutExtension(f);
                        if (n != DefaultName) res.Add(n);   // 万一有人手动放了 默认.json，不算独立方案
                    }
                res.Sort(1, res.Count - 1, StringComparer.CurrentCulture);
            }
            catch (Exception ex) { LogFail("Profiles.List", ex); }
            return res;
        }

        // 切换：先把当前生效配置存回原方案文件（含默认方案——离开默认时把本体存成 默认.json），
        // 再把目标方案写进 ps_cm_config.json 使其生效。覆盖前自动留快照，切错了能回滚。
        public static bool SwitchTo(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return false;
                string cur = Current();
                if (name == cur) return true;
                Directory.CreateDirectory(Dir);
                if (File.Exists(P2)) File.Copy(P2, PathOf(cur), true);
                string src = PathOf(name);
                if (!File.Exists(src)) return false;   // 上面 Copy 之后默认方案一定存在，到这里还是缺文件就是真坏档
                Snapshot("before-profile-switch");
                File.Copy(src, P2, true);
                _cache = null;
                SetCurrent(name);
                return true;
            }
            catch (Exception ex) { LogFail("Profiles.SwitchTo", ex); return false; }
        }

        // 另存：把当前生效配置（实时落盘的 ps_cm_config.json）复制为 profiles\<新名>.json 并切过去
        public static bool SaveAs(string name)
        {
            try
            {
                name = (name ?? "").Trim();
                if (!IsValidName(name)) return false;
                Directory.CreateDirectory(Dir);
                string dst = PathOf(name);
                if (File.Exists(dst)) return false;   // 重名（编辑器侧有预校验，这里是兜底）
                File.Copy(P2, dst);
                SetCurrent(name);
                return true;
            }
            catch (Exception ex) { LogFail("Profiles.SaveAs", ex); return false; }
        }

        // 删除方案：删文件。默认不可删；删的是当前方案时把方案名切回默认即可——配置本体
        // （ps_cm_config.json）就是被删方案的内容，不动它，免得用户正在用的设置凭空丢掉。
        public static bool Delete(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || name == DefaultName) return false;
                string f = PathOf(name);
                if (!File.Exists(f)) return true;   // 已不在了当作成功
                File.Delete(f);
                if (Current() == name) SetCurrent(DefaultName);
                return true;
            }
            catch (Exception ex) { LogFail("Profiles.Delete", ex); return false; }
        }
    }

    public static AC Default()
    {
        var c = new AC { Menus = new List<LM>() };
        var fillSub = new List<MI> {
            new MI{Title="填充前景色",Icon="fa:Solid_FillDrip",Action=ActionType.Keys,Value="!{DEL}"},
            new MI{Title="填充背景色",Icon="fa:Solid_Fill",Action=ActionType.Keys,Value="^!{DEL}"}
        };
        var fillG = new MI { Title = "填充颜色", Icon = "fa:Solid_Play", Sub = fillSub };
        var styleSub = new List<MI> {
            new MI{Title="复制图层样式",Icon="fa:Regular_Copy",Action=ActionType.Script,Value="var r=new ActionReference();r.putEnumerated(stringIDToTypeID('layer'),stringIDToTypeID('ordinal'),stringIDToTypeID('targetEnum'));var d=new ActionDescriptor();d.putReference(stringIDToTypeID('null'),r);executeAction(stringIDToTypeID('copyToLayer'),d,DialogModes.NO);"},
            new MI{Title="粘贴图层样式",Icon="fa:Regular_Clipboard",Action=ActionType.Script,Value="var r=new ActionReference();r.putEnumerated(stringIDToTypeID('layer'),stringIDToTypeID('ordinal'),stringIDToTypeID('targetEnum'));var d=new ActionDescriptor();d.putReference(stringIDToTypeID('null'),r);executeAction(stringIDToTypeID('pasteToLayer'),d,DialogModes.NO);"},
            new MI{Title="清除图层样式",Icon="fa:Solid_TrashAlt",Action=ActionType.Keys,Value="!l y a"}
        };
        var styleG = new MI { Title = "图层样式", Icon = "fa:Solid_LayerGroup", Sub = styleSub };
        var exportSub = new List<MI> {
            new MI{Title="快速导出为PNG",Icon="fa:Solid_FileImage",Action=ActionType.Keys,Value="^+'"},
            new MI{Title="导出为PNG",Icon="fa:Solid_FileExport",Action=ActionType.Script,Value="var f=File(app.activeDocument.fullName);var n=f.name.replace(/\\.[^.]+$/,'');var p=Folder.selectDialog('save to...');if(p){var o=new ExportOptionsSaveForWeb();o.format=SaveDocumentType.PNG;o.PNG8=false;app.activeDocument.exportDocument(new File(p+'/'+n+'.png'),ExportType.SAVEFORWEB,o);}"},
            new MI{Title="导出为JPG",Icon="fa:Solid_FileExport",Action=ActionType.Script,Value="var f=File(app.activeDocument.fullName);var n=f.name.replace(/\\.[^.]+$/,'');var p=Folder.selectDialog('save to...');if(p){var o=new ExportOptionsSaveForWeb();o.format=SaveDocumentType.JPEG;o.quality=90;app.activeDocument.exportDocument(new File(p+'/'+n+'.jpg'),ExportType.SAVEFORWEB,o);}"}
        };
        var exportG = new MI { Title = "导出图层...", Icon = "fa:Solid_FileExport", Sub = exportSub };
        var adjSub = new List<MI> {
            new MI{Title="色相/饱和度",Icon="fa:Solid_SlidersH",Action=ActionType.Keys,Value="^u"},
            new MI{Title="色阶",Icon="fa:Solid_ChartBar",Action=ActionType.Keys,Value="^l"},
            new MI{Title="曲线",Icon="fa:Solid_WaveSquare",Action=ActionType.Keys,Value="^m"}
        };
        var adjG = new MI { Title = "调整层", Icon = "fa:Solid_Play", Sub = adjSub };
        var tpSub = new List<MI> {
            new MI{Title="吸取文字属性",Icon="fa:Solid_Eyedropper",Action=ActionType.Script,Value=Txt.Copy()},
            new MI{Title="应用文字属性",Icon="fa:Solid_PaintBrush",Action=ActionType.Script,Value=Txt.Paste()}
        };
        var tpG = new MI { Title = "文字属性", Icon = "fa:Solid_Eyedropper", Sub = tpSub };

        c.Menus.Add(new LM { Kind = LayerKind.Pixel, Label = "像素层", Icon = "fa:Solid_Image", Items = new List<MI> { fillG, styleG, exportG, adjG } });
        c.Menus.Add(new LM { Kind = LayerKind.Text, Label = "文字层", Icon = "fa:Solid_Font", Items = new List<MI> { fillG, tpG, new MI { Title = "转换为形状", Icon = "fa:Solid_Shapes", Action = ActionType.Script, Value = "app.activeDocument.activeLayer.convertToShape();" }, new MI { Title = "栅格化文字", Icon = "fa:Solid_ThLarge", Action = ActionType.Script, Value = "app.activeDocument.activeLayer.rasterize(RasterizeType.TEXTCONTENTS);" } } });
        c.Menus.Add(new LM { Kind = LayerKind.Shape, Label = "形状层", Icon = "fa:Solid_Shapes", Items = new List<MI> { fillG, new MI { Title = "转为智能对象", Icon = "fa:Solid_Cube", Action = ActionType.Script, Value = "var id=charIDToTypeID('Mk  ');var d=new ActionDescriptor();d.putClass(charIDToTypeID('Nw  '),charIDToTypeID('Plc '));var r=new ActionReference();r.putClass(stringIDToTypeID('smartObject'));d.putReference(charIDToTypeID('Usng'),r);r.putEnumerated(charIDToTypeID('Lyr '),charIDToTypeID('Ordn'),charIDToTypeID('Trgt'));d.putReference(charIDToTypeID('At  '),r);executeAction(id,d,DialogModes.NO);" }, new MI { Title = "栅格化形状", Icon = "fa:Solid_ThLarge", Action = ActionType.Keys, Value = "^{r}" }, new MI { Title = "建立选区", Icon = "fa:Solid_BorderAll", Action = ActionType.Script, Value = "var r=new ActionReference();r.putProperty(stringIDToTypeID('property'),stringIDToTypeID('selection'));r.putEnumerated(stringIDToTypeID('layer'),stringIDToTypeID('ordinal'),stringIDToTypeID('targetEnum'));var d=new ActionDescriptor();d.putReference(stringIDToTypeID('null'),r);executeAction(stringIDToTypeID('load'),d,DialogModes.NO);" } } });
        c.Menus.Add(new LM { Kind = LayerKind.SmartObject, Label = "智能对象", Icon = "fa:Solid_Cube", Items = new List<MI> { new MI { Title = "编辑内容", Icon = "fa:Solid_Edit", Action = ActionType.Script, Value = "app.activeDocument.activeLayer.editContents();" }, new MI { Title = "通过拷贝新建", Icon = "fa:Regular_Copy", Action = ActionType.Script, Value = "var r=new ActionReference();r.putEnumerated(stringIDToTypeID('layer'),stringIDToTypeID('ordinal'),stringIDToTypeID('targetEnum'));var d=new ActionDescriptor();d.putReference(stringIDToTypeID('null'),r);executeAction(stringIDToTypeID('newPlacedLayer'),d,DialogModes.NO);" }, new MI { Title = "导出内容到文件", Icon = "fa:Solid_FileExport", Action = ActionType.Script, Value = "var id=stringIDToTypeID('placedLayerEditContent');var d=new ActionDescriptor();d.putEnumerated(charIDToTypeID('Actn'),charIDToTypeID('Actn'),stringIDToTypeID('placedLayerExportContents'));executeAction(id,d,DialogModes.NO);" }, new MI { Title = "栅格化", Icon = "fa:Solid_ThLarge", Action = ActionType.Keys, Value = "^{r}" } } });
        c.Menus.Add(new LM { Kind = LayerKind.Adjustment, Label = "调整层", Icon = "fa:Solid_SlidersH", Items = new List<MI> { new MI { Title = "编辑调整属性", Icon = "fa:Solid_SlidersH", Action = ActionType.Keys, Value = "{Return}" }, new MI { Title = "创建剪贴蒙版", Icon = "fa:Solid_Cut", Action = ActionType.Keys, Value = "^!g" }, new MI { Title = "删除调整层", Icon = "fa:Solid_TrashAlt", Action = ActionType.Keys, Value = "{DEL}" } } });
        c.Menus.Add(new LM { Kind = LayerKind.Group, Label = "图层组", Icon = "fa:Solid_Folder", Items = new List<MI> { new MI { Title = "展开/折叠组", Icon = "fa:Solid_FolderOpen", Action = ActionType.Script, Value = Txt.Collapse() }, new MI { Title = "解散图层组", Icon = "fa:Solid_ObjectUngroup", Action = ActionType.Keys, Value = "^+g" }, new MI { Title = "解锁组别", Icon = "fa:Solid_Play", Action = ActionType.Script, Value = Txt.Unlock() } } });
        c.Menus.Add(new LM { Kind = LayerKind.Background, Label = "背景层", Icon = "fa:Solid_Lock", Items = new List<MI> { new MI { Title = "解锁背景层", Icon = "fa:Solid_LockOpen", Action = ActionType.Keys, Value = "{DEL}" }, new MI { Title = "复制到新图层", Icon = "fa:Regular_Copy", Action = ActionType.Keys, Value = "^j" }, new MI { Title = "新建空白图层", Icon = "fa:Regular_File", Action = ActionType.Keys, Value = "^+n" } } });
        c.Menus.Add(new LM { Kind = LayerKind.Unknown, Label = "其他类型", Icon = "fa:Solid_QuestionCircle", Items = new List<MI> { new MI { Title = "复制图层", Icon = "fa:Regular_Copy", Action = ActionType.Keys, Value = "^j" }, new MI { Title = "删除图层", Icon = "fa:Solid_TrashAlt", Action = ActionType.Keys, Value = "{DEL}" } } });
        var fb = new List<MI> { new MI { Title = "进入对象", Icon = "fa:Solid_Play", Action = ActionType.Script, Value = Txt.EnterSO() } };
        fb.Add(new MI { IsSep = true });
        fb.Add(new MI { Title = "打开配置编辑器", Icon = "fa:Solid_Gear", Action = ActionType.EditConfig });
        fb.Add(new MI { Title = "导出配置", Icon = "fa:Solid_FileExport", Action = ActionType.ExportConfig });
        fb.Add(new MI { Title = "导入配置", Icon = "fa:Solid_FileImport", Action = ActionType.ImportConfig });
        c.Menus.Add(new LM { Kind = LayerKind.Fallback, Label = "通用", Icon = "fa:Solid_ThLarge", Items = fb });
        return c;
    }
}

// Embedded script storage
// #region Recent — 最近使用（图标/取色）落盘，跨 Quicker 重启保留
public static class Recent
{
    static string FilePath()
    {
        string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMenu");
        Directory.CreateDirectory(d);
        return Path.Combine(d, "ps_cm_recent.json");
    }
    static bool _loaded = false;
    public static double IconCell = 46;    // 选择器里图标格子的边长
    public static bool HelpShown = false;  // 「使用帮助」是否已自动弹过（只在第一次打开设置时弹一次）
    public static bool ShowNames = false;  // 选择器里是否在图标下显示名字
    public static void Ensure()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            string p = FilePath();
            if (!File.Exists(p)) return;
            var j = JObject.Parse(File.ReadAllText(p, Encoding.UTF8));
            var ia = j["icons"] as JArray;
            if (ia != null) foreach (var t in ia) { string v = t.ToString(); if (!string.IsNullOrEmpty(v) && !IconRecent.Contains(v)) IconRecent.Add(v); }
            var ca = j["colors"] as JArray;
            if (ca != null) foreach (var t in ca) { string v = t.ToString(); if (!string.IsNullOrEmpty(v) && !RecentColors.Contains(v)) RecentColors.Add(v); }
            try { if (j["iconCell"] != null) IconCell = Convert.ToDouble(j["iconCell"].ToString(), System.Globalization.CultureInfo.InvariantCulture); } catch { }
            try { if (j["showNames"] != null) ShowNames = j["showNames"].ToString().ToLowerInvariant() == "true"; } catch { }
            try { if (j["helpShown"] != null) HelpShown = j["helpShown"].ToString().ToLowerInvariant() == "true"; } catch { }
        }
        catch { }
    }
    public static void Save()
    {
        try
        {
            var j = new JObject();
            j["icons"] = new JArray(IconRecent.Take(24).ToArray());
            j["colors"] = new JArray(RecentColors.Take(12).ToArray());
            j["iconCell"] = IconCell;
            j["showNames"] = ShowNames;
            j["helpShown"] = HelpShown;
            string p = FilePath(), tmp = p + ".tmp";
            File.WriteAllText(tmp, j.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8);
            if (File.Exists(p)) File.Delete(p);
            File.Move(tmp, p);
        }
        catch (Exception ex) { LogFail("Recent.Save", ex); }
    }
}
// #endregion

static AC UiCfg = null;   // 供图标选择器取主题色（编辑器与菜单两条路径都会赋值）

}
}