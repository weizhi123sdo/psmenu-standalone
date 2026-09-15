using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using Newtonsoft.Json.Linq;

namespace PsMenuApp
{
// 触发类设置的唯一数据源：全局热键 + PS 前台中键唤出。
// 沿用外壳原来的 sidecar 文件 %APPDATA%\PSMenu\shell_state.json；
// 老文件里只有 middleCall 字段也能读（hkMod/hkVk 缺失就用默认值），行为兼容。
internal static class ShellConfig
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PSMenu", "shell_state.json");

    // hkMod = RegisterHotKey 的修饰位：1=Alt 2=Ctrl 4=Shift 8=Win；hkVk = 虚拟键码。
    // 默认 Ctrl+Alt+Space：裸 Ctrl+Space 是中文输入法的全局切换键，热键必冲突。
    public static int HkMod { get; set; } = 3;
    public static int HkVk { get; set; } = 0x20;
    public static bool MiddleCall { get; set; } = true;
    // 首次常驻启动是否已完成（「已就绪」气泡只弹一次）
    public static bool FirstRunDone { get; set; } = false;

    // 启动时调用一次：文件缺失 / 坏 JSON 一律保持默认值，绝不让外壳起不来
    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var o = JObject.Parse(File.ReadAllText(FilePath));
            int m, v;
            if (TryGetInt(o["hkMod"], out m)) HkMod = m;
            if (TryGetInt(o["hkVk"], out v)) HkVk = v;
            // 一次性迁移：v1.0 早期默认是裸 Ctrl+Space（撞中文输入法切换键），老配置升到 Ctrl+Alt+Space
            if (HkMod == 2 && HkVk == 0x20) HkMod = 3;
            if (o["middleCall"] != null) MiddleCall = o["middleCall"].ToString() == "true";
            if (o["firstRunDone"] != null) FirstRunDone = o["firstRunDone"].ToString() == "true";
        }
        catch { }
    }

    // 兼容十进制与 0x 前缀十六进制两种写法（JSON 标准里其实没有 0x，但手改配置的人爱这么写）
    static bool TryGetInt(JToken t, out int v)
    {
        v = 0;
        if (t == null) return false;
        string s = t.ToString().Trim();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return true;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        return false;
    }

    // 原子写：先写临时文件再 Replace，写一半断电也不会留下坏 JSON
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, new JObject(
                new JProperty("hkMod", HkMod),
                new JProperty("hkVk", HkVk),
                new JProperty("middleCall", MiddleCall),
                new JProperty("firstRunDone", FirstRunDone)).ToString());
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
        catch (Exception ex) { try { PsMenu.LogFail("ShellConfigSave", ex); } catch { } }
    }

    // 修饰位 + VK 的人类可读文本，如 "Ctrl + Alt + Space"；vk<=0 时只列修饰键（录制窗显示半截组合用）
    public static string HotkeyText(int mod, int vk)
    {
        var sb = new StringBuilder();
        if ((mod & 2) != 0) sb.Append("Ctrl + ");
        if ((mod & 1) != 0) sb.Append("Alt + ");
        if ((mod & 4) != 0) sb.Append("Shift + ");
        if ((mod & 8) != 0) sb.Append("Win + ");
        if (vk > 0) sb.Append(VkName(vk));
        string t = sb.ToString();
        return t.EndsWith(" + ") ? t.Substring(0, t.Length - 3) : t;
    }

    static string VkName(int vk)
    {
        try
        {
            Key k = KeyInterop.KeyFromVirtualKey(vk);
            if (k == Key.Space) return "Space";
            return k.ToString();
        }
        catch { return "0x" + vk.ToString("X2"); }
    }
}
}
