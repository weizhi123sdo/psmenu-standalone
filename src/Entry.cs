using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Newtonsoft.Json.Linq;

#pragma warning disable 169, 649, 414, 219, 67
namespace PsMenuApp
{
public static partial class PsMenu
{
// 独立版入口：由外壳（App）在触发时调用。menuKey="config" 打开设置窗；inParam 可携带 shot= 调试出图参数。
public static string Exec(string menuKey, string inParam)
{
    try
    {
        Recent.Ensure();
        // 脚本库只读本地缓存（取数归「PS脚本库」动作）——呼出路径上不联网、不起线程
        NetScripts.Load();
        var cfg = ConfigService.Load();
        UiCfg = cfg;
        string mk = menuKey ?? "";

        // 调试出图（不弹窗）：`shot=menu;...` 渲染菜单 / `shot=config;page=1` 渲染设置窗。
        // 用途是让我离线审界面（对齐/间距/配色），不必在用户屏幕上弹窗、也不抢焦点。
        string shotSpecEarly = null;
        try
        {
            string ip0 = inParam ?? "";
            if (ip0.StartsWith("shot=", StringComparison.OrdinalIgnoreCase)) shotSpecEarly = ip0;
        }
        catch { }

        // shot=config;page=N：直接把设置窗渲染成 PNG（不用走 menuKey，出图时更省事）
        string shotWhat = ShotField(shotSpecEarly, "shot");
        if (shotWhat != null && shotWhat.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            string rc1 = null;
            Application.Current.Dispatcher.Invoke(() => rc1 = ShowConfigEditor(cfg, shotSpecEarly));
            return string.IsNullOrEmpty(rc1) ? "OK" : rc1;
        }
        // shot=pick;sel=N：把「从脚本库添加」窗渲染成 PNG，不弹窗。
        // 它平时是设置窗里「＋ 从脚本库」点出来的，出图要能直接构造——否则"选中某条才出现"的
        // 风险徽章永远没法离线验收（菜单那套出图看不到这个窗）。
        if (shotWhat != null && shotWhat.Equals("pick", StringComparison.OrdinalIgnoreCase))
        {
            string lkv2 = ShotField(shotSpecEarly, "lk");
            string lbl2 = string.IsNullOrEmpty(lkv2) ? "文本" : LTM.L(LTM.Parse(lkv2));
            // 挑选窗要返回"用户挑中的菜单项"，塞不下出图结果，所以路径走这个静态字段带回来
            PickShotOut = "";
            Application.Current.Dispatcher.Invoke(() => { PickScriptsFromLib(null, lbl2, new Dictionary<string, string>(), shotSpecEarly); });
            return string.IsNullOrEmpty(PickShotOut) ? "OK" : PickShotOut;
        }
        if (mk == "config")
        {
            string rc0 = null;
            Application.Current.Dispatcher.Invoke(() => rc0 = ShowConfigEditor(cfg, shotSpecEarly));
            return string.IsNullOrEmpty(rc0) ? "OK" : rc0;
        }

        // 图层探测带 3s 超时：PS 被授权弹窗/启动画面卡住时 COM 调用会无限阻塞，
        // 菜单必须照样弹（回退通用菜单），绝不能"按了没反应"。
        LayerKind lk = LayerKind.None;
        try
        {
            var dt = System.Threading.Tasks.Task.Run(() => new LayerDetector().Detect());
            if (dt.Wait(3000)) lk = dt.Result;
        }
        catch { }
        if (lk == LayerKind.None) lk = LayerKind.Fallback;

        // 调试出图（不弹窗）：`shot=menu;lk=text;q=变换;pin=0` —— 把菜单渲染成 PNG 落盘并返回路径。
        // 用途是让我离线审界面（对齐/间距），不必在用户屏幕上弹窗、也不抢焦点。
        string shotSpec = shotSpecEarly;   // 顶部已解析（编辑器出图也要用）
        if (shotSpec != null)
        {
            string lkv = ShotField(shotSpec, "lk");
            if (!string.IsNullOrEmpty(lkv)) lk = LTM.Parse(lkv);
            // pinname=0/1：出图时临时切到"悬停预览/方格下常显"（改副本，不落盘）
            string pnv = ShotField(shotSpec, "pinname");
            if (!string.IsNullOrEmpty(pnv))
            {
                cfg = AppConfigSerializer.FromJson(AppConfigSerializer.ToJson(cfg)) ?? cfg;
                cfg.ShowPinName = pnv != "0";
                UiCfg = cfg;
            }
            // theme=<预设id>：把该预设的配色套到内存里的 cfg 上（只在出图时、不落盘），
            // 用来逐个验收 10 套预设的实际观感
            string thv = ShotField(shotSpec, "theme");
            if (!string.IsNullOrEmpty(thv))
            {
                var tp = DarkThemes.Concat(LightThemes).FirstOrDefault(x => x.th == thv);
                if (!string.IsNullOrEmpty(tp.th))
                {
                    // 必须改**副本**：ConfigService.Load() 返回的是进程内缓存的那个实例，
                    // 直接改它会把出图用的配色留在缓存里（后续出图、甚至用户保存时都会被污染）。
                    cfg = AppConfigSerializer.FromJson(AppConfigSerializer.ToJson(cfg)) ?? cfg;
                    cfg.BC = tp.bc; cfg.FC = tp.fc; cfg.HC = tp.hc; cfg.OC = tp.oc; cfg.IC = tp.ic; cfg.Theme = tp.th;
                    UiCfg = cfg;
                }
            }
        }

        string mr = null;
        Application.Current.Dispatcher.Invoke(() => { mr = ShowContextMenu(cfg, lk, shotSpec); });
        if (!string.IsNullOrEmpty(mr) && mr.StartsWith("SHOT")) return mr;
        if (string.IsNullOrEmpty(mr)) return "";

        JObject a;
        try { a = JObject.Parse(mr); } catch { return ""; }
        string act = a["action"]?.ToString() ?? "",
               val = a["value"]?.ToString() ?? "",
               title = a["title"]?.ToString() ?? "";

        switch (act)
        {
            case ACT_EDIT_CONFIG:
                Application.Current.Dispatcher.Invoke(() => ShowConfigEditor(cfg));
                return "OK";
            case ACT_EXPORT_CONFIG:
                ClipHelper.Set(AppConfigSerializer.ToJson(cfg));
                Application.Current.Dispatcher.Invoke(() =>
                    ShowInfo("已导出配置", "当前配置已以 JSON 形式复制到剪贴板。"));
                return "OK";
            case ACT_IMPORT_CONFIG:
                try
                {
                    string clip = ClipHelper.Get();
                    if (!string.IsNullOrEmpty(clip))
                    {
                        var imp = AppConfigSerializer.FromJson(clip);
                        if (imp != null && imp.Menus.Count > 0)
                        {
                            ConfigService.Save(imp);
                            Application.Current.Dispatcher.Invoke(() =>
                                ShowInfo("已导入配置", "剪贴板里的配置已写入并生效。"));
                        }
                        else Application.Current.Dispatcher.Invoke(() =>
                            ShowInfo("剪贴板内容无效", "剪贴板里没有可识别的配置 JSON。"));
                    }
                }
                catch
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        ShowInfo("导入失败", "读取剪贴板时出错，配置未改变。"));
                }
                return "OK";
            default:
                string er = ActionExecutor.Exec(act, val, title);
                if (!string.IsNullOrEmpty(er) && !er.StartsWith("Error") && er != "PS not running")
                {
                    ConfigService.Track(cfg, title, act, val);
                    ConfigService.Save(cfg);
                }
                return er;
        }
    }
    catch (Exception ex) { LogFail("Exec", ex); return "ERROR: " + ex.Message; }
}

}
}
