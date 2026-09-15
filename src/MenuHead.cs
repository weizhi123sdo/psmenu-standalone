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
// #region Context Menu — PS Native Style
static MI FindMenuByUK(List<LM> menus, string uk)
{
    if (menus == null || string.IsNullOrEmpty(uk)) return null;
    MI any = null;
    Func<List<MI>, MI> walk = null;
    walk = items =>
    {
        if (items == null) return null;
        foreach (var it in items)
        {
            if (it == null || it.IsSep) continue;
            if (it.UK == uk) { if (it.HasSub) return it; if (any == null) any = it; }
            var f = it.HasSub ? walk(it.Sub) : null;
            if (f != null) return f;
        }
        return null;
    };
    foreach (var m in menus) { var f = walk(m?.Items); if (f != null) return f; }
    return any;
}
// 置顶 → 当前菜单项：先按稳定键 K 找（改标题/图标/值都不丢），旧配置没有 K 再按 UK、
// 最后按「动作+值」唯一匹配兜底；找到后把当前内容回写进置顶副本，显示与执行都用最新的。
static MI PinSource(AC cfg, PI p)
{
    MI s = null;
    if (!string.IsNullOrEmpty(p.K))
    {
        MI anyK = null;
        Func<List<MI>, MI> walkK = null;
        walkK = items =>
        {
            if (items == null) return null;
            foreach (var it in items)
            {
                if (it == null || it.IsSep) continue;
                if (it.K == p.K) { if (it.HasSub) return it; if (anyK == null) anyK = it; }
                var f = it.HasSub ? walkK(it.Sub) : null;
                if (f != null) return f;
            }
            return null;
        };
        foreach (var m in cfg.Menus) { var f = walkK(m?.Items); if (f != null) { s = f; break; } }
        if (s == null) p.K = null;   // 原项已被删掉：清掉死键，走兜底/重新关联
    }
    if (s == null) s = FindMenuByUK(cfg.Menus, p.UK);
    if (s == null && (p.Action != ActionType.Keys || !string.IsNullOrEmpty(p.Value)))
    {
        MI hit = null; int n = 0;
        Action<List<MI>> walkV = null;
        walkV = items =>
        {
            if (items == null) return;
            foreach (var it in items)
            {
                if (it == null || it.IsSep) continue;
                if (it.Action == p.Action && it.Value == p.Value) { hit = it; n++; }
                if (it.HasSub) walkV(it.Sub);
            }
        };
        foreach (var m in cfg.Menus) walkV(m?.Items);
        if (n == 1) s = hit;   // 只有唯一候选才敢认，避免错绑
    }
    if (s != null)
    {
        p.K = s.K;
        p.Title = s.Title; p.Icon = s.Icon; p.Action = s.Action; p.Value = s.Value;
    }
    return s;
}

// #region PinYin — 汉字→拼音首字母（让菜单搜索支持 zybh → 自由变换）
// 中文标题原来必须切输入法打汉字才搜得到，而菜单呼出时切输入法很别扭。
// 表是生成物：remote-updater/scripts/build_pinyin_table.py（GB2312 汉字 6763 个）。
// 表外生僻字只是「拼音搜不到」，汉字直搜不受影响——安全退化。
}
}
