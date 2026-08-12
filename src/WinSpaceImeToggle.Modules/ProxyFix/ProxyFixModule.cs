using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinSpaceImeToggle.Contracts;

namespace WinSpaceImeToggle.Modules
{
    /// <summary>
    /// 代理修复实现模块：程序启动时若开启“开机自启动时自动修复代理问题”，
    /// 自动关闭系统代理（ProxyEnable=0）并通知系统刷新，解决第三方 VPN 异常退出后
    /// 残留代理导致的断网。独占“启动策略”设置分页。
    /// 删除本 DLL 后主程序仍可运行，该分页与启动修复逻辑一并移除。
    /// </summary>
    public class ProxyFixModule : IModule
    {
        public string Id { get { return "proxyfix"; } }
        public string DisplayName { get { return Context.T("proxyfix.name"); } }
        public ModuleContext Context { get; set; }
        public Action PrimaryAction { get { return null; } }
        public string StartupBalloonTitle { get { return null; } }
        public string StartupBalloonText { get { return null; } }

        public void Initialize()
        {
        }

        public IEnumerable<SettingsPage> GetSettingsPages()
        {
            SettingsPage page = new SettingsPage();
            page.Title = Context.T("proxyfix.pageTitle");

            ToggleItem toggle = new ToggleItem();
            toggle.Key = "autoFixProxy";
            toggle.Label = Context.T("proxyfix.toggleLabel");
            toggle.DefaultValue = false;
            toggle.Note = Context.T("proxyfix.toggleNote");
            toggle.Tooltip = Context.T("proxyfix.toggleTooltip");
            page.Items.Add(toggle);

            List<SettingsPage> pages = new List<SettingsPage>();
            pages.Add(page);
            return pages;
        }

        public void OnSettingsApplied()
        {
        }

        public void OnGlobalHotkeyCaptureChanged(bool capturing)
        {
        }

        public void OnAppStarted()
        {
            try
            {
                if (Context.GetBool("autoFixProxy", false)) ProxyFixer.DisableProxy();
            }
            catch { }
        }

        public void OnAppExit()
        {
        }

        public bool TryHandleCommandLine(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            if (args[0] == "--fix-proxy")
            {
                bool was = ProxyFixer.IsProxyEnabled();
                bool ok = ProxyFixer.DisableProxy();
                Console.WriteLine("proxyWasEnabled=" + was + " fix=" + ok + " nowEnabled=" + ProxyFixer.IsProxyEnabled());
                return true;
            }
            return false;
        }

        public IEnumerable<TrayItem> GetTrayItems()
        {
            return null;
        }
    }

    /// <summary>系统代理读写：通过 HKCU Internet Settings 的 ProxyEnable 与 wininet 刷新通知。</summary>
    internal static class ProxyFixer
    {
        private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_REFRESH = 37;
        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;

        public static bool IsProxyEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, false))
                {
                    if (key == null) return false;
                    object v = key.GetValue("ProxyEnable");
                    return v != null && Convert.ToInt32(v) != 0;
                }
            }
            catch { return false; }
        }

        public static bool DisableProxy()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, true))
                {
                    if (key == null) return false;
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                }
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
                return true;
            }
            catch { return false; }
        }
    }
}
