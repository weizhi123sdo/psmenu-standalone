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
// #region Screen Color Picker
static string PickScreenColor()
{
    var sw = System.Windows.SystemParameters.PrimaryScreenWidth; var sh = System.Windows.SystemParameters.PrimaryScreenHeight;
    var pw = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), ShowInTaskbar = false, Topmost = true, Width = sw, Height = sh, Left = 0, Top = 0, Cursor = Cursors.Cross };
    string r = null;
    var hint = new TextBlock { Text = "🔍 点击取色  Esc取消", FontSize = Tk.FTitle, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 36)), Padding = new Thickness(12, 6, 12, 6) };
    var hp = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, Width = 200, Height = 40, Left = sw / 2 - 100, Top = 20 };
    hp.Content = hint; hp.Show();
    pw.MouseLeftButtonDown += (_, e) => { try { var sp = System.Windows.Forms.Cursor.Position; using var bmp = new System.Drawing.Bitmap(1, 1); using var g = System.Drawing.Graphics.FromImage(bmp); g.CopyFromScreen(sp.X, sp.Y, 0, 0, new System.Drawing.Size(1, 1)); var px = bmp.GetPixel(0, 0); r = string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", px.A, px.R, px.G, px.B); } catch { } hp.Close(); pw.Close(); };
    pw.MouseMove += (_, e) => { var sp = System.Windows.Forms.Cursor.Position; try { using var bmp = new System.Drawing.Bitmap(1, 1); using var g = System.Drawing.Graphics.FromImage(bmp); g.CopyFromScreen(sp.X, sp.Y, 0, 0, new System.Drawing.Size(1, 1)); var px = bmp.GetPixel(0, 0); hint.Text = string.Format("🔍 #{0:X2}{1:X2}{2:X2}  点击取色  Esc取消", px.R, px.G, px.B); hint.Background = new SolidColorBrush(Color.FromArgb(220, px.R, px.G, px.B)); } catch { } };
    pw.KeyDown += (_, e) => { if (e.Key == Key.Escape) { hp.Close(); pw.Close(); } };
    pw.ShowDialog();
    if (!string.IsNullOrEmpty(r) && r.Length >= 9)
    {
        try
        {
            string h6 = string.Format("#{0:X2}{1:X2}{2:X2}", byte.Parse(r.Substring(3, 2), System.Globalization.NumberStyles.HexNumber), byte.Parse(r.Substring(5, 2), System.Globalization.NumberStyles.HexNumber), byte.Parse(r.Substring(7, 2), System.Globalization.NumberStyles.HexNumber));
            RecentColors.Remove(h6); RecentColors.Insert(0, h6);
            if (RecentColors.Count > 12) RecentColors.RemoveAt(RecentColors.Count - 1);
            Recent.Save();
        }
        catch { }
    }
    return r;
}

// #region Slider helper
static Slider CreateSlider(double val, double min, double max, Action<double> onChanged)
{
    var sl = new Slider { Minimum = min, Maximum = max, Value = val, Height = 26, Background = Th.BBI, Foreground = Th.B(Th.AB), TickFrequency = (max - min) / 10, IsSnapToTickEnabled = false };
    sl.ValueChanged += (_, __) => onChanged(sl.Value); return sl;
}

// #region Color row helper (label + textbox + color picker + preview)
static UIElement MkCR(string lb, string hx, Action<string> onCh, string note = "")
{
    var cr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
    cr.Children.Add(new TextBlock { Text = lb, FontSize = Tk.FSmall, Foreground = Th.BTS, VerticalAlignment = VerticalAlignment.Center, Width = 56, ToolTip = note });
    var ctb = new TextBox { Text = hx, FontSize = Tk.FSmall, Foreground = Th.BTP, Background = Th.BBI, BorderBrush = Th.B(Th.BS), BorderThickness = new Thickness(1), Padding = new Thickness(4, 2, 4, 2), Width = 80, CaretBrush = Th.BTP };
    ctb.TextChanged += (_, __) => { if (ctb.Text.Length >= 7) onCh(ctb.Text); };
    cr.Children.Add(ctb);
    var cp = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), BorderBrush = Th.B(Th.BS), BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 0, 0) };
    try { cp.Background = PB(hx, Colors.Gray); } catch { }
    cp.Cursor = Cursors.Hand; cp.ToolTip = "点击取色";
    cp.MouseLeftButtonDown += (_, __) => { var picked = PickScreenColor(); if (!string.IsNullOrEmpty(picked)) { ctb.Text = picked; onCh(picked); try { cp.Background = PB(picked, Colors.Gray); } catch { } } };
    ctb.TextChanged += (_, __) => { try { cp.Background = PB(ctb.Text, Colors.Gray); } catch { } };
    cr.Children.Add(cp);
    if (RecentColors.Count > 0)
    {
        var rcRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var rc3 in RecentColors.Take(5))
        {
            var rcB = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(3), BorderBrush = Th.B(Th.BS), BorderThickness = new Thickness(1), Margin = new Thickness(1), Cursor = Cursors.Hand, ToolTip = rc3 };
            try { rcB.Background = PB(rc3, Colors.Gray); } catch { }
            rcB.MouseLeftButtonDown += (_, __) => { ctb.Text = rc3; onCh(rc3); };
            rcRow.Children.Add(rcB);
        }
        cr.Children.Add(rcRow);
    }
    return cr;
}
}
}
