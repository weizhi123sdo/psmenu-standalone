using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Quicker.Public;

public static class Launcher
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PSMenu", "app");
    private static readonly string ExePath = Path.Combine(AppDir, "PSMenu.exe");
    private static readonly string VerPath = Path.Combine(AppDir, "version.txt");

    private const string ManifestGitee = "https://gitee.com/weizhiOWO/quicker-actions/raw/main/psmenu/latest.json";
    private const string ManifestGithub = "https://raw.githubusercontent.com/weizhi123sdo/psmenu-standalone/main/release/latest.json";

    public static string Exec(IStepContext context)
    {
        Directory.CreateDirectory(AppDir);

        string inParam = (context.GetVarValue("quicker_in_param") as string ?? "").Trim();
        bool forceUpdate = string.Equals(inParam, "update", StringComparison.OrdinalIgnoreCase);

        bool needInstall = !File.Exists(ExePath) || !File.Exists(VerPath);
        string currentVer = File.Exists(VerPath) ? File.ReadAllText(VerPath).Trim() : "0.0.0";

        // 1. 检查并处理更新/初次下载
        JObject manifest = FetchManifest();
        if (manifest != null)
        {
            string remoteVer = manifest["version"]?.ToString()?.Trim() ?? "";
            if (needInstall || forceUpdate || (remoteVer != currentVer && !string.IsNullOrEmpty(remoteVer)))
            {
                bool success = UpdateFromManifest(manifest, remoteVer);
                if (success)
                {
                    currentVer = remoteVer;
                }
                else if (needInstall)
                {
                    context.SetVarValue("errMessage", "初次安装失败，未能成功下载 PSMenu 资源包");
                    return "ERR: Install failed";
                }
            }
        }
        else if (needInstall)
        {
            context.SetVarValue("errMessage", "无法连接分发服务器，且本地尚未安装 PSMenu");
            return "ERR: Network unreachable";
        }

        if (!File.Exists(ExePath))
        {
            context.SetVarValue("errMessage", "未找到 PSMenu.exe 主程序");
            return "ERR: File not found";
        }

        // 2. 唤起已存在进程或启动常驻进程
        RunTarget(inParam);

        context.SetVarValue("rtn", "OK: " + currentVer);
        return "OK";
    }

    private static JObject FetchManifest()
    {
        string[] urls = new[] { ManifestGitee, ManifestGithub };
        foreach (var url in urls)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                {
                    string text = client.GetStringAsync(url + "?t=" + DateTime.UtcNow.Ticks).Result;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return JObject.Parse(text);
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private static bool UpdateFromManifest(JObject manifest, string remoteVer)
    {
        var files = manifest["files"] as JArray;
        if (files == null || files.Count == 0) return false;

        var file = files[0];
        string url1 = file["url"]?.ToString();
        string url2 = file["fallbackUrl"]?.ToString();
        string expectedSha = file["sha256"]?.ToString()?.ToLower();

        string tempZip = Path.Combine(Path.GetTempPath(), "psmenu_setup_" + Guid.NewGuid().ToString("N") + ".zip");
        bool ok = DownloadWithFallback(url1, url2, tempZip);
        if (!ok || !File.Exists(tempZip)) return false;

        if (!string.IsNullOrEmpty(expectedSha))
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(tempZip))
            {
                string calcSha = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                if (calcSha != expectedSha)
                {
                    try { File.Delete(tempZip); } catch { }
                    return false;
                }
            }
        }

        // 关闭占用进程
        var procs = Process.GetProcessesByName("PSMenu");
        foreach (var p in procs)
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        try
        {
            using (var zip = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in zip.Entries)
                {
                    string targetPath = Path.GetFullPath(Path.Combine(AppDir, entry.FullName));
                    if (!targetPath.StartsWith(Path.GetFullPath(AppDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        entry.ExtractToFile(targetPath, true);
                    }
                }
            }
            File.WriteAllText(VerPath, remoteVer);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { File.Delete(tempZip); } catch { }
        }
    }

    private static bool DownloadWithFallback(string u1, string u2, string savePath)
    {
        string[] urls = new[] { u1, u2 };
        foreach (var u in urls)
        {
            if (string.IsNullOrWhiteSpace(u)) continue;
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                using (var resp = client.GetAsync(u).Result)
                {
                    if (resp.IsSuccessStatusCode)
                    {
                        using (var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            resp.Content.CopyToAsync(fs).Wait();
                        }
                        return true;
                    }
                }
            }
            catch { }
        }
        return false;
    }

    private static void RunTarget(string inParam)
    {
        var procs = Process.GetProcessesByName("PSMenu");
        if (procs.Length > 0)
        {
            string cmd = "menu";
            if (string.Equals(inParam, "config", StringComparison.OrdinalIgnoreCase))
            {
                cmd = "config";
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = cmd,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ExePath,
                WorkingDirectory = AppDir,
                UseShellExecute = true
            });
        }
    }
}
