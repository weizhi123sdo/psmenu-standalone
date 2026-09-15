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
// #region NetIcons — 网络图标库（矢量清单：仓库出数据、本地缓存、WPF 现画，不需图片）
public static class NetIcons
{
    // 仓库里的清单（私有仓需 token，见 .ghtoken；公开仓则不需要）
    // 分发仓 quicker-dist（只放可抓取内容、公开）：Gitee 为主（国内直连、免 token），GitHub 为备
    public const string Url = "https://raw.githubusercontent.com/weizhi123sdo/quicker-dist/main/ps-icons/index.json";
    public const string ApiUrl = "https://api.github.com/repos/weizhi123sdo/quicker-dist/contents/ps-icons/index.json";

    // Gitee 镜像：国内直连可用、公开仓无需 token（raw 会 302 到 raw.giteeusercontent.com，跨主机后不带鉴权头）
    public const string GiteeUrl = "https://gitee.com/weizhiOWO/quicker-actions/raw/main/ps-icons/index.json";
    static string CachePath()
    {
        string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMenu");
        Directory.CreateDirectory(d);
        return Path.Combine(d, "ps_icons.json");
    }
    static JObject _data; static List<string> _names = new List<string>(); static bool _tried;
    static void Load()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            string p = CachePath();
            if (File.Exists(p)) _data = JObject.Parse(File.ReadAllText(p, Encoding.UTF8));
        }
        catch (Exception ex) { LogFail("NetIcons.Load", ex); }
        BuildNames();
    }
    static void BuildNames()
    {
        _names = new List<string>();
        try
        {
            var ic = _data == null ? null : _data["icons"] as JObject;
            if (ic != null) foreach (var kv in ic) _names.Add(kv.Key);
        }
        catch { }
        BuildGroups();
    }
    // 分组清单（清单 groups 字段）：名字 → 该组的图标名列表；老缓存没有此字段时为空
    static List<KeyValuePair<string, List<string>>> _groups;
    static void BuildGroups()
    {
        _groups = new List<KeyValuePair<string, List<string>>>();
        try
        {
            var gs = _data == null ? null : _data["groups"] as JArray;
            if (gs != null)
                foreach (var g in gs)
                {
                    string nm = g["name"] == null ? "" : g["name"].ToString();
                    var lst = new List<string>();
                    var ia = g["icons"] as JArray;
                    if (ia != null) foreach (var t in ia) lst.Add(t.ToString());
                    if (nm.Length > 0 && lst.Count > 0) _groups.Add(new KeyValuePair<string, List<string>>(nm, lst));
                }
        }
        catch (Exception ex) { LogFail("NetIcons.BuildGroups", ex); }
    }
    public static List<KeyValuePair<string, List<string>>> Groups { get { Load(); return _groups ?? new List<KeyValuePair<string, List<string>>>(); } }
    // 图标（net: 前缀或裸名）是否属于某分组；分组名在清单里不存在时不过滤（向后兼容老缓存）
    public static bool InGroup(string spec, string gname)
    {
        if (string.IsNullOrEmpty(gname)) return true;
        Load();
        string nm = string.IsNullOrEmpty(spec) ? "" : (spec.StartsWith("net:", StringComparison.OrdinalIgnoreCase) ? spec.Substring(4) : spec);
        foreach (var g in _groups ?? new List<KeyValuePair<string, List<string>>>())
        {
            if (g.Key != gname) continue;
            return g.Value.Contains(nm);
        }
        return true;
    }
    public static string Token()
    {
        try
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quicker", "RemoteCache", ".ghtoken");
            if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8).Trim();
        }
        catch { }
        return null;
    }
    // 联网刷新清单并写本地缓存；失败保留旧缓存
    // 通道式取数：任一成功即用；失败时把每个通道的原因一并报出来，便于定位
    public static string HttpGet(string url, string token, bool bypassProxy, int timeoutMs)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;   // 必须给超时：默认会一直挂着
        req.UserAgent = "ps-context-menu";
        req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        if (bypassProxy) req.Proxy = null;   // 绕过系统代理，直连试试
        if (!string.IsNullOrEmpty(token)) req.Headers["Authorization"] = "token " + token;
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            return sr.ReadToEnd();
    }
    public static string Brief(Exception ex)
    {
        var we = ex as WebException;
        if (we != null)
        {
            var hr = we.Response as HttpWebResponse;
            if (hr != null) { int c = (int)hr.StatusCode; try { hr.Close(); } catch { } return "HTTP " + c; }
            if (we.Status == WebExceptionStatus.Timeout) return "超时";
            return we.Status.ToString();
        }
        return ex.Message;
    }
    static string ApplyManifest(string js, string via)
    {
        var jo = JObject.Parse(js);
        if (jo["icons"] == null) return "清单格式不对，已保留本地缓存";
        // 内容没变也要照实说：否则用户点了刷新、界面毫无变化，会以为没反应
        bool same = false;
        try
        {
            string cp = CachePath();
            if (File.Exists(cp)) same = File.ReadAllText(cp, Encoding.UTF8).Trim() == js.Trim();
        }
        catch { }
        File.WriteAllText(CachePath(), js, new UTF8Encoding(false));
        _data = jo; _tried = true; BuildNames();
        return (same ? "已是最新：" : "已更新：") + _names.Count + " 个（来自 " + via + "）";
    }
    public static string Refresh()
    {
        try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }   // 老 .NET 默认只到 TLS1.1，会连不上
        string tk = Token();
        var errs = new List<string>();
        string u = Url + "?t=" + DateTime.Now.Ticks;
        try { return ApplyManifest(HttpGet(GiteeUrl + "?t=" + DateTime.Now.Ticks, null, false, 5000), "Gitee"); }   // ⓪ Gitee 分发仓：国内直连、无需 token（实测 0.9s）
        catch (Exception ex) { errs.Add("gitee=" + Brief(ex)); LogFail("NetIcons.gitee", ex); }
        try { return ApplyManifest(HttpGet(u, tk, false, 9000), "GitHub raw"); }        // ① GitHub raw，走系统代理
        catch (Exception ex) { errs.Add("raw走代理=" + Brief(ex)); LogFail("NetIcons.raw.proxy", ex); }
        try { return ApplyManifest(HttpGet(u, tk, true, 9000), "GitHub raw 直连"); }         // ② raw，绕过代理直连
        catch (Exception ex) { errs.Add("raw直连=" + Brief(ex)); LogFail("NetIcons.raw.direct", ex); }
        try                                                               // ③ GitHub API（另一台主机）
        {
            string js = HttpGet(ApiUrl, tk, false, 9000);
            var jo = JObject.Parse(js);
            string b64 = jo["content"] == null ? null : jo["content"].ToString();
            if (string.IsNullOrEmpty(b64)) throw new Exception("API 没返回 content");
            return ApplyManifest(Encoding.UTF8.GetString(Convert.FromBase64String(b64.Replace("\n", "").Replace("\r", ""))), "GitHub API");
        }
        catch (Exception ex) { errs.Add("api=" + Brief(ex)); LogFail("NetIcons.api", ex); }
        return "刷新失败（继续用本地缓存）：" + string.Join("；", errs.ToArray());
    }
    // 取中文名（清单里的 cn）
    public static string CnOf(string name)
    {
        Load();
        try
        {
            var ic = _data == null ? null : _data["icons"] == null ? null : _data["icons"][name];
            if (ic != null && ic["cn"] != null) return ic["cn"].ToString();
        }
        catch { }
        return null;
    }
    // 显示名：网络图标显示成「中文（net:xxx）」，其它原样返回
    public static string Label(string spec)
    {
        if (string.IsNullOrEmpty(spec) || !spec.StartsWith("net:", StringComparison.OrdinalIgnoreCase)) return spec;
        string cn = CnOf(spec.Substring(4));
        return string.IsNullOrEmpty(cn) ? spec : cn + "（" + spec + "）";
    }
    // 搜索命中：英文名 / 中文名 / 中文名拼音首字母；全部支持子序列模糊
    //（「文阴」→文字阴影、「wzsy」→文字阴影拼音首字母子序列、「LAY」→layers）
    public static bool Hit(string spec, string q)
    {
        if (string.IsNullOrEmpty(spec) || string.IsNullOrEmpty(q)) return false;
        bool isNet = spec.StartsWith("net:", StringComparison.OrdinalIgnoreCase);
        string s2 = isNet ? spec.Substring(4) : spec;
        string cn = isNet ? CnOf(s2) : null;
        if (s2.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (!string.IsNullOrEmpty(cn))
        {
            if (cn.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (PinYin.Hit(cn, q)) return true;                        // 中文拼音首字母（连续）
        }
        if (Subseq(s2, q)) return true;                                // 英文名子序列模糊
        if (!string.IsNullOrEmpty(cn) && Subseq(cn, q)) return true;   // 中文子序列模糊
        return false;
    }
    public static List<string> Names { get { Load(); return _names; } }
    public static List<string> Specs { get { Load(); return _names.Select(x => "net:" + x).ToList(); } }
    // 按 24x24 的 viewBox 画成矢量；描边型用 Stroke，填充型用 Fill
    public static UIElement Render(string name, double size, Brush fg)
    {
        Load();
        try
        {
            var ic = _data == null ? null : _data["icons"] == null ? null : _data["icons"][name];
            if (ic == null) return null;
            string d = ic["d"] == null ? null : ic["d"].ToString();
            if (string.IsNullOrEmpty(d)) return null;
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(d) };
            // 判据：只有清单里明确带 w(即 SVG 写了 stroke-width) 的才是描边型；
            // 其余一律填充——mdi 的 path 描述的是实心形状，若误按描边画，只会画出外轮廓、形状全变样
            double sw2 = 0;
            try { if (ic["w"] != null) sw2 = Convert.ToDouble(ic["w"].ToString(), System.Globalization.CultureInfo.InvariantCulture); } catch { }
            if (sw2 > 0)
            {
                path.Stroke = fg; path.StrokeThickness = sw2;
                path.StrokeStartLineCap = PenLineCap.Round; path.StrokeEndLineCap = PenLineCap.Round; path.StrokeLineJoin = PenLineJoin.Round;
            }
            else path.Fill = fg;
            // 关键：路径按 24x24 设计坐标画进固定画布，再整体缩放。
            // 但**不能统一放大**：mdi 各图标自带的内边距差很多（有的画满 24×24、有的只占中间一半），
            // 统一放大就会把"本来就画满"的那些切掉边缘（用户截图里被切的就是这类）。
            // 所以按几何包围盒逐个归一：等比缩放到"最长边占框 NetGlyphFraction"并居中，
            // 这样所有 net 图标的视觉大小一致、也永远不出框。描边型要把线宽算进包围盒。
            var gb = path.Data == null ? Rect.Empty : path.Data.Bounds;
            double padv = sw2 > 0 ? sw2 / 2.0 : 0;
            var eb = new Rect(gb.X - padv, gb.Y - padv, gb.Width + 2 * padv, gb.Height + 2 * padv);
            double k = (eb.Width <= 0.01 || eb.Height <= 0.01) ? 1 : (IconR.NetGlyphFraction * 24.0) / Math.Max(eb.Width, eb.Height);
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(k, k));
            tg.Children.Add(new TranslateTransform(12 - k * (eb.X + eb.Width / 2), 12 - k * (eb.Y + eb.Height / 2)));
            path.RenderTransform = tg;
            var frame = new Canvas { Width = 24, Height = 24 };
            frame.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = frame };
        }
        catch (Exception ex) { LogFail("NetIcons.Render", ex); return null; }
    }
}
// #endregion

}
}
