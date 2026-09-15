// Roslyn 编译验证：元组 + 局部函数 + 模式匹配 + out var（C#7）
using System;
using System.Linq;
using System.Collections.Generic;

namespace Hello2
{
    public static class Program
    {
        public static readonly (string name, string bc, double op, string th)[] Themes = {
            ("psdark", "#1B1B1B", 0.97, "psdark"),
            ("hires", "#0D1117", 0.98, "hires"),
        };
        [STAThread]
        public static void Main()
        {
            int Total() => Themes.Where(t => t.op > 0.5).Count();
            if (Themes[0] is ("psdark", _, _, _) t) { }
            string s = null; var len = s?.Length ?? -1;
            var ok = Total() == 2 && len == -1;
            if (!ok) throw new Exception("C#7 test failed");
            System.Windows.MessageBox.Show("C#7 WPF OK");
        }
    }
}
