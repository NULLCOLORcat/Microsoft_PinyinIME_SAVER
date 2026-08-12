using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WinSpaceImeToggle.Contracts;

namespace WinSpaceImeToggle
{
    /// <summary>
    /// 主程序入口：只负责进程生命周期、DPI、模块加载、命令行分发、托盘与设置界面宿主。
    /// 具体功能（输入法、代理修复）全部由 modules 目录中的模块 DLL 提供。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        /// <summary>程序入口：初始化 DPI，加载配置与模块，分发命令行参数或启动托盘。</summary>
        private static void Main(string[] args)
        {
            DpiAware.EnablePerMonitorV2();

            MainConfig main = MainConfig.Load();
            Localization.Load(main.Language);
            KeyNames.Language = main.Language;
            KeyNames.Resolve = Localization.T;
            MigrateLegacyConfig();

            ModuleManager modules = new ModuleManager(main);
            modules.LoadModules(args);

            if (args != null && args.Length > 0)
            {
                string a = args[0];
                if (a == "--config")
                {
                    Console.WriteLine("hotkeysEnabled=" + main.HotkeysEnabled);
                    Console.WriteLine("autoStart=" + main.AutoStart);
                    Console.WriteLine("modules=" + (modules.Modules.Count == 0 ? "(none)" : modules.Names()));
                    foreach (IModule m in modules.Modules)
                    {
                        Console.WriteLine("[" + m.Id + "] " + m.DisplayName + " -> " + m.Context.Dump());
                    }
                    return;
                }
                if (a == "--settings-smoke")
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    KeyRecorder rec = new KeyRecorder();
                    SettingsForm smokeForm = new SettingsForm(modules, main, rec);
                    TabControl smokeTabs = null;
                    foreach (Control c in smokeForm.Controls)
                    {
                        TabControl tc = c as TabControl;
                        if (tc != null) { smokeTabs = tc; break; }
                    }
                    int total = (smokeTabs != null && smokeTabs.TabPages.Count > 0) ? smokeTabs.TabPages.Count : 1;
                    int ticks = 0;
                    System.Windows.Forms.Timer st = new System.Windows.Forms.Timer();
                    st.Interval = 900;
                    st.Tick += delegate
                    {
                        ticks++;
                        if (smokeTabs != null && ticks - 1 < smokeTabs.TabPages.Count)
                        {
                            smokeTabs.SelectedIndex = ticks - 1;
                            smokeTabs.Refresh();
                            try
                            {
                                using (Bitmap bmp = new Bitmap(smokeForm.Width, smokeForm.Height))
                                {
                                    smokeForm.DrawToBitmap(bmp, new Rectangle(0, 0, smokeForm.Width, smokeForm.Height));
                                    bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-smoke-" + (ticks - 1) + ".png"),
                                        System.Drawing.Imaging.ImageFormat.Png);
                                }
                            }
                            catch { }
                        }
                        if (ticks > total)
                        {
                            st.Stop();
                            smokeForm.Close();
                        }
                    };
                    st.Start();
                    Application.Run(smokeForm);
                    rec.Dispose();
                    return;
                }
                if (modules.TryDispatchCommandLine(args)) return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\WinSpaceImeToggle", out createdNew))
            {
                if (!createdNew) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Application.Run(new TrayApplicationContext(main, modules));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Localization.T("app.startupFailed") + ex.Message, Localization.T("app.errorTitle"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>一次性迁移：把旧版单文件 config.ini 中属于模块的设置迁到各自的模块配置文件。</summary>
        private static void MigrateLegacyConfig()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinSpaceImeToggle");
                string legacy = Path.Combine(dir, "config.ini");
                if (!File.Exists(legacy)) return;
                Dictionary<string, string> old = ReadIni(legacy);
                string modDir = Path.Combine(dir, "modules");

                string ime = Path.Combine(modDir, "ime.ini");
                if (!File.Exists(ime))
                {
                    StringBuilder sb = new StringBuilder();
                    AppendKey(old, sb, "hotkey");
                    AppendKey(old, sb, "autoSwitch");
                    AppendKey(old, sb, "suppressKey");
                    AppendKey(old, sb, "debounceSeconds");
                    if (sb.Length > 0)
                    {
                        Directory.CreateDirectory(modDir);
                        File.WriteAllText(ime, sb.ToString(), Encoding.UTF8);
                    }
                }
                string proxy = Path.Combine(modDir, "proxyfix.ini");
                if (!File.Exists(proxy))
                {
                    string v;
                    if (old.TryGetValue("autoFixProxy", out v))
                    {
                        Directory.CreateDirectory(modDir);
                        File.WriteAllText(proxy, "autoFixProxy=" + v + "\r\n", Encoding.UTF8);
                    }
                }
            }
            catch { }
        }

        /// <summary>读取 INI 格式文本为键值字典（旧版配置迁移辅助）。</summary>
        private static Dictionary<string, string> ReadIni(string path)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            string[] lines = File.ReadAllLines(path);
            foreach (string line in lines)
            {
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;
                string k = line.Substring(0, idx).Trim();
                string v = line.Substring(idx + 1).Trim();
                if (k.Length > 0) map[k] = v;
            }
            return map;
        }

        /// <summary>把旧配置中的指定键追加到模块配置字符串（迁移辅助）。</summary>
        private static void AppendKey(Dictionary<string, string> old, StringBuilder sb, string key)
        {
            string v;
            if (old.TryGetValue(key, out v)) sb.AppendLine(key + "=" + v);
        }
    }

    internal static class DpiAware
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        public static void EnablePerMonitorV2()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
                // 回退：PROCESS_PER_MONITOR_DPI_AWARE = 2（Win8.1+）
                SetProcessDpiAwareness(2);
            }
            catch { }
            try
            {
                // 启用 WinForms 高 DPI 自动缩放（.NET 4.7+ 运行时生效）
                AppContext.SetSwitch("Switch.System.Windows.Forms.EnableWindowsFormsHighDpiAutoResizing", true);
            }
            catch { }
        }
    }
}


