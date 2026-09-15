using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

#pragma warning disable 169, 649, 414, 219, 67
namespace PsMenuApp
{
public static partial class PsMenu
{
const string ACT_KEYS = "keys"; const string ACT_SCRIPT = "script";
const string ACT_RUN = "run"; const string ACT_CLIPBOARD = "clipboard";
const string ACT_QK = "qk";   // 运行 Quicker 动作
const string ACT_EDIT_CONFIG = "edit_config";
const string ACT_EXPORT_CONFIG = "export_config";
const string ACT_IMPORT_CONFIG = "import_config";

// 调试出图参数解析：`shot=menu;lk=text;q=变换;pin=0` 里取某个键（; 或 & 分隔，大小写不敏感）
static string ShotField(string spec, string key)
{
    if (string.IsNullOrEmpty(spec)) return null;
    foreach (var part in spec.Split(';', '&'))
    {
        int i = part.IndexOf('=');
        if (i <= 0) continue;
        if (part.Substring(0, i).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            return part.Substring(i + 1).Trim();
    }
    return null;
}

}
}
