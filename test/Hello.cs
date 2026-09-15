// 编译去风险测试：WPF + FontAwesome(SvgAwesome) + Newtonsoft + WinForms NotifyIcon
using System;
using System.Windows;
using System.Windows.Media;
using FontAwesome5;
using FontAwesome5.WPF;
using Newtonsoft.Json.Linq;

namespace Hello
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var ja = new JArray { "psmenu", "ok" };
            var w = new Window { Title = "psmenu build test: " + ja.ToString(), Width = 300, Height = 120 };
            var icon = new SvgAwesome { Icon = EFontAwesomeIcon.Solid_Play, Foreground = Brushes.DodgerBlue, Width = 32, Height = 32 };
            var b = new System.Windows.Controls.Button { Content = icon, Width = 60, Height = 40 };
            w.Content = b;
            var ni = new System.Windows.Forms.NotifyIcon { Text = "psmenu", Visible = false };
            w.Loaded += (s, e) => { Console.WriteLine("LOADED"); w.Close(); };
            w.ShowDialog();
            ni.Dispose();
        }
    }
}
