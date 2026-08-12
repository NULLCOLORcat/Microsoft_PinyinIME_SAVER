using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinSpaceImeToggle
{
    /// <summary>
    /// 主程序自身配置（%APPDATA%\WinSpaceImeToggle\config.ini）：
    /// 只包含主程序负责的项目——快捷键总开关与开机自启动。
    /// 输入法/代理等模块设置由各模块自己的 ini 文件管理。
    /// </summary>
    internal class MainConfig
    {
        public bool HotkeysEnabled = true;
        public bool AutoStart;
        public string Language = "en";

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "WinSpaceImeToggle";

        private static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinSpaceImeToggle"); }
        }

        private static string FilePath
        {
            get { return Path.Combine(Dir, "config.ini"); }
        }

        /// <summary>读取 %APPDATA%\WinSpaceImeToggle\config.ini 中的主程序配置。</summary>
        public static MainConfig Load()
        {
            MainConfig c = new MainConfig();
            try
            {
                if (File.Exists(FilePath))
                {
                    string[] lines = File.ReadAllLines(FilePath);
                    bool hasHotkeys = false;
                    foreach (string line in lines)
                    {
                        int idx = line.IndexOf('=');
                        if (idx <= 0) continue;
                        string k = line.Substring(0, idx).Trim();
                        string v = line.Substring(idx + 1).Trim();
                        if (k == "hotkeysEnabled")
                        {
                            bool b;
                            if (bool.TryParse(v, out b)) { c.HotkeysEnabled = b; hasHotkeys = true; }
                        }
                        else if (k == "autoStart")
                        {
                            bool b;
                            if (bool.TryParse(v, out b)) c.AutoStart = b;
                        }
                        else if (k == "language")
                        {
                            if (v.Length > 0) c.Language = v;
                        }
                        else if (k == "enabled" && !hasHotkeys)
                        {
                            // 旧版单文件配置兼容：enabled 即快捷键总开关
                            bool b;
                            if (bool.TryParse(v, out b)) c.HotkeysEnabled = b;
                        }
                    }
                }
                c.AutoStart = IsAutoStartEnabled();
            }
            catch { }
            return c;
        }

        /// <summary>把当前配置写回 config.ini。</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("hotkeysEnabled=" + HotkeysEnabled.ToString());
                sb.AppendLine("autoStart=" + AutoStart.ToString());
                sb.AppendLine("language=" + Language);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>设置或取消开机自启动（写入 HKCU Run 注册表项）。</summary>
        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (enable)
                        key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                    else
                        key.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }

        /// <summary>查询开机自启动是否已启用。</summary>
        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }
    }
}
